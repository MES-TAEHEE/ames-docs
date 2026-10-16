-- ════════════════════════════════════════════════════════════════════════
--  migrate_md_codeitem_widen.sql
--  MD_CodeItem.Attribute1 NVARCHAR(40) → NVARCHAR(200), Description NVARCHAR(120) → NVARCHAR(500)
--
--  개발서버는 이미 200/500 으로 넓어져 있었고(마이그레이션 없이 수동 변경) 스키마·로컬은 40/120 이라
--  구조가 어긋나 있었다. 넓히는 방향이 데이터를 잃지 않으므로 개발서버 값을 정본으로 맞춘다.
--  순서 무관, 재실행 안전(현재 길이가 짧을 때만 ALTER).
--
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -C -f 65001 -I -b -i dist/migrate_md_codeitem_widen.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.MD_CodeItem', 'Attribute1') < 400
BEGIN
    ALTER TABLE dbo.MD_CodeItem ALTER COLUMN [Attribute1] NVARCHAR(200) NULL;
    PRINT 'MD_CodeItem.Attribute1 -> NVARCHAR(200)';
END
ELSE
    PRINT 'MD_CodeItem.Attribute1 already NVARCHAR(200)';

IF COL_LENGTH('dbo.MD_CodeItem', 'Description') < 1000
BEGIN
    ALTER TABLE dbo.MD_CodeItem ALTER COLUMN [Description] NVARCHAR(500) NULL;
    PRINT 'MD_CodeItem.Description -> NVARCHAR(500)';
END
ELSE
    PRINT 'MD_CodeItem.Description already NVARCHAR(500)';
GO

SELECT c.name, t.name AS type_name, c.max_length / 2 AS chars
FROM   sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.MD_CodeItem') AND c.name IN ('Attribute1', 'Description');
GO
