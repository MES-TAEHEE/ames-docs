-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_linestatelog_fill.sql
--  라인 상태 분 단위 기록 PP_LineStateLog 를 DB 가 스스로 채우는 분류 프로시저 dbo.SP_PP_LineStateLog_Fill (10-22)
--
--  PP-ODM 비가동 모니터·PP-OEE(SP_PP_LineOEE_Fill)가 읽는 PP_LineStateLog 에는 쓰는 곳이 없었다(시연 시드뿐).
--  InjAgent·PP_EquipSignal 같은 설비 신호 없이(10-22 사용자 결정) 이미 쌓이는 세 가지 근거로 분마다 상태를 정한다:
--    PlanState  = 그 날 라인 시간 패턴(PP-LSB 저장 행 PatternID → 라인 전용 ACTIVE → 전역, LineScheduleRepository.ReadDayCapacity 와 같은 해석)의
--                 OPERATING 구간이면 'PLAN-RUN', 그 밖(휴게·식사·IDLE·패턴 없음)과 PP-LSB PM 슬롯은 'PLAN-STOP'
--    State      = 그 분을 덮는 PP_LineDowntimeLog(안돈·MNT 라인 정지·PP-DTL)가 있으면 'DOWN'
--               → 그 라인의 POP 활동(PR_ProductionResult.EntryAt · tbl_Lot.CreatedTS)이 "활동 뒤 Thr 분" 안이면 'RUN'
--                 (Thr = max(PP_LINE_RUN_GAP_MIN, ceil(PP_LINE_RUN_GAP_CYCLES × 그 라인 스테이션 BOP 최대 표준 사이클 ÷ 60)))
--               → 그 밖은 'IDLE'
--    RunFlag    = State = 'RUN',  WoID = 그 시각의 WO 슬롯(PP_LineSchedule, 취소 제외) → 없으면 창 안 마지막 실적의 WO
--  대상 = 활성 MD_Line(작업장 공정 RWK 제외 — 재작업은 생산 라인이 아니다) × [@From, @To) 의 분. @LineID 를 주면 마스터에 없어도 그 라인만(시험·백필).
--  기본 창 = 현재 분(진행 중인 분 제외) 앞 24시간 — PP-DTL 이 과거 시각으로 넣은 비가동도 그 안이면 따라간다. 더 오래된 수정은 @From 으로 백필.
--  같은 (라인, 분) 행은 값이 바뀐 것만 덮어쓴다(ClassifiedAt 갱신). MinuteTS 는 분으로 내린 값이다.
--  실행마다 SYS_InterfaceMonitor 의 STATEFILL 행(SYS-Interfaces)에 OK·건수 / ERROR·메시지를 남긴다.
--
--  유니크 인덱스 UX_PP_LineStateLog_Line_Minute(LineID, MinuteTS) INCLUDE(State, PlanState) — 10-22 같은 날 먼저 만든
--  비유니크 IX_PP_LineStateLog_Line_Minute 를 대신한다(있으면 떨어뜨림). 같은 키 행이 있으면 목록을 보이고 중단.
--  설정 SYS_Config PP_LINE_RUN_GAP_CYCLES(기본 2, 0~10) · PP_LINE_RUN_GAP_MIN(기본 3분, 1~60) — SYS-009 PP. 없거나 틀리면 기본값.
--
--  순서 무관(migrate_pp_lineoee_fill.sql 과 독립), 재실행 안전(CREATE OR ALTER · 인덱스·설정은 없을 때만). 테이블 변경 없음.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_pp_linestatelog_fill.sql
--  확인:  EXEC dbo.SP_PP_LineStateLog_Fill @DryRun = 1;                                   -- 최근 24시간 계산값만 보기
--         EXEC dbo.SP_PP_LineStateLog_Fill @From = '2026-10-20', @To = '2026-10-22';    -- 백필(최대 31일)
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ① 유니크 인덱스
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PP_LineStateLog_Line_Minute' AND object_id = OBJECT_ID('dbo.PP_LineStateLog'))
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.PP_LineStateLog GROUP BY LineID, MinuteTS HAVING COUNT(*) > 1)
    BEGIN
        SELECT LineID, MinuteTS, COUNT(*) AS Cnt, STRING_AGG(CAST(StateLogID AS varchar(20)), ',') AS StateLogIDs
        FROM   dbo.PP_LineStateLog GROUP BY LineID, MinuteTS HAVING COUNT(*) > 1;
        THROW 50031, 'PP_LineStateLog 에 같은 (LineID, MinuteTS) 행이 있습니다 — 위 목록에서 남길 행만 두고 지운 뒤 다시 실행하십시오.', 1;
    END
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PP_LineStateLog_Line_Minute' AND object_id = OBJECT_ID('dbo.PP_LineStateLog'))
        DROP INDEX IX_PP_LineStateLog_Line_Minute ON dbo.PP_LineStateLog;
    CREATE UNIQUE NONCLUSTERED INDEX UX_PP_LineStateLog_Line_Minute ON dbo.PP_LineStateLog (LineID, MinuteTS) INCLUDE (State, PlanState);
    PRINT 'UX_PP_LineStateLog_Line_Minute created';
END
ELSE PRINT 'UX_PP_LineStateLog_Line_Minute already exists';
GO

-- ② 설정
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Config WHERE ConfigKey = 'PP_LINE_RUN_GAP_CYCLES')
BEGIN
    INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy)
    VALUES ('PP_LINE_RUN_GAP_CYCLES', 'INT', 'PP', N'2',
            N'Line state RUN window: multiple of longest BOP cycle (SP_PP_LineStateLog_Fill)', 'x', NULL, 11, 1, 'migration');
    PRINT 'SYS_Config PP_LINE_RUN_GAP_CYCLES inserted (2)';
END
ELSE PRINT 'SYS_Config PP_LINE_RUN_GAP_CYCLES already exists';
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Config WHERE ConfigKey = 'PP_LINE_RUN_GAP_MIN')
BEGIN
    INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, Unit, UsedByModulesJSON, SortOrder, IsActive, CreatedBy)
    VALUES ('PP_LINE_RUN_GAP_MIN', 'INT', 'PP', N'3',
            N'Line state RUN window: minimum minutes after POP scan (SP_PP_LineStateLog_Fill)', 'min', NULL, 12, 1, 'migration');
    PRINT 'SYS_Config PP_LINE_RUN_GAP_MIN inserted (3)';
END
ELSE PRINT 'SYS_Config PP_LINE_RUN_GAP_MIN already exists';
GO

-- ③ 실행 결과를 SYS_InterfaceMonitor 의 STATEFILL 행에 남긴다(SP_PP_LineOEE_Fill_Monitor 와 같은 규칙, 분류 트랜잭션 밖에서 부른다)
CREATE OR ALTER PROCEDURE dbo.SP_PP_LineStateLog_Fill_Monitor
    @Ok    bit,
    @Count int,
    @Actor varchar(20),
    @Error nvarchar(1000)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Code varchar(20) = 'STATEFILL', @Name nvarchar(60) = N'PP-ODM Line State Fill', @Ep varchar(255) = 'dbo.SP_PP_LineStateLog_Fill';
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
                5, CASE WHEN @Ok = 1 THEN @Count ELSE NULL END,
                CASE WHEN @Ok = 1 THEN 0 ELSE 1 END,
                CASE WHEN @Ok = 1 THEN NULL ELSE @Error END, 1, @Actor);
END
GO

-- ④ 분류
CREATE OR ALTER PROCEDURE dbo.SP_PP_LineStateLog_Fill
    @From   datetime2(0) = NULL,        -- 분류 시작(분으로 내림). 기본 = @To − 24시간
    @To     datetime2(0) = NULL,        -- 분류 끝(제외, 분으로 내림). 기본 = 현재 분 — 진행 중인 분은 제외
    @LineID varchar(20)  = NULL,        -- 한 라인만(마스터에 없어도 됨 — 시험·백필)
    @Actor  varchar(20)  = 'SQL-AGENT',
    @DryRun bit          = 0            -- 1 = 계산값만 보이고 저장하지 않는다
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Actor = ISNULL(NULLIF(LTRIM(RTRIM(@Actor)), ''), 'SQL-AGENT');
    IF @To IS NULL SET @To = SYSDATETIME();
    SET @To = DATEADD(MINUTE, DATEDIFF(MINUTE, 0, @To), 0);
    IF @From IS NULL SET @From = DATEADD(HOUR, -24, @To);
    SET @From = DATEADD(MINUTE, DATEDIFF(MINUTE, 0, @From), 0);

    IF @From >= @To
    BEGIN
        SELECT @From AS FromTS, @To AS ToTS, 0 AS Lines, 0 AS Minutes, 0 AS Inserted, 0 AS Updated, 0 AS Unchanged, @DryRun AS DryRun;
        RETURN;
    END
    IF DATEDIFF(MINUTE, @From, @To) > 31 * 1440
        THROW 50032, N'The window must not exceed 31 days.', 1;

    -- 설정(없거나 범위 밖이면 기본값)
    DECLARE @GapCycles int = (SELECT TRY_CAST(ConfigValue AS int) FROM dbo.SYS_Config WHERE ConfigKey = 'PP_LINE_RUN_GAP_CYCLES');
    DECLARE @GapMin    int = (SELECT TRY_CAST(ConfigValue AS int) FROM dbo.SYS_Config WHERE ConfigKey = 'PP_LINE_RUN_GAP_MIN');
    IF @GapCycles IS NULL OR @GapCycles < 0 OR @GapCycles > 10 SET @GapCycles = 2;
    IF @GapMin    IS NULL OR @GapMin    < 1 OR @GapMin    > 60 SET @GapMin    = 3;

    DECLARE @Inserted int = 0, @Updated int = 0, @Minutes int = 0, @Lines int = 0;

    -- ⑴ 라인 + 가동 창(분) — 라인 스테이션 BOP 의 가장 긴 표준 사이클 기준
    DECLARE @L TABLE (LineID varchar(20) PRIMARY KEY, ThrMin int NOT NULL);
    IF @LineID IS NOT NULL
        INSERT @L (LineID, ThrMin) VALUES (@LineID, 0);
    ELSE
        INSERT @L (LineID, ThrMin)
        SELECT l.LineID, 0
        FROM   dbo.MD_Line l
        LEFT JOIN dbo.MD_WorkCenter w ON w.WCID = l.WCID
        WHERE  ISNULL(l.Status, 'ACTIVE') <> 'INACTIVE' AND ISNULL(w.ProcessCode, '') <> 'RWK';

    UPDATE l
    SET    ThrMin = CASE WHEN c.MaxCycle IS NULL THEN @GapMin
                         ELSE (SELECT MAX(v) FROM (VALUES (@GapMin), (CEILING(@GapCycles * c.MaxCycle / 60.0))) m(v)) END
    FROM   @L l
    OUTER APPLY (SELECT MAX(b.StdCycleTime) AS MaxCycle
                 FROM   dbo.MD_Bop b
                 JOIN   dbo.MD_Station s ON s.StationCode = b.StationCode
                 WHERE  s.LineID = l.LineID AND ISNULL(b.ActiveFlag, 1) = 1 AND b.StdCycleTime > 0) c;
    SET @Lines = (SELECT COUNT(*) FROM @L);

    -- ⑵ (라인, 날짜) 패턴 — ReadDayCapacity 와 같은 해석
    DECLARE @D TABLE (D date PRIMARY KEY);
    ;WITH d AS (SELECT CAST(@From AS date) AS D
                UNION ALL SELECT DATEADD(DAY, 1, D) FROM d WHERE D < CAST(DATEADD(MINUTE, -1, @To) AS date))
    INSERT @D SELECT D FROM d OPTION (MAXRECURSION 40);

    DECLARE @P TABLE (LineID varchar(20) NOT NULL, D date NOT NULL, PatternID varchar(20) NULL, PRIMARY KEY (LineID, D));
    INSERT @P (LineID, D, PatternID)
    SELECT l.LineID, d.D,
           COALESCE((SELECT TOP (1) s.PatternID FROM dbo.PP_LineSchedule s
                     WHERE  s.LineID = l.LineID AND s.ScheduleDate = d.D AND s.PatternID IS NOT NULL
                     ORDER  BY s.ScheduleID),
                    (SELECT TOP (1) p.PatternID FROM dbo.MD_LineTimePattern p
                     WHERE  (p.LineID = l.LineID OR p.LineID IS NULL)
                       AND  ISNULL(p.Status, 'ACTIVE') = 'ACTIVE'
                       AND  (p.EffectiveFrom IS NULL OR p.EffectiveFrom <= d.D)
                       AND  (p.EffectiveTo   IS NULL OR p.EffectiveTo   >= d.D)
                     ORDER  BY CASE WHEN p.LineID IS NULL THEN 1 ELSE 0 END, p.PatternID))
    FROM   @L l CROSS JOIN @D d;

    -- ⑶ 분 격자 × 분류
    DECLARE @R TABLE (LineID varchar(20) NOT NULL, MinuteTS datetime2(0) NOT NULL,
                      State varchar(20) NOT NULL, PlanState varchar(20) NOT NULL, RunFlag bit NOT NULL, WoID int NULL,
                      PRIMARY KEY (LineID, MinuteTS));
    ;WITH n AS (
        SELECT TOP (DATEDIFF(MINUTE, @From, @To)) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
        FROM   sys.all_objects a CROSS JOIN sys.all_objects b
    ),
    g AS (
        SELECT l.LineID, l.ThrMin, DATEADD(MINUTE, n.i, @From) AS M
        FROM   @L l CROSS JOIN n
    ),
    g2 AS (
        SELECT g.LineID, g.ThrMin, g.M,
               CAST(g.M AS date) AS D,
               DATEDIFF(MINUTE, CAST(CAST(g.M AS date) AS datetime2(0)), g.M) AS Mod
        FROM   g
    ),
    c AS (
        SELECT g2.LineID, g2.M, g2.D, g2.Mod,
               CASE WHEN EXISTS (SELECT 1 FROM @P p JOIN dbo.MD_LineTimeSegment sg ON sg.PatternID = p.PatternID
                                 WHERE  p.LineID = g2.LineID AND p.D = g2.D AND sg.SegmentState = 'OPERATING'
                                   AND  sg.StartMin <= g2.Mod AND sg.EndMin > g2.Mod)
                     AND NOT EXISTS (SELECT 1 FROM dbo.PP_LineSchedule s
                                     WHERE  s.LineID = g2.LineID AND s.ScheduleDate = g2.D AND s.EntryType = 'PM'
                                       AND  ISNULL(s.Status, '') <> 'CANCELLED'
                                       AND  ISNULL(s.StartMin, 0) <= g2.Mod AND ISNULL(s.EndMin, 0) > g2.Mod)
                    THEN 'PLAN-RUN' ELSE 'PLAN-STOP' END AS PlanState,
               CASE WHEN EXISTS (SELECT 1 FROM dbo.PP_LineDowntimeLog dt
                                 WHERE  dt.LineID = g2.LineID AND dt.StartTS < DATEADD(MINUTE, 1, g2.M)
                                   AND  (dt.EndTS IS NULL OR dt.EndTS > g2.M))
                    THEN 'DOWN'
                    WHEN EXISTS (SELECT 1 FROM dbo.PR_ProductionResult r
                                 WHERE  r.LineID = g2.LineID
                                   AND  r.EntryAt >= DATEADD(MINUTE, -(g2.ThrMin - 1), g2.M) AND r.EntryAt < DATEADD(MINUTE, 1, g2.M))
                      OR EXISTS (SELECT 1 FROM dbo.tbl_Lot t
                                 WHERE  t.LineID = g2.LineID
                                   AND  t.CreatedTS >= DATEADD(MINUTE, -(g2.ThrMin - 1), g2.M) AND t.CreatedTS < DATEADD(MINUTE, 1, g2.M))
                    THEN 'RUN'
                    ELSE 'IDLE' END AS State,
               COALESCE((SELECT TOP (1) s.WoID FROM dbo.PP_LineSchedule s
                         WHERE  s.LineID = g2.LineID AND s.ScheduleDate = g2.D AND s.EntryType = 'WO' AND s.WoID IS NOT NULL
                           AND  ISNULL(s.Status, '') <> 'CANCELLED'
                           AND  ISNULL(s.StartMin, 0) <= g2.Mod AND ISNULL(s.EndMin, 0) > g2.Mod
                         ORDER  BY s.StartMin, s.ScheduleID),
                        (SELECT TOP (1) r.WoID FROM dbo.PR_ProductionResult r
                         WHERE  r.LineID = g2.LineID AND r.WoID IS NOT NULL
                           AND  r.EntryAt >= DATEADD(MINUTE, -(g2.ThrMin - 1), g2.M) AND r.EntryAt < DATEADD(MINUTE, 1, g2.M)
                         ORDER  BY r.EntryAt DESC)) AS WoID
        FROM   g2
    )
    INSERT @R (LineID, MinuteTS, State, PlanState, RunFlag, WoID)
    SELECT LineID, M, State, PlanState, CASE WHEN State = 'RUN' THEN 1 ELSE 0 END, WoID
    FROM   c;
    SET @Minutes = @@ROWCOUNT;

    IF @DryRun = 1
    BEGIN
        SELECT r.LineID, r.MinuteTS, r.State, r.PlanState, r.RunFlag, r.WoID,
               CASE WHEN o.StateLogID IS NULL THEN 'INSERT'
                    WHEN NOT EXISTS (SELECT o.State, o.PlanState, ISNULL(o.RunFlag, 0), o.WoID
                                     INTERSECT SELECT r.State, r.PlanState, r.RunFlag, r.WoID) THEN 'UPDATE'
                    ELSE 'UNCHANGED' END AS Action
        FROM   @R r
        LEFT JOIN dbo.PP_LineStateLog o ON o.LineID = r.LineID AND o.MinuteTS = r.MinuteTS
        ORDER  BY r.LineID, r.MinuteTS;
        SELECT @From AS FromTS, @To AS ToTS, @Lines AS Lines, @Minutes AS Minutes, 0 AS Inserted, 0 AS Updated, @Minutes AS Unchanged, @DryRun AS DryRun;
        RETURN;
    END

    -- ⑷ 저장 — 값이 바뀐 분만 덮어쓰고 없는 분은 만든다. 에이전트와 수동 실행이 겹쳐도 한 번에 하나만
    BEGIN TRY
        BEGIN TRAN;
        EXEC sp_getapplock @Resource = 'SP_PP_LineStateLog_Fill', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000;

        UPDATE o
        SET    State = r.State, PlanState = r.PlanState, RunFlag = r.RunFlag, WoID = r.WoID,
               ClassifiedAt = SYSDATETIME(), ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
        FROM   dbo.PP_LineStateLog o WITH (UPDLOCK, HOLDLOCK)
        JOIN   @R r ON r.LineID = o.LineID AND r.MinuteTS = o.MinuteTS
        WHERE  NOT EXISTS (SELECT o.State, o.PlanState, ISNULL(o.RunFlag, 0), o.WoID
                           INTERSECT SELECT r.State, r.PlanState, r.RunFlag, r.WoID);
        SET @Updated = @@ROWCOUNT;

        INSERT dbo.PP_LineStateLog (LineID, MinuteTS, State, PlanState, RunFlag, WoID, ClassifiedAt, CreatedBy, CreatedTS)
        SELECT r.LineID, r.MinuteTS, r.State, r.PlanState, r.RunFlag, r.WoID, SYSDATETIME(), @Actor, SYSDATETIME()
        FROM   @R r
        WHERE  NOT EXISTS (SELECT 1 FROM dbo.PP_LineStateLog o WITH (UPDLOCK, HOLDLOCK)
                           WHERE o.LineID = r.LineID AND o.MinuteTS = r.MinuteTS);
        SET @Inserted = @@ROWCOUNT;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        DECLARE @ErrMsg nvarchar(1000) = ERROR_MESSAGE();
        EXEC dbo.SP_PP_LineStateLog_Fill_Monitor @Ok = 0, @Count = 0, @Actor = @Actor, @Error = @ErrMsg;
        THROW;
    END CATCH;

    DECLARE @Written int = @Inserted + @Updated;
    EXEC dbo.SP_PP_LineStateLog_Fill_Monitor @Ok = 1, @Count = @Written, @Actor = @Actor, @Error = NULL;

    PRINT CONCAT('SP_PP_LineStateLog_Fill ', CONVERT(varchar(16), @From, 120), '~', CONVERT(varchar(16), @To, 120),
                 ': lines ', @Lines, ', minutes ', @Minutes, ', inserted ', @Inserted, ', updated ', @Updated,
                 ', unchanged ', @Minutes - @Inserted - @Updated);
    SELECT @From AS FromTS, @To AS ToTS, @Lines AS Lines, @Minutes AS Minutes,
           @Inserted AS Inserted, @Updated AS Updated, @Minutes - @Inserted - @Updated AS Unchanged, @DryRun AS DryRun;
END
GO

SELECT OBJECT_ID('dbo.SP_PP_LineStateLog_Fill')         AS FillProc,
       OBJECT_ID('dbo.SP_PP_LineStateLog_Fill_Monitor') AS MonitorProc,
       (SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_PP_LineStateLog_Line_Minute') AS UniqueIndex,
       (SELECT COUNT(*) FROM dbo.SYS_Config WHERE ConfigKey IN ('PP_LINE_RUN_GAP_CYCLES', 'PP_LINE_RUN_GAP_MIN')) AS ConfigRows;
GO
