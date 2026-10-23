using System.Data;
using AMES.Data.Connection;
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

        CREATE TABLE #ev (EventTypeCode varchar(20) NOT NULL, RefType varchar(20) NOT NULL, RefID varchar(40) NOT NULL,
                          Subject nvarchar(200) NULL, Body nvarchar(1000) NULL);

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
        CREATE TABLE #rr (RuleID int NOT NULL, EventTypeCode varchar(20) NOT NULL, RoleID nvarchar(450) NOT NULL);
        INSERT #rr
        SELECT r.NotificationRuleID, r.EventTypeCode, CAST(j.value AS nvarchar(450))
        FROM   dbo.SYS_NotificationRule r
        CROSS APPLY OPENJSON(CASE WHEN ISJSON(r.RecipientRolesJSON) = 1 THEN r.RecipientRolesJSON ELSE '[]' END) j
        WHERE  ISNULL(r.IsEnabled, 0) = 1 AND ISJSON(r.ChannelsJSON) = 1
          AND  EXISTS (SELECT 1 FROM OPENJSON(r.ChannelsJSON) c WHERE CAST(c.value AS varchar(10)) = @Ch);

        -- 수신자(사번): 역할을 가진 활성 웹 사용자 + Operator 규칙이면 PIN 이 있는 활성 작업자(같은 사번의 웹 계정이 있으면 웹 계정 쪽)
        CREATE TABLE #rcp (RuleID int NOT NULL, EventTypeCode varchar(20) NOT NULL, EmpNo varchar(20) NOT NULL);
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
                INSERT dbo.SYS_NotificationChannel (UserID, Channel, Address, IsEnabled, VerifiedAt, CreatedBy, CreatedTS)
                VALUES (@Emp, @Ch, @Token, 1, SYSDATETIME(), @Actor, SYSDATETIME());
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
