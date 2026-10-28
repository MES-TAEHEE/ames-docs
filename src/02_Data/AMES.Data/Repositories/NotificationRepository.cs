using System.Data;
using AMES.Data.Connection;
using AMES.Data.Services;
using AMES.Data.Services.Notify;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// PUSH 알림 — 발송기(AMES.Api 「NOTIFY」 Worker)의 감지·발송 이력과 안드로이드 앱의 기기 등록·알림 조회. 규칙은 <see cref="NotifyRules"/>.
/// 수신자 키는 사번이다(SYS_NotificationChannel.UserID·SYS_NotificationHistory.RecipientUserID 에 사번을 넣는다).
/// </summary>
public sealed class NotificationRepository(AmesConnectionFactory f)
{
    public sealed record EnqueueResult(int Events, int Created, int Quiet);
    public sealed record PushItem(long Id, string Token, string? Subject, string? Body,
        string? EventTypeCode, string? SourceRefType, string? SourceRefId, int RetryCount);
    public sealed record InboxRow(long Id, string? EventTypeCode, string? EventName, string? Subject, string? Body,
        string? SourceRefType, string? SourceRefId, string? Status, DateTime? CreatedTs, DateTime? SentAt, DateTime? ReadAt);
    /// <summary>앱 상세 — 알림 행 + 원천의 지금 상태(원천이 지워졌거나 모르는 종류면 Source null).</summary>
    public sealed record InboxDetail(long Id, string? EventTypeCode, string? EventName, string? Subject, string? Body,
        string? SourceRefType, string? SourceRefId, string? Status, DateTime? CreatedTs, DateTime? SentAt, DateTime? ReadAt,
        NotifySource? Source);
    /// <summary>
    /// 알림 원천의 지금 상태. 공통 칸은 원천마다 있는 것만 채우고, 종류별 항목은 Fields(키는 영어 camelCase 고정 — 앱이 이름을 번역한다).
    /// Status 는 원천 테이블의 상태 코드 그대로(PM 은 예정일로 계산한 OK·DUE·OVERDUE·CANCELLED, 예비품은 LOW·OK·INACTIVE).
    /// </summary>
    public sealed record NotifySource(string RefType, string RefId, string? Status, DateTime? OccurredAt, DateTime? ClosedAt,
        string? LineId, string? LineName, string? EquipId, string? EquipName, string? ItemNo, string? ItemName, string? Severity,
        IReadOnlyList<SourceField> Fields);
    public sealed record SourceField(string Key, string? Value);
    /// <summary>방해 금지 시간(그 사번의 PUSH 기기 중 가장 최근에 바뀐 행 기준)과 사용 중인 기기 수.</summary>
    public sealed record QuietHours(TimeSpan? Start, TimeSpan? End, int Devices);

    // 이벤트 → 규칙(PUSH 채널·사용) → 수신 역할의 사번 → 사용 중인 PUSH 기기 → 이력 PENDING/QUIET.
    // 같은 (이벤트, 원천, 수신자, 기기) 이력이 있으면 다시 만들지 않는다. 여러 Api(개발서버·로컬 IIS)가 같은 DB 를 쓰므로 applock 으로 한 번에 하나.
    const string EnqueueSql = """
        SET XACT_ABORT ON;
        DECLARE @now datetime2(7) = SYSDATETIME();
        DECLARE @today date = CAST(@now AS date), @nowT time = CAST(@now AS time);
        DECLARE @from datetime2(7) = DATEADD(MINUTE, -@Lookback, @now);
        DECLARE @day char(8) = CONVERT(char(8), @today, 112);

        IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'NOTIFICATION_CHANNEL' AND CodeValue = @Ch AND ISNULL(UseFlag, 1) = 1)
        BEGIN
            SELECT 0 AS Events, 0 AS Created, 0 AS Quiet;
            RETURN;
        END

        -- 임시 테이블 문자열 열은 DB 콜레이션으로(tempdb 콜레이션이 DB 와 다른 서버에서 조인 충돌 — 10-29)
        CREATE TABLE #ev (EventTypeCode varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, RefType varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
                          RefID varchar(40) COLLATE DATABASE_DEFAULT NOT NULL, Subject nvarchar(200) COLLATE DATABASE_DEFAULT NULL,
                          Body nvarchar(1000) COLLATE DATABASE_DEFAULT NULL);

        INSERT #ev
        SELECT 'FAILURE_OPEN', 'FAILURE', f.FailureNumber,
               LEFT(CONCAT(N'Equipment failure ', f.FailureNumber), 200),
               CONCAT(N'Equipment: ', f.EquipID, N' ', e.EquipName, CHAR(10), N'Severity: ', f.Severity, CHAR(10),
                      N'Symptom: ', f.Symptom, CHAR(10), N'Reported: ', CONVERT(varchar(16), f.ReportedAt, 120))
        FROM   dbo.MNT_FailureRegister f
        LEFT JOIN dbo.MD_Equipment e ON e.EquipID = f.EquipID
        WHERE  f.ReportedAt >= @from AND f.Status IN ('OPEN', 'REGISTERED', 'IN_PROGRESS') AND NULLIF(f.FailureNumber, '') IS NOT NULL
        UNION ALL
        SELECT 'ANDON_RAISED', 'ANDON', CAST(a.AndonID AS varchar(40)),
               LEFT(CONCAT(N'Andon call ', a.LineID), 200),
               CONCAT(N'Line: ', a.LineID, N' ', l.LineName, CHAR(10), N'Equipment: ', a.EquipID, CHAR(10),
                      N'Severity: ', a.Severity, CHAR(10), N'Raised: ', CONVERT(varchar(16), a.TriggeredAt, 120))
        FROM   dbo.PR_AndonCall a
        LEFT JOIN dbo.MD_Line l ON l.LineID = a.LineID
        WHERE  a.TriggeredAt >= @from AND ISNULL(a.Status, '') NOT IN ('RESOLVED', 'CLOSED')
        UNION ALL
        SELECT 'QC_HOLD', 'QC_HOLD', COALESCE(NULLIF(h.HoldNumber, ''), CAST(h.HoldID AS varchar(40))),
               LEFT(CONCAT(N'QC hold ', COALESCE(NULLIF(h.HoldNumber, ''), CAST(h.HoldID AS varchar(40)))), 200),
               CONCAT(N'Item: ', h.ItemNo, CHAR(10), N'Qty: ', h.HeldQty, CHAR(10), N'Severity: ', h.Severity, CHAR(10),
                      N'Held: ', CONVERT(varchar(16), h.HeldAt, 120))
        FROM   dbo.QC_Hold h
        WHERE  h.HeldAt >= @from AND h.Status = 'Held'
        UNION ALL
        SELECT 'PM_OVERDUE', 'PM', LEFT(p.PMPlanNumber, 31) + '@' + @day,
               LEFT(CONCAT(N'PM overdue ', p.PMPlanNumber), 200),
               CONCAT(N'Equipment: ', p.EquipID, N' ', e.EquipName, CHAR(10), N'PM: ', p.PMClass, N' ', p.PMType, CHAR(10),
                      N'Due: ', CONVERT(varchar(10), p.NextDueDate, 120))
        FROM   dbo.MNT_PMSchedule p
        LEFT JOIN dbo.MD_Equipment e ON e.EquipID = p.EquipID
        WHERE  p.NextDueDate < @today AND ISNULL(p.Status, '') <> 'CANCELLED' AND NULLIF(p.PMPlanNumber, '') IS NOT NULL
        UNION ALL
        SELECT 'LOW_STOCK', 'SPARE_PART', LEFT(s.SparePartNo, 31) + '@' + @day,
               LEFT(CONCAT(N'Low stock ', s.SparePartNo), 200),
               CONCAT(N'Part: ', s.PartNo, N' ', s.PartName, CHAR(10), N'On hand: ', s.OnHandQty, N' ', s.UOM, CHAR(10),
                      N'Reorder point: ', s.ReorderPoint)
        FROM   dbo.MD_SparePart s
        WHERE  ISNULL(s.ActiveFlag, 1) = 1 AND s.ReorderPoint > 0 AND s.OnHandQty <= s.ReorderPoint;

        DECLARE @events int = (SELECT COUNT(*) FROM #ev);

        -- 규칙 × 수신 역할(PUSH 채널이 있는 사용 중 규칙만)
        CREATE TABLE #rr (RuleID int NOT NULL, EventTypeCode varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, RoleID nvarchar(450) COLLATE DATABASE_DEFAULT NOT NULL);
        INSERT #rr
        SELECT r.NotificationRuleID, r.EventTypeCode, CAST(j.value AS nvarchar(450))
        FROM   dbo.SYS_NotificationRule r
        CROSS APPLY OPENJSON(CASE WHEN ISJSON(r.RecipientRolesJSON) = 1 THEN r.RecipientRolesJSON ELSE '[]' END) j
        WHERE  ISNULL(r.IsEnabled, 0) = 1 AND ISJSON(r.ChannelsJSON) = 1
          AND  EXISTS (SELECT 1 FROM OPENJSON(r.ChannelsJSON) c WHERE CAST(c.value AS varchar(10)) = @Ch);

        -- 수신자(사번): 역할을 가진 활성 웹 사용자 + Operator 규칙이면 PIN 이 있는 활성 작업자(같은 사번의 웹 계정이 있으면 웹 계정 쪽)
        CREATE TABLE #rcp (RuleID int NOT NULL, EventTypeCode varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, EmpNo varchar(20) COLLATE DATABASE_DEFAULT NOT NULL);
        INSERT #rcp
        SELECT DISTINCT rr.RuleID, rr.EventTypeCode, p.EmployeeNo
        FROM   #rr rr
        JOIN   dbo.AspNetUserRoles ur ON ur.RoleId = rr.RoleID
        JOIN   dbo.SYS_UserProfile p  ON p.UserID = ur.UserId
        WHERE  p.AccountStatus = 'ACTIVE' AND NULLIF(LTRIM(RTRIM(p.EmployeeNo)), '') IS NOT NULL
        UNION
        SELECT rr.RuleID, rr.EventTypeCode, w.EmployeeNo
        FROM   #rr rr
        JOIN   dbo.MD_Worker w ON rr.RoleID = @WorkerRole
        WHERE  ISNULL(w.ActiveFlag, 1) = 1 AND NULLIF(w.PinHash, '') IS NOT NULL
          AND  NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile p2 WHERE p2.EmployeeNo = w.EmployeeNo);

        BEGIN TRAN;
        EXEC sp_getapplock @Resource = 'NOTIFY_ENQUEUE', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;

        ;WITH c AS (
            SELECT ev.EventTypeCode, ev.RefType, ev.RefID, ev.Subject, ev.Body, x.EmpNo, ch.Address,
                   MIN(x.RuleID) AS RuleID,
                   MAX(CASE WHEN ch.QuietHoursStart IS NOT NULL AND ch.QuietHoursEnd IS NOT NULL AND ch.QuietHoursStart <> ch.QuietHoursEnd
                             AND ((ch.QuietHoursStart < ch.QuietHoursEnd AND @nowT >= ch.QuietHoursStart AND @nowT < ch.QuietHoursEnd)
                               OR (ch.QuietHoursStart > ch.QuietHoursEnd AND (@nowT >= ch.QuietHoursStart OR @nowT < ch.QuietHoursEnd)))
                            THEN 1 ELSE 0 END) AS Quiet
            FROM   #ev ev
            JOIN   #rcp x ON x.EventTypeCode = ev.EventTypeCode
            JOIN   dbo.SYS_NotificationChannel ch ON ch.UserID = x.EmpNo AND ch.Channel = @Ch AND ISNULL(ch.IsEnabled, 0) = 1
                                                 AND NULLIF(ch.Address, '') IS NOT NULL
            GROUP  BY ev.EventTypeCode, ev.RefType, ev.RefID, ev.Subject, ev.Body, x.EmpNo, ch.Address
        )
        INSERT dbo.SYS_NotificationHistory
               (NotificationRuleID, EventTypeCode, RecipientUserID, Channel, Address, Subject, Body,
                SourceRefType, SourceRefID, Status, RetryCount, CreatedBy, CreatedTS)
        SELECT c.RuleID, c.EventTypeCode, c.EmpNo, @Ch, c.Address, c.Subject, c.Body, c.RefType, c.RefID,
               CASE WHEN c.Quiet = 1 THEN @Quiet ELSE @Pending END, 0, @Actor, @now
        FROM   c
        WHERE  NOT EXISTS (SELECT 1 FROM dbo.SYS_NotificationHistory h
                           WHERE h.EventTypeCode = c.EventTypeCode AND h.SourceRefType = c.RefType AND h.SourceRefID = c.RefID
                             AND h.RecipientUserID = c.EmpNo AND h.Channel = @Ch AND h.Address = c.Address);
        DECLARE @created int = @@ROWCOUNT;
        DECLARE @quietN int = (SELECT COUNT(*) FROM dbo.SYS_NotificationHistory WHERE CreatedTS = @now AND CreatedBy = @Actor AND Status = @Quiet);
        COMMIT;

        SELECT @events AS Events, @created AS Created, @quietN AS Quiet;
        """;

    public EnqueueResult EnqueuePush(int lookbackMin, string actor = NotifyRules.Actor)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(EnqueueSql, conn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@Lookback", SqlDbType.Int).Value = lookbackMin;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        cmd.Parameters.Add("@WorkerRole", SqlDbType.NVarChar, 450).Value = NotifyRules.WorkerRoleId;
        cmd.Parameters.Add("@Pending", SqlDbType.VarChar, 15).Value = NotifyRules.Status.Pending;
        cmd.Parameters.Add("@Quiet", SqlDbType.VarChar, 15).Value = NotifyRules.Status.Quiet;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? new((int)rdr["Events"], (int)rdr["Created"], (int)rdr["Quiet"]) : new(0, 0, 0);
    }

    /// <summary>
    /// 보낼 행을 잡는다 — PENDING, 기다린 시간이 지난 FAILED(재시도 남음), 10분 넘게 SENDING 인 행(발송 중 멈춘 것). 하루 지난 행은 보내지 않는다.
    /// 잡은 행은 SENDING 으로 바꿔 다른 Api 인스턴스가 같은 행을 다시 잡지 않게 한다(READPAST).
    /// </summary>
    public List<PushItem> ClaimDue(int maxRetry, int take, string actor = NotifyRules.Actor)
    {
        const string sql = """
            UPDATE TOP (@Take) h
            SET    Status = @Sending, ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
            OUTPUT inserted.NotificationHistoryID, inserted.Address, inserted.Subject, inserted.Body,
                   inserted.EventTypeCode, inserted.SourceRefType, inserted.SourceRefID, inserted.RetryCount
            FROM   dbo.SYS_NotificationHistory h WITH (READPAST, UPDLOCK, ROWLOCK)
            WHERE  h.Channel = @Ch AND h.CreatedTS >= DATEADD(DAY, -1, SYSDATETIME()) AND NULLIF(h.Address, '') IS NOT NULL
              AND  (h.Status = @Pending
                    OR (h.Status = @Failed AND ISNULL(h.RetryCount, 0) < @MaxRetry
                        AND ISNULL(h.ModifiedTS, h.CreatedTS) <= DATEADD(MINUTE, -CASE WHEN ISNULL(h.RetryCount, 0) >= 5 THEN 30
                                                                                         ELSE POWER(2, ISNULL(h.RetryCount, 0)) END, SYSDATETIME()))
                    OR (h.Status = @Sending AND ISNULL(h.ModifiedTS, h.CreatedTS) < DATEADD(MINUTE, -10, SYSDATETIME())));
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        cmd.Parameters.Add("@MaxRetry", SqlDbType.Int).Value = maxRetry;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        cmd.Parameters.Add("@Pending", SqlDbType.VarChar, 15).Value = NotifyRules.Status.Pending;
        cmd.Parameters.Add("@Failed", SqlDbType.VarChar, 15).Value = NotifyRules.Status.Failed;
        cmd.Parameters.Add("@Sending", SqlDbType.VarChar, 15).Value = NotifyRules.Status.Sending;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        var list = new List<PushItem>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            list.Add(new(Convert.ToInt64(rdr["NotificationHistoryID"]), (string)rdr["Address"], rdr["Subject"] as string,
                rdr["Body"] as string, rdr["EventTypeCode"] as string, rdr["SourceRefType"] as string,
                rdr["SourceRefID"] as string, rdr["RetryCount"] as int? ?? 0));
        return list;
    }

    public void MarkSent(long id, string actor = NotifyRules.Actor)
    {
        const string sql = """
            UPDATE dbo.SYS_NotificationHistory
            SET    Status = @Sent, SentAt = SYSDATETIME(), ErrorMsg = NULL, ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
            WHERE  NotificationHistoryID = @Id;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@Sent", SqlDbType.VarChar, 15).Value = NotifyRules.Status.Sent;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        cmd.ExecuteNonQuery();
    }

    /// <summary>실패 기록. permanent(토큰 무효 등)면 재시도하지 않게 횟수를 상한으로 올리고, disableToken 이면 그 토큰의 기기 행을 모두 끈다.</summary>
    public void MarkFailed(long id, string error, bool permanent, int maxRetry, string? disableToken, string actor = NotifyRules.Actor)
    {
        const string sql = """
            UPDATE dbo.SYS_NotificationHistory
            SET    Status = @Failed,
                   RetryCount = CASE WHEN @Permanent = 1 THEN CASE WHEN @Max > ISNULL(RetryCount, 0) + 1 THEN @Max ELSE ISNULL(RetryCount, 0) + 1 END
                                     ELSE ISNULL(RetryCount, 0) + 1 END,
                   ErrorMsg = @Err, ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
            WHERE  NotificationHistoryID = @Id;
            IF @Token IS NOT NULL
                UPDATE dbo.SYS_NotificationChannel
                SET    IsEnabled = 0, ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
                WHERE  Channel = @Ch AND Address = @Token AND ISNULL(IsEnabled, 0) = 1;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@Failed", SqlDbType.VarChar, 15).Value = NotifyRules.Status.Failed;
        cmd.Parameters.Add("@Permanent", SqlDbType.Bit).Value = permanent;
        cmd.Parameters.Add("@Max", SqlDbType.Int).Value = maxRetry;
        cmd.Parameters.Add("@Err", SqlDbType.NVarChar, 500).Value = error.Length > 500 ? error[..500] : error;
        cmd.Parameters.Add("@Token", SqlDbType.VarChar, 255).Value = (object?)disableToken ?? DBNull.Value;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        cmd.ExecuteNonQuery();
    }

    // ── 앱 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 기기 등록 — 같은 토큰이 다른 사번에 붙어 있으면 지우고(공용 기기는 마지막에 로그인한 사람이 받는다) 이 사번 행을 켜거나 만든다.
    /// 새 행은 그 사번의 다른 기기에 둔 방해 금지 시간을 이어받는다(방해 금지는 사람 단위 설정).
    /// </summary>
    public void RegisterDevice(string employeeNo, string token, string actor)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DELETE dbo.SYS_NotificationChannel WHERE Channel = @Ch AND Address = @Token AND UserID <> @Emp;
            UPDATE dbo.SYS_NotificationChannel WITH (UPDLOCK, HOLDLOCK)
            SET    IsEnabled = 1, VerifiedAt = SYSDATETIME(), ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
            WHERE  Channel = @Ch AND Address = @Token AND UserID = @Emp;
            IF @@ROWCOUNT = 0
                INSERT dbo.SYS_NotificationChannel (UserID, Channel, Address, IsEnabled, QuietHoursStart, QuietHoursEnd, VerifiedAt, CreatedBy, CreatedTS)
                SELECT @Emp, @Ch, @Token, 1, q.QuietHoursStart, q.QuietHoursEnd, SYSDATETIME(), @Actor, SYSDATETIME()
                FROM   (SELECT 1 AS one) d
                OUTER APPLY (SELECT TOP (1) c.QuietHoursStart, c.QuietHoursEnd FROM dbo.SYS_NotificationChannel c
                             WHERE c.UserID = @Emp AND c.Channel = @Ch
                             ORDER BY ISNULL(c.ModifiedTS, c.CreatedTS) DESC, c.NotificationChannelID DESC) q;
            COMMIT;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 450).Value = employeeNo;
        cmd.Parameters.Add("@Token", SqlDbType.VarChar, 255).Value = token;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        cmd.ExecuteNonQuery();
    }

    /// <summary>앱 「알림 끄기」 — 이 사번의 그 기기 행만 끈다. 없으면 false.</summary>
    public bool DisableDevice(string employeeNo, string token, string actor)
    {
        const string sql = """
            UPDATE dbo.SYS_NotificationChannel
            SET    IsEnabled = 0, ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
            WHERE  Channel = @Ch AND Address = @Token AND UserID = @Emp;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 450).Value = employeeNo;
        cmd.Parameters.Add("@Token", SqlDbType.VarChar, 255).Value = token;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        return cmd.ExecuteNonQuery() > 0;
    }

    // 기기마다 한 행이라 앱 목록은 (이벤트, 원천)마다 가장 최근 행 하나로 묶는다. 읽음은 묶음 중 하나라도 읽었으면 읽음.
    const string InboxCte = """
        ;WITH g AS (
            SELECT h.NotificationHistoryID, h.EventTypeCode, h.Subject, h.Body, h.SourceRefType, h.SourceRefID, h.Status,
                   h.CreatedTS, h.SentAt,
                   MAX(h.ReadAt) OVER (PARTITION BY h.EventTypeCode, h.SourceRefType, h.SourceRefID) AS ReadAt,
                   ROW_NUMBER() OVER (PARTITION BY h.EventTypeCode, h.SourceRefType, h.SourceRefID
                                      ORDER BY h.NotificationHistoryID DESC) AS rn
            FROM   dbo.SYS_NotificationHistory h
            WHERE  h.RecipientUserID = @Emp AND h.Channel = @Ch
        )
        """;

    public List<InboxRow> ListInbox(string employeeNo, bool unreadOnly, long? beforeId, int take)
    {
        var sql = InboxCte + """
            SELECT TOP (@Take) g.NotificationHistoryID, g.EventTypeCode, r.EventName, g.Subject, g.Body, g.SourceRefType, g.SourceRefID,
                   g.Status, g.CreatedTS, g.SentAt, g.ReadAt
            FROM   g
            OUTER APPLY (SELECT TOP (1) EventName FROM dbo.SYS_NotificationRule WHERE EventTypeCode = g.EventTypeCode
                         ORDER BY NotificationRuleID) r
            WHERE  g.rn = 1 AND (@Unread = 0 OR g.ReadAt IS NULL) AND (@Before IS NULL OR g.NotificationHistoryID < @Before)
            ORDER  BY g.NotificationHistoryID DESC;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 450).Value = employeeNo;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        cmd.Parameters.Add("@Take", SqlDbType.Int).Value = Math.Clamp(take, 1, 100);
        cmd.Parameters.Add("@Unread", SqlDbType.Bit).Value = unreadOnly;
        cmd.Parameters.Add("@Before", SqlDbType.BigInt).Value = (object?)beforeId ?? DBNull.Value;
        return ReadInbox(cmd);
    }

    /// <summary>상세 — 그 사번의 행일 때만. 다른 사람 행이나 없는 번호면 null.</summary>
    public InboxRow? GetInboxItem(string employeeNo, long id)
    {
        const string sql = """
            SELECT h.NotificationHistoryID, h.EventTypeCode, r.EventName, h.Subject, h.Body, h.SourceRefType, h.SourceRefID,
                   h.Status, h.CreatedTS, h.SentAt,
                   (SELECT MAX(x.ReadAt) FROM dbo.SYS_NotificationHistory x
                    WHERE x.RecipientUserID = h.RecipientUserID AND x.Channel = h.Channel AND x.EventTypeCode = h.EventTypeCode
                      AND x.SourceRefType = h.SourceRefType AND x.SourceRefID = h.SourceRefID) AS ReadAt
            FROM   dbo.SYS_NotificationHistory h
            OUTER APPLY (SELECT TOP (1) EventName FROM dbo.SYS_NotificationRule WHERE EventTypeCode = h.EventTypeCode
                         ORDER BY NotificationRuleID) r
            WHERE  h.NotificationHistoryID = @Id AND h.RecipientUserID = @Emp AND h.Channel = @Ch;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 450).Value = employeeNo;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        return ReadInbox(cmd).FirstOrDefault();
    }

    /// <summary>읽음 — 그 알림과 같은 (이벤트, 원천)의 이 사번 행을 모두 읽음으로(기기가 여럿이어도 한 번). 그 사번 행이 아니면 false.</summary>
    public bool MarkRead(string employeeNo, long id, string actor)
    {
        const string sql = """
            UPDATE x
            SET    ReadAt = SYSDATETIME(), ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
            FROM   dbo.SYS_NotificationHistory x
            JOIN   dbo.SYS_NotificationHistory h ON h.NotificationHistoryID = @Id AND h.RecipientUserID = @Emp AND h.Channel = @Ch
            WHERE  x.RecipientUserID = h.RecipientUserID AND x.Channel = h.Channel AND x.EventTypeCode = h.EventTypeCode
              AND  x.SourceRefType = h.SourceRefType AND x.SourceRefID = h.SourceRefID AND x.ReadAt IS NULL;
            SELECT COUNT(*) FROM dbo.SYS_NotificationHistory WHERE NotificationHistoryID = @Id AND RecipientUserID = @Emp AND Channel = @Ch;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 450).Value = employeeNo;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public int CountUnread(string employeeNo)
    {
        var sql = InboxCte + "SELECT COUNT(*) FROM g WHERE g.rn = 1 AND g.ReadAt IS NULL;";
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 450).Value = employeeNo;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── 방해 금지 시간(앱) ───────────────────────────────────────────────────
    // 칸은 기기 행(SYS_NotificationChannel)마다 있지만 사람 단위 설정으로 다룬다 — 쓰면 그 사번의 PUSH 행 전부, 읽으면 가장 최근에 바뀐 행.

    public QuietHours GetQuietHours(string employeeNo)
    {
        const string sql = """
            SELECT TOP (1) c.QuietHoursStart, c.QuietHoursEnd,
                   (SELECT COUNT(*) FROM dbo.SYS_NotificationChannel x
                    WHERE x.UserID = @Emp AND x.Channel = @Ch AND ISNULL(x.IsEnabled, 0) = 1) AS Devices
            FROM   dbo.SYS_NotificationChannel c
            WHERE  c.UserID = @Emp AND c.Channel = @Ch
            ORDER  BY ISNULL(c.ModifiedTS, c.CreatedTS) DESC, c.NotificationChannelID DESC;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 450).Value = employeeNo;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read()
            ? new(rdr["QuietHoursStart"] as TimeSpan?, rdr["QuietHoursEnd"] as TimeSpan?, (int)rdr["Devices"])
            : new(null, null, 0);
    }

    /// <summary>그 사번의 PUSH 기기 행 전부에 방해 금지 시간을 쓴다(둘 다 null = 해제). 바꾼 기기 수 — 0 이면 등록된 기기가 없다.</summary>
    public int SetQuietHours(string employeeNo, TimeSpan? start, TimeSpan? end, string actor)
    {
        const string sql = """
            UPDATE dbo.SYS_NotificationChannel
            SET    QuietHoursStart = @Start, QuietHoursEnd = @End, ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
            WHERE  UserID = @Emp AND Channel = @Ch;
            SELECT @@ROWCOUNT;
            """;
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Emp", SqlDbType.NVarChar, 450).Value = employeeNo;
        cmd.Parameters.Add("@Ch", SqlDbType.VarChar, 10).Value = NotifyRules.Channel;
        cmd.Parameters.Add("@Start", SqlDbType.Time).Value = (object?)start ?? DBNull.Value;
        cmd.Parameters.Add("@End", SqlDbType.Time).Value = (object?)end ?? DBNull.Value;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── 알림 상세의 원천 정보(앱) ─────────────────────────────────────────────

    /// <summary>상세 — <see cref="GetInboxItem"/> 에 원천의 지금 상태를 붙인다. 그 사번의 행이 아니면 null.</summary>
    public InboxDetail? GetInboxDetail(string employeeNo, long id)
    {
        if (GetInboxItem(employeeNo, id) is not { } r) return null;
        return new(r.Id, r.EventTypeCode, r.EventName, r.Subject, r.Body, r.SourceRefType, r.SourceRefId, r.Status,
            r.CreatedTs, r.SentAt, r.ReadAt, ReadSource(r.SourceRefType, r.SourceRefId));
    }

    // 사람 코드(사번 — 옛 행은 사용자 ID) → "이름 (사번)", 모르면 코드 그대로
    static string Person(string code) => $$"""
        CASE WHEN NULLIF({{code}}, '') IS NULL THEN NULL ELSE COALESCE(
            (SELECT TOP (1) CONCAT(COALESCE(npp.EmployeeName, npp.EmployeeNo), N' (', npp.EmployeeNo, N')') FROM dbo.SYS_UserProfile npp
             WHERE NULLIF(npp.EmployeeNo, '') IS NOT NULL AND (npp.EmployeeNo = {{code}} OR npp.UserID = {{code}})),
            (SELECT TOP (1) CONCAT(npw.EmployeeName, N' (', npw.EmployeeNo, N')') FROM dbo.MD_Worker npw WHERE npw.EmployeeNo = {{code}}),
            {{code}}) END
        """;

    // 원천 종류별 조회 — 공통 칸(Status·OccurredAt·ClosedAt·LineID·LineName·EquipID·EquipName·ItemNo·ItemName·Severity) + f_ 로 시작하는 종류별 항목.
    // PM 은 상태를 PmRules 로 계산하려고 x_ 칸(저장 상태·예정일·DB 날짜·선행일 설정)을 더 읽는다.
    static readonly Dictionary<string, string> SourceSql = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FAILURE"] = $$"""
            SELECT f.Status, f.ReportedAt AS OccurredAt, f.ResolvedAt AS ClosedAt, e.LineID, l.LineName, f.EquipID, e.EquipName,
                   CAST(NULL AS varchar(20)) AS ItemNo, CAST(NULL AS nvarchar(100)) AS ItemName, f.Severity,
                   f.FailureType AS f_failureType, f.Symptom AS f_symptom, f.Source AS f_source,
                   {{Person("f.ReportedBy")}} AS f_reportedBy,
                   w.WoNumber AS f_workOrder, w.Status AS f_workOrderStatus, {{Person("w.AssignedTechID")}} AS f_technician,
                   w.StartedAt AS f_workStartedAt
            FROM   dbo.MNT_FailureRegister f
            LEFT JOIN dbo.MD_Equipment e ON e.EquipID = f.EquipID
            LEFT JOIN dbo.MD_Line l      ON l.LineID = e.LineID
            LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = f.WorkOrderID
            WHERE  f.FailureNumber = @Ref;
            """,
        ["ANDON"] = """
            SELECT a.Status, a.TriggeredAt AS OccurredAt, a.ResumedAt AS ClosedAt, a.LineID, l.LineName, a.EquipID, e.EquipName,
                   CAST(NULL AS varchar(20)) AS ItemNo, CAST(NULL AS nvarchar(100)) AS ItemName, a.Severity,
                   a.ReasonCode AS f_cause, a.SupervisorName AS f_supervisor, a.AckedAt AS f_supervisorAckedAt,
                   (SELECT STRING_AGG(CONCAT(d.DeptCode, ':', CASE WHEN d.AckedAt IS NOT NULL THEN 'ACKED'
                                                                   WHEN d.ArrivedAt IS NOT NULL THEN 'ARRIVED' ELSE 'CALLED' END), ',')
                           WITHIN GROUP (ORDER BY d.DeptCallID)
                    FROM dbo.PR_AndonDeptCall d WHERE d.AndonID = a.AndonID) AS f_departments,
                   a.CorrectiveAction AS f_correctiveAction
            FROM   dbo.PR_AndonCall a
            LEFT JOIN dbo.MD_Line l      ON l.LineID = a.LineID
            LEFT JOIN dbo.MD_Equipment e ON e.EquipID = a.EquipID
            WHERE  a.AndonID = TRY_CAST(@Ref AS int);
            """,
        ["QC_HOLD"] = $$"""
            SELECT TOP (1) h.Status, h.HeldAt AS OccurredAt,
                   CASE WHEN ISNULL(h.Status, '') <> 'Held' THEN h.ModifiedTS END AS ClosedAt,
                   t.LineID, l.LineName, CAST(NULL AS varchar(20)) AS EquipID, CAST(NULL AS nvarchar(100)) AS EquipName,
                   h.ItemNo, i.ItemName, h.Severity,
                   h.HeldQty AS f_heldQty, h.AffectedType AS f_affectedType, t.LotCode AS f_lot, h.PhysicalLocation AS f_location,
                   {{Person("h.HeldBy")}} AS f_heldBy
            FROM   dbo.QC_Hold h
            LEFT JOIN dbo.tbl_Lot t  ON t.LotID = h.LotID
            LEFT JOIN dbo.MD_Line l  ON l.LineID = t.LineID
            LEFT JOIN dbo.MD_Item i  ON i.ItemNo = h.ItemNo
            WHERE  h.HoldNumber = @Ref OR (NULLIF(h.HoldNumber, '') IS NULL AND CAST(h.HoldID AS varchar(40)) = @Ref)
            ORDER  BY h.HoldID DESC;
            """,
        ["PM"] = """
            SELECT TOP (1) p.Status, CAST(p.NextDueDate AS datetime2) AS OccurredAt, CAST(NULL AS datetime2) AS ClosedAt,
                   e.LineID, l.LineName, p.EquipID, e.EquipName,
                   CAST(NULL AS varchar(20)) AS ItemNo, CAST(NULL AS nvarchar(100)) AS ItemName, CAST(NULL AS varchar(10)) AS Severity,
                   p.PMClass AS f_pmClass, p.PMType AS f_pmType, p.NextDueDate AS f_nextDueDate, p.LastPMDate AS f_lastPmDate,
                   w.WoNumber AS f_workOrder, w.Status AS f_workOrderStatus,
                   p.Status AS x_stored, p.NextDueDate AS x_next, CAST(SYSDATETIME() AS date) AS x_today,
                   (SELECT TOP (1) ConfigValue FROM dbo.SYS_Config WHERE ConfigKey = @LeadKey) AS x_lead
            FROM   dbo.MNT_PMSchedule p
            LEFT JOIN dbo.MD_Equipment e  ON e.EquipID = p.EquipID
            LEFT JOIN dbo.MD_Line l       ON l.LineID = e.LineID
            LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = p.ActiveWoID
            WHERE  p.PMPlanNumber = @Ref
            ORDER  BY p.PMScheduleID DESC;
            """,
        ["SPARE_PART"] = """
            SELECT CASE WHEN ISNULL(s.ActiveFlag, 1) = 0 THEN 'INACTIVE'
                        WHEN s.ReorderPoint > 0 AND s.OnHandQty <= s.ReorderPoint THEN 'LOW' ELSE 'OK' END AS Status,
                   CAST(NULL AS datetime2) AS OccurredAt, CAST(NULL AS datetime2) AS ClosedAt,
                   CAST(NULL AS varchar(20)) AS LineID, CAST(NULL AS nvarchar(100)) AS LineName,
                   CAST(NULL AS varchar(20)) AS EquipID, CAST(NULL AS nvarchar(100)) AS EquipName,
                   s.PartNo AS ItemNo, s.PartName AS ItemName, CAST(NULL AS varchar(10)) AS Severity,
                   s.SparePartNo AS f_sparePartNo, s.OnHandQty AS f_onHand, s.ReorderPoint AS f_reorderPoint,
                   s.SafetyStock AS f_safetyStock, s.UOM AS f_uom,
                   CONCAT_WS(' ', s.ZoneCode, s.Slot, s.ExtraLocation) AS f_location
            FROM   dbo.MD_SparePart s
            WHERE  s.SparePartNo = @Ref;
            """,
    };

    /// <summary>원천 종류·번호(상태형 이벤트의 @날짜 접미사는 뗀다)로 지금 상태를 읽는다. 모르는 종류·지워진 원천은 null.</summary>
    public NotifySource? ReadSource(string? refType, string? refId)
    {
        if (string.IsNullOrWhiteSpace(refType) || string.IsNullOrWhiteSpace(refId)
            || !SourceSql.TryGetValue(refType.Trim(), out var sql)) return null;
        var baseRef = NotifyRules.BaseRef(refId.Trim());
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Ref", SqlDbType.VarChar, 40).Value = baseRef;
        cmd.Parameters.Add("@LeadKey", SqlDbType.VarChar, 60).Value = PmRules.LeadDaysKey;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;

        string? S(string col) => rdr[col] as string;
        DateTime? D(string col) => rdr[col] as DateTime?;
        var status = S("Status");
        var fields = new List<SourceField>();
        for (var i = 0; i < rdr.FieldCount; i++)
        {
            var name = rdr.GetName(i);
            if (name.StartsWith("f_", StringComparison.Ordinal)) fields.Add(new(name[2..], FieldText(rdr.GetValue(i), rdr.GetDataTypeName(i))));
        }
        if (string.Equals(refType.Trim(), "PM", StringComparison.OrdinalIgnoreCase))
            status = PmRules.DisplayStatus(rdr["x_stored"] as string, rdr["x_next"] as DateTime?, (DateTime)rdr["x_today"],
                PmRules.ParseLeadDays(rdr["x_lead"] as string));
        return new(refType.Trim(), baseRef, status, D("OccurredAt"), D("ClosedAt"), S("LineID"), S("LineName"),
            S("EquipID"), S("EquipName"), S("ItemNo"), S("ItemName"), S("Severity"), fields);
    }

    // 앱이 그대로 보이거나 파싱할 수 있게 문화권 없는 글자로 — 날짜 yyyy-MM-dd, 일시 yyyy-MM-dd HH:mm:ss(DB 서버 시각)
    static string? FieldText(object v, string sqlType) => v switch
    {
        DBNull => null,
        DateTime d => sqlType.Equals("date", StringComparison.OrdinalIgnoreCase) ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-dd HH:mm:ss"),
        decimal m => m.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
    };

    static List<InboxRow> ReadInbox(SqlCommand cmd)
    {
        var list = new List<InboxRow>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            list.Add(new(Convert.ToInt64(rdr["NotificationHistoryID"]), rdr["EventTypeCode"] as string, rdr["EventName"] as string,
                rdr["Subject"] as string, rdr["Body"] as string, rdr["SourceRefType"] as string, rdr["SourceRefID"] as string,
                rdr["Status"] as string, rdr["CreatedTS"] as DateTime?, rdr["SentAt"] as DateTime?, rdr["ReadAt"] as DateTime?));
        return list;
    }
}
