-- ════════════════════════════════════════════════════════════════════════
-- migrate_audit_columns.sql
-- 감사 컬럼 누락 / 명칭 오류 보정 (실제 DB 적용용)
-- 대상 DB : AMES_DEV
--
-- ※ 컬럼 순서 변경(133개 테이블)은 SQL Server에서 테이블 재생성이 필요하므로
--    이 스크립트에서는 제외합니다. 순서는 스키마 SQL(fresh install) 기준으로만
--    반영되어 있으며, 애플리케이션 코드는 컬럼명으로 접근하므로 기능 영향 없음.
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO

-- ── 1. WH_PurchaseOrder : CreatedAt → CreatedTS 이름 변경 ────────────
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.WH_PurchaseOrder') AND name = 'CreatedAt'
)
BEGIN
    EXEC sp_rename 'dbo.WH_PurchaseOrder.CreatedAt', 'CreatedTS', 'COLUMN';
    PRINT '✓ WH_PurchaseOrder.CreatedAt → CreatedTS';
END
ELSE
    PRINT '– WH_PurchaseOrder.CreatedAt not found (already renamed or never created)';
GO

-- ── 2. PR_DashTileCache : ModifiedTS 추가 ───────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.PR_DashTileCache') AND name = 'ModifiedTS'
)
BEGIN
    ALTER TABLE dbo.PR_DashTileCache ADD [ModifiedTS] DATETIME2 NULL;
    PRINT '✓ PR_DashTileCache.ModifiedTS added';
END
ELSE
    PRINT '– PR_DashTileCache.ModifiedTS already exists';
GO

-- ── 3. PR_DefectRateCache : ModifiedTS 추가 ─────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.PR_DefectRateCache') AND name = 'ModifiedTS'
)
BEGIN
    ALTER TABLE dbo.PR_DefectRateCache ADD [ModifiedTS] DATETIME2 NULL;
    PRINT '✓ PR_DefectRateCache.ModifiedTS added';
END
ELSE
    PRINT '– PR_DefectRateCache.ModifiedTS already exists';
GO

-- ── 4. PNT_SeqAllocator : ModifiedTS 추가 ───────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.PNT_SeqAllocator') AND name = 'ModifiedTS'
)
BEGIN
    ALTER TABLE dbo.PNT_SeqAllocator ADD [ModifiedTS] DATETIME2 NULL;
    PRINT '✓ PNT_SeqAllocator.ModifiedTS added';
END
ELSE
    PRINT '– PNT_SeqAllocator.ModifiedTS already exists';
GO

-- ── 5. PNT_StationStatsCache : ModifiedTS 추가 ──────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.PNT_StationStatsCache') AND name = 'ModifiedTS'
)
BEGIN
    ALTER TABLE dbo.PNT_StationStatsCache ADD [ModifiedTS] DATETIME2 NULL;
    PRINT '✓ PNT_StationStatsCache.ModifiedTS added';
END
ELSE
    PRINT '– PNT_StationStatsCache.ModifiedTS already exists';
GO

-- ── 확인 ─────────────────────────────────────────────────────────────
SELECT t.name AS TableName, c.name AS ColumnName, c.column_id AS ColOrder
FROM   sys.columns c
JOIN   sys.tables  t ON t.object_id = c.object_id
WHERE  t.name IN (
    'WH_PurchaseOrder','PR_DashTileCache','PR_DefectRateCache',
    'PNT_SeqAllocator','PNT_StationStatsCache'
)
  AND  c.name IN ('CreatedBy','CreatedTS','ModifiedBy','ModifiedTS')
ORDER  BY t.name, c.column_id;
GO
