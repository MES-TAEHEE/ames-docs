/* ------------------------------------------------------------------
   migrate_md_item_scan_required.sql  (2026-10-06)
   MD_Item 에 스캔 필수 여부 ScanRequired 를 추가

     ScanRequired  BIT NOT NULL DEFAULT 0 (DF_MD_Item_ScanRequired)

   · 개발 DB 에 10-06 직접 추가된 컬럼을 소스·로컬 DB 에 맞춘다 — 개발 DB 와 같이 맨 뒤(LeadTimeDays 다음)에 둔다.
   · 기존 행은 0. 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · MD-003(/md/fd/items) 신 Web 은 이 컬럼을 읽으므로 이 마이그레이션 없이 올리면 품목 목록이 예외(Invalid column name 'ScanRequired').
   ------------------------------------------------------------------ */
SET NOCOUNT ON;

IF COL_LENGTH('dbo.MD_Item', 'ScanRequired') IS NOT NULL
BEGIN
    PRINT N'· MD_Item.ScanRequired 이미 있음';
    RETURN;
END

ALTER TABLE dbo.MD_Item ADD ScanRequired BIT NOT NULL
    CONSTRAINT DF_MD_Item_ScanRequired DEFAULT (0);
PRINT N'· MD_Item.ScanRequired 추가';
GO

SELECT c.column_id, c.name, TYPE_NAME(c.user_type_id) AS type_name, c.is_nullable, d.name AS default_name
FROM   sys.columns c
LEFT   JOIN sys.default_constraints d ON d.object_id = c.default_object_id
WHERE  c.object_id = OBJECT_ID('dbo.MD_Item') AND c.name = 'ScanRequired';
GO
