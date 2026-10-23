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

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.SYS_NotificationHistory WHERE RecipientUserID IN (@W, @N) OR Address IN (@A, @B);
        DELETE dbo.SYS_NotificationChannel WHERE UserID IN (@W, @N) OR Address IN (@A, @B);
        DELETE dbo.SYS_NotificationRule WHERE CreatedBy = @By;
        DELETE dbo.MNT_FailureRegister WHERE FailureNumber LIKE 'FAIL-ZNTF-%';
        DELETE dbo.MD_Worker WHERE EmployeeNo IN (@W, @N);
        """, ("@W", Worker), ("@N", NoPin), ("@A", TokenA), ("@B", TokenB), ("@By", Actor));

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
}
