-- ════════════════════════════════════════════════════════════════════════
-- migrate_mold_cleanup.sql — deprecated 금형 객체 제거
--   MD_MoldItemMap        : migrate_mold_master.sql 로 MD_MoldItem 이관 완료 후 미사용
--   MD_Mold.CompatItemsJSON: 전체 코드 사용처 0건 (MD_Jig.CompatItemsJSON 은 대상 아님 — 유지)
-- 전제: migrate_mold_master.sql 적용 완료 (MD_MoldItem 존재·데이터 보유).
-- idempotent. 새로 재구축한 DB(AMES_Schema 최신)에서는 no-op.
-- 적용: sqlcmd(ODBC17 전체경로) -S localhost,1433 -U sa -P ... -d AMES_DEV -f 65001 -b -i dist/migrate_mold_cleanup.sql
-- ════════════════════════════════════════════════════════════════════════

-- 안전 가드: 대체 테이블이 준비되기 전이면 중단
IF OBJECT_ID(N'dbo.MD_MoldItemMap', N'U') IS NOT NULL
   AND (OBJECT_ID(N'dbo.MD_MoldItem', N'U') IS NULL
        OR NOT EXISTS (SELECT 1 FROM dbo.MD_MoldItem))
    THROW 50002, N'MD_MoldItem 이 비어 있습니다. migrate_mold_master.sql 을 먼저 적용하세요.', 1;
GO

IF OBJECT_ID(N'dbo.MD_MoldItemMap', N'U') IS NOT NULL
    DROP TABLE dbo.MD_MoldItemMap;
GO

IF COL_LENGTH(N'dbo.MD_Mold', N'CompatItemsJSON') IS NOT NULL
    ALTER TABLE dbo.MD_Mold DROP COLUMN [CompatItemsJSON];
GO

PRINT N'✓ migrate_mold_cleanup.sql applied (MD_MoldItemMap dropped, MD_Mold.CompatItemsJSON dropped)';
GO
