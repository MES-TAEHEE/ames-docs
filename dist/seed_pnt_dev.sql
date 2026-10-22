-- ════════════════════════════════════════════════════════════════════════
--  seed_pnt_dev.sql  (운영 금지 — 개발 DB 전용)
--  PNT-MAIN(도장 통합 화면, 2026-10-22) 을 띄우기 위한 데모 도장 완제품 2개.
--
--  품목·BOM 정본(docs/260828 BOM Master List.xlsx)에는 아직 도장품(라우팅 B)이 없어 PNT 스테이션 BOP 가 비고
--  라벨 발행 버튼이 켜지지 않는다. 그래서 dev 에만 PNTDEMO-A/B(ASSY, RoutingType 'B') 와
--    · APPROVED BOM 1건씩 — 자식 = 마스터의 실제 사출 코어(InjFlag 1, 82311-DW% 우선) 1 EA + 레진 21326B1-PIDHA 500 G(있을 때만)
--    · ST-PNT-01 BOP(라우팅 B, StepSeq 20, 사이클 120초)
--  를 넣는다. 재실행 안전(있는 행은 건너뜀, 없던 BOM 자식은 다음 실행이 더한다). InjFlag 1 인 품목이 없으면 코어 자식 없이 넣고 안내만 한다. seed_md_item_bom_master_list.sql 은 MD_Item 을 전부 지우고 다시 넣으므로
--  그 뒤에 다시 적용한다. 실제 도장 품번은 엑셀 정본에 들어올 때 생성기가 만든다.
--  적용:  sqlcmd -S 192.168.0.132,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/seed_pnt_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRAN;

DECLARE @Core varchar(20) =
    COALESCE((SELECT TOP 1 ItemNo FROM dbo.MD_Item WHERE InjFlag = 1 AND ISNULL(ActiveFlag,1) = 1 AND ItemNo LIKE '82311-DW%' ORDER BY ItemNo),
             (SELECT TOP 1 ItemNo FROM dbo.MD_Item WHERE InjFlag = 1 AND ISNULL(ActiveFlag,1) = 1 ORDER BY ItemNo));
DECLARE @Resin varchar(20) = (SELECT ItemNo FROM dbo.MD_Item WHERE ItemNo = '21326B1-PIDHA');
-- 새 DB 재구축에서는 아직 InjFlag 가 전부 0 이다(마스터 시드가 세우지 않음) — 코어 자식 없이 품목·BOP 만 넣고 멈추지 않는다.
IF @Core IS NULL PRINT N'seed_pnt_dev: no injection item (MD_Item.InjFlag = 1) — BOM will have no core child; set InjFlag and re-run to add it';
PRINT CONCAT(N'core child = ', ISNULL(@Core, N'(none)'), N', resin child = ', ISNULL(@Resin, N'(none)'));

-- 1) 도장 완제품 2개
INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, RoutingType, DefaultUOM, PGN, ALC, MountPos, PalletQty, ActiveFlag, CreatedBy, CreatedTS)
SELECT v.ItemNo, v.ItemName, 'ASSY', 'TRIM', 'B', 'EA', 'PDEM', v.Alc, v.MountPos, 8, 1, 'SEED-DEMO', SYSDATETIME()
FROM (VALUES ('PNTDEMO-A', N'PNT DEMO PAINTED TRIM A', 'P001', 'FL'),
             ('PNTDEMO-B', N'PNT DEMO PAINTED TRIM B', 'P002', 'FR')) v (ItemNo, ItemName, Alc, MountPos)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Item i WHERE i.ItemNo = v.ItemNo);
PRINT CONCAT(N'§1 MD_Item PNTDEMO: ', @@ROWCOUNT);

-- 2) BOM 버전 + 라인 (코어 1 EA + 레진 500 G)
INSERT INTO dbo.MD_BomVersion (VersionID, RootItemNo, VersionNo, EffFrom, EffTo, Status, CreatedBy)
SELECT 'V-' + v.ItemNo + '-01', v.ItemNo, 'V1.0', '2026-08-28', NULL, 'APPROVED', 'SEED-DEMO'
FROM (VALUES ('PNTDEMO-A'), ('PNTDEMO-B')) v (ItemNo)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_BomVersion b WHERE b.VersionID = 'V-' + v.ItemNo + '-01');
PRINT CONCAT(N'§2 MD_BomVersion: ', @@ROWCOUNT);

INSERT INTO dbo.MD_Bom (BOMID, ParentItemNo, CompItemNo, BOMLevel, QtyPer, UOM, VersionID, ActiveFlag, CreatedBy)
SELECT 'B' + LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 23), v.ItemNo, c.CompItemNo, 1, c.QtyPer, c.UOM, 'V-' + v.ItemNo + '-01', 1, 'SEED-DEMO'
FROM (VALUES ('PNTDEMO-A'), ('PNTDEMO-B')) v (ItemNo)
CROSS JOIN (SELECT @Core AS CompItemNo, CAST(1 AS decimal(14,4)) AS QtyPer, 'EA' AS UOM WHERE @Core IS NOT NULL
            UNION ALL SELECT @Resin, 500, 'G' WHERE @Resin IS NOT NULL) c
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Bom b WHERE b.VersionID = 'V-' + v.ItemNo + '-01' AND b.CompItemNo = c.CompItemNo);
PRINT CONCAT(N'§2 MD_Bom lines: ', @@ROWCOUNT);

-- 3) ST-PNT-01 BOP
INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, StdCycleTime, ActiveFlag, CreatedBy, CreatedTS)
SELECT 'P' + LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 23), v.ItemNo, 'B', 20, 'ST-PNT-01', 120, 1, 'SEED-DEMO', SYSDATETIME()
FROM (VALUES ('PNTDEMO-A'), ('PNTDEMO-B')) v (ItemNo)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Bop b WHERE b.ItemNo = v.ItemNo AND b.StationCode = 'ST-PNT-01');
PRINT CONCAT(N'§3 MD_Bop ST-PNT-01: ', @@ROWCOUNT);

COMMIT;
GO
