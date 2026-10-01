-- ════════════════════════════════════════════════════════════════════════
-- migrate_wo_step_item.sql — WO 공정 단계별 생산 품번 (PP_WorkOrderRouting.ItemNo)
--   · PP_WorkOrderRouting.ItemNo VARCHAR(20) NULL 추가. 의미 = 그 단계가 생산하는 품번, NULL = WO 품번(PP_WorkOrder.ItemNo).
--   · WO 발행(WorkOrderRepository.ReleaseCore)이 INJ(코어) 단계에만 코어 품번을 스냅샷한다. 백필하지 않는다 —
--     기존 WO 는 NULL 이라 종전대로 WO 품번으로 동작한다.
--   · 조회는 COALESCE(r.ItemNo, w.ItemNo) 한 식만 쓴다. 인덱스: (LineID, ItemNo) — POP 이 라인·단계 품번으로 열린 WO 를 찾는다.
-- 적용 순서: migrate_wo_step_line.sql 다음.
-- idempotent(멱등). 적용: sqlcmd(ODBC17) -f 65001 -I -b -i dist/migrate_wo_step_item.sql
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1) 컬럼 ────────────────────────────────────────────────────────────
IF COL_LENGTH('dbo.PP_WorkOrderRouting', 'ItemNo') IS NULL
    ALTER TABLE dbo.PP_WorkOrderRouting ADD ItemNo VARCHAR(20) NULL;
GO

-- ── 2) 인덱스 ──────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_PP_WorkOrderRouting_Line_Item'
                 AND object_id = OBJECT_ID('dbo.PP_WorkOrderRouting'))
    CREATE NONCLUSTERED INDEX IX_PP_WorkOrderRouting_Line_Item
        ON dbo.PP_WorkOrderRouting (LineID, ItemNo)
        INCLUDE (WoID, StepSeq, Status);
GO

-- ── 3) 확인 ────────────────────────────────────────────────────────────
SELECT COL_LENGTH('dbo.PP_WorkOrderRouting', 'ItemNo') AS ItemNoLen,
       (SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_PP_WorkOrderRouting_Line_Item') AS IdxCount;
PRINT N'migrate_wo_step_item: 완료';
GO
