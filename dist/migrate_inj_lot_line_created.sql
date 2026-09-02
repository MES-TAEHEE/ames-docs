-- ════════════════════════════════════════════════════════════════════════
--  migrate_inj_lot_line_created.sql
--  tbl_Lot (LineID, CreatedTS) 인덱스
--
--  INJ-MAIN 좌측 품번 패널(InjLotRepository.GetDailyItemSummary)과 상단 칩
--  (GetTodayStats)이 5초마다 "이 라인의 오늘 LOT" 을 센다. tbl_Lot 은 사출
--  샷마다 한 행이라 기존 IX_tbl_Lot_Line(LineID, LotID) 만으로는 매번
--  라인 전체 LOT 을 훑는다. 날짜 범위 seek 이 되도록 CreatedTS 를 키에 넣고
--  집계에 쓰는 ItemNo·LotID 를 INCLUDE 한다.
--
--  스키마 변경 없음 — 적용 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.2.137,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_inj_lot_line_created.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- tbl_Lot 에 필터 인덱스(UX_tbl_Lot_LotCode)가 있어 이 테이블의 CREATE INDEX 는 QUOTED_IDENTIFIER ON 이 필수 — sqlcmd 기본은 OFF
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_tbl_Lot_Line_Created'
                 AND object_id = OBJECT_ID('dbo.tbl_Lot'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_tbl_Lot_Line_Created
        ON dbo.tbl_Lot (LineID, CreatedTS)
        INCLUDE (ItemNo, LotID);
    PRINT 'IX_tbl_Lot_Line_Created created';
END
ELSE
    PRINT 'IX_tbl_Lot_Line_Created already exists';
GO

SELECT i.name, c.name AS col, ic.is_included_column
FROM   sys.indexes i
JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN   sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE  i.object_id = OBJECT_ID('dbo.tbl_Lot') AND i.name = 'IX_tbl_Lot_Line_Created'
ORDER  BY ic.is_included_column, ic.key_ordinal;
GO
