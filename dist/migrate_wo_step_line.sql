-- ════════════════════════════════════════════════════════════════════════
-- migrate_wo_step_line.sql — WO 공정 단계별 라인 배정·실적 (PP_WorkOrderRouting 정본화)
--   · PP_WorkOrderRouting.CompletedQty·TerminalLock 추가, (WoID,StepSeq) 유니크, (LineID,Status) 인덱스
--   · 단계 Status 'Pending' → Released / In Progress / Closed 로 통일
--   · 단계 행 없는 Released·In Progress·Closed WO 백필
--       RoutingType 있음: 템플릿 단계 전부. 헤더 라인 공정 = 헤더 라인, 타 공정 = 첫 활성 라인, 활성 라인 없음 = NULL
--       RoutingType NULL : 헤더 라인 공정 단계 1개
--       헤더 CompletedQty 는 손대지 않는다 (동기화는 다음 실적부터)
--       ※ 헤더 라인이 마지막 라인 단계가 아닌 WO(예: A 라우팅을 INJ 라인으로 발행)는 첫 후속 실적에서 헤더 CompletedQty 가 마지막 단계 값으로 내려갈 수 있다 (PP-04 PROGRESS 감소).
--   · 헤더 LineID NULL 인 대상은 백필하지 않고 WoID 를 PRINT
--   · TerminalLock 신설 컬럼: 이미 In Progress 인 단계(헤더 라인과 일치)는 헤더 TerminalLock 을 그대로 백필
-- 적용 순서: migrate_routing_step.sql 다음.
-- idempotent(멱등). 적용: sqlcmd(ODBC17) -f 65001 -b -i dist/migrate_wo_step_line.sql
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1) 컬럼 ────────────────────────────────────────────────────────────
IF COL_LENGTH('dbo.PP_WorkOrderRouting', 'CompletedQty') IS NULL
    ALTER TABLE dbo.PP_WorkOrderRouting
        ADD CompletedQty DECIMAL(14,3) NOT NULL
            CONSTRAINT DF_PP_WorkOrderRouting_CompletedQty DEFAULT (0);
GO

IF COL_LENGTH('dbo.PP_WorkOrderRouting','TerminalLock') IS NULL
    ALTER TABLE dbo.PP_WorkOrderRouting ADD TerminalLock VARCHAR(20) NULL;
GO

-- ── 2) 인덱스 ──────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_PP_WorkOrderRouting_Wo_Step'
                 AND object_id = OBJECT_ID('dbo.PP_WorkOrderRouting'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_PP_WorkOrderRouting_Wo_Step
        ON dbo.PP_WorkOrderRouting (WoID, StepSeq);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_PP_WorkOrderRouting_Line_Status'
                 AND object_id = OBJECT_ID('dbo.PP_WorkOrderRouting'))
    CREATE NONCLUSTERED INDEX IX_PP_WorkOrderRouting_Line_Status
        ON dbo.PP_WorkOrderRouting (LineID, Status)
        INCLUDE (WoID, StepSeq, CompletedQty);
GO

-- ── 3) Pending → 헤더 기준 상태 ────────────────────────────────────────
UPDATE r
SET    r.Status = CASE WHEN w.Status = 'Closed'                                    THEN 'Closed'
                       WHEN w.Status = 'In Progress' AND r.LineID = w.LineID       THEN 'In Progress'
                       ELSE 'Released' END,
       r.ModifiedBy = 'MIGRATE', r.ModifiedTS = SYSDATETIME()
FROM   dbo.PP_WorkOrderRouting r
JOIN   dbo.PP_WorkOrder w ON w.WoID = r.WoID
WHERE  r.Status = 'Pending';
GO

-- ── 4) 백필 ────────────────────────────────────────────────────────────
DECLARE @Wo TABLE (
    WoID int, RoutingType char(1), LineID varchar(20), Status varchar(20),
    CompletedQty decimal(14,3), LineProc varchar(10));

INSERT INTO @Wo
SELECT w.WoID, w.RoutingType, w.LineID, w.Status, ISNULL(w.CompletedQty, 0), wc.ProcessCode
FROM   dbo.PP_WorkOrder w
LEFT JOIN dbo.MD_Line       l  ON l.LineID = w.LineID
LEFT JOIN dbo.MD_WorkCenter wc ON wc.WCID  = l.WCID
WHERE  w.Status IN ('Released', 'In Progress', 'Closed')
  AND  NOT EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID);

DECLARE @skip nvarchar(max) =
    (SELECT STRING_AGG(CAST(WoID AS nvarchar(10)), ',') FROM @Wo WHERE LineID IS NULL OR LineProc IS NULL);
IF @skip IS NOT NULL
    PRINT N'migrate_wo_step_line: 헤더 LineID 없음/공정 불명 — 백필 제외 WoID: ' + @skip;

-- 4a) RoutingType 있음 → 템플릿 단계 전부
INSERT INTO dbo.PP_WorkOrderRouting
       (WoID, StepSeq, ProcessCode, LineID, StdCycleSec, StdYieldPct, Status, CompletedQty, CreatedBy, CreatedTS)
SELECT x.WoID, rs.StepSeq, rs.ProcessCode,
       CASE WHEN rs.ProcessCode = x.LineProc THEN x.LineID
            ELSE (SELECT TOP 1 l.LineID
                  FROM dbo.MD_Line l JOIN dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
                  WHERE wc.ProcessCode = rs.ProcessCode
                    AND ISNULL(l.Status, 'ACTIVE') <> 'INACTIVE'
                  ORDER BY l.LineID) END,
       NULL, NULL,
       CASE WHEN x.Status = 'Closed'              THEN 'Closed'
            WHEN rs.ProcessCode = x.LineProc      THEN x.Status
            ELSE 'Released' END,
       CASE WHEN rs.ProcessCode = x.LineProc THEN x.CompletedQty ELSE 0 END,
       'MIGRATE', SYSDATETIME()
FROM   @Wo x
JOIN   dbo.MD_RoutingStep rs ON rs.RoutingType = x.RoutingType AND ISNULL(rs.ActiveFlag, 1) = 1
WHERE  x.LineID IS NOT NULL AND x.LineProc IS NOT NULL AND x.RoutingType IS NOT NULL;

-- 4b) RoutingType NULL → 헤더 라인 공정 단계 1개
INSERT INTO dbo.PP_WorkOrderRouting
       (WoID, StepSeq, ProcessCode, LineID, StdCycleSec, StdYieldPct, Status, CompletedQty, CreatedBy, CreatedTS)
SELECT x.WoID, 1, x.LineProc, x.LineID, NULL, NULL, x.Status, x.CompletedQty, 'MIGRATE', SYSDATETIME()
FROM   @Wo x
WHERE  x.LineID IS NOT NULL AND x.LineProc IS NOT NULL AND x.RoutingType IS NULL;

-- ── 5) TerminalLock 백필 ───────────────────────────────────────────────
-- 헤더가 이미 In Progress 로 잡고 있던 단계(=헤더 LineID 와 일치하는 단계)는
-- TerminalLock 신설 컬럼이 NULL 로 시작해 "미접수"로 잘못 보인다.
-- 헤더 TerminalLock 을 그 단계로 그대로 옮겨 GetActiveForTerminal 의 단계 기준 필터와 맞춘다.
UPDATE r
SET    r.TerminalLock = w.TerminalLock,
       r.ModifiedBy = 'MIGRATE', r.ModifiedTS = SYSDATETIME()
FROM   dbo.PP_WorkOrderRouting r
JOIN   dbo.PP_WorkOrder w ON w.WoID = r.WoID
WHERE  r.Status = 'In Progress' AND r.TerminalLock IS NULL
  AND  r.LineID = w.LineID AND w.TerminalLock IS NOT NULL;
GO

PRINT N'migrate_wo_step_line: 완료';
GO
