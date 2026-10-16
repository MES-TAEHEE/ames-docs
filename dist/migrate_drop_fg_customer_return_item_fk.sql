-- ════════════════════════════════════════════════════════════════════════
--  migrate_drop_fg_customer_return_item_fk.sql
--  FG_CustomerReturn.ItemNo → MD_Item FK(FK_FG_CustomerReturn_Item) 삭제
--
--  품목 마스터는 엑셀 정본으로 통째로 재적재하는데(seed_md_item_bom_master_list.sql),
--  반품 이력 1건이 이 FK 로 MD_Item DELETE 를 막았다. 반품 행의 품번은 받은 시점의
--  기록으로 남기고 마스터와 묶지 않는다(사용자 결정). Lot·Order FK 는 유지한다.
--  AMES_Schema.sql · pda/PDA_SCHEMA.sql 도 더 이상 이 FK 를 만들지 않는다.
--
--  스키마 변경: 제약 삭제만. 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/migrate_drop_fg_customer_return_item_fk.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.FK_FG_CustomerReturn_Item', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT FK_FG_CustomerReturn_Item;
    PRINT 'FK_FG_CustomerReturn_Item dropped';
END
ELSE
    PRINT 'FK_FG_CustomerReturn_Item already absent';
GO

SELECT fk.name, OBJECT_NAME(fk.referenced_object_id) AS referenced_table
FROM   sys.foreign_keys fk
WHERE  fk.parent_object_id = OBJECT_ID(N'dbo.FG_CustomerReturn')
ORDER  BY fk.name;
GO
