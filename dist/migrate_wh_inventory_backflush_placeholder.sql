-- BOM 자동 차감(backflush) 자리표시 LOT 의 음수 재고 허용 (2026-10-15 사용자 결정).
-- WH_Inventory.Qty 는 CK_WH_Inventory_Qty(Qty >= 0) 로 막혀 있었다. 라인 재고가 모자랄 때 라인×품번 자리표시
-- LOT `BF-{MD_Line.LotPrefix}-{품번}` 만 음수로 내려가게 그 행에 한해 제약을 푼다 — 실물 창고 LOT 은 종전대로 0 아래로 못 간다.
-- 재실행 안전. 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P ... -f 65001 -I -b -i dist/migrate_wh_inventory_backflush_placeholder.sql
-- 순서: migrate_unify_inventory.sql(WH_Inventory 통합) 뒤. 이 마이그레이션 없이 신 Pop 을 올리면 라인 재고가 모자란
-- 확정마다 차감 블록만 CHECK 위반으로 되돌아가고(확정은 커밋) POP 에 "자재 자동 차감 실패" 토스트가 뜬다.
USE [AMES_DEV];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NULL
    THROW 51700, 'dbo.WH_Inventory does not exist — apply migrate_unify_inventory.sql first.', 1;
GO

DECLARE @Def nvarchar(max) = (SELECT definition FROM sys.check_constraints
                              WHERE name = N'CK_WH_Inventory_Qty' AND parent_object_id = OBJECT_ID(N'dbo.WH_Inventory'));
IF @Def IS NULL OR @Def NOT LIKE N'%BF-%'
BEGIN
    IF @Def IS NOT NULL
        ALTER TABLE dbo.WH_Inventory DROP CONSTRAINT CK_WH_Inventory_Qty;
    ALTER TABLE dbo.WH_Inventory WITH CHECK
        ADD CONSTRAINT CK_WH_Inventory_Qty CHECK (Qty >= 0 OR LotNo LIKE N'BF-%');
    PRINT 'CK_WH_Inventory_Qty: negative Qty allowed for BF- placeholder lots.';
END
ELSE
    PRINT 'CK_WH_Inventory_Qty already allows BF- placeholder lots — skipped.';
GO

-- 멱등 판정(SourceType BACKFLUSH + SourceID = LotID)이 확정마다 돈다 — 인덱스가 없으면 LOT 잠금을 쥔 채 거래 표 전체를 훑는다.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InventoryTransaction_Source' AND object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WH_InventoryTransaction_Source ON dbo.WH_InventoryTransaction (SourceType, SourceID);
    PRINT 'IX_WH_InventoryTransaction_Source created.';
END
ELSE
    PRINT 'IX_WH_InventoryTransaction_Source already exists — skipped.';
GO
