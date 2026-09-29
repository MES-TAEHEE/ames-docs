-- ════════════════════════════════════════════════════════════════════════
--  migrate_img_core_lot.sql
--  IMG 완제품 LOT ↔ 사출 Core LOT 연결의 1:1 보장
--
--  IMG-MAIN 이 사출 Core 라벨을 스캔해 완제품 LOT 을 만들 때 tbl_Lot.ParentLotID 에
--  Core LotID 를 넣는다(ImgLotRepository.CreateFromCore). Core 하나로 완제품은 하나만 —
--  앱은 Core 행 잠금으로 막고, 이 필터 유니크 인덱스가 DB 차원에서 보장한다.
--  인덱스가 없어도 기능은 동작한다(ParentLotID 조회가 인덱스 없이 돈다).
--
--  스키마 변경 없음 — 적용 순서 무관, 재실행 안전. 중복 연결이 이미 있으면 중단한다.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -i dist/migrate_img_core_lot.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- tbl_Lot 의 필터 인덱스 때문에 이 테이블의 CREATE INDEX 는 QUOTED_IDENTIFIER ON 이 필수 — sqlcmd 기본은 OFF
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'UX_tbl_Lot_ImgParent' AND object_id = OBJECT_ID('dbo.tbl_Lot'))
    PRINT 'UX_tbl_Lot_ImgParent already exists';
ELSE
BEGIN
    IF EXISTS (SELECT ParentLotID FROM dbo.tbl_Lot
               WHERE  ParentLotID IS NOT NULL AND ProcessCode = 'IMG'
               GROUP  BY ParentLotID HAVING COUNT(*) > 1)
        THROW 50001, N'IMG LOT 중 같은 Core(ParentLotID)를 가진 행이 있다 — 정리 후 다시 실행', 1;

    CREATE UNIQUE NONCLUSTERED INDEX UX_tbl_Lot_ImgParent
        ON dbo.tbl_Lot (ParentLotID)
        WHERE ParentLotID IS NOT NULL AND ProcessCode = 'IMG';
    PRINT 'UX_tbl_Lot_ImgParent created';
END
GO

SELECT name, is_unique, has_filter, filter_definition
FROM   sys.indexes
WHERE  object_id = OBJECT_ID('dbo.tbl_Lot') AND name = 'UX_tbl_Lot_ImgParent';
GO
