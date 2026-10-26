using AMES.Data.Repositories;
using AMES.Data.Services.Notify;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-24 PUSH 알림 발송기(NotificationRepository) — Operator 규칙은 PIN 이 있는 활성 작업자에게 기기마다 이력을 만들고,
// 같은 이벤트는 다시 만들지 않으며, 방해 금지 시간은 QUIET, 공용 기기는 마지막 로그인 사번으로 옮겨지고, 발송 결과·토큰 무효·앱 목록·읽음이 맞는지.
// 쓰기라서 AMES_TEST_CONN(로컬 DB)을 지정했을 때만 돈다. 시험 행(사번 ZNTF*, 고장 FAIL-ZNTF-*, 규칙 CreatedBy itest-ntf)은 끝에 지운다.
public class NotificationRepositoryTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes test workers, rule, failures, devices and notifications";
    const string Actor   = "itest-ntf";
    const string Worker  = "ZNTF01";   // PIN 있음 → 받는다
    const string NoPin   = "ZNTF02";   // PIN 없음 → 받지 않는다
    const string TokenA  = "itest-ntf-token-A";
    const string TokenB  = "itest-ntf-token-B";
    const string TokenC  = "itest-ntf-token-C";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.SYS_NotificationHistory WHERE RecipientUserID IN (@W, @N) OR Address IN (@A, @B, @C);
        DELETE dbo.SYS_NotificationChannel WHERE UserID IN (@W, @N) OR Address IN (@A, @B, @C);
        DELETE dbo.SYS_NotificationRule WHERE CreatedBy = @By;
        DELETE dbo.MNT_FailureRegister WHERE FailureNumber LIKE 'FAIL-ZNTF-%';
        DELETE dbo.MD_Worker WHERE EmployeeNo IN (@W, @N);
        """, ("@W", Worker), ("@N", NoPin), ("@A", TokenA), ("@B", TokenB), ("@C", TokenC), ("@By", Actor));

    static void NewFailure(Connection.AmesConnectionFactory f, string no) => Exec(f, """
        INSERT dbo.MNT_FailureRegister (FailureNumber, FailureType, Symptom, Severity, Source, Status, ReportedAt, CreatedBy, CreatedTS)
        VALUES (@No, 'ITEST', N'itest symptom', 'MAJOR', 'OPERATOR', 'OPEN', SYSDATETIME(), @By, SYSDATETIME());
        """, ("@No", no), ("@By", Actor));

    static int Rows(Connection.AmesConnectionFactory f, string emp, string refId, string? status = null)
        => Convert.ToInt32(Scalar(f, """
            SELECT COUNT(*) FROM dbo.SYS_NotificationHistory
            WHERE RecipientUserID = @E AND SourceRefID = @R AND Channel = 'PUSH' AND (@S IS NULL OR Status = @S)
            """, ("@E", emp), ("@R", refId), ("@S", (object?)status ?? DBNull.Value)));

    [SkippableFact]
    public void Operator_rule_reaches_pin_workers_once_per_device_and_app_inbox_follows()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        Skip.If(Scalar(f!, "SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='NOTIFICATION_CHANNEL' AND CodeValue='PUSH' AND ISNULL(UseFlag,1)=1") is null,
            "NOTIFICATION_CHANNEL PUSH is not in use");
        var repo = new NotificationRepository(f!);
        try
        {
            Cleanup(f!);
            Exec(f!, """
                INSERT dbo.MD_Worker (EmployeeNo, EmployeeName, PinHash, ActiveFlag, CreatedBy, CreatedTS) VALUES
                    (@W, N'itest pin', 'itest-hash', 1, @By, SYSDATETIME()),
                    (@N, N'itest nopin', NULL, 1, @By, SYSDATETIME());
                INSERT dbo.SYS_NotificationRule (EventTypeCode, EventName, ModuleCode, IsEnabled, ChannelsJSON, RecipientRolesJSON, CreatedBy, CreatedTS)
                VALUES ('FAILURE_OPEN', N'itest failure', 'MNT', 1, N'["PUSH"]', N'["ROLE-OPERATOR"]', @By, SYSDATETIME());
                """, ("@W", Worker), ("@N", NoPin), ("@By", Actor));
            repo.RegisterDevice(Worker, TokenA, Actor);
            repo.RegisterDevice(NoPin, TokenB, Actor);
            NewFailure(f!, "FAIL-ZNTF-001");

            repo.EnqueuePush(60);
            Assert.Equal(1, Rows(f!, Worker, "FAIL-ZNTF-001", NotifyRules.Status.Pending));
            Assert.Equal(0, Rows(f!, NoPin, "FAIL-ZNTF-001"));          // PIN 없는 작업자는 받지 않는다

            repo.EnqueuePush(60);                                        // 같은 이벤트는 다시 만들지 않는다
            Assert.Equal(1, Rows(f!, Worker, "FAIL-ZNTF-001"));

            // 방해 금지 시간 — 지금을 덮으면 QUIET(보내지 않고 목록에만)
            Exec(f!, """
                UPDATE dbo.SYS_NotificationChannel
                SET QuietHoursStart = DATEADD(HOUR, -1, CAST(SYSDATETIME() AS time)), QuietHoursEnd = DATEADD(HOUR, 1, CAST(SYSDATETIME() AS time))
                WHERE UserID = @W AND Address = @A;
                """, ("@W", Worker), ("@A", TokenA));
            NewFailure(f!, "FAIL-ZNTF-002");
            repo.EnqueuePush(60);
            Assert.Equal(1, Rows(f!, Worker, "FAIL-ZNTF-002", NotifyRules.Status.Quiet));

            // 발송 — PENDING 만 잡히고 SENDING 으로 바뀐다(QUIET 는 잡지 않는다)
            var due = repo.ClaimDue(3, 100).Where(i => i.Token == TokenA).ToList();
            var item = Assert.Single(due);
            Assert.Equal("FAIL-ZNTF-001", item.SourceRefId);
            Assert.DoesNotContain(repo.ClaimDue(3, 100), i => i.Token == TokenA);   // 이미 잡힌 행은 다시 안 잡힌다
            repo.MarkSent(item.Id);
            Assert.Equal(1, Rows(f!, Worker, "FAIL-ZNTF-001", NotifyRules.Status.Sent));

            // 앱 — 목록·상세·읽음·안 읽은 수, 다른 사번은 못 봄
            var inbox = repo.ListInbox(Worker, unreadOnly: false, beforeId: null, take: 50);
            Assert.Contains(inbox, r => r.SourceRefId == "FAIL-ZNTF-001");
            Assert.Contains(inbox, r => r.SourceRefId == "FAIL-ZNTF-002");
            var unreadBefore = repo.CountUnread(Worker);
            Assert.NotNull(repo.GetInboxItem(Worker, item.Id));
            Assert.Null(repo.GetInboxItem(NoPin, item.Id));
            Assert.False(repo.MarkRead(NoPin, item.Id, Actor));
            Assert.True(repo.MarkRead(Worker, item.Id, Actor));
            Assert.Equal(unreadBefore - 1, repo.CountUnread(Worker));
            Assert.DoesNotContain(repo.ListInbox(Worker, unreadOnly: true, beforeId: null, take: 50), r => r.SourceRefId == "FAIL-ZNTF-001");

            // 토큰 무효 — 영구 실패로 기록하고 그 기기를 끈다
            Exec(f!, "UPDATE dbo.SYS_NotificationChannel SET QuietHoursStart = NULL, QuietHoursEnd = NULL WHERE UserID = @W;", ("@W", Worker));
            NewFailure(f!, "FAIL-ZNTF-003");
            repo.EnqueuePush(60);
            var third = Assert.Single(repo.ClaimDue(3, 100), i => i.Token == TokenA);
            repo.MarkFailed(third.Id, "FCM 404: UNREGISTERED", permanent: true, maxRetry: 3, disableToken: TokenA);
            Assert.Equal(1, Rows(f!, Worker, "FAIL-ZNTF-003", NotifyRules.Status.Failed));
            Assert.Equal(false, Scalar(f!, "SELECT IsEnabled FROM dbo.SYS_NotificationChannel WHERE UserID = @W AND Address = @A",
                ("@W", Worker), ("@A", TokenA)));
            Assert.DoesNotContain(repo.ClaimDue(3, 100), i => i.Token == TokenA);   // 재시도 횟수를 상한까지 올려 다시 안 잡힌다

            // 공용 기기 — 다른 사번이 같은 토큰을 등록하면 옮겨 간다, 다시 켜기
            repo.RegisterDevice(NoPin, TokenA, Actor);
            Assert.Null(Scalar(f!, "SELECT 1 FROM dbo.SYS_NotificationChannel WHERE UserID = @W AND Address = @A", ("@W", Worker), ("@A", TokenA)));
            repo.RegisterDevice(Worker, TokenA, Actor);
            Assert.Equal(true, Scalar(f!, "SELECT IsEnabled FROM dbo.SYS_NotificationChannel WHERE UserID = @W AND Address = @A",
                ("@W", Worker), ("@A", TokenA)));
            Assert.True(repo.DisableDevice(Worker, TokenA, Actor));
            Assert.False(repo.DisableDevice(Worker, "itest-ntf-no-such-token", Actor));
        }
        finally { Cleanup(f!); }
    }

    // 원천 5종의 조회 SQL 이 실제 스키마에서 돈다 — DB 에 있는 행 하나씩(없는 종류는 건너뜀). 읽기만 한다.
    [SkippableFact]
    public void Source_queries_run_against_the_real_schema()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new NotificationRepository(f!);
        var refs = new (string Type, string Sql)[]
        {
            ("FAILURE",    "SELECT TOP (1) FailureNumber FROM dbo.MNT_FailureRegister WHERE NULLIF(FailureNumber, '') IS NOT NULL ORDER BY FailureID DESC"),
            ("ANDON",      "SELECT TOP (1) CAST(AndonID AS varchar(40)) FROM dbo.PR_AndonCall ORDER BY AndonID DESC"),
            ("QC_HOLD",    "SELECT TOP (1) COALESCE(NULLIF(HoldNumber, ''), CAST(HoldID AS varchar(40))) FROM dbo.QC_Hold ORDER BY HoldID DESC"),
            ("PM",         "SELECT TOP (1) PMPlanNumber + '@20261024' FROM dbo.MNT_PMSchedule WHERE NULLIF(PMPlanNumber, '') IS NOT NULL ORDER BY PMScheduleID DESC"),
            ("SPARE_PART", "SELECT TOP (1) SparePartNo + '@20261024' FROM dbo.MD_SparePart ORDER BY SparePartNo"),
        };
        var read = 0;
        foreach (var (type, sql) in refs)
        {
            Assert.Null(repo.ReadSource(type, "ZNTF-NO-SUCH-REF"));                       // 행이 없는 종류도 SQL 은 돌린다
            if (Scalar(f!, sql) is not string id) continue;
            var src = repo.ReadSource(type, id);
            Assert.NotNull(src);
            Assert.Equal(type, src!.RefType);
            Assert.DoesNotContain("@", src.RefId);
            Assert.NotEmpty(src.Fields);
            read++;
        }
        Assert.True(read > 0, "no source rows in this DB");
    }

    // 10-24 앱 — 방해 금지 시간은 사번의 기기 전부에 쓰고 새 기기가 이어받는다. 상세는 원천의 지금 상태(고장 상태·증상·보고자 이름)를 붙인다.
    [SkippableFact]
    public void Quiet_hours_are_per_employee_and_detail_carries_the_current_source_state()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var repo = new NotificationRepository(f!);
        try
        {
            Cleanup(f!);
            Exec(f!, "INSERT dbo.MD_Worker (EmployeeNo, EmployeeName, PinHash, ActiveFlag, CreatedBy, CreatedTS) VALUES (@W, N'itest pin', 'itest-hash', 1, @By, SYSDATETIME());",
                ("@W", Worker), ("@By", Actor));

            // 기기가 없으면 쓸 곳이 없다
            Assert.Equal(0, repo.SetQuietHours(Worker, new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), Actor));
            Assert.Equal(new NotificationRepository.QuietHours(null, null, 0), repo.GetQuietHours(Worker));

            repo.RegisterDevice(Worker, TokenA, Actor);
            Assert.Equal(1, repo.SetQuietHours(Worker, new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), Actor));
            repo.RegisterDevice(Worker, TokenC, Actor);                                    // 새 기기는 이어받는다
            Assert.Equal(new TimeSpan(22, 0, 0), Scalar(f!, "SELECT QuietHoursStart FROM dbo.SYS_NotificationChannel WHERE UserID = @W AND Address = @C",
                ("@W", Worker), ("@C", TokenC)));
            Assert.Equal(new NotificationRepository.QuietHours(new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), 2), repo.GetQuietHours(Worker));
            Assert.Equal(2, repo.SetQuietHours(Worker, null, null, Actor));                 // 해제는 기기 전부
            Assert.Equal(new NotificationRepository.QuietHours(null, null, 2), repo.GetQuietHours(Worker));

            // 상세 — 고장 원천의 지금 상태
            Exec(f!, """
                INSERT dbo.MNT_FailureRegister (FailureNumber, FailureType, Symptom, Severity, Source, Status, ReportedBy, ReportedAt, CreatedBy, CreatedTS)
                VALUES ('FAIL-ZNTF-010', 'ITEST', N'itest noise', 'MAJOR', 'OPERATOR', 'OPEN', @W, SYSDATETIME(), @By, SYSDATETIME());
                INSERT dbo.SYS_NotificationHistory (EventTypeCode, RecipientUserID, Channel, Address, Subject, SourceRefType, SourceRefID, Status, RetryCount, CreatedBy, CreatedTS)
                VALUES ('FAILURE_OPEN', @W, 'PUSH', @A, N'itest', 'FAILURE', 'FAIL-ZNTF-010', 'SENT', 0, @By, SYSDATETIME()),
                       ('LOW_STOCK',    @W, 'PUSH', @A, N'itest', 'SPARE_PART', 'ZNTF-NO-PART@20261024', 'SENT', 0, @By, SYSDATETIME());
                """, ("@W", Worker), ("@A", TokenA), ("@By", Actor));
            var failId = Convert.ToInt64(Scalar(f!, "SELECT NotificationHistoryID FROM dbo.SYS_NotificationHistory WHERE RecipientUserID = @W AND SourceRefID = 'FAIL-ZNTF-010'", ("@W", Worker)));
            var d = repo.GetInboxDetail(Worker, failId);
            Assert.NotNull(d?.Source);
            Assert.Equal("OPEN", d!.Source!.Status);
            Assert.Equal("MAJOR", d.Source.Severity);
            Assert.Equal("itest noise", d.Source.Fields.Single(x => x.Key == "symptom").Value);
            Assert.Equal($"itest pin ({Worker})", d.Source.Fields.Single(x => x.Key == "reportedBy").Value);
            Assert.Null(d.Source.ClosedAt);

            Exec(f!, "UPDATE dbo.MNT_FailureRegister SET Status = 'RESOLVED', ResolvedAt = SYSDATETIME() WHERE FailureNumber = 'FAIL-ZNTF-010';");
            d = repo.GetInboxDetail(Worker, failId);                                       // 알림 뒤에 바뀐 상태가 보인다
            Assert.Equal("RESOLVED", d!.Source!.Status);
            Assert.NotNull(d.Source.ClosedAt);

            // 원천이 없거나(@날짜 접미사는 떼고 찾음) 남의 알림이면
            var stockId = Convert.ToInt64(Scalar(f!, "SELECT NotificationHistoryID FROM dbo.SYS_NotificationHistory WHERE RecipientUserID = @W AND SourceRefType = 'SPARE_PART'", ("@W", Worker)));
            Assert.Null(repo.GetInboxDetail(Worker, stockId)!.Source);
            Assert.Null(repo.GetInboxDetail(NoPin, failId));
            Assert.Null(repo.ReadSource("UNKNOWN", "X"));
        }
        finally { Cleanup(f!); }
    }
}
