-- ════════════════════════════════════════════════════════════════════════
--  migrate_md_item_mount_pos.sql
--  MD_Item.MountPos — 차량 장착 위치 (FL / FR / RL / RR)
--
--  IMG 완제품 라벨 우상단에 찍는 값. 고객 표준 DataMatrix 에는 들어가지 않고
--  사람이 읽는 글자로만 쓴다. 품번 마스터(MD-001)에서 관리한다.
--
--  스키마 변경: 컬럼 추가만. 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_md_item_mount_pos.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.MD_Item', 'MountPos') IS NULL
BEGIN
    ALTER TABLE dbo.MD_Item ADD [MountPos] VARCHAR(2) NULL;  -- FL / FR / RL / RR
    PRINT 'MD_Item.MountPos added';
END
ELSE
    PRINT 'MD_Item.MountPos already exists';
GO

SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c
JOIN   sys.types   t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.MD_Item') AND c.name IN ('PGN','ALC','MountPos')
ORDER  BY c.column_id;
GO
