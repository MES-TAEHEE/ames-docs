-- ============================================================================
-- AMES.Pop 이 쓰는 인라인 SQL 을 저장 프로시저로 이전 (AMES.Data 리포지토리 -> dbo.SP_POP_*) — 144개
--   · 프로시저 이름 규칙: dbo.SP_POP_{영역}_{메서드}[_{동작테이블}]  (docs/POP_SP_전환목록_v2.xlsx 참조)
--   · 파라미터 이름·타입은 기존 C# 호출과 같다. SET NOCOUNT ON 은 넣지 않았다 —
--     ExecuteNonQuery 의 영향 행 수를 쓰는 호출(상태 전이 등)이 인라인 SQL 과 똑같이 동작해야 한다.
--   · 재실행 안전(CREATE OR ALTER). sqlcmd -f 65001 -b
--   · 배포 순서: 이 마이그레이션 -> 신 AMES.Pop / Web / Api (프로시저 없는 DB 에 신 바이너리를 올리면
--     로그인·LOT 확정·안돈 등 POP 경로가 모두 Could not find stored procedure 로 실패한다.
--     구 바이너리는 인라인 SQL 이라 이 마이그레이션만 먼저 적용돼도 그대로 동작한다)
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
GO

-- ===== POP 하루 경계 (10-27) =====
-- POP 현황의 하루 = 전기일(SYS_Config.DAY_CUTOFF_TIME 'HH:mm', 이 시각 전은 전날 — 웹 보고서·실적 ProdDate 와 같다).
-- 설정이 없거나 읽을 수 없으면 0(자정 — 종전 동작). C# 쪽은 AMES.Data.Services.PopDay 가 같은 값을 쓴다.
CREATE OR ALTER FUNCTION dbo.FN_POP_DayCutoffMin()
RETURNS INT
AS
BEGIN
    DECLARE @v VARCHAR(20) = (SELECT TOP 1 LTRIM(RTRIM(ConfigValue)) FROM dbo.SYS_Config
                              WHERE ConfigKey = 'DAY_CUTOFF_TIME' ORDER BY ConfigID);
    DECLARE @h INT, @m INT;
    IF @v LIKE '[0-9][0-9]:[0-9][0-9]'   SELECT @h = CAST(LEFT(@v,2) AS INT), @m = CAST(RIGHT(@v,2) AS INT);
    ELSE IF @v LIKE '[0-9]:[0-9][0-9]'   SELECT @h = CAST(LEFT(@v,1) AS INT), @m = CAST(RIGHT(@v,2) AS INT);
    ELSE IF @v LIKE '[0-9][0-9][0-9][0-9]' SELECT @h = CAST(LEFT(@v,2) AS INT), @m = CAST(RIGHT(@v,2) AS INT);
    IF @h IS NULL OR @h > 23 OR @m > 59 RETURN 0;
    RETURN @h * 60 + @m;
END;
GO

-- 전기일 @d 의 시작 시각(= @d + 경계 시각). 끝은 하루 뒤.
CREATE OR ALTER FUNCTION dbo.FN_POP_DayStart(@d DATE)
RETURNS DATETIME2
AS
BEGIN
    RETURN DATEADD(MINUTE, dbo.FN_POP_DayCutoffMin(), CAST(@d AS DATETIME2));
END;
GO

-- 지금(DB 시각)의 전기일
CREATE OR ALTER FUNCTION dbo.FN_POP_ProdToday()
RETURNS DATE
AS
BEGIN
    RETURN CAST(DATEADD(MINUTE, -dbo.FN_POP_DayCutoffMin(), SYSDATETIME()) AS DATE);
END;
GO

-- ===== Auth.FindByEmployeeNo (AUTH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_AUTH_FindByEmployeeNo
    @EmployeeNo VARCHAR(20)
AS
BEGIN
    SELECT TOP 1
        u.Id, u.UserName, u.PasswordHash, p.PinHash,
        p.EmployeeNo, p.EmployeeName, p.Department, p.DefaultShift,
        p.AccountStatus, p.FailedLoginCount
    FROM   dbo.SYS_UserProfile p
    JOIN   dbo.AspNetUsers     u ON u.Id = p.UserID
    WHERE  p.EmployeeNo = @EmployeeNo;
END;
GO

-- ===== Auth.ListAllProfiles (AUTH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_AUTH_ListAllProfiles
AS
BEGIN
    SELECT u.Id, u.UserName, u.PasswordHash, p.PinHash,
           p.EmployeeNo, p.EmployeeName, p.Department, p.DefaultShift,
           p.AccountStatus, ISNULL(p.FailedLoginCount,0) AS FailedLoginCount
    FROM   dbo.SYS_UserProfile p
    JOIN   dbo.AspNetUsers     u ON u.Id = p.UserID
    WHERE  ISNULL(p.AccountStatus, 'ACTIVE') = 'ACTIVE'
    ORDER  BY p.EmployeeNo;
END;
GO

-- ===== Auth.IncrementFailedCount (AUTH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_AUTH_IncrementFailedCount
    @ModifiedBy VARCHAR(20),
    @UserID NVARCHAR(450)
AS
BEGIN
    UPDATE dbo.SYS_UserProfile
    SET    FailedLoginCount = ISNULL(FailedLoginCount, 0) + 1,
           AccountStatus    = CASE
                                  WHEN ISNULL(FailedLoginCount, 0) + 1 >= 5
                                  THEN 'LOCKED'
                                  ELSE ISNULL(AccountStatus, 'ACTIVE')
                              END,
           -- 행위자 컬럼은 varchar(20) — GUID 대신 본인 사번
           ModifiedBy       = COALESCE(@ModifiedBy, NULLIF(LTRIM(RTRIM(EmployeeNo)), ''), LEFT(@UserID, 20)),
           ModifiedTS       = SYSDATETIME()
    WHERE  UserID = @UserID;
    SELECT ISNULL(AccountStatus, 'ACTIVE') FROM dbo.SYS_UserProfile WHERE UserID = @UserID;
END;
GO

-- ===== Auth.RecordSuccessfulLogin (AUTH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_AUTH_RecordSuccessfulLogin
    @ModifiedBy VARCHAR(20),
    @UserID NVARCHAR(450),
    -- POP·PDA 로그인은 0 — 배지·PIN 성공이 웹 비밀번호·PP-CAL PIN 실패 횟수를 지우면 잠금이 무력화된다
    @ResetFailures BIT = 1
AS
BEGIN
    UPDATE dbo.SYS_UserProfile
    SET    FailedLoginCount = CASE WHEN @ResetFailures = 1 THEN 0 ELSE FailedLoginCount END,
           LastLoginTS      = SYSDATETIME(),
           -- 행위자 컬럼은 varchar(20)(09-23 migrate_audit_actor_varchar20) — GUID 대신 본인 사번, 없으면 앞 20자
           ModifiedBy       = COALESCE(@ModifiedBy, NULLIF(LTRIM(RTRIM(EmployeeNo)), ''), LEFT(@UserID, 20)),
           ModifiedTS       = SYSDATETIME()
    WHERE  UserID = @UserID;
END;
GO

-- ===== Worker.FindByEmployeeNo (WORKER) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WORKER_FindByEmployeeNo
    @EmployeeNo VARCHAR(20)
AS
BEGIN
    SELECT EmployeeNo, EmployeeName, PinHash, ActiveFlag
            FROM   dbo.MD_Worker
    WHERE  EmployeeNo = @EmployeeNo;
END;
GO

-- ===== Worker.ListAllActive (WORKER) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WORKER_ListAllActive
AS
BEGIN
    SELECT EmployeeNo, EmployeeName, PinHash, ActiveFlag
            FROM   dbo.MD_Worker
    WHERE  ActiveFlag = 1
    ORDER  BY EmployeeNo;
END;
GO

-- ===== Worker.EnsureRegistered (WORKER) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WORKER_EnsureRegistered
    @EmployeeNo VARCHAR(20),
    @EmployeeName NVARCHAR(50)
AS
BEGIN
    INSERT INTO dbo.MD_Worker (EmployeeNo, EmployeeName, ActiveFlag, CreatedBy)
    SELECT @EmployeeNo, @EmployeeName, 1, @EmployeeNo
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Worker WHERE EmployeeNo = @EmployeeNo);
END;
GO

-- ===== Worker.SetPin (WORKER) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WORKER_SetPin
    @PinHash NVARCHAR(200),
    @EmployeeNo VARCHAR(20)
AS
BEGIN
    UPDATE dbo.MD_Worker
    SET    PinHash    = @PinHash,
           ModifiedBy = @EmployeeNo,
           ModifiedTS = SYSDATETIME()
    WHERE  EmployeeNo   = @EmployeeNo;
END;
GO

-- ===== PopSession.CreateSession (SESSION) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_SESSION_CreateSession_InsPopSession
    @OperatorID NVARCHAR(450),
    @TerminalID VARCHAR(20),
    @LineID VARCHAR(20),
    @ShiftCode VARCHAR(10),
    @AuthMethod VARCHAR(20),
    @Lifetime INT,
    @CreatedBy VARCHAR(20)
AS
BEGIN
    DECLARE @Now datetime2 = SYSDATETIME();
    INSERT INTO dbo.PR_PopSession
        (OperatorID, TerminalID, LineID, ShiftCode, AuthMethod,
         StartedAt, ExpiresAt, CreatedBy, CreatedTS)
    OUTPUT INSERTED.SessionID, INSERTED.StartedAt, INSERTED.ExpiresAt
    VALUES
        (@OperatorID, @TerminalID, @LineID, @ShiftCode, @AuthMethod,
         @Now, DATEADD(second, @Lifetime, @Now), @CreatedBy, @Now);
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_SESSION_CreateSession_GetAspNetUserRoles
    @UserId NVARCHAR(450),
    @RoleId NVARCHAR(450)
AS
BEGIN
    SELECT CAST(CASE WHEN EXISTS (
        SELECT 1 FROM dbo.AspNetUserRoles ur
        WHERE ur.UserId = @UserId AND ur.RoleId = @RoleId
    ) THEN 1 ELSE 0 END AS bit);
END;
GO

-- ===== PopSession.CloseSession (SESSION) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_SESSION_CloseSession
    @Reason VARCHAR(20),
    @ModBy VARCHAR(20),
    @SessionID INT
AS
BEGIN
    UPDATE dbo.PR_PopSession
    SET    LoggedOutAt   = SYSDATETIME(),
           LogoutReason  = @Reason,
           ModifiedBy    = @ModBy,
           ModifiedTS    = SYSDATETIME()
    WHERE  SessionID     = @SessionID
      AND  LoggedOutAt IS NULL;
END;
GO

-- ===== PopSession.WriteAuthLog (SESSION) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_SESSION_WriteAuthLog
    @TerminalID VARCHAR(20),
    @AttemptedID VARCHAR(50),
    @AuthMethod VARCHAR(20),
    @Result VARCHAR(10),
    @FailReason VARCHAR(40),
    @CreatedBy VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_PopAuthLog
        (TerminalID, AttemptedID, AuthMethod, Result, FailReason,
         AttemptedAt, CreatedBy, CreatedTS)
    VALUES
        (@TerminalID, @AttemptedID, @AuthMethod, @Result, @FailReason,
         SYSDATETIME(), @CreatedBy, SYSDATETIME());
END;
GO

-- ===== WorkOrder.GetById (WO) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WO_GetById
    @WoID INT
AS
BEGIN
    SELECT TOP 1 w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
           w.OrderQty, w.OpenQty, w.CompletedQty, w.LineID,
           w.MoldID, w.RecipeID, w.DueDate, w.Status, w.TerminalLock,
           ISNULL(w.Priority,5) AS Priority
    FROM   dbo.PP_WorkOrder w
    JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
    WHERE  w.WoID = @WoID;
END;
GO

-- ===== WorkOrder.FindOpenForItem (WO) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WO_FindOpenForItem
    @Line VARCHAR(20),
    @Item VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
           w.OrderQty, w.OpenQty, r.CompletedQty, r.LineID,
           COALESCE(w.MoldID,
                   (SELECT TOP 1 ls.MoldID FROM dbo.PP_LineSchedule ls
                    WHERE ls.WoID = w.WoID AND ls.LineID = r.LineID AND ls.MoldID IS NOT NULL
                      AND EXISTS (SELECT 1 FROM dbo.MD_MoldItem mi WHERE mi.MoldID = ls.MoldID AND mi.ItemNo = COALESCE(r.ItemNo, w.ItemNo) AND ISNULL(mi.ActiveFlag,1) = 1)
                    ORDER BY ABS(DATEDIFF(day, CAST(SYSDATETIME() AS date), ls.ScheduleDate)), ls.ScheduleDate),
                   (SELECT TOP 1 es.MountedMoldID FROM dbo.MNT_EquipmentStatus es
                    WHERE es.LineID = r.LineID AND es.MountedMoldID IS NOT NULL
                      AND EXISTS (SELECT 1 FROM dbo.MD_MoldItem mi WHERE mi.MoldID = es.MountedMoldID AND mi.ItemNo = COALESCE(r.ItemNo, w.ItemNo) AND ISNULL(mi.ActiveFlag,1) = 1)
                    ORDER BY ISNULL(es.ModifiedTS, es.CreatedTS) DESC, es.EquipStatusID DESC)) AS MoldID, w.RecipeID, w.DueDate, r.Status, r.TerminalLock,
           ISNULL(w.Priority,5) AS Priority, w.RoutingType,
           r.RoutingLineID, r.StepSeq, r.ProcessCode,
           COALESCE(r.ItemNo, w.ItemNo) AS StepItemNo
    FROM   dbo.PP_WorkOrderRouting r
    JOIN   dbo.PP_WorkOrder w ON w.WoID   = r.WoID
    JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
    WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = @Item
      AND          r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')
    ORDER  BY         CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID
    ;
END;
GO

-- ===== WorkOrder.ListForItemOnLine (WO) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WO_ListForItemOnLine
    @Line VARCHAR(20),
    @Item VARCHAR(20),
    @Days INT
AS
BEGIN
    SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
           w.OrderQty, w.OpenQty, r.CompletedQty, r.LineID,
           COALESCE(w.MoldID,
                   (SELECT TOP 1 ls.MoldID FROM dbo.PP_LineSchedule ls
                    WHERE ls.WoID = w.WoID AND ls.LineID = r.LineID AND ls.MoldID IS NOT NULL
                      AND EXISTS (SELECT 1 FROM dbo.MD_MoldItem mi WHERE mi.MoldID = ls.MoldID AND mi.ItemNo = COALESCE(r.ItemNo, w.ItemNo) AND ISNULL(mi.ActiveFlag,1) = 1)
                    ORDER BY ABS(DATEDIFF(day, CAST(SYSDATETIME() AS date), ls.ScheduleDate)), ls.ScheduleDate),
                   (SELECT TOP 1 es.MountedMoldID FROM dbo.MNT_EquipmentStatus es
                    WHERE es.LineID = r.LineID AND es.MountedMoldID IS NOT NULL
                      AND EXISTS (SELECT 1 FROM dbo.MD_MoldItem mi WHERE mi.MoldID = es.MountedMoldID AND mi.ItemNo = COALESCE(r.ItemNo, w.ItemNo) AND ISNULL(mi.ActiveFlag,1) = 1)
                    ORDER BY ISNULL(es.ModifiedTS, es.CreatedTS) DESC, es.EquipStatusID DESC)) AS MoldID, w.RecipeID, w.DueDate, w.ProdDeadline, r.Status, r.TerminalLock,
           ISNULL(w.Priority,5) AS Priority, w.RoutingType,
           r.RoutingLineID, r.StepSeq, r.ProcessCode,
           COALESCE(r.ItemNo, w.ItemNo) AS StepItemNo,
           so.SoNumber AS SoNumber
    FROM   dbo.PP_WorkOrderRouting r
    JOIN   dbo.PP_WorkOrder w ON w.WoID   = r.WoID
    JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
    LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
    WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = @Item
      AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
      AND (r.Status <> 'Closed'
           OR COALESCE(r.ActualEnd, r.ModifiedTS) >= DATEADD(day, -@Days, CAST(GETDATE() AS date)))
    ORDER  BY CASE r.Status WHEN 'In Progress' THEN 0
                            WHEN 'Released'    THEN 1
                            WHEN 'Closed'      THEN 3
                            ELSE 2 END,
              CASE WHEN r.Status = 'Closed' THEN COALESCE(r.ActualEnd, r.ModifiedTS) END DESC,
              ISNULL(w.Priority,5),
              ISNULL(w.DueDate,'9999-12-31'),
              w.WoID, r.StepSeq;
END;
GO

-- ===== WorkOrder.BumpStepCompleted (WO) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WO_BumpStepCompleted
    @RL INT,
    @Qty DECIMAL(18,4),
    @Actor VARCHAR(20)
AS
BEGIN
    DECLARE @WoID int, @Seq int, @OrderQty decimal(14,3), @New decimal(14,3), @LastSeq int, @HdrStatus varchar(20);

    SELECT @WoID = r.WoID, @Seq = r.StepSeq, @OrderQty = ISNULL(w.OrderQty, 0), @HdrStatus = w.Status
    FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PP_WorkOrder        w WITH (UPDLOCK, ROWLOCK) ON w.WoID = r.WoID
    WHERE  r.RoutingLineID = @RL;
    IF @WoID IS NULL THROW 50001, 'Routing step not found.', 1;
    -- 단계를 찾은 뒤 WO 가 취소됐으면 실적을 붙이지 않는다(10-27) — 확정 트랜잭션이 되돌아가고 다시 스캔하면 열린 WO 로 간다
    IF @Qty > 0 AND @HdrStatus = 'Cancelled' THROW 50002, 'Work order was cancelled — scan again.', 1;

    -- 접수(AcceptWo)가 없는 INJ 에서는 첫 실적이 착수다. Released 만 올린다.
    -- 양수는 Closed 를 되돌리지 않지만, 음수(불량 역분개)는 OrderQty 밑으로 떨어지면 단계 Closed 를 다시 연다.
    -- 헤더는 마지막 라인 단계의 동기화 블록에서만 다시 연다 — 헤더 CompletedQty 를 움직이는 건 그 단계뿐이다.
    UPDATE dbo.PP_WorkOrderRouting
    SET    CompletedQty = CompletedQty + @Qty,
           Status       = CASE WHEN CompletedQty + @Qty >= @OrderQty          THEN 'Closed'
                               WHEN @Qty < 0 AND Status = 'Closed'            THEN 'In Progress'
                               WHEN Status = 'Released'                       THEN 'In Progress'
                               ELSE Status END,
           ActualStart  = ISNULL(ActualStart, SYSDATETIME()),
           ActualEnd    = CASE WHEN CompletedQty + @Qty >= @OrderQty THEN ISNULL(ActualEnd, SYSDATETIME())
                               WHEN @Qty < 0                         THEN NULL
                               ELSE ActualEnd END,
           ModifiedBy   = @Actor, ModifiedTS = SYSDATETIME()
    WHERE  RoutingLineID = @RL;

    UPDATE dbo.PP_WorkOrder
    SET    Status      = CASE WHEN Status = 'Released' THEN 'In Progress' ELSE Status END,
           ActualStart = ISNULL(ActualStart, SYSDATETIME()),
           ModifiedBy  = @Actor, ModifiedTS = SYSDATETIME()
    WHERE  WoID = @WoID AND ISNULL(Status,'Draft') NOT IN ('Cancelled','Closed');

    SELECT @New = CompletedQty FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID = @RL;
    SELECT @LastSeq = MAX(StepSeq) FROM dbo.PP_WorkOrderRouting WHERE WoID = @WoID AND LineID IS NOT NULL;

    IF @Seq = @LastSeq
        UPDATE dbo.PP_WorkOrder
        SET    CompletedQty = @New,
               Status       = CASE WHEN @New >= @OrderQty                  THEN 'Closed'
                                   WHEN @Qty < 0 AND Status = 'Closed'     THEN 'In Progress'
                                   ELSE Status END,
               ActualEnd    = CASE WHEN @New >= @OrderQty THEN ISNULL(ActualEnd, SYSDATETIME())
                                   WHEN @Qty < 0          THEN NULL
                                   ELSE ActualEnd END,
               ModifiedBy   = @Actor, ModifiedTS = SYSDATETIME()
        WHERE  WoID = @WoID;

    SELECT @New;
END;
GO

-- ===== WorkOrder.FindStepId (WO) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_WO_FindStepId
    @WoID INT,
    @LineID VARCHAR(20)
AS
BEGIN
    -- 취소된 WO 의 단계는 돌려주지 않는다 — 재작업 양품이 취소 WO 에 귀속되지 않게(10-27). 호출자는 열린 WO 를 다시 찾거나 WO 없이 확정한다
    SELECT TOP 1 r.RoutingLineID
    FROM   dbo.PP_WorkOrderRouting r
    JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
    WHERE  r.WoID = @WoID AND r.LineID = @LineID
      AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
    ORDER  BY r.StepSeq;
END;
GO

-- ===== Defect.ListForProcess (DEFECT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_DEFECT_ListForProcess
    @P VARCHAR(10)
AS
BEGIN
    SELECT DefectCode, DefectName, DefectNameEn, ProcessCode,
           SeverityLevel, DefaultCauseCode
    FROM   dbo.MD_DefectCode
    WHERE  ProcessCode = @P
      AND  ActiveFlag = 1
    ORDER  BY DefectCode;
END;
GO

-- ===== Defect.ListCausesForProcess (DEFECT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_DEFECT_ListCausesForProcess
    @P VARCHAR(10)
AS
BEGIN
    SELECT CauseCode, CauseName, CauseNameEn, ProcessCode
    FROM   dbo.MD_DefectCause
    WHERE  ISNULL(ActiveFlag,1) = 1
      AND  (ProcessCode = @P OR ProcessCode IS NULL)
    ORDER  BY ISNULL(SortOrder,9999), CauseCode;
END;
GO

-- ===== Mold.GetById (MOLD) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_MOLD_GetById
    @M VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 MoldID, MoldName, ISNULL(RatedShots,0) AS RatedShots,
           ISNULL(CurrentShots,0) AS CurrentShots,
           ISNULL(CavityCount,1)  AS CavityCount,
           LastMaintDate, Status
    FROM   dbo.MD_Mold
    WHERE  MoldID = @M;
END;
GO

-- ===== Equipment.GetPrimaryForLine (EQUIP) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_EQUIP_GetPrimaryForLine
    @L VARCHAR(20)
AS
BEGIN
    WITH latest AS (
      SELECT EquipID, MAX(EquipStatusLogID) AS MaxId
      FROM   dbo.PR_EquipStatusLog
      GROUP  BY EquipID
    )
    SELECT TOP 1 e.EquipID, e.EquipName, e.LineID,
           COALESCE(es.Status, e.Status, 'OFFLINE') AS Status,
           es.StartedAt
    FROM   dbo.MD_Equipment e
    LEFT  JOIN latest          l  ON l.EquipID = e.EquipID
    LEFT  JOIN dbo.PR_EquipStatusLog es ON es.EquipStatusLogID = l.MaxId
    WHERE  e.LineID = @L
      AND  ISNULL(e.ActiveFlag,1) = 1
    ORDER  BY e.EquipID;
END;
GO

-- ===== Equipment.LogStatus (EQUIP) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_EQUIP_LogStatus
    @E VARCHAR(20),
    @L VARCHAR(20),
    @S VARCHAR(8),
    @R VARCHAR(30),
    @W INT,
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_EquipStatusLog
        (EquipID, LineID, Status, ReasonCode, WoID, StartedAt, CreatedBy, CreatedTS)
    VALUES (@E, @L, @S, @R, @W, SYSDATETIME(), @By, SYSDATETIME());

    UPDATE dbo.MD_Equipment SET Status = @S, ModifiedBy = @By, ModifiedTS = SYSDATETIME() WHERE EquipID = @E;
END;
GO

-- ===== Andon.Raise (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_Raise
    @L VARCHAR(20),
    @E VARCHAR(20),
    @T VARCHAR(20),
    @R VARCHAR(20),
    @S VARCHAR(10),
    @By VARCHAR(20),
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    SET XACT_ABORT ON;
    BEGIN TRAN;
    -- 같은 라인의 두 터미널이 동시에 눌러도 한 건만 — 라인 단위로 줄을 세운 뒤 열린 안돈을 확인한다(확인과 INSERT 사이 틈 제거).
    -- 이미 열린 안돈이 있으면 새로 만들지 않고 그 번호를 돌려준다(Created = 0). 열림 정의는 GetOpenForLine 과 같다.
    DECLARE @lock INT, @res NVARCHAR(100) = N'ANDON:' + @L;
    EXEC @lock = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
    IF @lock < 0 THROW 52090, N'andon raise lock timeout', 1;
    DECLARE @open INT = (SELECT TOP 1 AndonID FROM dbo.PR_AndonCall
                         WHERE LineID = @L AND Status IN ('OPEN', 'SUP_ACKED', 'DEPT_CALLED') ORDER BY AndonID DESC);
    IF @open IS NOT NULL
    BEGIN
        COMMIT;
        SELECT @open AS AndonID, CAST(0 AS BIT) AS Created;
        RETURN;
    END;
    DECLARE @now DATETIME2 = @evtTs;
    DECLARE @ids TABLE (AndonID INT);
    INSERT INTO dbo.PR_AndonCall
        (LineID, EquipID, TriggerSource, RuleID, Severity, TriggeredAt,
         Status, CreatedBy, CreatedTS)
    OUTPUT INSERTED.AndonID INTO @ids
    VALUES (@L, @E, @T, @R, @S, @now, 'OPEN', @By, @now);
    DECLARE @id INT = (SELECT TOP 1 AndonID FROM @ids);
    -- 같은 라인에 이미 열린 비가동(PP-DTL 등록·MNT-002 라인 정지·다른 기록)이 있으면 새로 열지 않는다 — 겹친 시간을 OEE·비가동 분석이
    -- 두 번 세지 않게(10-27, 웹 쪽 규칙과 같음). 그 행은 주인이 닫고, 안돈 종료는 AndonID 행이 없으니 아무것도 닫지 않는다
    IF NOT EXISTS (SELECT 1 FROM dbo.PP_LineDowntimeLog WITH (UPDLOCK, HOLDLOCK) WHERE LineID = @L AND EndTS IS NULL)
        INSERT INTO dbo.PP_LineDowntimeLog
            (LineID, StartTS, ReasonCode, Comment, LoggedBy, AndonID, CreatedBy, CreatedTS)
        VALUES (@L, @now, 'ANDON', N'Andon #' + CAST(@id AS NVARCHAR(10)), @By, @id, @By, @now);
    COMMIT;
    SELECT @id AS AndonID, CAST(1 AS BIT) AS Created;
END;
GO

-- ===== Andon.RecordPush (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_RecordPush
    @A INT,
    @R VARCHAR(100),
    @C VARCHAR(20),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_AndonPush
        (AndonID, Recipient, Channel, CreatedBy, CreatedTS)
    VALUES (@A, @R, @C, @By, SYSDATETIME());
END;
GO

-- ===== Andon.GetOpenForLine (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_GetOpenForLine_GetAndonCall
    @L VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 AndonID, LineID, EquipID, Status, TriggeredAt, CreatedBy,
           AckedBy, AckedAt, SupervisorName, ReasonCode, ResumedAt, Severity
    FROM   dbo.PR_AndonCall
    WHERE  LineID = @L AND Status IN ('OPEN', 'SUP_ACKED', 'DEPT_CALLED')
    ORDER  BY AndonID DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_GetOpenForLine_GetAndonDeptCall
    @A INT
AS
BEGIN
    SELECT d.DeptCallID, d.DeptCode, d.CalledAt, d.ArrivedAt, d.ArrivedNo, d.ArrivedName, d.AckedAt,
           COALESCE(c.CodeName, d.DeptCode) AS DeptName, c.CodeNameEn
    FROM   dbo.PR_AndonDeptCall d
    LEFT   JOIN dbo.MD_CodeItem c ON c.GroupCode = 'ANDON_DEPT' AND c.CodeValue = d.DeptCode
    WHERE  d.AndonID = @A
    ORDER  BY d.DeptCallID;
END;
GO

-- ===== Andon.IsLineSupervisor (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_IsLineSupervisor
    @L VARCHAR(20),
    @W VARCHAR(20)
AS
BEGIN
    SELECT 1 FROM dbo.MD_LineSupervisor
    WHERE  LineID = @L AND EmployeeNo = @W AND ActiveFlag = 1;
END;
GO

-- ===== Andon.ListLineSupervisors (ANDON) — 오프라인 사본(슈퍼바이저 배지 확인), 10-28 =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_ListLineSupervisors
    @L VARCHAR(20)
AS
BEGIN
    SELECT EmployeeNo FROM dbo.MD_LineSupervisor
    WHERE  LineID = @L AND ActiveFlag = 1;
END;
GO

-- ===== Andon.FindDeptCall (ANDON) — 오프라인에서 호출한 부서 행을 복구 때 찾는다, 10-28 =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_FindDeptCall
    @ID INT,
    @D VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 DeptCallID FROM dbo.PR_AndonDeptCall
    WHERE  AndonID = @ID AND DeptCode = @D
    ORDER  BY DeptCallID;
END;
GO

-- ===== Andon.ListCauses (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_ListCauses
AS
BEGIN
    SELECT CodeValue, COALESCE(CodeName, CodeValue), CodeNameEn, NULLIF(LTRIM(RTRIM(Attribute1)), N'')
    FROM   dbo.MD_CodeItem
    WHERE  GroupCode = 'ANDON_CAUSE' AND ISNULL(UseFlag, 1) = 1
    ORDER  BY ISNULL(SortOrder, 9999), CodeValue;
END;
GO

-- ===== Andon.ListDepts (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_ListDepts
AS
BEGIN
    SELECT CodeValue, COALESCE(CodeName, CodeValue), CodeNameEn
    FROM   dbo.MD_CodeItem
    WHERE  GroupCode = 'ANDON_DEPT' AND ISNULL(UseFlag, 1) = 1
    ORDER  BY ISNULL(SortOrder, 9999), CodeValue;
END;
GO

-- ===== Andon.ListSeverities (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_ListSeverities
AS
BEGIN
    SELECT CodeValue, COALESCE(CodeName, CodeValue), CodeNameEn
    FROM   dbo.MD_CodeItem
    WHERE  GroupCode = 'DEFECT_SEVERITY' AND ISNULL(UseFlag, 1) = 1
    ORDER  BY ISNULL(SortOrder, 9999), CodeValue;
END;
GO

-- ===== Andon.AcknowledgeBySupervisor (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_AcknowledgeBySupervisor
    @W VARCHAR(20),
    @N NVARCHAR(50),
    @Op VARCHAR(20),
    @ID INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    UPDATE dbo.PR_AndonCall
    SET    AckedBy = @W, AckedAt = @evtTs, SupervisorName = @N,
           Status = 'SUP_ACKED', ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  AndonID = @ID AND Status = 'OPEN';
END;
GO

-- ===== Andon.CallDepts (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_CallDepts_UpdAndonCall
    @C VARCHAR(30),
    @S VARCHAR(10),
    @Op VARCHAR(20),
    @ID INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    UPDATE dbo.PR_AndonCall
    SET    ReasonCode = @C, Severity = @S, Status = 'DEPT_CALLED', ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  AndonID = @ID AND Status IN ('SUP_ACKED', 'DEPT_CALLED');
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_CallDepts_UpdLineDowntimeLog
    @C VARCHAR(30),
    @Op VARCHAR(20),
    @ID INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    UPDATE dbo.PP_LineDowntimeLog
    SET    CauseCode = @C, ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  AndonID = @ID AND EndTS IS NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_CallDepts_InsMNT_FailureRegister
    @Ref VARCHAR(24),
    @C VARCHAR(30),
    @S VARCHAR(10),
    @By VARCHAR(20),
    @Op VARCHAR(20),
    @ID INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    IF NOT EXISTS (SELECT 1 FROM dbo.MNT_FailureRegister WHERE Source = 'ANDON' AND AndonRefID = @Ref)
    BEGIN
        -- 고장번호 채번 = SYS_NumberSeq 카운터(웹 MntRepository.ClaimNumber 와 공용, dist/migrate_sys_number_seq.sql) — 범위 잠금 교착 방지(10-26)
        DECLARE @ym  CHAR(4) = FORMAT(@evtTs, 'yyMM');
        DECLARE @seq INT;
        EXEC dbo.SP_SYS_NumberSeq_Next @SeqCode = 'FAIL', @Ym = @ym, @No = @seq OUTPUT;
        INSERT INTO dbo.MNT_FailureRegister
            (FailureNumber, EquipID, FailureType, Symptom, Severity, Source, AndonRefID, DowntimeID,
             Status, ReportedBy, ReportedAt, CreatedBy, CreatedTS)
        SELECT 'FAIL-' + @ym + '-' + RIGHT('000' + CAST(@seq AS VARCHAR(4)), 3),
               a.EquipID, @C,
               N'Andon #' + @Ref + N' · ' + COALESCE(ci.CodeName, @C),
               @S, 'ANDON', @Ref, d.DowntimeID,
               'OPEN', @By, @evtTs, @Op, @evtTs
        FROM   dbo.PR_AndonCall a
        LEFT   JOIN dbo.MD_CodeItem ci ON ci.GroupCode = 'ANDON_CAUSE' AND ci.CodeValue = @C
        OUTER  APPLY (SELECT TOP 1 DowntimeID FROM dbo.PP_LineDowntimeLog WHERE AndonID = a.AndonID ORDER BY DowntimeID DESC) d
        WHERE  a.AndonID = @ID;
        DECLARE @failId INT = SCOPE_IDENTITY();

        -- 보전 작업지시(CM)를 같이 발행한다(10-28 — 웹 MntRepository.IssueFailureWorkOrder 와 같은 값: 출처 ANDON·SourceRefID 고장번호·
        -- 우선순위는 심각도·담당자 없음 = ISSUED). 그래야 MNT-007 배정·착수·PDA 부품 출고를 쓸 수 있다. 채번 잠금 순서 FAIL → MWO(웹과 같음).
        IF @failId IS NOT NULL
        BEGIN
            DECLARE @mwo INT;
            EXEC dbo.SP_SYS_NumberSeq_Next @SeqCode = 'MWO', @Ym = @ym, @No = @mwo OUTPUT;
            INSERT INTO dbo.MNT_WorkOrder
                (WoNumber, WoType, EquipID, Priority, SourceType, SourceRefID, AssignedTechID,
                 ActionDesc, Status, IssuedAt, CreatedBy, CreatedTS)
            SELECT 'MWO-' + @ym + '-' + RIGHT('000' + CAST(@mwo AS VARCHAR(4)), 3), 'CM', f.EquipID,
                   CASE UPPER(ISNULL(f.Severity, '')) WHEN 'CRITICAL' THEN 'HIGH' WHEN 'MAJOR' THEN 'MED' ELSE 'LOW' END,
                   'ANDON', f.FailureNumber, NULL,
                   ISNULL(f.FailureType, 'CM') + N' — ' + f.FailureNumber + N': ' + LEFT(LTRIM(RTRIM(ISNULL(f.Symptom, N''))), 200),
                   'ISSUED', @evtTs, @Op, @evtTs
            FROM   dbo.MNT_FailureRegister f
            WHERE  f.FailureID = @failId;
            UPDATE dbo.MNT_FailureRegister SET WorkOrderID = SCOPE_IDENTITY() WHERE FailureID = @failId;
        END
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_CallDepts_InsAndonDeptCall
    @ID INT,
    @D VARCHAR(20),
    @By VARCHAR(20),
    @Op VARCHAR(20),
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    INSERT INTO dbo.PR_AndonDeptCall (AndonID, DeptCode, CalledAt, CalledBy, CreatedBy, CreatedTS)
    SELECT @ID, @D, @evtTs, @By, @Op, @evtTs
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.PR_AndonDeptCall WHERE AndonID = @ID AND DeptCode = @D);
END;
GO

-- ===== Andon.RecordArrival (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_RecordArrival
    @W VARCHAR(20),
    @N NVARCHAR(50),
    @Op VARCHAR(20),
    @ID INT,
    @Maint VARCHAR(20),
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    SET XACT_ABORT ON;
    BEGIN TRAN;
    UPDATE dbo.PR_AndonDeptCall
    SET    ArrivedAt = @evtTs, ArrivedNo = @W, ArrivedName = @N,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  DeptCallID = @ID AND ArrivedAt IS NULL;
    IF @@ROWCOUNT > 0
    BEGIN
        UPDATE f SET Status = 'IN_PROGRESS', ModifiedBy = @Op, ModifiedTS = @evtTs
        FROM   dbo.MNT_FailureRegister f
        JOIN   dbo.PR_AndonDeptCall d ON d.AndonID = TRY_CAST(f.AndonRefID AS INT)
        WHERE  d.DeptCallID = @ID AND d.DeptCode = @Maint AND f.Source = 'ANDON' AND f.Status = 'OPEN';
        INSERT INTO dbo.MNT_FailureAction (FailureID, ActionType, Description, TechnicianID, ActionAt, CreatedBy, CreatedTS)
        SELECT f.FailureID, 'ARRIVED', N'Andon responder arrived: ' + @W + COALESCE(N' ' + @N, N''), @W, @evtTs, @Op, @evtTs
        FROM   dbo.MNT_FailureRegister f
        JOIN   dbo.PR_AndonDeptCall d ON d.AndonID = TRY_CAST(f.AndonRefID AS INT)
        WHERE  d.DeptCallID = @ID AND d.DeptCode = @Maint AND f.Source = 'ANDON';
    END
    COMMIT;
END;
GO

-- ===== Andon.AckDept (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_AckDept
    @Op VARCHAR(20),
    @ID INT,
    @Maint VARCHAR(20),
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    SET XACT_ABORT ON;
    BEGIN TRAN;
    UPDATE dbo.PR_AndonDeptCall
    SET    AckedAt = @evtTs, ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  DeptCallID = @ID AND ArrivedAt IS NOT NULL AND AckedAt IS NULL;
    IF @@ROWCOUNT > 0
        INSERT INTO dbo.MNT_FailureAction (FailureID, ActionType, Description, TechnicianID, ActionAt, CreatedBy, CreatedTS)
        SELECT f.FailureID, 'ACK', N'Andon responder acknowledged', d.ArrivedNo, @evtTs, @Op, @evtTs
        FROM   dbo.MNT_FailureRegister f
        JOIN   dbo.PR_AndonDeptCall d ON d.AndonID = TRY_CAST(f.AndonRefID AS INT)
        WHERE  d.DeptCallID = @ID AND d.DeptCode = @Maint AND f.Source = 'ANDON';
    COMMIT;
END;
GO

-- ===== Andon.Resolve (ANDON) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_Resolve
    @C VARCHAR(20),
    @S VARCHAR(10),
    @Op VARCHAR(20),
    @ID INT,
    @Ref VARCHAR(24),
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    SET XACT_ABORT ON;
    BEGIN TRAN;
    DECLARE @now DATETIME2 = @evtTs;
    UPDATE dbo.PR_AndonCall
    SET    ResumedAt   = @now,
           DowntimeSec = DATEDIFF(SECOND, TriggeredAt, @now),
           ReasonCode  = COALESCE(@C, ReasonCode),
           Severity    = COALESCE(@S, Severity),
           Status      = 'RESOLVED',
           ModifiedBy  = @Op,
           ModifiedTS  = @now
    WHERE  AndonID = @ID AND Status <> 'RESOLVED';
    IF @@ROWCOUNT > 0
    BEGIN
        UPDATE dbo.PP_LineDowntimeLog
        SET    EndTS = @now, DurationMin = DATEDIFF(MINUTE, StartTS, @now),
               CauseCode = COALESCE(CauseCode, @C), ModifiedBy = @Op, ModifiedTS = @now
        WHERE  AndonID = @ID AND EndTS IS NULL;
        -- 안돈이 끝나면 그 고장의 보전 작업지시도 끝낸다(10-28 — 결과 OK, 점검 항목이 없는 CM 이라 완료 차단 규칙과 무관).
        -- 이미 완료·종료된 작업지시는 그대로 둔다. 고장보다 먼저(웹 잠금 순서 WO → 고장).
        UPDATE w
        SET    Status = 'COMPLETED', CompletedAt = @now, StartedAt = ISNULL(w.StartedAt, @now),
               ChecklistResultsJSON = (SELECT 'OK' AS Result, N'Andon #' + @Ref + N' resolved' AS Action, CAST(NULL AS nvarchar(1)) AS RootCause,
                                              @Op AS [By], @now AS [At] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
               ModifiedBy = @Op, ModifiedTS = @now
        FROM   dbo.MNT_WorkOrder w
        JOIN   dbo.MNT_FailureRegister f ON f.WorkOrderID = w.WorkOrderID
        WHERE  f.Source = 'ANDON' AND f.AndonRefID = @Ref AND f.Status NOT IN ('RESOLVED', 'CLOSED')
          AND  ISNULL(w.Status, '') NOT IN ('COMPLETED', 'CLOSED');
        -- MNT-007 「종료」로 이미 닫힌 고장(CLOSED — 수리 없이 닫음, 해결 시각 없음)은 해결로 덮지 않는다
        UPDATE dbo.MNT_FailureRegister
        SET    Status = 'RESOLVED', ResolvedAt = @now, ModifiedBy = @Op, ModifiedTS = @now
        WHERE  Source = 'ANDON' AND AndonRefID = @Ref AND Status NOT IN ('RESOLVED', 'CLOSED');
    END
    COMMIT;
END;
GO

-- ===== MasterData.ListActiveStations (MASTER) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_MASTER_ListActiveStations
    @L VARCHAR(20)
AS
BEGIN
    SELECT st.StationCode, st.StationName, st.StationNameEn, st.LineID,
           wc.ProcessCode AS ProcessCode,
           st.FormName, st.OrderSeq, st.Status,
           st.CreatedBy, st.CreatedTS, st.ModifiedBy, st.ModifiedTS
    FROM   dbo.MD_Station st
    LEFT JOIN dbo.MD_Line       l  ON l.LineID = st.LineID
    LEFT JOIN dbo.MD_WorkCenter wc ON wc.WCID  = l.WCID
    WHERE  st.LineID = @L
      AND  ISNULL(st.Status,'ACTIVE') <> 'INACTIVE'
    ORDER  BY st.OrderSeq, st.StationCode;
END;
GO

-- ===== MasterData.ListPopLineOptions (MASTER) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_MASTER_ListPopLineOptions
AS
BEGIN
    SELECT l.LineID, l.LineName, l.LineNameEn, wc.ProcessCode
    FROM   dbo.MD_Line l
    JOIN   dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
    WHERE  ISNULL(l.Status,'ACTIVE') <> 'INACTIVE'
      AND  wc.ProcessCode IN ('INJ','IMG','PNT','QC','RWK')
    ORDER  BY l.LineID;
END;
GO

-- ===== PntLot.InsertRawLot (PNT) =====
-- PNT(도장) 원천 LOT — IMG 판(SP_POP_IMG_*)과 같은 흐름에서 코어 스캔(ParentLotID)·원단·본딩만 뺐다.
-- 조회 SELECT 목록은 ImgLotDto 공용 뷰에 맞춰 원단·코어 컬럼을 NULL 로 돌려준다.
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_InsertRawLot_GetItem
    @Item VARCHAR(20)
AS
BEGIN
    SELECT ItemName, PGN, ALC, MountPos FROM dbo.MD_Item WHERE ItemNo = @Item;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_InsertRawLot_GetWorkOrderRouting
    @Line VARCHAR(20),
    @Item VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 c.CustomerCode
    FROM   dbo.PP_WorkOrderRouting r
    JOIN   dbo.PP_WorkOrder        w  ON w.WoID  = r.WoID
    LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
    LEFT JOIN dbo.MD_Customer      c  ON c.CustomerID = so.CustomerID
    WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = @Item
      AND          r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')
    ORDER  BY         CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID
    ;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_InsertRawLot_GetEquipment
    @L VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 EquipID FROM dbo.MD_Equipment
    WHERE  LineID = @L AND ISNULL(ActiveFlag,1) = 1
    ORDER  BY EquipID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_InsertRawLot_InsLot
    @LotCode VARCHAR(40),
    @ItemNo VARCHAR(20),
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty, ParentLotID,
         ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
    OUTPUT INSERTED.LotID, INSERTED.CreatedTS
    VALUES
        (@LotCode, @ItemNo, NULL, @LineID, @Proc, 1, 1, NULL,
         SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_InsertRawLot_InsPntLot
    @LotID INT,
    @Equip VARCHAR(20),
    @Cust VARCHAR(20),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_PntLot (LotID, EquipID, CustomerCode, ConfirmStatus, PrintedCount, CreatedBy, CreatedTS)
    VALUES (@LotID, @Equip, @Cust, 'RAW', 0, @By, SYSDATETIME());
END;
GO

-- ===== PntLot.GetTodayLots (PNT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_GetTodayLots
    @Line VARCHAR(20),
    @Top INT
AS
BEGIN
    SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, mi.PGN, mi.ALC, mi.MountPos, l.LineID,
           e.EquipID, e.CustomerCode, e.ConfirmStatus, e.ConfirmedAt,
           CAST(NULL AS INT) AS FabricRollLotID, CAST(NULL AS DECIMAL(8,3)) AS FabricConsumedM, CAST(NULL AS INT) AS BondSetupID,
           e.PrintedCount, l.CreatedTS, CAST(NULL AS VARCHAR(40)) AS CoreLotCode
    FROM   dbo.tbl_Lot l
    JOIN   dbo.PR_PntLot e ON e.LotID = l.LotID
    LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
    WHERE  l.LineID = @Line
      AND  l.CreatedTS >= dbo.FN_POP_DayStart(dbo.FN_POP_ProdToday())
      AND  l.CreatedTS <  dbo.FN_POP_DayStart(DATEADD(day, 1, dbo.FN_POP_ProdToday()))
    ORDER  BY l.CreatedTS DESC, l.LotID DESC
    OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY;
END;
GO

-- ===== PntLot.GetByLotCode (PNT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_GetByLotCode
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, mi.PGN, mi.ALC, mi.MountPos, l.LineID,
           e.EquipID, e.CustomerCode, e.ConfirmStatus, e.ConfirmedAt,
           CAST(NULL AS INT) AS FabricRollLotID, CAST(NULL AS DECIMAL(8,3)) AS FabricConsumedM, CAST(NULL AS INT) AS BondSetupID,
           e.PrintedCount, l.CreatedTS, CAST(NULL AS VARCHAR(40)) AS CoreLotCode
    FROM   dbo.tbl_Lot l
    JOIN   dbo.PR_PntLot e ON e.LotID = l.LotID
    LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
    WHERE  l.LotCode = @Code AND l.ProcessCode = 'PNT';
END;
GO

-- ===== PntLot.IncrementPrintedCount (PNT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_IncrementPrintedCount
    @By VARCHAR(20),
    @LotID INT
AS
BEGIN
    UPDATE dbo.PR_PntLot
    SET    PrintedCount = PrintedCount + 1, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
    OUTPUT INSERTED.PrintedCount
    WHERE  LotID = @LotID;
END;
GO

-- ===== PntLot.RegisterDefect (PNT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_RegisterDefect_GetLot
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.ItemNo, l.LineID, e.ConfirmStatus,
           CAST(NULL AS varchar(40)) AS UsedByLotCode,
           -- 출하된 LOT(그 LOT 또는 실렸던 팔레트의 출하 기록이 있고 양수 재고 행이 없음) — 라인 불량으로 받지 않는다(10-28)
           CAST(CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory iq WHERE iq.LotNo = l.LotCode AND iq.Qty > 0)
                      AND (EXISTS (SELECT 1 FROM dbo.FG_Shipment s WHERE s.LotNo = l.LotCode)
                           OR EXISTS (SELECT 1 FROM dbo.WH_Inventory ip JOIN dbo.FG_Shipment s ON s.LotNo = ip.PalletNo OR s.PalletNo = ip.PalletNo
                                      WHERE ip.LotNo = l.LotCode AND ip.PalletNo IS NOT NULL))
                     THEN 1 ELSE 0 END AS bit) AS ShippedFlag
    FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PR_PntLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
    WHERE  l.LotCode = @Code;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_RegisterDefect_UpdPntLot
    @Op VARCHAR(20),
    @Lot INT
AS
BEGIN
    UPDATE dbo.PR_PntLot
    SET    ConfirmStatus = 'DEFECT', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @Lot;
    -- LOT 마스터 상태도 DEFECT — 확정(CONFIRMED)·폐기(SCRAP)·수리 양품(CONFIRMED)과 맞춘다. 전에는 CONFIRMED 로 남아 추적성에 양품으로 보였다(10-28)
    UPDATE dbo.tbl_Lot
    SET    Status = 'DEFECT', QualityFlag = 'NG', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @Lot;
END;
GO

-- ===== PntLot.ConfirmByLotCode (PNT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_ConfirmByLotCode_GetLot
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.ItemNo, l.LineID, l.CreatedTS, e.ConfirmStatus, e.CustomerCode
    FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PR_PntLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
    WHERE  l.LotCode = @Code;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_ConfirmByLotCode_GetWorkOrderRouting
    @Line VARCHAR(20),
    @Item VARCHAR(20),
    @Cust VARCHAR(20) = NULL
AS
BEGIN
    -- 라벨에 찍힌 수주처(발행 때 박아 둔 CustomerCode)와 같은 수주의 WO 를 먼저 — 라벨과 실적의 WO 가 갈리지 않게(10-28)
    SELECT TOP 1 r.WoID, r.RoutingLineID
    FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
    LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
    LEFT JOIN dbo.MD_Customer      c  ON c.CustomerID = so.CustomerID
    WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = @Item
      AND          r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')
    ORDER  BY         CASE WHEN @Cust IS NOT NULL AND c.CustomerCode = @Cust THEN 0 ELSE 1 END,
                      CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID
    ;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_ConfirmByLotCode_GetLot2
    @ThisTs DATETIME2,
    @Line VARCHAR(20)
AS
BEGIN
    SELECT ISNULL(DATEDIFF(SECOND, MAX(pl.CreatedTS), @ThisTs), 0)
    FROM   dbo.tbl_Lot pl
    JOIN   dbo.PR_PntLot pe ON pe.LotID = pl.LotID
    WHERE  pl.LineID = @Line AND pl.CreatedTS < @ThisTs;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_ConfirmByLotCode_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @CT INT,
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
    OUTPUT INSERTED.ResultID
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, @Proc, 1, @CT,
         @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_ConfirmByLotCode_UpdLot
    @WoID INT,
    @Op NVARCHAR(450),
    @LotID INT,
    @Sess INT
AS
BEGIN
    UPDATE dbo.tbl_Lot
    SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;

    UPDATE dbo.PR_PntLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;
END;
GO

-- ===== PntLot.GetDailyItemSummary (PNT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_GetDailyItemSummary
    @Station VARCHAR(20),
    @Line VARCHAR(20),
    @Today DATE
AS
BEGIN
    WITH bop AS (
        SELECT DISTINCT b.ItemNo
        FROM   dbo.MD_Bop b
        WHERE  b.StationCode = @Station AND ISNULL(b.ActiveFlag,1) = 1
    ),
    sched AS (
        SELECT COALESCE(s.ItemNo, st.ItemNo, w.ItemNo) AS ItemNo, SUM(ISNULL(s.PlannedQty,0)) AS PlanQty
        FROM   dbo.PP_LineSchedule s
        JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
        OUTER  APPLY (SELECT TOP 1 r.ItemNo FROM dbo.PP_WorkOrderRouting r
                      WHERE  r.WoID = s.WoID AND r.LineID = s.LineID ORDER BY r.StepSeq) st
        WHERE  s.LineID = @Line AND s.ScheduleDate = @Today AND s.EntryType = 'WO'
          AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
        GROUP  BY COALESCE(s.ItemNo, st.ItemNo, w.ItemNo)
    ),
    lots AS (
        SELECT l.ItemNo,
               COUNT(*)                                                                  AS InputQty,
               SUM(CASE WHEN e.ConfirmStatus = 'CONFIRMED' THEN 1 ELSE 0 END)            AS ConfirmedQty,
               SUM(CASE WHEN e.ConfirmStatus IN ('DEFECT','SCRAPPED') THEN 1 ELSE 0 END) AS NgQty,
               SUM(CASE WHEN e.ConfirmStatus = 'RAW'       THEN 1 ELSE 0 END)            AS PendingQty
        FROM   dbo.tbl_Lot   l
        JOIN   dbo.PR_PntLot e ON e.LotID = l.LotID
        WHERE  l.LineID = @Line
          AND  l.CreatedTS >= dbo.FN_POP_DayStart(@Today) AND l.CreatedTS < dbo.FN_POP_DayStart(DATEADD(day, 1, @Today))
        GROUP  BY l.ItemNo
    ),
    itemkeys AS (
        SELECT ItemNo FROM bop
        UNION SELECT ItemNo FROM sched
        UNION SELECT ItemNo FROM lots
    )
    SELECT k.ItemNo,
           COALESCE(i.ItemName, N'')  AS ItemName,
           ISNULL(p.PlanQty, 0)       AS PlanQty,
           ISNULL(t.InputQty, 0)      AS InputQty,
           ISNULL(t.ConfirmedQty, 0)  AS ConfirmedQty,
           ISNULL(t.NgQty, 0)         AS NgQty,
           ISNULL(t.PendingQty, 0)    AS PendingQty,
           CASE WHEN b.ItemNo IS NULL THEN 0 ELSE 1 END AS InBop,
           CASE WHEN EXISTS (
                SELECT 1
                FROM   dbo.PP_WorkOrderRouting r
                JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
                WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = k.ItemNo
                  AND  r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')) THEN 1 ELSE 0 END AS HasOpenWo
    FROM   itemkeys k
    LEFT JOIN dbo.MD_Item i ON i.ItemNo = k.ItemNo
    LEFT JOIN bop    b ON b.ItemNo = k.ItemNo
    LEFT JOIN sched  p ON p.ItemNo = k.ItemNo
    LEFT JOIN lots   t ON t.ItemNo = k.ItemNo
    ORDER  BY InBop DESC, k.ItemNo;
END;
GO

-- ===== Qc.ListInspections (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ListInspections
    @N INT,
    @T VARCHAR(15)
AS
BEGIN
    SELECT TOP (@N)
           i.InspectionID, i.InspectionNo, i.InspectionType, i.LotID, i.WoID,
           i.LineID, i.ItemNo, m.ItemName, i.Mode,
           ISNULL(i.SampleSize,0) AS SampleSize,
           ISNULL(i.BatchQty,0)   AS BatchQty,
           ISNULL(i.CumulativeGood,0) AS CumulativeGood,
           ISNULL(i.DefectQtyTotal,0) AS DefectQtyTotal,
           ISNULL(i.Verdict,'IN_PROGRESS') AS Verdict,
           ISNULL(i.CriticalFlag,0) AS CriticalFlag,
           i.InspectorID, i.InsStartTS, i.InsEndTS
    FROM   dbo.QC_Inspection i
    LEFT JOIN dbo.MD_Item m ON m.ItemNo = i.ItemNo
    WHERE  (@T = '' OR i.InspectionType = @T)
    ORDER BY i.InsStartTS DESC;
END;
GO

-- ===== Qc.ListInspectionItems (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ListInspectionItems
    @I INT
AS
BEGIN
    SELECT InspectionItemID, InspectionID, ISNULL(ItemSeq,0) AS ItemSeq,
           ItemName, Standard, Measured, Result
    FROM   dbo.QC_InspectionItem
    WHERE  InspectionID = @I
    ORDER BY ItemSeq;
END;
GO

-- ===== Qc.FinalizeInspection (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_FinalizeInspection
    @V VARCHAR(15),
    @A VARCHAR(20),
    @I INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    SET NOCOUNT ON;
    -- 진행 중 검사만 판정한다 — 이미 판정된 검사를 덮어쓰지 않게(10-27). 0 이면 화면이 안내한다
    UPDATE dbo.QC_Inspection
    SET    Verdict = @V, InsEndTS = @evtTs, ApproverID = @A,
           ModifiedBy = @A, ModifiedTS = @evtTs
    WHERE  InspectionID = @I AND Verdict = 'IN_PROGRESS';
    SELECT @@ROWCOUNT AS Updated;
END;
GO

-- ===== Qc.CreateInspection (QC) =====
-- ===== Qc.ResolveInspectionTarget (QC) =====
-- 검사 대상 = 스캔한 LOT(품번·WO·라인을 LOT 에서) → 없으면 품번(MD_Item). 둘 다 아니면 빈 결과(10-28 — 전에는 품번이 상수였다).
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ResolveInspectionTarget
    @Code VARCHAR(40)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 l.LotID, l.ItemNo, l.WoID, l.LineID
    FROM   dbo.tbl_Lot l WHERE l.LotCode = @Code
    UNION ALL
    SELECT TOP 1 CAST(NULL AS int), i.ItemNo, CAST(NULL AS int), CAST(NULL AS varchar(20))
    FROM   dbo.MD_Item i
    WHERE  i.ItemNo = @Code AND NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot l2 WHERE l2.LotCode = @Code);
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_CreateInspection
    @T VARCHAR(15),
    @I VARCHAR(20),
    @S INT,
    @Op NVARCHAR(450),
    @By VARCHAR(20),
    @Lot INT = NULL,
    @Wo INT = NULL,
    @Line VARCHAR(20) = NULL,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    -- 번호는 분 단위라 같은 분의 두 번째부터 -2, -3 … 을 붙인다(10-27). 그 분 번호를 잠가 동시 등록도 겹치지 않는다
    DECLARE @base VARCHAR(20) = CONCAT(LEFT(@T,3), '-', FORMAT(@evtTs, 'yyMMddHHmm'));
    BEGIN TRAN;
    DECLARE @n INT = (SELECT COUNT(*) FROM dbo.QC_Inspection WITH (UPDLOCK, HOLDLOCK)
                      WHERE InspectionNo = @base OR InspectionNo LIKE @base + '-%');
    INSERT INTO dbo.QC_Inspection
        (InspectionNo, InspectionType, LotID, WoID, LineID, ItemNo, Mode, SampleSize, BatchQty,
         CumulativeGood, DefectQtyTotal, Verdict, CriticalFlag,
         InspectorID, InsStartTS, CreatedBy, CreatedTS)
    OUTPUT INSERTED.InspectionID
    VALUES (CASE WHEN @n = 0 THEN @base ELSE CONCAT(@base, '-', @n + 1) END,
            @T, @Lot, @Wo, @Line, @I, 'Normal', @S, @S*6, 0, 0,
            'IN_PROGRESS', 0, @Op, @evtTs, @By, @evtTs);
    COMMIT;
END;
GO

-- ===== Qc.ListNcrs (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ListNcrs
    @TopN INT,
    @F VARCHAR(80)
AS
BEGIN
    SELECT TOP (@TopN)
           n.NcrID, n.NcrNumber, n.SourceType, n.SourceID, n.Severity,
           n.LotID, n.ItemNo, m.ItemName,
           ISNULL(n.AffectedQty,0) AS AffectedQty,
           n.Disposition, n.Status, n.ReportedBy, n.ReportedAt,
           n.HoldID, n.CapaID
    FROM   dbo.QC_NCR n
    LEFT JOIN dbo.MD_Item m ON m.ItemNo = n.ItemNo
    WHERE  (@F = '' OR n.Status IN (SELECT value FROM STRING_SPLIT(@F, ',')))
    ORDER BY n.ReportedAt DESC;
END;
GO

-- ===== Qc.CreateNcr (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_CreateNcr
    @ST VARCHAR(20),
    @SI VARCHAR(24),
    @SV VARCHAR(10),
    @I VARCHAR(20),
    @Q DECIMAL(18,4),
    @D VARCHAR(15),
    @Op VARCHAR(20),
    @By VARCHAR(20),
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    -- 검사 번호와 같은 규칙 — 같은 분 두 번째부터 -2, -3 …(10-27)
    DECLARE @base VARCHAR(20) = CONCAT('NCR-', FORMAT(@evtTs, 'yyMMddHHmm'));
    BEGIN TRAN;
    DECLARE @n INT = (SELECT COUNT(*) FROM dbo.QC_NCR WITH (UPDLOCK, HOLDLOCK)
                      WHERE NcrNumber = @base OR NcrNumber LIKE @base + '-%');
    INSERT INTO dbo.QC_NCR
        (NcrNumber, SourceType, SourceID, Severity, ItemNo, AffectedQty,
         Disposition, Status, ReportedBy, ReportedAt, CreatedBy, CreatedTS)
    OUTPUT INSERTED.NcrID
    VALUES (CASE WHEN @n = 0 THEN @base ELSE CONCAT(@base, '-', @n + 1) END,
            @ST, @SI, @SV, @I, @Q, @D, 'Open', @Op, @evtTs,
            @By, @evtTs);
    COMMIT;
END;
GO

-- ===== Qc.ListHolds (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ListHolds
    @TopN INT,
    @F VARCHAR(80)
AS
BEGIN
    SELECT TOP (@TopN)
           h.HoldID, h.HoldNumber, h.SourceNcrID, n.NcrNumber, h.Severity,
           h.AffectedType, h.LotID, h.ItemNo, m.ItemName,
           ISNULL(h.HeldQty,0) AS HeldQty,
           h.PhysicalLocation, h.Status, h.HeldAt
    FROM   dbo.QC_Hold h
    LEFT JOIN dbo.QC_NCR  n ON n.NcrID = h.SourceNcrID
    LEFT JOIN dbo.MD_Item m ON m.ItemNo = h.ItemNo
    WHERE  (@F = '' OR h.Status IN (SELECT value FROM STRING_SPLIT(@F, ',')))
    ORDER BY h.HeldAt DESC;
END;
GO

-- ===== Qc.ReleaseHold (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ReleaseHold_UpdHold
    @A VARCHAR(15),
    @By VARCHAR(20),
    @H INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    SET NOCOUNT ON;
    -- 보류 중(Held)인 것만 해제한다 — 이미 해제·거부된 홀드를 다시 처리하지 않게(10-27). 0 이면 해제 이력도 남기지 않는다
    UPDATE dbo.QC_Hold
    SET    Status = CASE WHEN @A IN ('USE-AS-IS','REWORK') THEN 'Released' ELSE 'Rejected' END,
           ModifiedBy = @By,
           ModifiedTS = @evtTs
    WHERE  HoldID = @H AND Status = 'Held';
    SELECT @@ROWCOUNT AS Updated;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ReleaseHold_InsHoldRelease
    @H INT,
    @A VARCHAR(15),
    @R NVARCHAR(500),
    @By VARCHAR(20),
    @Emp VARCHAR(20),
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    INSERT INTO dbo.QC_HoldRelease
        (HoldID, EventType, ReleaseAction, ReleaseReason,
         ReleasedBy, ReleasedAt, Note, CreatedBy, CreatedTS)
    VALUES (@H, 'Release', @A, @R, @By, @evtTs, @R, @Emp, @evtTs);
END;
GO

-- ===== Qc.ListCapas (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ListCapas
    @TopN INT,
    @F VARCHAR(120)
AS
BEGIN
    SELECT TOP (@TopN)
           c.CapaID, c.CapaNumber, c.Type, c.TriggerType, c.Phase, c.Status,
           c.RootCause, c.Cause4M, c.OwnerID, c.OpenedAt, c.DueDate, c.ClosedAt,
           (SELECT COUNT(*) FROM dbo.QC_CAPA_Action a WHERE a.CapaID = c.CapaID) AS ActionsTotal,
           (SELECT COUNT(*) FROM dbo.QC_CAPA_Action a WHERE a.CapaID = c.CapaID AND a.CompletedAt IS NOT NULL) AS ActionsCompleted
    FROM   dbo.QC_CAPA c
    WHERE  (@F = '' OR c.Status IN (SELECT value FROM STRING_SPLIT(@F, ',')))
    ORDER BY c.OpenedAt DESC;
END;
GO

-- ===== Qc.ListCapaActions (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ListCapaActions
    @C INT
AS
BEGIN
    SELECT CapaActionID, CapaID, ActionType, ISNULL(CheckDay,0) AS CheckDay,
           Description, Metric, TargetValue, ActualValue, Verdict,
           DueDate, CompletedAt
    FROM   dbo.QC_CAPA_Action
    WHERE  CapaID = @C
    ORDER BY CheckDay, CapaActionID;
END;
GO

-- ===== Qc.GetModuleDefectRates (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_GetModuleDefectRates
AS
BEGIN
    DECLARE @From datetime2 = dbo.FN_POP_DayStart(dbo.FN_POP_ProdToday());
    DECLARE @To   datetime2 = dbo.FN_POP_DayStart(DATEADD(day,1,dbo.FN_POP_ProdToday()));
    -- 양품과 불량을 라인별로 따로 모아 합친다 — 실적이 없는 라인(전부 RAW·로봇 NG 불량)의 불량도 빠지지 않게.
    -- 미확정(RAW)·로봇 NG LOT 의 불량은 ResultID 가 없다 — LOT 의 라인으로 귀속시킨다.
    ;WITH good AS (
      SELECT r.LineID, SUM(r.GoodQty) AS GoodQty
      FROM   dbo.PR_ProductionResult r
      WHERE  r.EntryAt >= @From AND r.EntryAt < @To
      GROUP BY r.LineID
    ), defect AS (
      SELECT COALESCE(r2.LineID, tl.LineID) AS LineID, SUM(d.Qty) AS DefectQty
      FROM   dbo.PR_DefectDetail d
      LEFT JOIN dbo.PR_ProductionResult r2 ON r2.ResultID = d.ResultID
      LEFT JOIN dbo.tbl_Lot             tl ON tl.LotID    = d.LotID
      WHERE  d.DetectedAt >= @From AND d.DetectedAt < @To
      GROUP BY COALESCE(r2.LineID, tl.LineID)
    ), prod AS (
      SELECT CASE WHEN x.LineID LIKE 'LINE-INJ-%' THEN 'INJ'
                  WHEN x.LineID LIKE 'LINE-IMG-%' THEN 'IMG'
                  WHEN x.LineID LIKE 'LINE-PNT-%' THEN 'PNT'
                  ELSE 'OTHER' END AS Module,
             ISNULL(g.GoodQty,0)   AS GoodQty,
             ISNULL(df.DefectQty,0) AS DefectQty
      FROM   (SELECT LineID FROM good UNION SELECT LineID FROM defect) x
      LEFT JOIN good   g  ON g.LineID  = x.LineID
      LEFT JOIN defect df ON df.LineID = x.LineID
    )
    SELECT Module,
           SUM(GoodQty)   AS GoodQty,
           SUM(DefectQty) AS DefectQty
    FROM   prod
    WHERE  Module IN ('INJ','IMG','PNT')   -- PNT 도 10-22 부터 같은 실적·불량 테이블(PNT-MAIN) — 구 가상 LOT 테이블 UNION 폐지
    GROUP BY Module;
END;
GO

-- ===== Qc.GetDashboardCounts (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_GetDashboardCounts
AS
BEGIN
    SELECT
      (SELECT COUNT(*) FROM dbo.QC_NCR  WHERE ISNULL(Status,'Open') IN ('Open','Investigating'))   AS OpenNcr,
      (SELECT COUNT(*) FROM dbo.QC_Hold WHERE ISNULL(Status,'Held') = 'Held')                      AS ActiveHold,
      (SELECT COUNT(*) FROM dbo.QC_CAPA WHERE ClosedAt IS NULL)                                    AS OpenCapa;
END;
GO

-- ===== Qc.GetDefectPareto (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_GetDefectPareto
    @TopN INT
AS
BEGIN
    SELECT TOP (@TopN)
           d.DefectCode, m.DefectName, SUM(ISNULL(d.Qty,1)) AS Count
    FROM   dbo.PR_DefectDetail d
    LEFT JOIN dbo.MD_DefectCode m ON m.DefectCode = d.DefectCode
    WHERE  d.DetectedAt >= dbo.FN_POP_DayStart(dbo.FN_POP_ProdToday()) AND d.DetectedAt < dbo.FN_POP_DayStart(DATEADD(day,1,dbo.FN_POP_ProdToday()))
    GROUP BY d.DefectCode, m.DefectName
    ORDER BY SUM(ISNULL(d.Qty,1)) DESC;
END;
GO

-- ===== Qc.ListInspectionStds (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_ListInspectionStds
AS
BEGIN
    SELECT s.StdID, s.StdCode, s.VerNo, s.StdName, s.InsType,
           s.ItemNo, m.ItemName, ISNULL(s.AQLLevel,0) AS AQLLevel,
           ISNULL(s.SampleInterval,0) AS SampleInterval, s.InspItemsJSON,
           s.Status, s.EffectiveDate
    FROM   dbo.QC_InspectionStd s
    LEFT JOIN dbo.MD_Item m ON m.ItemNo = s.ItemNo
    WHERE  ISNULL(s.Status,'Active') = 'Active'
    ORDER BY s.StdCode, s.VerNo DESC;
END;
GO

-- ===== 자재 LOT 사용 기록 (10-28) =====
-- 완제품 LOT 이 쓴 자재 LOT 의 단일 기준. 지금은 자동 차감 장부(WH_InventoryTransaction BACKFLUSH·BACKFLUSH_NET,
-- SourceID = 완제품 tbl_Lot.LotID)에서 만든다 — FIFO 장부상 LOT 이고 실제 투입 스캔이 아니다.
-- 자재 투입 스캔을 만들면 이 뷰만 그 기록으로 바꾸거나 UNION 하면 된다(QC-TRC 프로시저는 이 뷰만 읽는다).
-- Qty 는 사용량(+). 자리표시(BF-) 행은 차감 − 메우기라 합이 아직 실물 LOT 을 모르는 양(Basis UNRESOLVED)이다.
-- 메우기 행은 부족을 만든 원래 LOT 으로 기록된다(BackflushRepository, 10-28 — 그 전 행은 메운 LOT 쪽에 남아 있다).
CREATE OR ALTER VIEW dbo.VW_MaterialLotUsage
AS
SELECT t.SourceID      AS FinishedLotID,
       t.PartNo,
       t.LotNo         AS MaterialLotNo,
       -t.QtyChange    AS Qty,
       t.LocationNo,
       t.TransactionTime AS UsedAt,
       CAST(CASE WHEN t.LotNo LIKE 'BF-%' THEN 'UNRESOLVED' ELSE 'LEDGER' END AS VARCHAR(10)) AS Basis,
       t.TransactionID
FROM   dbo.WH_InventoryTransaction t
WHERE  t.SourceID IS NOT NULL
  AND ((t.SourceType = 'BACKFLUSH' AND t.TransactionType = 'OUT') OR t.SourceType = 'BACKFLUSH_NET');
GO

-- ===== Qc.TraceMaterialLot (QC, 10-28) =====
-- 자재 LOT(WH_Inventory.LotNo) → 그 LOT 을 쓴 완제품 LOT. tbl_Lot 에 없는 코드일 때 QC-TRC 가 부른다.
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_TraceMaterialLot
    @M NVARCHAR(50)
AS
BEGIN
    SELECT r.Kind, r.RefID, r.Title, r.Subtitle, r.OccurredAt
    FROM  (SELECT TOP 1 'MATLOT' AS Kind, t.LotNo AS RefID, t.PartNo AS Title,
                  CONCAT(ISNULL(i.ItemName, ''), ' · ', ISNULL(w.LocationNo, '—'), ' · ',
                         CAST(CAST(ISNULL(w.Qty, 0) AS DECIMAL(18,3)) AS FLOAT)) AS Subtitle,
                  w.ReceivedAt AS OccurredAt
           FROM   dbo.WH_InventoryTransaction t
           LEFT JOIN dbo.WH_Inventory w ON w.LotNo = t.LotNo
           LEFT JOIN dbo.MD_Item i ON i.ItemNo = t.PartNo
           WHERE  t.LotNo = @M
           ORDER  BY t.TransactionID) r

    UNION ALL
    SELECT 'LOT', CAST(l.LotID AS VARCHAR(24)), l.LotCode,
           CONCAT(l.ItemNo, ' · ', CAST(CAST(SUM(u.Qty) AS DECIMAL(18,3)) AS FLOAT), ' ', MAX(u.PartNo), ' · ', ISNULL(l.Status,'')),
           MAX(u.UsedAt)
    FROM   dbo.VW_MaterialLotUsage u
    JOIN   dbo.tbl_Lot l ON l.LotID = u.FinishedLotID
    WHERE  u.MaterialLotNo = @M
    GROUP  BY l.LotID, l.LotCode, l.ItemNo, l.Status
    HAVING SUM(u.Qty) > 0;
END;
GO

-- ===== Qc.TraceLot (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_TraceLot
    @L INT
AS
BEGIN
    -- ROOT
    SELECT 'LOT' AS Kind, CAST(l.LotID AS VARCHAR(24)) AS RefID,
           l.LotCode AS Title,
           CONCAT(l.ItemNo, ' · ', ISNULL(l.Status,'')) AS Subtitle,
           l.ProducedAt AS OccurredAt
    FROM   dbo.tbl_Lot l WHERE l.LotID = @L

    UNION ALL
    -- WO (parent)
    SELECT 'WO', CAST(w.WoID AS VARCHAR(24)), w.WoNumber,
           CONCAT(w.ItemNo, ' · ', w.Status),
           w.ReleasedAt
    FROM   dbo.tbl_Lot l
    JOIN   dbo.PP_WorkOrder w ON w.WoID = l.WoID
    WHERE  l.LotID = @L

    UNION ALL
    -- Item master
    SELECT 'ITEM', l.ItemNo, m.ItemName,
           CONCAT(ISNULL(m.ItemType,'?'),' · ', m.ItemCategory), NULL
    FROM   dbo.tbl_Lot l
    JOIN   dbo.MD_Item  m ON m.ItemNo = l.ItemNo
    WHERE  l.LotID = @L

    UNION ALL
    -- Sibling lots from same WO (downstream within prod)
    SELECT 'LOT', CAST(s.LotID AS VARCHAR(24)), s.LotCode,
           CONCAT(s.ItemNo, ' · ', ISNULL(s.Status,'')),
           s.ProducedAt
    FROM   dbo.tbl_Lot l
    JOIN   dbo.tbl_Lot s ON s.WoID = l.WoID AND s.LotID <> l.LotID
    WHERE  l.LotID = @L

    UNION ALL
    -- Core LOT this lot was built on (IMG 코어 스캔 — tbl_Lot.ParentLotID) and lots built on this one
    SELECT 'CORE', CAST(p.LotID AS VARCHAR(24)), p.LotCode,
           CONCAT(p.ItemNo, ' · ', ISNULL(p.Status,'')), p.ProducedAt
    FROM   dbo.tbl_Lot l JOIN dbo.tbl_Lot p ON p.LotID = l.ParentLotID
    WHERE  l.LotID = @L

    UNION ALL
    SELECT 'USED-IN', CAST(c.LotID AS VARCHAR(24)), c.LotCode,
           CONCAT(c.ItemNo, ' · ', ISNULL(c.Status,'')), c.ProducedAt
    FROM   dbo.tbl_Lot c WHERE c.ParentLotID = @L

    UNION ALL
    -- Material LOTs consumed (VW_MaterialLotUsage — 자재 LOT 추적의 단일 기준). 자리표시(BF-)에 남은 양은 자재 LOT 미확정
    SELECT CASE WHEN u.Basis = 'UNRESOLVED' THEN 'MAT-SHORT' ELSE 'MATERIAL' END,
           u.MaterialLotNo, u.PartNo,
           CONCAT(CAST(CAST(SUM(u.Qty) AS DECIMAL(18,3)) AS FLOAT), ' · ', MAX(u.LocationNo), ' · ', u.Basis),
           MAX(u.UsedAt)
    FROM   dbo.VW_MaterialLotUsage u
    WHERE  u.FinishedLotID = @L
    GROUP  BY u.MaterialLotNo, u.PartNo, u.Basis
    HAVING SUM(u.Qty) > 0

    UNION ALL
    -- NCRs touching this lot
    SELECT 'NCR', n.NcrNumber, CONCAT(n.Severity,' · ',n.Status),
           ISNULL(n.Disposition,'?'), n.ReportedAt
    FROM   dbo.QC_NCR n
    WHERE  n.LotID = @L

    UNION ALL
    -- Holds
    SELECT 'HOLD', h.HoldNumber, CONCAT(h.Severity,' · ',h.Status),
           ISNULL(h.PhysicalLocation,'?'), h.HeldAt
    FROM   dbo.QC_Hold h WHERE h.LotID = @L
    ;
END;
GO

-- ===== Qc.FindLotIdByCode (QC) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_QC_FindLotIdByCode
    @C VARCHAR(40)
AS
BEGIN
    SELECT TOP 1 LotID FROM dbo.tbl_Lot WHERE LotCode = @C;
END;
GO

-- ===== InjLot.CreateManualRawLots (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_CreateManualRawLots_GetMoldItem
    @Mold VARCHAR(20),
    @Item VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 m.MoldCodeClean, mi.Color, mi.CavitySeq, mi.CavityPos
    FROM   dbo.MD_MoldItem mi
    JOIN   dbo.MD_Mold m ON m.MoldID = mi.MoldID
    WHERE  mi.MoldID = @Mold AND mi.ItemNo = @Item AND mi.ActiveFlag = 1
    ORDER  BY mi.CavitySeq;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_CreateManualRawLots_GetItem
    @Item VARCHAR(20)
AS
BEGIN
    SELECT ItemName FROM dbo.MD_Item WHERE ItemNo = @Item;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_CreateManualRawLots_GetEquipment
    @L VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 EquipID FROM dbo.MD_Equipment
    WHERE  LineID = @L AND ISNULL(ActiveFlag,1) = 1
    ORDER  BY EquipID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_CreateManualRawLots_InsLot
    @LotCode VARCHAR(40),
    @ItemNo VARCHAR(20),
    @LineID VARCHAR(20),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty,
         ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
    OUTPUT INSERTED.LotID, INSERTED.CreatedTS
    VALUES
        (@LotCode, @ItemNo, NULL, @LineID, 'INJ', 1, 1,
         SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_CreateManualRawLots_InsInjLot
    @LotID INT,
    @Equip VARCHAR(20),
    @Mold VARCHAR(20),
    @Color VARCHAR(10),
    @MoldID VARCHAR(20),
    @CavNo INT,
    @CavPos VARCHAR(4),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_InjLot
        (LotID, EquipID, MoldCode, ColorCode, MoldID, CavityNo, CavityPos,
         PressType, MachineShotCount, ConfirmStatus, CreatedBy, CreatedTS)
    VALUES
        (@LotID, @Equip, @Mold, @Color, @MoldID, @CavNo, @CavPos,
         NULL, NULL, 'RAW', @By, SYSDATETIME());
END;
GO

-- ===== InjLot.GetUnconfirmed (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_GetUnconfirmed
    @Line VARCHAR(20),
    @Top INT
AS
BEGIN
    SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, l.LineID,
           e.EquipID, e.MoldCode, e.ColorCode, e.MoldID, e.CavityNo, e.CavityPos,
           e.PressType, e.ConfirmStatus, e.MachineShotCount, e.PrintedCount, l.CreatedTS,
           ri.OverallNg,
           CASE WHEN ri.InspectionID IS NULL THEN NULL ELSE
             LTRIM(STUFF(
               CASE WHEN ri.ShortMold = 'NG' THEN N', 미성형 NG'   ELSE N'' END +
               CASE WHEN ri.WeldLine  = 'NG' THEN N', 웰드라인 NG' ELSE N'' END +
               CASE WHEN ri.Gas       = 'NG' THEN N', 가스 NG'     ELSE N'' END +
               CASE WHEN ri.Weight    = 'NG' THEN N', 중량 NG'     ELSE N'' END,
             1, 1, N'')) END AS InspectionSummary
    FROM   dbo.tbl_Lot l
    JOIN   dbo.PR_InjLot e ON e.LotID = l.LotID
    LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
    OUTER  APPLY (SELECT TOP 1 * FROM dbo.PR_RobotInspection r
                  WHERE r.LotID = l.LotID ORDER BY r.InspectionID DESC) ri
    WHERE  l.LineID = @Line AND e.ConfirmStatus IN ('RAW','NG_BLOCKED')
    ORDER  BY l.CreatedTS DESC
    OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY;
END;
GO

-- ===== InjLot.GetByLotCode (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_GetByLotCode
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, l.LineID,
           e.EquipID, e.MoldCode, e.ColorCode, e.MoldID, e.CavityNo, e.CavityPos,
           e.PressType, e.ConfirmStatus, e.MachineShotCount, e.PrintedCount, l.CreatedTS,
           ri.OverallNg,
           CASE WHEN ri.InspectionID IS NULL THEN NULL ELSE
             LTRIM(STUFF(
               CASE WHEN ri.ShortMold = 'NG' THEN N', 미성형 NG'   ELSE N'' END +
               CASE WHEN ri.WeldLine  = 'NG' THEN N', 웰드라인 NG' ELSE N'' END +
               CASE WHEN ri.Gas       = 'NG' THEN N', 가스 NG'     ELSE N'' END +
               CASE WHEN ri.Weight    = 'NG' THEN N', 중량 NG'     ELSE N'' END,
             1, 1, N'')) END AS InspectionSummary
    FROM   dbo.tbl_Lot l
    JOIN   dbo.PR_InjLot e ON e.LotID = l.LotID
    LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
    OUTER  APPLY (SELECT TOP 1 * FROM dbo.PR_RobotInspection r
                  WHERE r.LotID = l.LotID ORDER BY r.InspectionID DESC) ri
    WHERE  l.LotCode = @Code;
END;
GO

-- ===== InjLot.IncrementPrintedCount (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_IncrementPrintedCount
    @By VARCHAR(20),
    @L INT
AS
BEGIN
    UPDATE dbo.PR_InjLot
    SET    PrintedCount = PrintedCount + 1, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
    OUTPUT INSERTED.PrintedCount
    WHERE  LotID = @L;
END;
GO

-- ===== InjLot.DecrementPrintedCount (INJ) — 출력 실패 시 선행 카운트 되돌리기 (10-26) =====
-- 카운트는 출력 전에 올린다. 출력이 실패하면 여기서 되돌려 클레임이 다시 잡을 수 있게 한다.
-- PrintedCount 0 이하로는 내려가지 않는다.
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_DecrementPrintedCount
    @By VARCHAR(20),
    @L INT
AS
BEGIN
    UPDATE dbo.PR_InjLot
    SET    PrintedCount = PrintedCount - 1, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
    OUTPUT INSERTED.PrintedCount
    WHERE  LotID = @L AND PrintedCount > 0;
END;
GO

-- ===== InjLot.GetMaxLotId (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_GetMaxLotId
    @Line VARCHAR(20)
AS
BEGIN
    SELECT ISNULL(MAX(LotID),0) FROM dbo.tbl_Lot WHERE LineID = @Line AND ProcessCode = 'INJ';
END;
GO

-- ===== InjLot.ClaimForPrint (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ClaimForPrint
    @Top INT,
    @Station VARCHAR(20),
    @Line VARCHAR(20),
    @After INT,
    @Stale INT
AS
BEGIN
    DECLARE @claimed TABLE (LotID INT);

    -- 오래된 LOT 부터 — TOP 만으로는 어느 5개를 잡을지 정해지지 않아 라벨이 뒤섞여 나온다.
    ;WITH pick AS (
        SELECT TOP (@Top) e.LotID, e.PrintClaimTS, e.PrintClaimStation
        FROM   dbo.PR_InjLot e
        JOIN   dbo.tbl_Lot   l ON l.LotID = e.LotID
        WHERE  l.LineID       = @Line
          AND  e.LotID        > @After
          AND  e.PrintedCount = 0
          AND  (e.PrintClaimTS IS NULL
                OR e.PrintClaimTS < DATEADD(second, -@Stale, SYSDATETIME()))
        ORDER BY e.LotID
    )
    UPDATE pick
    SET    PrintClaimTS = SYSDATETIME(), PrintClaimStation = @Station
    OUTPUT INSERTED.LotID INTO @claimed;
    SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, l.LineID,
           e.EquipID, e.MoldCode, e.ColorCode, e.MoldID, e.CavityNo, e.CavityPos,
           e.PressType, e.ConfirmStatus, e.MachineShotCount, e.PrintedCount, l.CreatedTS,
           ri.OverallNg,
           CASE WHEN ri.InspectionID IS NULL THEN NULL ELSE
             LTRIM(STUFF(
               CASE WHEN ri.ShortMold = 'NG' THEN N', 미성형 NG'   ELSE N'' END +
               CASE WHEN ri.WeldLine  = 'NG' THEN N', 웰드라인 NG' ELSE N'' END +
               CASE WHEN ri.Gas       = 'NG' THEN N', 가스 NG'     ELSE N'' END +
               CASE WHEN ri.Weight    = 'NG' THEN N', 중량 NG'     ELSE N'' END,
             1, 1, N'')) END AS InspectionSummary
    FROM   dbo.tbl_Lot l
    JOIN   dbo.PR_InjLot e ON e.LotID = l.LotID
    LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
    OUTER  APPLY (SELECT TOP 1 * FROM dbo.PR_RobotInspection r
                  WHERE r.LotID = l.LotID ORDER BY r.InspectionID DESC) ri
    WHERE  l.LotID IN (SELECT LotID FROM @claimed)
    ORDER  BY l.LotID;
END;
GO

-- ===== InjLot.ReleasePrintClaim (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ReleasePrintClaim
    @L INT,
    @Station VARCHAR(20)
AS
BEGIN
    UPDATE dbo.PR_InjLot
    SET    PrintClaimTS = NULL, PrintClaimStation = NULL
    WHERE  LotID = @L AND PrintedCount = 0 AND PrintClaimStation = @Station;
END;
GO

-- ===== InjLot.ConfirmByLotCode (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ConfirmByLotCode_GetLot
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.ItemNo, l.LineID, l.CreatedTS,
           e.ConfirmStatus, e.EquipID, e.MoldID, e.CavityPos
    FROM   dbo.tbl_Lot l WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PR_InjLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
    WHERE  l.LotCode = @Code;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ConfirmByLotCode_GetWorkOrderRouting
    @Line VARCHAR(20),
    @Item VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 r.WoID, r.RoutingLineID
    FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
    WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = @Item
      AND          r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')
    ORDER  BY         CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID
    ;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ConfirmByLotCode_GetLot2
    @ThisTs DATETIME2,
    @Equip VARCHAR(20),
    @Pos VARCHAR(4),
    @ThisLot INT = NULL
AS
BEGIN
    -- @ThisLot 없는 구 Pop 호출은 종전 계산. 오프라인 재생 LOT 은 LotID 와 CreatedTS 순서가 어긋날 수 있어 CreatedTS 조건을 남긴다
    IF @ThisLot IS NULL
        SELECT ISNULL(DATEDIFF(SECOND, MAX(pl.CreatedTS), @ThisTs), 0)
        FROM   dbo.tbl_Lot pl
        JOIN   dbo.PR_InjLot pe ON pe.LotID = pl.LotID
        WHERE  pe.EquipID = @Equip AND pe.CavityPos = @Pos AND pl.CreatedTS < @ThisTs;
    ELSE
        SELECT ISNULL((SELECT TOP 1 DATEDIFF(SECOND, pl.CreatedTS, @ThisTs)
                       FROM   dbo.PR_InjLot pe
                       JOIN   dbo.tbl_Lot pl ON pl.LotID = pe.LotID
                       WHERE  pe.EquipID = @Equip AND pe.CavityPos = @Pos
                         AND  pe.LotID < @ThisLot AND pl.CreatedTS < @ThisTs
                       ORDER  BY pe.LotID DESC), 0);
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ConfirmByLotCode_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @CT INT,
    @Mold VARCHAR(20),
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         MoldID, OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
    OUTPUT INSERTED.ResultID
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, 'INJ', 1, @CT,
         @Mold, @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ConfirmByLotCode_UpdLot
    @WoID INT,
    @Op NVARCHAR(450),
    @LotID INT,
    @Sess INT
AS
BEGIN
    UPDATE dbo.tbl_Lot
    SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;

    UPDATE dbo.PR_InjLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;
END;
GO

-- ===== InjLot.RegisterDefect (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_RegisterDefect_GetLot
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.ItemNo, l.LineID, e.ConfirmStatus,
           CASE WHEN ri.InspectionID IS NULL THEN NULL ELSE
             LTRIM(STUFF(
               CASE WHEN ri.ShortMold = 'NG' THEN N', 미성형 NG'   ELSE N'' END +
               CASE WHEN ri.WeldLine  = 'NG' THEN N', 웰드라인 NG' ELSE N'' END +
               CASE WHEN ri.Gas       = 'NG' THEN N', 가스 NG'     ELSE N'' END +
               CASE WHEN ri.Weight    = 'NG' THEN N', 중량 NG'     ELSE N'' END,
             1, 1, N'')) END AS InspectionSummary,
           -- IMG 가 이 코어로 만든 완제품 LOT — 있으면 코어만 불량 처리하지 않는다(10-28)
           (SELECT TOP 1 c.LotCode FROM dbo.tbl_Lot c WHERE c.ParentLotID = l.LotID ORDER BY c.LotID) AS UsedByLotCode,
           -- 출하된 LOT(그 LOT 또는 실렸던 팔레트의 출하 기록이 있고 양수 재고 행이 없음) — 라인 불량으로 받지 않는다(10-28)
           CAST(CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory iq WHERE iq.LotNo = l.LotCode AND iq.Qty > 0)
                      AND (EXISTS (SELECT 1 FROM dbo.FG_Shipment s WHERE s.LotNo = l.LotCode)
                           OR EXISTS (SELECT 1 FROM dbo.WH_Inventory ip JOIN dbo.FG_Shipment s ON s.LotNo = ip.PalletNo OR s.PalletNo = ip.PalletNo
                                      WHERE ip.LotNo = l.LotCode AND ip.PalletNo IS NOT NULL))
                     THEN 1 ELSE 0 END AS bit) AS ShippedFlag
    FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PR_InjLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
    OUTER  APPLY (SELECT TOP 1 * FROM dbo.PR_RobotInspection r
                  WHERE r.LotID = l.LotID ORDER BY r.InspectionID DESC) ri
    WHERE  l.LotCode = @Code;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_RegisterDefect_UpdInjLot
    @Op VARCHAR(20),
    @Lot INT
AS
BEGIN
    UPDATE dbo.PR_InjLot
    SET    ConfirmStatus = 'DEFECT', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @Lot;
    -- LOT 마스터 상태도 DEFECT — 확정(CONFIRMED)·폐기(SCRAP)·수리 양품(CONFIRMED)과 맞춘다. 전에는 CONFIRMED 로 남아 추적성에 양품으로 보였다(10-28)
    UPDATE dbo.tbl_Lot
    SET    Status = 'DEFECT', QualityFlag = 'NG', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @Lot;
END;
GO

-- ===== InjLot.GetTodayStats (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_GetTodayStats
    @Line VARCHAR(20)
AS
BEGIN
    SELECT
      (SELECT COUNT(*) FROM dbo.tbl_Lot l JOIN dbo.PR_InjLot e ON e.LotID = l.LotID
       WHERE l.LineID = @Line AND e.CavityNo = 1
         AND l.CreatedTS >= dbo.FN_POP_DayStart(dbo.FN_POP_ProdToday())
         AND l.CreatedTS <  dbo.FN_POP_DayStart(DATEADD(day, 1, dbo.FN_POP_ProdToday()))) AS TodayShots,
      (SELECT COUNT(*) FROM dbo.tbl_Lot l JOIN dbo.PR_InjLot e ON e.LotID = l.LotID
       WHERE l.LineID = @Line AND e.ConfirmStatus = 'RAW') AS RawCount,
      (SELECT COUNT(*) FROM dbo.tbl_Lot l JOIN dbo.PR_InjLot e ON e.LotID = l.LotID
       WHERE l.LineID = @Line AND e.ConfirmStatus = 'NG_BLOCKED') AS NgBlocked;
END;
GO

-- ===== InjLot.GetDailyItemSummary (INJ) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_GetDailyItemSummary
    @Station VARCHAR(20),
    @Line VARCHAR(20),
    @Today DATE
AS
BEGIN
    WITH bop AS (
        SELECT DISTINCT b.ItemNo
        FROM   dbo.MD_Bop b
        WHERE  b.StationCode = @Station AND ISNULL(b.ActiveFlag,1) = 1
    ),
    sched AS (
        SELECT COALESCE(s.ItemNo, st.ItemNo, w.ItemNo) AS ItemNo, SUM(ISNULL(s.PlannedQty,0)) AS PlanQty
        FROM   dbo.PP_LineSchedule s
        JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
        OUTER  APPLY (SELECT TOP 1 r.ItemNo FROM dbo.PP_WorkOrderRouting r
                      WHERE  r.WoID = s.WoID AND r.LineID = s.LineID ORDER BY r.StepSeq) st
        WHERE  s.LineID = @Line AND s.ScheduleDate = @Today AND s.EntryType = 'WO'
          AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
        GROUP  BY COALESCE(s.ItemNo, st.ItemNo, w.ItemNo)
    ),
    lots AS (
        SELECT l.ItemNo,
               COUNT(*)                                                                               AS InputQty,
               SUM(CASE WHEN e.ConfirmStatus = 'CONFIRMED' THEN 1 ELSE 0 END)                         AS ConfirmedQty,
               SUM(CASE WHEN e.ConfirmStatus IN ('NG_BLOCKED','DEFECT','SCRAPPED') THEN 1 ELSE 0 END) AS NgQty,
               SUM(CASE WHEN e.ConfirmStatus = 'RAW' THEN 1 ELSE 0 END)                               AS PendingQty
        FROM   dbo.tbl_Lot   l
        JOIN   dbo.PR_InjLot e ON e.LotID = l.LotID
        WHERE  l.LineID = @Line
          AND  l.CreatedTS >= dbo.FN_POP_DayStart(@Today) AND l.CreatedTS < dbo.FN_POP_DayStart(DATEADD(day, 1, @Today))
        GROUP  BY l.ItemNo
    ),
    itemkeys AS (
        SELECT ItemNo FROM bop
        UNION SELECT ItemNo FROM sched
        UNION SELECT ItemNo FROM lots
    )
    SELECT k.ItemNo,
           COALESCE(i.ItemName, N'')  AS ItemName,
           ISNULL(p.PlanQty, 0)       AS PlanQty,
           ISNULL(t.InputQty, 0)      AS InputQty,
           ISNULL(t.ConfirmedQty, 0)  AS ConfirmedQty,
           ISNULL(t.NgQty, 0)         AS NgQty,
           ISNULL(t.PendingQty, 0)    AS PendingQty,
           CASE WHEN b.ItemNo IS NULL THEN 0 ELSE 1 END AS InBop,
           CASE WHEN EXISTS (
                SELECT 1
                FROM   dbo.PP_WorkOrderRouting r
                JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
                WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = k.ItemNo
                  AND  r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')) THEN 1 ELSE 0 END AS HasOpenWo
    FROM   itemkeys k
    LEFT JOIN dbo.MD_Item i ON i.ItemNo = k.ItemNo
    LEFT JOIN bop    b ON b.ItemNo = k.ItemNo
    LEFT JOIN sched  p ON p.ItemNo = k.ItemNo
    LEFT JOIN lots   t ON t.ItemNo = k.ItemNo
    ORDER  BY InBop DESC, k.ItemNo;
END;
GO

-- ===== ImgLot.CreateFromCore (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_CreateFromCore_GetLot
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.ItemNo, e.ConfirmStatus
    FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PR_InjLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
    WHERE  l.LotCode = @Code AND l.ProcessCode = 'INJ';
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_CreateFromCore_GetWorkOrderRouting
    @Line VARCHAR(20),
    @Fg VARCHAR(20),
    @CoreProc VARCHAR(10),
    @Core VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 r.WoID, w.ItemNo
    FROM   dbo.PP_WorkOrderRouting r
    JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
    WHERE  r.LineID = @Line
      AND  (@Fg IS NULL OR w.ItemNo = @Fg)
      AND  EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting c
                   WHERE  c.WoID = w.WoID AND c.ProcessCode = @CoreProc
                     AND  COALESCE(c.ItemNo, w.ItemNo) = @Core)
      AND          r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')
    ORDER  BY         CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID
    ;
END;
GO

-- ===== ImgLot.ItemExists (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_ItemExists
    @I VARCHAR(20)
AS
BEGIN
    SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @I AND ISNULL(ActiveFlag,1) = 1;
END;
GO

-- ===== ImgLot.FindCoreUser (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_FindCoreUser
    @Core INT
AS
BEGIN
    SELECT TOP 1 LotCode FROM dbo.tbl_Lot
    WHERE  ParentLotID = @Core AND ProcessCode = 'IMG'
    ORDER  BY LotID;
END;
GO

-- ===== ImgLot.InsertRawLot (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_InsertRawLot_GetItem
    @Item VARCHAR(20)
AS
BEGIN
    SELECT ItemName, PGN, ALC, MountPos FROM dbo.MD_Item WHERE ItemNo = @Item;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_InsertRawLot_GetWorkOrderRouting
    @Line VARCHAR(20),
    @Item VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 c.CustomerCode
    FROM   dbo.PP_WorkOrderRouting r
    JOIN   dbo.PP_WorkOrder        w  ON w.WoID  = r.WoID
    LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
    LEFT JOIN dbo.MD_Customer      c  ON c.CustomerID = so.CustomerID
    WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = @Item
      AND          r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')
    ORDER  BY         CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID
    ;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_InsertRawLot_GetWorkOrder
    @Wo INT
AS
BEGIN
    SELECT c.CustomerCode
    FROM   dbo.PP_WorkOrder        w
    LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
    LEFT JOIN dbo.MD_Customer      c  ON c.CustomerID = so.CustomerID
    WHERE  w.WoID = @Wo;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_InsertRawLot_GetEquipment
    @L VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 EquipID FROM dbo.MD_Equipment
    WHERE  LineID = @L AND ISNULL(ActiveFlag,1) = 1
    ORDER  BY EquipID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_InsertRawLot_InsLot
    @LotCode VARCHAR(40),
    @ItemNo VARCHAR(20),
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @Parent INT,
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty, ParentLotID,
         ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
    OUTPUT INSERTED.LotID, INSERTED.CreatedTS
    VALUES
        (@LotCode, @ItemNo, NULL, @LineID, @Proc, 1, 1, @Parent,
         SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_InsertRawLot_InsImgLot
    @LotID INT,
    @Equip VARCHAR(20),
    @Cust VARCHAR(20),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ImgLot (LotID, EquipID, CustomerCode, ConfirmStatus, PrintedCount, CreatedBy, CreatedTS)
    VALUES (@LotID, @Equip, @Cust, 'RAW', 0, @By, SYSDATETIME());
END;
GO

-- ===== ImgLot.GetTodayLots (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_GetTodayLots
    @Line VARCHAR(20),
    @Top INT
AS
BEGIN
    SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, mi.PGN, mi.ALC, mi.MountPos, l.LineID,
           e.EquipID, e.CustomerCode, e.ConfirmStatus, e.ConfirmedAt,
           e.FabricRollLotID, e.FabricConsumedM, e.BondSetupID,
           e.PrintedCount, l.CreatedTS, pl.LotCode AS CoreLotCode
    FROM   dbo.tbl_Lot l
    JOIN   dbo.PR_ImgLot e ON e.LotID = l.LotID
    LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
    LEFT   JOIN dbo.tbl_Lot pl ON pl.LotID = l.ParentLotID
    WHERE  l.LineID = @Line
      AND  l.CreatedTS >= dbo.FN_POP_DayStart(dbo.FN_POP_ProdToday())
      AND  l.CreatedTS <  dbo.FN_POP_DayStart(DATEADD(day, 1, dbo.FN_POP_ProdToday()))
    ORDER  BY l.CreatedTS DESC, l.LotID DESC
    OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY;
END;
GO

-- ===== ImgLot.GetByLotCode (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_GetByLotCode
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.LotCode, l.ItemNo, mi.ItemName, mi.PGN, mi.ALC, mi.MountPos, l.LineID,
           e.EquipID, e.CustomerCode, e.ConfirmStatus, e.ConfirmedAt,
           e.FabricRollLotID, e.FabricConsumedM, e.BondSetupID,
           e.PrintedCount, l.CreatedTS, pl.LotCode AS CoreLotCode
    FROM   dbo.tbl_Lot l
    JOIN   dbo.PR_ImgLot e ON e.LotID = l.LotID
    LEFT   JOIN dbo.MD_Item mi ON mi.ItemNo = l.ItemNo
    LEFT   JOIN dbo.tbl_Lot pl ON pl.LotID = l.ParentLotID
    WHERE l.LotCode = @Code;
END;
GO

-- ===== ImgLot.IncrementPrintedCount (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_IncrementPrintedCount
    @By VARCHAR(20),
    @LotID INT
AS
BEGIN
    UPDATE dbo.PR_ImgLot
    SET    PrintedCount = PrintedCount + 1, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
    OUTPUT INSERTED.PrintedCount
    WHERE  LotID = @LotID;
END;
GO

-- ===== ImgLot.RegisterDefect (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_RegisterDefect_GetLot
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.ItemNo, l.LineID, e.ConfirmStatus,
           CAST(NULL AS varchar(40)) AS UsedByLotCode,
           -- 출하된 LOT(그 LOT 또는 실렸던 팔레트의 출하 기록이 있고 양수 재고 행이 없음) — 라인 불량으로 받지 않는다(10-28)
           CAST(CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory iq WHERE iq.LotNo = l.LotCode AND iq.Qty > 0)
                      AND (EXISTS (SELECT 1 FROM dbo.FG_Shipment s WHERE s.LotNo = l.LotCode)
                           OR EXISTS (SELECT 1 FROM dbo.WH_Inventory ip JOIN dbo.FG_Shipment s ON s.LotNo = ip.PalletNo OR s.PalletNo = ip.PalletNo
                                      WHERE ip.LotNo = l.LotCode AND ip.PalletNo IS NOT NULL))
                     THEN 1 ELSE 0 END AS bit) AS ShippedFlag
    FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PR_ImgLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
    WHERE  l.LotCode = @Code;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_RegisterDefect_UpdImgLot
    @Op VARCHAR(20),
    @Lot INT
AS
BEGIN
    UPDATE dbo.PR_ImgLot
    SET    ConfirmStatus = 'DEFECT', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @Lot;
    -- LOT 마스터 상태도 DEFECT — 확정(CONFIRMED)·폐기(SCRAP)·수리 양품(CONFIRMED)과 맞춘다. 전에는 CONFIRMED 로 남아 추적성에 양품으로 보였다(10-28)
    UPDATE dbo.tbl_Lot
    SET    Status = 'DEFECT', QualityFlag = 'NG', ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @Lot;
END;
GO

-- ===== ImgLot.ConfirmByLotCode (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_ConfirmByLotCode_GetLot
    @Code VARCHAR(40)
AS
BEGIN
    SELECT l.LotID, l.ItemNo, l.LineID, l.CreatedTS, e.ConfirmStatus, e.CustomerCode
    FROM   dbo.tbl_Lot   l WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PR_ImgLot e WITH (UPDLOCK, ROWLOCK) ON e.LotID = l.LotID
    WHERE  l.LotCode = @Code;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_ConfirmByLotCode_GetWorkOrderRouting
    @Line VARCHAR(20),
    @Item VARCHAR(20),
    @Cust VARCHAR(20) = NULL
AS
BEGIN
    -- 라벨에 찍힌 수주처(발행 때 박아 둔 CustomerCode)와 같은 수주의 WO 를 먼저 — 라벨과 실적의 WO 가 갈리지 않게(10-28)
    SELECT TOP 1 r.WoID, r.RoutingLineID
    FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
    LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
    LEFT JOIN dbo.MD_Customer      c  ON c.CustomerID = so.CustomerID
    WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = @Item
      AND          r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')
    ORDER  BY         CASE WHEN @Cust IS NOT NULL AND c.CustomerCode = @Cust THEN 0 ELSE 1 END,
                      CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID
    ;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_ConfirmByLotCode_GetLot2
    @ThisTs DATETIME2,
    @Line VARCHAR(20)
AS
BEGIN
    SELECT ISNULL(DATEDIFF(SECOND, MAX(pl.CreatedTS), @ThisTs), 0)
    FROM   dbo.tbl_Lot pl
    JOIN   dbo.PR_ImgLot pe ON pe.LotID = pl.LotID
    WHERE  pl.LineID = @Line AND pl.CreatedTS < @ThisTs;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_ConfirmByLotCode_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @CT INT,
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
    OUTPUT INSERTED.ResultID
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, @Proc, 1, @CT,
         @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_ConfirmByLotCode_UpdLot
    @WoID INT,
    @Op NVARCHAR(450),
    @LotID INT,
    @Sess INT
AS
BEGIN
    UPDATE dbo.tbl_Lot
    SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;

    UPDATE dbo.PR_ImgLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;
END;
GO

-- ===== ImgLot.GetDailyItemSummary (IMG) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_GetDailyItemSummary
    @Station VARCHAR(20),
    @Line VARCHAR(20),
    @Today DATE
AS
BEGIN
    WITH bop AS (
        SELECT DISTINCT b.ItemNo
        FROM   dbo.MD_Bop b
        WHERE  b.StationCode = @Station AND ISNULL(b.ActiveFlag,1) = 1
    ),
    sched AS (
        SELECT COALESCE(s.ItemNo, st.ItemNo, w.ItemNo) AS ItemNo, SUM(ISNULL(s.PlannedQty,0)) AS PlanQty
        FROM   dbo.PP_LineSchedule s
        JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
        OUTER  APPLY (SELECT TOP 1 r.ItemNo FROM dbo.PP_WorkOrderRouting r
                      WHERE  r.WoID = s.WoID AND r.LineID = s.LineID ORDER BY r.StepSeq) st
        WHERE  s.LineID = @Line AND s.ScheduleDate = @Today AND s.EntryType = 'WO'
          AND  ISNULL(w.Status,'Draft') <> 'Cancelled'
        GROUP  BY COALESCE(s.ItemNo, st.ItemNo, w.ItemNo)
    ),
    lots AS (
        SELECT l.ItemNo,
               COUNT(*)                                                                  AS InputQty,
               SUM(CASE WHEN e.ConfirmStatus = 'CONFIRMED' THEN 1 ELSE 0 END)            AS ConfirmedQty,
               SUM(CASE WHEN e.ConfirmStatus IN ('DEFECT','SCRAPPED') THEN 1 ELSE 0 END) AS NgQty,
               SUM(CASE WHEN e.ConfirmStatus = 'RAW'       THEN 1 ELSE 0 END)            AS PendingQty
        FROM   dbo.tbl_Lot   l
        JOIN   dbo.PR_ImgLot e ON e.LotID = l.LotID
        WHERE  l.LineID = @Line
          AND  l.CreatedTS >= dbo.FN_POP_DayStart(@Today) AND l.CreatedTS < dbo.FN_POP_DayStart(DATEADD(day, 1, @Today))
        GROUP  BY l.ItemNo
    ),
    itemkeys AS (
        SELECT ItemNo FROM bop
        UNION SELECT ItemNo FROM sched
        UNION SELECT ItemNo FROM lots
    )
    SELECT k.ItemNo,
           COALESCE(i.ItemName, N'')  AS ItemName,
           ISNULL(p.PlanQty, 0)       AS PlanQty,
           ISNULL(t.InputQty, 0)      AS InputQty,
           ISNULL(t.ConfirmedQty, 0)  AS ConfirmedQty,
           ISNULL(t.NgQty, 0)         AS NgQty,
           ISNULL(t.PendingQty, 0)    AS PendingQty,
           CASE WHEN b.ItemNo IS NULL THEN 0 ELSE 1 END AS InBop,
           CASE WHEN EXISTS (
                SELECT 1
                FROM   dbo.PP_WorkOrderRouting r
                JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
                WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = k.ItemNo
                  AND  r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')) THEN 1 ELSE 0 END AS HasOpenWo
    FROM   itemkeys k
    LEFT JOIN dbo.MD_Item i ON i.ItemNo = k.ItemNo
    LEFT JOIN bop    b ON b.ItemNo = k.ItemNo
    LEFT JOIN sched  p ON p.ItemNo = k.ItemNo
    LEFT JOIN lots   t ON t.ItemNo = k.ItemNo
    ORDER  BY InBop DESC, k.ItemNo;
END;
GO

-- ===== Rework.ListPending (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_ListPending
AS
BEGIN
    SELECT d.DefectID, d.LotID, l.LotCode, l.ItemNo, COALESCE(i.ItemName, N'') AS ItemName,
           d.ProcessCode, l.LineID, d.DefectCode, c.DefectName, c.DefectNameEn, c.DefaultCauseCode,
           d.PriorStatus, d.ReasonNote, d.DetectedAt, d.CreatedBy, d.WoID, w.WoNumber,
           d.Disposition, d.CauseCode, d.CorrectiveAction, d.DispositionBy, d.DispositionAt
    FROM   dbo.PR_DefectDetail d
    JOIN   dbo.tbl_Lot          l ON l.LotID = d.LotID
    LEFT   JOIN dbo.MD_Item       i ON i.ItemNo = l.ItemNo
    LEFT   JOIN dbo.MD_DefectCode c ON c.DefectCode = d.DefectCode
    LEFT   JOIN dbo.PP_WorkOrder  w ON w.WoID = d.WoID
    WHERE  d.Disposition IS NULL AND d.LotID IS NOT NULL
    ORDER  BY d.DetectedAt DESC, d.DefectID DESC;
END;
GO

-- ===== Rework.FindPendingByLotCode (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_FindPendingByLotCode
    @Code VARCHAR(40)
AS
BEGIN
    SELECT d.DefectID, d.LotID, l.LotCode, l.ItemNo, COALESCE(i.ItemName, N'') AS ItemName,
           d.ProcessCode, l.LineID, d.DefectCode, c.DefectName, c.DefectNameEn, c.DefaultCauseCode,
           d.PriorStatus, d.ReasonNote, d.DetectedAt, d.CreatedBy, d.WoID, w.WoNumber,
           d.Disposition, d.CauseCode, d.CorrectiveAction, d.DispositionBy, d.DispositionAt
    FROM   dbo.PR_DefectDetail d
    JOIN   dbo.tbl_Lot          l ON l.LotID = d.LotID
    LEFT   JOIN dbo.MD_Item       i ON i.ItemNo = l.ItemNo
    LEFT   JOIN dbo.MD_DefectCode c ON c.DefectCode = d.DefectCode
    LEFT   JOIN dbo.PP_WorkOrder  w ON w.WoID = d.WoID
    WHERE  d.Disposition IS NULL AND l.LotCode = @Code;
END;
GO

-- ===== Rework.ListTodayDecided (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_ListTodayDecided
    @Top INT
AS
BEGIN
    SELECT d.DefectID, d.LotID, l.LotCode, l.ItemNo, COALESCE(i.ItemName, N'') AS ItemName,
           d.ProcessCode, l.LineID, d.DefectCode, c.DefectName, c.DefectNameEn, c.DefaultCauseCode,
           d.PriorStatus, d.ReasonNote, d.DetectedAt, d.CreatedBy, d.WoID, w.WoNumber,
           d.Disposition, d.CauseCode, d.CorrectiveAction, d.DispositionBy, d.DispositionAt
    FROM   dbo.PR_DefectDetail d
    JOIN   dbo.tbl_Lot          l ON l.LotID = d.LotID
    LEFT   JOIN dbo.MD_Item       i ON i.ItemNo = l.ItemNo
    LEFT   JOIN dbo.MD_DefectCode c ON c.DefectCode = d.DefectCode
    LEFT   JOIN dbo.PP_WorkOrder  w ON w.WoID = d.WoID
    WHERE  d.Disposition IN ('REWORKED','SCRAPPED')
      AND  d.DispositionAt >= dbo.FN_POP_DayStart(dbo.FN_POP_ProdToday())
      AND  d.DispositionAt <  dbo.FN_POP_DayStart(DATEADD(day, 1, dbo.FN_POP_ProdToday()))
    ORDER  BY d.DispositionAt DESC, d.DefectID DESC
    OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY;
END;
GO

-- ===== Rework.GetTodayStats (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_GetTodayStats
AS
BEGIN
    DECLARE @Today datetime2 = dbo.FN_POP_DayStart(dbo.FN_POP_ProdToday());   -- 전기일 시작(10-27)
    SELECT
      (SELECT COUNT(*) FROM dbo.PR_DefectDetail WHERE Disposition IS NULL AND LotID IS NOT NULL) AS Pending,
      (SELECT COUNT(*) FROM dbo.PR_DefectDetail WHERE Disposition = 'REWORKED'
         AND DispositionAt >= @Today AND DispositionAt < DATEADD(day,1,@Today)) AS TodayReworked,
      (SELECT COUNT(*) FROM dbo.PR_DefectDetail WHERE Disposition = 'SCRAPPED'
         AND DispositionAt >= @Today AND DispositionAt < DATEADD(day,1,@Today)) AS TodayScrapped;
END;
GO

-- ===== Rework.LockOpenRow (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_LockOpenRow
    @D INT
AS
BEGIN
    SELECT d.LotID, d.WoID, d.ProcessCode, d.Disposition, d.PriorStatus, l.ItemNo, l.LineID
    FROM   dbo.PR_DefectDetail d WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.tbl_Lot         l WITH (UPDLOCK, ROWLOCK) ON l.LotID = d.LotID
    WHERE  d.DefectID = @D;
END;
GO

-- ===== Rework.CloseRow (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_CloseRow
    @Disp VARCHAR(20),
    @Cause VARCHAR(16),
    @Note NVARCHAR(500),
    @By VARCHAR(20),
    @Op VARCHAR(20),
    @D INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    UPDATE dbo.PR_DefectDetail
    SET    Disposition = @Disp, CauseCode = @Cause, CorrectiveAction = @Note,
           DispositionBy = @By, DispositionAt = @evtTs,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  DefectID = @D;
END;
GO

-- ===== Rework.Rework (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_Rework_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, 'RWK', 1, 0,
         @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_Rework_UpdLotConfirm
    @ProcessCode VARCHAR(10),
    @Op NVARCHAR(450),
    @Sess INT,
    @Lot INT,
    @WoID INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    IF @ProcessCode = 'INJ'
    BEGIN
    UPDATE dbo.PR_InjLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = @evtTs,
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  LotID = @Lot;
    END
    ELSE IF @ProcessCode = 'IMG'
    BEGIN
    UPDATE dbo.PR_ImgLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = @evtTs,
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  LotID = @Lot;
    END
    ELSE IF @ProcessCode = 'PNT'
    BEGIN
    UPDATE dbo.PR_PntLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = @evtTs,
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  LotID = @Lot;
    END
    ELSE
        THROW 51000, N'No lot table for process', 1;
    UPDATE dbo.tbl_Lot
    SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  LotID = @Lot;
END;
GO

-- ===== Rework.ReadLotCode (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_ReadLotCode
    @L INT
AS
BEGIN
    SELECT LotCode FROM dbo.tbl_Lot WHERE LotID = @L;
END;
GO

-- ===== Rework.Scrap (RWK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_Scrap
    @ProcessCode VARCHAR(10),
    @Op VARCHAR(20),
    @Lot INT,
    @At DATETIME2 = NULL
AS
BEGIN
    -- 오프라인 POP 이 복구 때 일어난 시각을 넘긴다(10-28) — 없으면 서버 시각
    DECLARE @evtTs DATETIME2 = ISNULL(@At, SYSDATETIME());
    IF @ProcessCode = 'INJ'
    BEGIN
    UPDATE dbo.PR_InjLot
    SET    ConfirmStatus = 'SCRAPPED', ConfirmedAt = @evtTs, ConfirmedBy = @Op,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  LotID = @Lot;
    END
    ELSE IF @ProcessCode = 'IMG'
    BEGIN
    UPDATE dbo.PR_ImgLot
    SET    ConfirmStatus = 'SCRAPPED', ConfirmedAt = @evtTs, ConfirmedBy = @Op,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  LotID = @Lot;
    END
    ELSE IF @ProcessCode = 'PNT'
    BEGIN
    UPDATE dbo.PR_PntLot
    SET    ConfirmStatus = 'SCRAPPED', ConfirmedAt = @evtTs, ConfirmedBy = @Op,
           ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  LotID = @Lot;
    END
    ELSE
        THROW 51000, N'No lot table for process', 1;
    UPDATE dbo.tbl_Lot
    SET    Status = 'SCRAP', QualityFlag = 'NG', ModifiedBy = @Op, ModifiedTS = @evtTs
    WHERE  LotID = @Lot;
END;
GO

-- ===== LotDefectWriter.ReverseConfirmedResult (LOT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_ReverseConfirmedResult_GetProductionResult
    @Lot INT
AS
BEGIN
    SELECT TOP 1 ResultID, WoID, LineID, ProcessCode, MoldID
    FROM   dbo.PR_ProductionResult
    WHERE  LotID = @Lot AND GoodQty > 0
    ORDER  BY ResultID DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_ReverseConfirmedResult_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @Mold VARCHAR(20),
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         MoldID, OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS)
    OUTPUT INSERTED.ResultID
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, @Proc, -1, 0,
         @Mold, @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME());
END;
GO

-- ===== LotDefectWriter.InsertDefectDetail (LOT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_InsertDefectDetail
    @R INT,
    @W INT,
    @L INT,
    @P VARCHAR(10),
    @C VARCHAR(16),
    @N NVARCHAR(500),
    @Prior VARCHAR(16),
    @Rev INT,
    @Op VARCHAR(20),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_DefectDetail
        (ResultID, WoID, LotID, ProcessCode, DefectCode, Qty, ReasonNote,
         PriorStatus, ReversalResultID, DetectedAt, RegisteredBy, CreatedBy, CreatedTS)
    OUTPUT INSERTED.DefectID
    VALUES
        (@R, @W, @L, @P, @C, 1, @N,
         @Prior, @Rev, SYSDATETIME(), @Op, @By, SYSDATETIME());
END;
GO

-- ===== LotDefectWriter.ResolveOpenWo (LOT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_ResolveOpenWo
    @Line VARCHAR(20),
    @Item VARCHAR(20)
AS
BEGIN
    SELECT TOP 1 r.WoID, r.RoutingLineID
    FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
    JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
    WHERE  r.LineID = @Line AND COALESCE(r.ItemNo, w.ItemNo) = @Item
      AND          r.Status IN ('Released','In Progress')
                  AND  ISNULL(w.Status,'Draft') NOT IN ('Completed','Stocked','Closed','Cancelled')
    ORDER  BY         CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID
    ;
END;
GO

-- ===== Backflush.AlreadyApplied (BACKFLUSH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BACKFLUSH_AlreadyApplied
    @S VARCHAR(30),
    @Lot INT
AS
BEGIN
    SELECT TOP 1 1 FROM dbo.WH_InventoryTransaction WHERE SourceType = @S AND SourceID = @Lot;
END;
GO

-- ===== Backflush.LinePrefix (BACKFLUSH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BACKFLUSH_LinePrefix
    @L VARCHAR(20)
AS
BEGIN
    SELECT NULLIF(RTRIM(LotPrefix), '') FROM dbo.MD_Line WHERE LineID = @L;
END;
GO

-- ===== Backflush.ReadBomRows (BACKFLUSH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BACKFLUSH_ReadBomRows
    @Item VARCHAR(20)
AS
BEGIN
    WITH eff AS (
        SELECT v.VersionID, v.EffFrom
        FROM   dbo.MD_BomVersion v
        WHERE  v.Status = 'APPROVED'
          AND (v.EffFrom IS NULL OR v.EffFrom <= CAST(SYSDATETIME() AS date))
          AND (v.EffTo   IS NULL OR v.EffTo   >= CAST(SYSDATETIME() AS date))),
    rows AS (
        SELECT b.CompItemNo, b.QtyPer, b.ScrapPct, b.UOM, b.ActiveFlag,
               DENSE_RANK() OVER (ORDER BY e.EffFrom DESC, e.VersionID DESC) AS Rk
        FROM   dbo.MD_Bom b
        JOIN   eff e ON e.VersionID = b.VersionID
        WHERE  b.ParentItemNo = @Item)
    SELECT r.CompItemNo, i.ItemType, ISNULL(r.QtyPer, 0) AS QtyPer, ISNULL(r.ScrapPct, 0) AS ScrapPct,
           r.UOM, ISNULL(r.ActiveFlag, 1) AS Active, ISNULL(i.InjFlag, 0) AS InjFlag
    FROM   rows r
    LEFT JOIN dbo.MD_Item i ON i.ItemNo = r.CompItemNo
    WHERE  r.Rk = 1 AND r.CompItemNo IS NOT NULL
    ORDER  BY r.CompItemNo;
END;
GO

-- ===== Backflush.ReadLineLots (BACKFLUSH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BACKFLUSH_ReadLineLots
    @Loc VARCHAR(20),
    @Part VARCHAR(20),
    @Ph NVARCHAR(50)
AS
BEGIN
    SELECT W.LotNo, W.Qty
    FROM   dbo.WH_Inventory W WITH (UPDLOCK, ROWLOCK)
    WHERE  W.LocationNo = @Loc AND W.PartNo = @Part AND W.Qty > 0 AND W.LotNo <> @Ph
      AND  NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo = W.LotNo)
    ORDER  BY W.ReceivedAt, W.CreatedAt, W.LotNo;
END;
GO

-- ===== Backflush.ReadLotQty (BACKFLUSH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BACKFLUSH_ReadLotQty
    @L NVARCHAR(50)
AS
BEGIN
    SELECT Qty FROM dbo.WH_Inventory WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE LotNo = @L;
END;
GO

-- ===== Backflush.AdjustLot (BACKFLUSH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BACKFLUSH_AdjustLot
    @D DECIMAL(18, 3),
    @L NVARCHAR(50)
AS
BEGIN
    UPDATE dbo.WH_Inventory SET Qty = Qty + @D, UpdatedAt = SYSDATETIME()
    OUTPUT DELETED.Qty AS Before, INSERTED.Qty AS After
    WHERE  LotNo = @L;
END;
GO

-- ===== Backflush.EnsurePlaceholder (BACKFLUSH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BACKFLUSH_EnsurePlaceholder
    @L NVARCHAR(50),
    @P VARCHAR(20),
    @Loc VARCHAR(20),
    @Q DECIMAL(18, 3)
AS
BEGIN
    INSERT dbo.WH_Inventory (LotNo, UnitType, PartNo, PartName, LocationNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
    SELECT @L, 'PART', @P, (SELECT TOP 1 ItemName FROM dbo.MD_Item WHERE ItemNo = @P), @Loc, @Q,
           SYSDATETIME(), SYSDATETIME(), SYSDATETIME();
END;
GO

-- ===== Backflush.WriteTxn (BACKFLUSH) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BACKFLUSH_WriteTxn
    @T VARCHAR(10),
    @P VARCHAR(20),
    @Loc VARCHAR(20),
    @L NVARCHAR(50),
    @B DECIMAL(18, 3),
    @C DECIMAL(18, 3),
    @A DECIMAL(18, 3),
    @S VARCHAR(30),
    @Src INT,
    @Op VARCHAR(20),
    @N NVARCHAR(500),
    @By VARCHAR(20)
AS
BEGIN
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime, TransactionType, PartNo, LocationNo, LotNo, QtyBefore, QtyChange, QtyAfter,
         SourceType, SourceID, OperatorID, Note, CreatedBy, CreatedTS)
    VALUES
        (SYSDATETIME(), @T, @P, @Loc, @L, @B, @C, @A, @S, @Src, @Op, @N, @By, SYSDATETIME());
END;
GO

-- ===== LotNoGenerator.NextLotNo (LOT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_NextLotNo
    @L VARCHAR(20)
AS
BEGIN
    SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L;
END;
GO

-- ===== LotNoGenerator.NextSeq (LOT) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_NextSeq_UpdLotSeq
    @H CHAR(5)
AS
BEGIN
    UPDATE dbo.SYS_LotSeq SET LastSeq += 1, ModifiedTS = SYSDATETIME()
    OUTPUT inserted.LastSeq WHERE Header = @H;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_NextSeq_InsLotSeq
    @H CHAR(5)
AS
BEGIN
    INSERT INTO dbo.SYS_LotSeq (Header, LastSeq) VALUES (@H, 1);
END;
GO

-- ===== ProdCalendar.ResolveNow (CALENDAR) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_CALENDAR_ResolveNow
    @CutoffKey VARCHAR(60)
AS
BEGIN
    SELECT SYSDATETIME();

    SELECT TOP 1 ConfigValue
    FROM   dbo.SYS_Config
    WHERE  ConfigKey = @CutoffKey
    ORDER  BY ConfigID;

    SELECT CodeValue, Attribute1
    FROM   dbo.MD_CodeItem
    WHERE  GroupCode = 'WORK_SHIFT' AND ISNULL(UseFlag,1) = 1
    ORDER  BY ISNULL(SortOrder,0), CodeValue;
END;
GO

-- ===== CoreItemResolver.ReadByInjFlag (BOM) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BOM_ReadByInjFlag
    @Today DATE
AS
BEGIN
    WITH eff AS (
        SELECT v.VersionID, v.EffFrom
        FROM   dbo.MD_BomVersion v
        WHERE  v.Status = 'APPROVED'
          AND (v.EffFrom IS NULL OR v.EffFrom <= @Today)
          AND (v.EffTo   IS NULL OR v.EffTo   >= @Today)),
    lines AS (
        SELECT b.ParentItemNo, b.CompItemNo,
               DENSE_RANK() OVER (PARTITION BY b.ParentItemNo ORDER BY e.EffFrom DESC, e.VersionID DESC) AS Rk
        FROM   dbo.MD_Bom b
        JOIN   eff e ON e.VersionID = b.VersionID
        WHERE  ISNULL(b.ActiveFlag,1) = 1 AND b.ParentItemNo IS NOT NULL AND b.CompItemNo IS NOT NULL)
    SELECT l.ParentItemNo, l.CompItemNo, c.ItemType, c.ItemName, ISNULL(c.InjFlag, 0) AS InjFlag
    FROM   lines l
    JOIN   dbo.MD_Item c ON c.ItemNo = l.CompItemNo
    WHERE  l.Rk = 1 AND ISNULL(c.ActiveFlag, 1) = 1;
END;
GO

-- ===== CoreItemResolver.Read (BOM) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_BOM_Read
    @Today DATE
AS
BEGIN
    WITH eff AS (
        SELECT v.VersionID, v.EffFrom
        FROM   dbo.MD_BomVersion v
        WHERE  v.Status = 'APPROVED'
          AND (v.EffFrom IS NULL OR v.EffFrom <= @Today)
          AND (v.EffTo   IS NULL OR v.EffTo   >= @Today)),
    lines AS (
        SELECT b.ParentItemNo, b.CompItemNo,
               DENSE_RANK() OVER (PARTITION BY b.ParentItemNo ORDER BY e.EffFrom DESC, e.VersionID DESC) AS Rk
        FROM   dbo.MD_Bom b
        JOIN   eff e ON e.VersionID = b.VersionID
        WHERE  ISNULL(b.ActiveFlag,1) = 1 AND b.ParentItemNo IS NOT NULL AND b.CompItemNo IS NOT NULL)
    SELECT l.ParentItemNo, l.CompItemNo, c.ItemType, c.ItemName
    FROM   lines l
    JOIN   dbo.MD_Item c ON c.ItemNo = l.CompItemNo
    WHERE  l.Rk = 1 AND c.ItemType = 'SUB';
END;
GO

-- ===== DbClock.ReadNow (CLOCK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_CLOCK_ReadNow
AS
BEGIN
    SELECT SYSDATETIME();
END;
GO

-- ===== DbClock.Sync (CLOCK) =====
CREATE OR ALTER PROCEDURE dbo.SP_POP_CLOCK_Sync
AS
BEGIN
    SELECT SYSDATETIME();
END;
GO

