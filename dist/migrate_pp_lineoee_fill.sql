-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_lineoee_fill.sql
--  PP-OEE 라인 OEE 자동 집계 프로시저 dbo.SP_PP_LineOEE_Fill + 원천 조회 인덱스 2개 (10-22)
--
--  SQL Server 에이전트 작업 "[AMES] Line OEE Fill"(dist/setup_job_pp_lineoee_fill.sql, 기본 5분 간격)이
--  이 프로시저를 돌려 PP_LineOEE(라인 × 일자 × 교대 스냅샷)를 채운다 — 전에는 PP-OEE 화면의 「계산」 탭에서
--  라인·날짜·교대를 골라 손으로 계산·저장했다. 계산 규칙은 OeeRepository.ComputeOee 와 같다:
--    Loading   = 구간의 PP_LineStateLog 분 수 (계획 정지 포함)
--    Planned   = PlanState 가 PLAN-RUN 이 아닌 분
--    Operating = PLAN-RUN 이면서 State = 'RUN' 인 분
--    Unplanned = Loading − Planned − Operating (0 미만은 0)
--    Q = 양품 ÷ (양품 + 불량) — PR_ProductionResult(EntryAt) + PR_DefectDetail(ResultID 연결, 실적 없는 LOT 불량은 LOT 라인·DetectedAt)
--    P = Σ(수량 × BOP 표준 사이클) ÷ 가동 초 — 실적이 없거나 표준 사이클이 하나라도 없거나 가동 0분이면 100% 로 간주, 100% 상한
--    비율은 백분율을 소수 2자리로 반올림한 뒤 소수(0.9267)로 저장한다(화면 저장과 같은 값)
--  교대 창 = 공통코드 WORK_SHIFT.Attribute1 'HHMM-HHMM'(끝이 시작보다 빠르거나 같으면 익일) — 화면 「계산」 탭의 ShiftWindow 와 같다.
--  대상 = (기준일 − @DaysBack … 기준일) × 사용 중 교대 × 그 범위에 상태 로그가 있는 라인. 상태 로그가 없는 구간은 건너뛴다
--  (화면과 같음 — 행을 만들지 않는다). 일 단위(ShiftCode NULL) 행은 만들지 않는다 — 화면이 기간의 행을 전부 더하므로 겹친다.
--  같은 키 행이 있으면 값이 달라졌을 때만 덮어쓴다(ModifiedBy = @Actor) — 손으로 저장한 스냅샷도 같은 키면 덮인다.
--  실행마다 SYS_InterfaceMonitor 의 OEEFILL 행(SYS-Interfaces 화면)에 OK·건수 / ERROR·메시지를 남긴다.
--
--  인덱스: IX_PP_LineStateLog_Line_Minute(LineID, MinuteTS) — 상태 로그는 라인당 하루 1,440행이라 범위 조회에 필요
--          IX_PR_ProductionResult_Line_EntryAt(LineID, EntryAt) — 실적 구간 조회(화면 계산도 같은 조건)
--
--  순서 무관, 재실행 안전(CREATE OR ALTER · 인덱스는 없을 때만). 테이블 변경 없음.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_pp_lineoee_fill.sql
--  확인:  EXEC dbo.SP_PP_LineOEE_Fill @DryRun = 1;                      -- 오늘·어제 계산값만 보기(저장 없음)
--         EXEC dbo.SP_PP_LineOEE_Fill @Date = '2026-06-02', @DaysBack = 0; -- 특정 날짜 채우기(백필)
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PP_LineStateLog_Line_Minute' AND object_id = OBJECT_ID('dbo.PP_LineStateLog'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PP_LineStateLog_Line_Minute ON dbo.PP_LineStateLog (LineID, MinuteTS) INCLUDE (State, PlanState);
    PRINT 'IX_PP_LineStateLog_Line_Minute created';
END
ELSE PRINT 'IX_PP_LineStateLog_Line_Minute already exists';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PR_ProductionResult_Line_EntryAt' AND object_id = OBJECT_ID('dbo.PR_ProductionResult'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PR_ProductionResult_Line_EntryAt ON dbo.PR_ProductionResult (LineID, EntryAt) INCLUDE (WoID, GoodQty);
    PRINT 'IX_PR_ProductionResult_Line_EntryAt created';
END
ELSE PRINT 'IX_PR_ProductionResult_Line_EntryAt already exists';
GO

-- 실행 결과를 SYS_InterfaceMonitor 의 OEEFILL 행에 남긴다(SysRepository.UpsertInterfaceMonitor 와 같은 규칙 —
-- 성공이면 LastSyncTS·건수 갱신·RetryCount 0, 실패면 ERROR·메시지·RetryCount +1, 마지막 성공 시각은 유지).
-- 집계 트랜잭션 밖에서 부른다 — 실패 기록이 롤백에 휩쓸리지 않게.
CREATE OR ALTER PROCEDURE dbo.SP_PP_LineOEE_Fill_Monitor
    @Ok    bit,
    @Count int,
    @Actor varchar(20),
    @Error nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Code varchar(20) = 'OEEFILL', @Name nvarchar(60) = N'PP-OEE Line OEE Fill', @Ep varchar(255) = 'dbo.SP_PP_LineOEE_Fill';
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

CREATE OR ALTER PROCEDURE dbo.SP_PP_LineOEE_Fill
    @Date     date        = NULL,        -- 기준일(기본 = DB 오늘)
    @DaysBack int         = 1,           -- 기준일 앞으로 더 계산할 일수(0~31) — 자정을 넘는 교대·늦게 들어온 실적 때문에 기본 1(어제·오늘)
    @LineID   varchar(20) = NULL,        -- 한 라인만
    @Actor    varchar(20) = 'SQL-AGENT', -- PP_LineOEE.CreatedBy/ModifiedBy
    @DryRun   bit         = 0            -- 1 = 계산값만 보이고 저장하지 않는다
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @DaysBack IS NULL OR @DaysBack < 0 OR @DaysBack > 31
        THROW 50021, N'@DaysBack must be between 0 and 31.', 1;
    IF @Date IS NULL SET @Date = CAST(SYSDATETIME() AS date);
    SET @Actor = ISNULL(NULLIF(LTRIM(RTRIM(@Actor)), ''), 'SQL-AGENT');

    DECLARE @From date = DATEADD(DAY, -@DaysBack, @Date);
    DECLARE @Targets int = 0, @Computed int = 0, @Inserted int = 0, @Updated int = 0;

    -- ① 교대 창 = WORK_SHIFT.Attribute1 'HHMM-HHMM' (같은 코드가 둘이면 SortOrder 앞 행 — 화면의 FirstOrDefault 와 같음)
    DECLARE @Shift TABLE (ShiftCode varchar(10) PRIMARY KEY, StartMin int NOT NULL, EndMin int NOT NULL);
    INSERT @Shift (ShiftCode, StartMin, EndMin)
    SELECT s.CodeValue, p.sh * 60 + p.sm,
           CASE WHEN p.eh * 60 + p.em <= p.sh * 60 + p.sm THEN p.eh * 60 + p.em + 1440 ELSE p.eh * 60 + p.em END
    FROM (
        SELECT CodeValue, LTRIM(RTRIM(Attribute1)) AS A1,
               ROW_NUMBER() OVER (PARTITION BY CodeValue ORDER BY ISNULL(SortOrder, 0), CodeID) AS rn
        FROM   dbo.MD_CodeItem
        WHERE  GroupCode = 'WORK_SHIFT' AND ISNULL(UseFlag, 1) = 1
          AND  NULLIF(LTRIM(RTRIM(CodeValue)), '') IS NOT NULL
    ) s
    CROSS APPLY (SELECT TRY_CAST(SUBSTRING(s.A1, 1, 2) AS int) AS sh, TRY_CAST(SUBSTRING(s.A1, 3, 2) AS int) AS sm,
                        TRY_CAST(SUBSTRING(s.A1, 6, 2) AS int) AS eh, TRY_CAST(SUBSTRING(s.A1, 8, 2) AS int) AS em) p
    WHERE  s.rn = 1 AND LEN(s.A1) = 9 AND SUBSTRING(s.A1, 5, 1) = '-'
      AND  p.sh IS NOT NULL AND p.sm IS NOT NULL AND p.eh IS NOT NULL AND p.em IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM @Shift)
    BEGIN
        EXEC dbo.SP_PP_LineOEE_Fill_Monitor @Ok = 0, @Count = 0, @Actor = @Actor,
             @Error = N'WORK_SHIFT has no usable shift window (Attribute1 HHMM-HHMM).';
        THROW 50022, N'WORK_SHIFT has no usable shift window (Attribute1 HHMM-HHMM).', 1;
    END

    -- ② 대상 = 날짜 × 교대 × 그 범위에 상태 로그가 있는 라인
    DECLARE @RangeStart datetime2(0) = DATEADD(MINUTE, (SELECT MIN(StartMin) FROM @Shift), CAST(@From AS datetime2(0)));
    DECLARE @RangeEnd   datetime2(0) = DATEADD(MINUTE, (SELECT MAX(EndMin)   FROM @Shift), CAST(@Date AS datetime2(0)));

    DECLARE @T TABLE (LineID varchar(20) NOT NULL, PeriodDate date NOT NULL, ShiftCode varchar(10) NOT NULL,
                      WStart datetime2(0) NOT NULL, WEnd datetime2(0) NOT NULL,
                      PRIMARY KEY (LineID, PeriodDate, ShiftCode));
    ;WITH d AS (
        SELECT @From AS D
        UNION ALL SELECT DATEADD(DAY, 1, D) FROM d WHERE D < @Date
    )
    INSERT @T (LineID, PeriodDate, ShiftCode, WStart, WEnd)
    SELECT l.LineID, d.D, s.ShiftCode,
           DATEADD(MINUTE, s.StartMin, CAST(d.D AS datetime2(0))),
           DATEADD(MINUTE, s.EndMin,   CAST(d.D AS datetime2(0)))
    FROM   d
    CROSS  JOIN @Shift s
    CROSS  JOIN (SELECT DISTINCT LineID
                 FROM   dbo.PP_LineStateLog
                 WHERE  LineID IS NOT NULL AND MinuteTS >= @RangeStart AND MinuteTS < @RangeEnd
                   AND  (@LineID IS NULL OR LineID = @LineID)) l
    OPTION (MAXRECURSION 40);
    SET @Targets = @@ROWCOUNT;

    -- ③ 계산 (OeeRepository.ComputeOee 와 같은 식)
    DECLARE @R TABLE (LineID varchar(20) NOT NULL, PeriodDate date NOT NULL, ShiftCode varchar(10) NOT NULL,
                      LoadingMin int NOT NULL, PlannedDownMin int NOT NULL, UnplannedDownMin int NOT NULL, OperatingMin int NOT NULL,
                      TotalProducedQty decimal(14,3) NOT NULL, GoodQty decimal(14,3) NOT NULL,
                      Availability decimal(5,4) NOT NULL, Performance decimal(5,4) NOT NULL, Quality decimal(5,4) NOT NULL, OEE decimal(5,4) NOT NULL,
                      PerformanceAssumed bit NOT NULL,
                      PRIMARY KEY (LineID, PeriodDate, ShiftCode));
    ;WITH st AS (
        SELECT t.LineID, t.PeriodDate, t.ShiftCode,
               COUNT(*) AS LoadingMin,
               SUM(CASE WHEN ISNULL(l.PlanState, 'PLAN-RUN') <> 'PLAN-RUN' THEN 1 ELSE 0 END) AS PlannedDownMin,
               SUM(CASE WHEN ISNULL(l.PlanState, 'PLAN-RUN') =  'PLAN-RUN' AND l.State = 'RUN' THEN 1 ELSE 0 END) AS OperatingMin
        FROM   @T t
        JOIN   dbo.PP_LineStateLog l ON l.LineID = t.LineID AND l.MinuteTS >= t.WStart AND l.MinuteTS < t.WEnd
        GROUP  BY t.LineID, t.PeriodDate, t.ShiftCode
    ),
    x AS (   -- 실적 행 + 실적 없는 LOT 불량(ResultID NULL — LOT 라인·DetectedAt 로 귀속)
        SELECT t.LineID, t.PeriodDate, t.ShiftCode, r.WoID,
               CAST(ISNULL(r.GoodQty, 0) AS decimal(14,3)) AS GoodQty, CAST(ISNULL(d.DefQty, 0) AS decimal(14,3)) AS DefQty
        FROM   @T t
        JOIN   dbo.PR_ProductionResult r ON r.LineID = t.LineID AND r.EntryAt >= t.WStart AND r.EntryAt < t.WEnd
        LEFT JOIN (SELECT ResultID, SUM(ISNULL(Qty, 0)) AS DefQty
                   FROM dbo.PR_DefectDetail WHERE ResultID IS NOT NULL GROUP BY ResultID) d ON d.ResultID = r.ResultID
        UNION ALL
        SELECT t.LineID, t.PeriodDate, t.ShiftCode, dd.WoID, 0, CAST(ISNULL(dd.Qty, 0) AS decimal(14,3))
        FROM   @T t
        JOIN   dbo.tbl_Lot tl        ON tl.LineID = t.LineID
        JOIN   dbo.PR_DefectDetail dd ON dd.LotID = tl.LotID AND dd.ResultID IS NULL
                                     AND dd.DetectedAt >= t.WStart AND dd.DetectedAt < t.WEnd
    ),
    pr AS (
        SELECT x.LineID, x.PeriodDate, x.ShiftCode,
               SUM(x.GoodQty) AS GoodQty, SUM(x.DefQty) AS DefectQty,
               SUM(CASE WHEN b.StdCycle IS NOT NULL THEN (x.GoodQty + x.DefQty) * b.StdCycle END) AS StdSec,
               SUM(CASE WHEN b.StdCycle IS NOT NULL THEN 1 ELSE 0 END) AS RowsWithStd,
               COUNT(*) AS Rows
        FROM   x
        LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = x.WoID
        OUTER APPLY (SELECT AVG(CAST(bp.StdCycleTime AS decimal(12,3))) AS StdCycle
                     FROM   dbo.MD_Bop bp
                     WHERE  bp.ItemNo = w.ItemNo AND ISNULL(bp.ActiveFlag, 1) = 1
                       AND  (w.RoutingType IS NULL OR bp.RoutingType = w.RoutingType)) b
        GROUP  BY x.LineID, x.PeriodDate, x.ShiftCode
    ),
    c AS (
        SELECT st.LineID, st.PeriodDate, st.ShiftCode, st.LoadingMin, st.PlannedDownMin, st.OperatingMin,
               CASE WHEN st.LoadingMin - st.PlannedDownMin - st.OperatingMin < 0 THEN 0
                    ELSE st.LoadingMin - st.PlannedDownMin - st.OperatingMin END AS UnplannedDownMin,
               ISNULL(pr.GoodQty, 0) AS GoodQty,
               ISNULL(pr.GoodQty, 0) + ISNULL(pr.DefectQty, 0) AS Produced,
               pr.StdSec, ISNULL(pr.RowsWithStd, 0) AS RowsWithStd, ISNULL(pr.Rows, 0) AS Rows
        FROM   st
        LEFT JOIN pr ON pr.LineID = st.LineID AND pr.PeriodDate = st.PeriodDate AND pr.ShiftCode = st.ShiftCode
        WHERE  st.LoadingMin > 0
    ),
    k AS (
        SELECT c.*,
               ROUND(CAST(c.OperatingMin AS decimal(18,6)) * 100 / c.LoadingMin, 2) AS A,
               CASE WHEN c.Produced > 0 THEN ROUND(c.GoodQty * 100 / c.Produced, 2) ELSE CAST(100 AS decimal(18,2)) END AS Q,
               CASE WHEN c.Rows = 0 OR c.RowsWithStd < c.Rows OR c.StdSec IS NULL OR c.OperatingMin = 0 THEN 1 ELSE 0 END AS Assumed
        FROM   c
    ),
    k2 AS (
        SELECT k.*,
               CASE WHEN k.Assumed = 1 THEN CAST(100 AS decimal(18,2))
                    ELSE (SELECT MIN(v) FROM (VALUES (CAST(100 AS decimal(18,2))),
                                                     (ROUND(k.StdSec * 100 / (k.OperatingMin * 60.0), 2))) m(v)) END AS P
        FROM   k
    )
    INSERT @R (LineID, PeriodDate, ShiftCode, LoadingMin, PlannedDownMin, UnplannedDownMin, OperatingMin,
               TotalProducedQty, GoodQty, Availability, Performance, Quality, OEE, PerformanceAssumed)
    SELECT LineID, PeriodDate, ShiftCode, LoadingMin, PlannedDownMin, UnplannedDownMin, OperatingMin,
           Produced, GoodQty,
           ROUND(A / 100, 4), ROUND(P / 100, 4), ROUND(Q / 100, 4),
           ROUND(ROUND(A / 100 * P / 100 * Q / 100 * 100, 2) / 100, 4),
           Assumed
    FROM   k2;
    SET @Computed = @@ROWCOUNT;

    IF @DryRun = 1
    BEGIN
        SELECT r.LineID, r.PeriodDate, r.ShiftCode, r.LoadingMin, r.PlannedDownMin, r.UnplannedDownMin, r.OperatingMin,
               r.TotalProducedQty, r.GoodQty, r.Availability, r.Performance, r.Quality, r.OEE, r.PerformanceAssumed,
               CASE WHEN o.OeeSnapshotID IS NULL THEN 'INSERT'
                    WHEN NOT EXISTS (SELECT ISNULL(o.LoadingMin,0), ISNULL(o.PlannedDownMin,0), ISNULL(o.UnplannedDownMin,0), ISNULL(o.OperatingMin,0),
                                            ISNULL(o.TotalProducedQty,0), ISNULL(o.GoodQty,0), ISNULL(o.Availability,0), ISNULL(o.Performance,0), ISNULL(o.Quality,0), ISNULL(o.OEE,0)
                                     INTERSECT
                                     SELECT r.LoadingMin, r.PlannedDownMin, r.UnplannedDownMin, r.OperatingMin,
                                            r.TotalProducedQty, r.GoodQty, r.Availability, r.Performance, r.Quality, r.OEE) THEN 'UPDATE'
                    ELSE 'UNCHANGED' END AS Action
        FROM   @R r
        LEFT JOIN dbo.PP_LineOEE o ON o.LineID = r.LineID AND o.PeriodDate = r.PeriodDate AND o.ShiftCode = r.ShiftCode
        ORDER  BY r.PeriodDate, r.LineID, r.ShiftCode;

        SELECT @Date AS BaseDate, @DaysBack AS DaysBack, @Targets AS Targets, @Computed AS Computed,
               0 AS Inserted, 0 AS Updated, @Computed AS Unchanged, @DryRun AS DryRun;
        RETURN;
    END

    -- ④ 저장 — 같은 키 행은 값이 바뀐 것만 덮어쓰고 없으면 만든다. 에이전트와 화면 「지금 집계」가 겹쳐도 한 번에 하나만
    BEGIN TRY
        BEGIN TRAN;
        EXEC sp_getapplock @Resource = 'SP_PP_LineOEE_Fill', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000;

        UPDATE o
        SET    LoadingMin = r.LoadingMin, PlannedDownMin = r.PlannedDownMin, UnplannedDownMin = r.UnplannedDownMin, OperatingMin = r.OperatingMin,
               TotalProducedQty = r.TotalProducedQty, GoodQty = r.GoodQty,
               Availability = r.Availability, Performance = r.Performance, Quality = r.Quality, OEE = r.OEE,
               ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
        FROM   dbo.PP_LineOEE o WITH (UPDLOCK, HOLDLOCK)
        JOIN   @R r ON r.LineID = o.LineID AND r.PeriodDate = o.PeriodDate AND r.ShiftCode = o.ShiftCode
        WHERE  NOT EXISTS (SELECT ISNULL(o.LoadingMin,0), ISNULL(o.PlannedDownMin,0), ISNULL(o.UnplannedDownMin,0), ISNULL(o.OperatingMin,0),
                                  ISNULL(o.TotalProducedQty,0), ISNULL(o.GoodQty,0), ISNULL(o.Availability,0), ISNULL(o.Performance,0), ISNULL(o.Quality,0), ISNULL(o.OEE,0)
                           INTERSECT
                           SELECT r.LoadingMin, r.PlannedDownMin, r.UnplannedDownMin, r.OperatingMin,
                                  r.TotalProducedQty, r.GoodQty, r.Availability, r.Performance, r.Quality, r.OEE);
        SET @Updated = @@ROWCOUNT;

        INSERT dbo.PP_LineOEE (LineID, PeriodDate, ShiftCode,
                               LoadingMin, PlannedDownMin, UnplannedDownMin, OperatingMin,
                               TotalProducedQty, GoodQty, Availability, Performance, Quality, OEE,
                               CreatedBy, CreatedTS)
        SELECT r.LineID, r.PeriodDate, r.ShiftCode,
               r.LoadingMin, r.PlannedDownMin, r.UnplannedDownMin, r.OperatingMin,
               r.TotalProducedQty, r.GoodQty, r.Availability, r.Performance, r.Quality, r.OEE,
               @Actor, SYSDATETIME()
        FROM   @R r
        WHERE  NOT EXISTS (SELECT 1 FROM dbo.PP_LineOEE o WITH (UPDLOCK, HOLDLOCK)
                           WHERE o.LineID = r.LineID AND o.PeriodDate = r.PeriodDate AND o.ShiftCode = r.ShiftCode);
        SET @Inserted = @@ROWCOUNT;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        DECLARE @ErrMsg nvarchar(1000) = ERROR_MESSAGE();
        EXEC dbo.SP_PP_LineOEE_Fill_Monitor @Ok = 0, @Count = 0, @Actor = @Actor, @Error = @ErrMsg;
        THROW;
    END CATCH;

    DECLARE @Written int = @Inserted + @Updated;
    EXEC dbo.SP_PP_LineOEE_Fill_Monitor @Ok = 1, @Count = @Written, @Actor = @Actor, @Error = NULL;

    PRINT CONCAT('SP_PP_LineOEE_Fill ', CONVERT(varchar(10), @From, 120), '~', CONVERT(varchar(10), @Date, 120),
                 ': targets ', @Targets, ', computed ', @Computed, ', inserted ', @Inserted, ', updated ', @Updated,
                 ', unchanged ', @Computed - @Inserted - @Updated);
    SELECT @Date AS BaseDate, @DaysBack AS DaysBack, @Targets AS Targets, @Computed AS Computed,
           @Inserted AS Inserted, @Updated AS Updated, @Computed - @Inserted - @Updated AS Unchanged, @DryRun AS DryRun;
END
GO

SELECT OBJECT_ID('dbo.SP_PP_LineOEE_Fill')         AS FillProc,
       OBJECT_ID('dbo.SP_PP_LineOEE_Fill_Monitor') AS MonitorProc,
       (SELECT COUNT(*) FROM sys.indexes WHERE name IN ('IX_PP_LineStateLog_Line_Minute', 'IX_PR_ProductionResult_Line_EntryAt')) AS Indexes;
GO
