-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_oeelog_fill.sql
--  설비 OEE 자동 채움 프로시저 dbo.SP_MNT_OEELog_Fill(+Monitor) + MNT_OEELog 키 유니크 인덱스 + 시연 행 정리 (10-23)
--
--  MNT_OEELog(설비 × 일자 × 교대)는 MNT-003 OEE 분석·RPT-006 설비 OEE·RPT-007 월간 KPI 가용률·RPT-010 OEE 원본·
--  MNT 대시보드 「오늘 평균 OEE」·Api /api/mnt/oee 가 읽지만 채우는 코드가 없어 06-02 시연 행(mnt-seed)만 있었다.
--  SQL Server 에이전트 작업 "[AMES] MNT OEE Fill"(dist/setup_job_mnt_oeelog_fill.sql, 기본 5분 간격)이 이 프로시저로
--  라인 OEE 스냅샷 PP_LineOEE(dbo.SP_PP_LineOEE_Fill 이 채움 — 그 프로시저는 고치지 않고 결과만 읽는다)를
--  그 라인의 설비 행으로 옮겨 쓴다(방안 A — 지금 설비는 사출 라인마다 1대):
--    대상 설비   = 활성(ActiveFlag 1) MD_Equipment 중 라인이 있는 것. 한 라인에 활성 설비가 둘 이상이면 그 라인은 건너뛴다
--                  (같은 값을 여러 설비에 나눠 쓰지 않는다 — 결과 SkippedLines 에 센다)
--    원천        = PP_LineOEE 교대 행(ShiftCode 있음) — 일 단위 행(ShiftCode NULL)은 쓰지 않는다
--    AggLevel    = 'SHIFT', AggDate = PeriodDate, ShiftCode 그대로
--    PlannedTimeMin = LoadingMin, DowntimeMin = PlannedDownMin + UnplannedDownMin
--                  → 화면의 가동 분(PlannedTimeMin − DowntimeMin)이 라인의 OperatingMin 과 같고, 가용률도 라인 값과 같다
--    Availability·Performance·Quality·OEE = PP_LineOEE 소수(0.9267) × 100 → 백분율(92.67) — MNT_OEELog 는 백분율로 저장한다
--    GoodQty = GoodQty, TotalQty = TotalProducedQty
--    LossBreakdownJSON = {"plannedDownMin":…,"unplannedDownMin":…,"operatingMin":…,"source":"PP_LineOEE"}
--    OEERecordNumber = 'OEE-' + yyMMdd + '-' + 설비 + '-' + 교대 (40자에서 자름)
--  대상 기간 = 기준일 − @DaysBack … 기준일. 기본 2 — 매일 06:00 「Line State Recalc」가 이틀치 라인 OEE 를 다시 집계하므로
--  5분 실행이 그 결과까지 따라간다. 같은 (설비, 일자, 교대) 행이 있으면 값이 바뀐 것만 덮어쓴다(ModifiedBy = @Actor).
--  PP_LineOEE 에서 행이 사라져도 MNT_OEELog 행은 지우지 않는다. 실행마다 SYS_InterfaceMonitor 의 MNTOEEFILL 행에 남긴다.
--
--  유니크 인덱스 UX_MNT_OEELog_Equip_Date_Shift(EquipID, AggDate, ShiftCode, AggLevel) — 동시 실행·수동 입력의 중복 방지 안전망.
--    필터 인덱스로 두지 않는다(그 테이블을 고치는 모든 연결에 QUOTED_IDENTIFIER ON 을 요구한다 — Msg 1934).
--  시연 행 정리: CreatedBy = 'mnt-seed' 행(06-02 시연 56건)을 지운다 — 실제 숫자와 섞이지 않게(재실행해도 지울 것이 없다).
--
--  순서 무관(테이블 변경 없음 — PP_LineOEE 가 없으면 프로시저 실행만 실패), 재실행 안전.
--  적용:  sqlcmd -S <서버> -U <계정> -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_mnt_oeelog_fill.sql
--  확인:  EXEC dbo.SP_MNT_OEELog_Fill @DryRun = 1;                         -- 그제~오늘 옮길 값만 보기(저장 없음)
--         EXEC dbo.SP_MNT_OEELog_Fill @Date = '2026-10-23', @DaysBack = 31; -- 지난 기간 채우기(백필)
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF EXISTS (SELECT 1 FROM dbo.MNT_OEELog WHERE CreatedBy = 'mnt-seed')
BEGIN
    DELETE dbo.MNT_OEELog WHERE CreatedBy = 'mnt-seed';
    PRINT CONCAT('MNT_OEELog demo rows (mnt-seed) deleted: ', @@ROWCOUNT);
END
ELSE PRINT 'MNT_OEELog has no demo rows';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MNT_OEELog_Equip_Date_Shift' AND object_id = OBJECT_ID('dbo.MNT_OEELog'))
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.MNT_OEELog GROUP BY EquipID, AggDate, ShiftCode, AggLevel HAVING COUNT(*) > 1)
    BEGIN
        SELECT EquipID, AggDate, ShiftCode, AggLevel, COUNT(*) AS Rows
        FROM   dbo.MNT_OEELog GROUP BY EquipID, AggDate, ShiftCode, AggLevel HAVING COUNT(*) > 1;
        THROW 50031, N'MNT_OEELog has duplicate (EquipID, AggDate, ShiftCode, AggLevel) rows — clean them up before creating UX_MNT_OEELog_Equip_Date_Shift.', 1;
    END
    CREATE UNIQUE NONCLUSTERED INDEX UX_MNT_OEELog_Equip_Date_Shift ON dbo.MNT_OEELog (EquipID, AggDate, ShiftCode, AggLevel);
    PRINT 'UX_MNT_OEELog_Equip_Date_Shift created';
END
ELSE PRINT 'UX_MNT_OEELog_Equip_Date_Shift already exists';
GO

-- 실행 결과를 SYS_InterfaceMonitor 의 MNTOEEFILL 행에 남긴다(SP_PP_LineOEE_Fill_Monitor 와 같은 규칙 —
-- 성공이면 LastSyncTS·건수 갱신·RetryCount 0, 실패면 ERROR·메시지·RetryCount +1, 마지막 성공 시각은 유지).
-- 채움 트랜잭션 밖에서 부른다 — 실패 기록이 롤백에 휩쓸리지 않게.
CREATE OR ALTER PROCEDURE dbo.SP_MNT_OEELog_Fill_Monitor
    @Ok    bit,
    @Count int,
    @Actor varchar(20),
    @Error nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Code varchar(20) = 'MNTOEEFILL', @Name nvarchar(60) = N'MNT Equipment OEE Fill', @Ep varchar(255) = 'dbo.SP_MNT_OEELog_Fill';
    UPDATE dbo.SYS_InterfaceMonitor
    SET    InterfaceName   = @Name,
           Endpoint        = @Ep,
           ConnStatus      = CASE WHEN @Ok = 1 THEN 'OK' ELSE 'ERROR' END,
           LastSyncTS      = CASE WHEN @Ok = 1 THEN SYSDATETIME() ELSE LastSyncTS END,
           LastRecordCount = CASE WHEN @Ok = 1 THEN @Count ELSE LastRecordCount END,
           LastErrorMsg    = CASE WHEN @Ok = 1 THEN NULL ELSE @Error END,
           RetryCount      = CASE WHEN @Ok = 1 THEN 0 ELSE ISNULL(RetryCount, 0) + 1 END,
           ModifiedBy      = @Actor, ModifiedTS = SYSDATETIME()
    WHERE  InterfaceCode = @Code;
    IF @@ROWCOUNT = 0
        INSERT INTO dbo.SYS_InterfaceMonitor
               (InterfaceCode, InterfaceName, Direction, Endpoint, Protocol, ConnStatus,
                LastSyncTS, MaxGapMinutes, LastRecordCount, RetryCount, LastErrorMsg, IsEnabled, CreatedBy)
        VALUES (@Code, @Name, 'INBOUND', @Ep, 'TSQL',
                CASE WHEN @Ok = 1 THEN 'OK' ELSE 'ERROR' END,
                CASE WHEN @Ok = 1 THEN SYSDATETIME() ELSE NULL END,
                10, CASE WHEN @Ok = 1 THEN @Count ELSE NULL END,
                CASE WHEN @Ok = 1 THEN 0 ELSE 1 END,
                CASE WHEN @Ok = 1 THEN NULL ELSE @Error END, 1, @Actor);
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_MNT_OEELog_Fill
    @Date     date        = NULL,        -- 기준일(기본 = DB 오늘)
    @DaysBack int         = 2,           -- 기준일 앞으로 더 옮길 일수(0~31) — 06:00 재집계가 이틀치를 다시 쓰므로 기본 2
    @EquipID  varchar(20) = NULL,        -- 한 설비만
    @Actor    varchar(20) = 'SQL-AGENT', -- MNT_OEELog.CreatedBy/ModifiedBy
    @DryRun   bit         = 0            -- 1 = 옮길 값만 보이고 저장하지 않는다
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @DaysBack IS NULL OR @DaysBack < 0 OR @DaysBack > 31
        THROW 50032, N'@DaysBack must be between 0 and 31.', 1;
    IF @Date IS NULL SET @Date = CAST(SYSDATETIME() AS date);
    SET @Actor = ISNULL(NULLIF(LTRIM(RTRIM(@Actor)), ''), 'SQL-AGENT');

    DECLARE @From date = DATEADD(DAY, -@DaysBack, @Date);
    DECLARE @Computed int = 0, @Inserted int = 0, @Updated int = 0, @SkippedLines int = 0;

    -- ① 라인 → 설비(활성 설비가 정확히 1대인 라인만)
    DECLARE @Map TABLE (LineID varchar(20) PRIMARY KEY, EquipID varchar(20) NOT NULL);
    INSERT @Map (LineID, EquipID)
    SELECT LineID, MIN(EquipID)
    FROM   dbo.MD_Equipment
    WHERE  ISNULL(ActiveFlag, 1) = 1 AND NULLIF(LTRIM(RTRIM(LineID)), '') IS NOT NULL
    GROUP  BY LineID
    HAVING COUNT(*) = 1;

    SELECT @SkippedLines = COUNT(*)
    FROM  (SELECT LineID FROM dbo.MD_Equipment
           WHERE ISNULL(ActiveFlag, 1) = 1 AND NULLIF(LTRIM(RTRIM(LineID)), '') IS NOT NULL
           GROUP BY LineID HAVING COUNT(*) > 1) x;

    -- ② 옮길 값
    DECLARE @R TABLE (EquipID varchar(20) NOT NULL, AggDate date NOT NULL, ShiftCode varchar(10) NOT NULL,
                      LineID varchar(20) NOT NULL, OEERecordNumber varchar(40) NOT NULL,
                      PlannedTimeMin int NOT NULL, DowntimeMin int NOT NULL,
                      Availability decimal(5,2) NULL, Performance decimal(5,2) NULL, Quality decimal(5,2) NULL, OEE decimal(5,2) NULL,
                      GoodQty decimal(12,3) NULL, TotalQty decimal(12,3) NULL, LossBreakdownJSON nvarchar(400) NOT NULL,
                      PRIMARY KEY (EquipID, AggDate, ShiftCode));
    INSERT @R (EquipID, AggDate, ShiftCode, LineID, OEERecordNumber, PlannedTimeMin, DowntimeMin,
               Availability, Performance, Quality, OEE, GoodQty, TotalQty, LossBreakdownJSON)
    SELECT m.EquipID, o.PeriodDate, o.ShiftCode, o.LineID,
           LEFT(CONCAT('OEE-', CONVERT(varchar(6), o.PeriodDate, 12), '-', m.EquipID, '-', o.ShiftCode), 40),
           ISNULL(o.LoadingMin, 0),
           ISNULL(o.PlannedDownMin, 0) + ISNULL(o.UnplannedDownMin, 0),
           CAST(o.Availability * 100 AS decimal(5,2)), CAST(o.Performance * 100 AS decimal(5,2)),
           CAST(o.Quality * 100 AS decimal(5,2)),      CAST(o.OEE * 100 AS decimal(5,2)),
           CAST(o.GoodQty AS decimal(12,3)), CAST(o.TotalProducedQty AS decimal(12,3)),
           CONCAT(N'{"plannedDownMin":', ISNULL(o.PlannedDownMin, 0), N',"unplannedDownMin":', ISNULL(o.UnplannedDownMin, 0),
                  N',"operatingMin":', ISNULL(o.OperatingMin, 0), N',"source":"PP_LineOEE"}')
    FROM   dbo.PP_LineOEE o
    JOIN   @Map m ON m.LineID = o.LineID
    WHERE  o.PeriodDate >= @From AND o.PeriodDate <= @Date
      AND  NULLIF(LTRIM(RTRIM(o.ShiftCode)), '') IS NOT NULL
      AND  (@EquipID IS NULL OR m.EquipID = @EquipID);
    SET @Computed = @@ROWCOUNT;

    IF @DryRun = 1
    BEGIN
        SELECT r.*,
               CASE WHEN g.OEELogID IS NULL THEN 'INSERT'
                    WHEN NOT EXISTS (SELECT g.LineID, g.OEERecordNumber, g.PlannedTimeMin, g.DowntimeMin, g.Availability, g.Performance,
                                            g.Quality, g.OEE, g.GoodQty, g.TotalQty, CAST(g.LossBreakdownJSON AS nvarchar(400))
                                     INTERSECT
                                     SELECT r.LineID, r.OEERecordNumber, r.PlannedTimeMin, r.DowntimeMin, r.Availability, r.Performance,
                                            r.Quality, r.OEE, r.GoodQty, r.TotalQty, r.LossBreakdownJSON) THEN 'UPDATE'
                    ELSE 'UNCHANGED' END AS Action
        FROM   @R r
        LEFT JOIN dbo.MNT_OEELog g ON g.AggLevel = 'SHIFT' AND g.EquipID = r.EquipID AND g.AggDate = r.AggDate AND g.ShiftCode = r.ShiftCode
        ORDER  BY r.AggDate, r.EquipID, r.ShiftCode;

        SELECT @Date AS BaseDate, @DaysBack AS DaysBack, @Computed AS Computed,
               0 AS Inserted, 0 AS Updated, @Computed AS Unchanged, @SkippedLines AS SkippedLines, @DryRun AS DryRun;
        RETURN;
    END

    -- ③ 저장 — 같은 키 행은 값이 바뀐 것만 덮어쓰고 없으면 만든다. 에이전트 실행이 겹쳐도 한 번에 하나만
    BEGIN TRY
        BEGIN TRAN;
        EXEC sp_getapplock @Resource = 'SP_MNT_OEELog_Fill', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000;

        UPDATE g
        SET    LineID = r.LineID, OEERecordNumber = r.OEERecordNumber,
               PlannedTimeMin = r.PlannedTimeMin, DowntimeMin = r.DowntimeMin,
               Availability = r.Availability, Performance = r.Performance, Quality = r.Quality, OEE = r.OEE,
               GoodQty = r.GoodQty, TotalQty = r.TotalQty, LossBreakdownJSON = r.LossBreakdownJSON,
               ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
        FROM   dbo.MNT_OEELog g WITH (UPDLOCK, HOLDLOCK)
        JOIN   @R r ON g.AggLevel = 'SHIFT' AND g.EquipID = r.EquipID AND g.AggDate = r.AggDate AND g.ShiftCode = r.ShiftCode
        WHERE  NOT EXISTS (SELECT g.LineID, g.OEERecordNumber, g.PlannedTimeMin, g.DowntimeMin, g.Availability, g.Performance,
                                  g.Quality, g.OEE, g.GoodQty, g.TotalQty, CAST(g.LossBreakdownJSON AS nvarchar(400))
                           INTERSECT
                           SELECT r.LineID, r.OEERecordNumber, r.PlannedTimeMin, r.DowntimeMin, r.Availability, r.Performance,
                                  r.Quality, r.OEE, r.GoodQty, r.TotalQty, r.LossBreakdownJSON);
        SET @Updated = @@ROWCOUNT;

        INSERT dbo.MNT_OEELog (OEERecordNumber, EquipID, LineID, AggLevel, AggDate, ShiftCode,
                               PlannedTimeMin, DowntimeMin, Availability, Performance, Quality, OEE,
                               GoodQty, TotalQty, LossBreakdownJSON, CreatedBy, CreatedTS)
        SELECT r.OEERecordNumber, r.EquipID, r.LineID, 'SHIFT', r.AggDate, r.ShiftCode,
               r.PlannedTimeMin, r.DowntimeMin, r.Availability, r.Performance, r.Quality, r.OEE,
               r.GoodQty, r.TotalQty, r.LossBreakdownJSON, @Actor, SYSDATETIME()
        FROM   @R r
        WHERE  NOT EXISTS (SELECT 1 FROM dbo.MNT_OEELog g WITH (UPDLOCK, HOLDLOCK)
                           WHERE g.AggLevel = 'SHIFT' AND g.EquipID = r.EquipID AND g.AggDate = r.AggDate AND g.ShiftCode = r.ShiftCode);
        SET @Inserted = @@ROWCOUNT;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        DECLARE @ErrMsg nvarchar(1000) = ERROR_MESSAGE();
        EXEC dbo.SP_MNT_OEELog_Fill_Monitor @Ok = 0, @Count = 0, @Actor = @Actor, @Error = @ErrMsg;
        THROW;
    END CATCH;

    DECLARE @Written int = @Inserted + @Updated;
    EXEC dbo.SP_MNT_OEELog_Fill_Monitor @Ok = 1, @Count = @Written, @Actor = @Actor, @Error = NULL;

    PRINT CONCAT('SP_MNT_OEELog_Fill ', CONVERT(varchar(10), @From, 120), '~', CONVERT(varchar(10), @Date, 120),
                 ': computed ', @Computed, ', inserted ', @Inserted, ', updated ', @Updated,
                 ', unchanged ', @Computed - @Inserted - @Updated, ', skipped lines ', @SkippedLines);
    SELECT @Date AS BaseDate, @DaysBack AS DaysBack, @Computed AS Computed,
           @Inserted AS Inserted, @Updated AS Updated, @Computed - @Inserted - @Updated AS Unchanged,
           @SkippedLines AS SkippedLines, @DryRun AS DryRun;
END
GO

SELECT OBJECT_ID('dbo.SP_MNT_OEELog_Fill')         AS FillProc,
       OBJECT_ID('dbo.SP_MNT_OEELog_Fill_Monitor') AS MonitorProc,
       (SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_MNT_OEELog_Equip_Date_Shift') AS UniqueIndex,
       (SELECT COUNT(*) FROM dbo.MNT_OEELog WHERE CreatedBy = 'mnt-seed') AS DemoRowsLeft;
GO
