/* ------------------------------------------------------------------
   migrate_md_item_inj_flag.sql  (2026-10-03)
   MD_Item 에 사출품 여부 InjFlag 를 ItemCategory 바로 뒤에 추가

     InjFlag  BIT NOT NULL DEFAULT 0   사출품 여부 — 품목 유형 SUB 만 1 이 될 수 있다(그 밖의 유형은 저장할 때 0)
       이름은 기존 사출 약어 Inj(PR_InjLot·MD_InjCondItem·MD_Mold.AssyInjResultFlag)와 bit 접미사 Flag(ToteFlag·ActiveFlag)를 따랐다.

   · 기존 행은 0 — 자동으로 채우지 않는다(MD-003 에서 지정).
   · 컬럼 순서 때문에 테이블을 재생성한다(migrate_md_item_pallet_qty.sql 과 같은 방식). 가드: 컬럼이 없거나 ItemCategory 바로 뒤가 아니면 재생성.
   · MD_Item 을 참조하는 FK 와 컬럼 설명(MS_Description)은 DB 에서 읽어 두었다가 재생성 뒤 그대로 되살린다.
   · 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · MD-003(/md/fd/items) 신 Web 은 이 컬럼을 읽으므로 이 마이그레이션 없이 올리면 품목 목록이 예외(Invalid column name 'InjFlag').
     MD_Item 을 SELECT 하는 다른 화면은 컬럼을 이름으로 고르므로 영향 없다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.MD_Item', 'InjFlag') IS NOT NULL
   AND (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_Item') AND name = 'InjFlag')
     = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_Item') AND name = 'ItemCategory') + 1
BEGIN
    PRINT N'· MD_Item.InjFlag 이미 ItemCategory 뒤에 있음';
    RETURN;
END

BEGIN TRAN;

-- 1) 들어오는 FK 를 스크립트로 저장 (단일·복합 컬럼 모두)
DECLARE @fk TABLE (Name sysname, DropSql nvarchar(max), AddSql nvarchar(max));
INSERT @fk (Name, DropSql, AddSql)
SELECT fk.name,
       N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name),
       N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
         + CASE WHEN fk.is_not_trusted = 1 THEN N' WITH NOCHECK' ELSE N' WITH CHECK' END
         + N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ('
         + (SELECT STRING_AGG(QUOTENAME(COL_NAME(fc.parent_object_id, fc.parent_column_id)), N',') WITHIN GROUP (ORDER BY fc.constraint_column_id)
            FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id = fk.object_id)
         + N') REFERENCES [dbo].[MD_Item] ('
         + (SELECT STRING_AGG(QUOTENAME(COL_NAME(fc.referenced_object_id, fc.referenced_column_id)), N',') WITHIN GROUP (ORDER BY fc.constraint_column_id)
            FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id = fk.object_id)
         + N')'
         + CASE fk.delete_referential_action WHEN 1 THEN N' ON DELETE CASCADE' WHEN 2 THEN N' ON DELETE SET NULL' WHEN 3 THEN N' ON DELETE SET DEFAULT' ELSE N'' END
         + CASE fk.update_referential_action WHEN 1 THEN N' ON UPDATE CASCADE' WHEN 2 THEN N' ON UPDATE SET NULL' WHEN 3 THEN N' ON UPDATE SET DEFAULT' ELSE N'' END
         + CASE WHEN fk.is_disabled = 1 THEN N'; ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) ELSE N'' END
FROM   sys.foreign_keys fk
JOIN   sys.tables t ON t.object_id = fk.parent_object_id
WHERE  fk.referenced_object_id = OBJECT_ID('dbo.MD_Item');

-- 2) 컬럼·테이블 설명 저장
DECLARE @ep TABLE (ColName sysname NULL, Name sysname, Value sql_variant);
INSERT @ep (ColName, Name, Value)
SELECT CASE WHEN ep.minor_id = 0 THEN NULL ELSE COL_NAME(ep.major_id, ep.minor_id) END, ep.name, ep.value
FROM   sys.extended_properties ep
WHERE  ep.class = 1 AND ep.major_id = OBJECT_ID('dbo.MD_Item');

DECLARE @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT DropSql FROM @fk;
OPEN c; FETCH NEXT FROM c INTO @sql;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC (@sql); FETCH NEXT FROM c INTO @sql; END
CLOSE c; DEALLOCATE c;

-- 3) 새 순서로 재생성
IF OBJECT_ID('dbo.MD_Item_new', 'U') IS NOT NULL DROP TABLE dbo.MD_Item_new;
CREATE TABLE dbo.MD_Item_new (
    ItemNo              varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
    ItemName            nvarchar(80)  COLLATE Korean_Wansung_CI_AS NOT NULL,
    ItemNameEN          nvarchar(80)  COLLATE Korean_Wansung_CI_AS NULL,
    ItemType            varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
    ItemCategory        varchar(30)   COLLATE Korean_Wansung_CI_AS NULL,
    InjFlag             bit           NOT NULL DEFAULT ((0)),
    DefaultUOM          varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
    RoutingType         char(1)       COLLATE Korean_Wansung_CI_AS NULL,
    MinStock            decimal(14,4) NULL,
    MaxStock            decimal(14,4) NULL,
    SafetyStock         decimal(14,4) NULL,
    UnitCost            decimal(14,2) NULL,
    CustItemNoSAV       varchar(30)   COLLATE Korean_Wansung_CI_AS NULL,
    CustItemNoGEO       varchar(30)   COLLATE Korean_Wansung_CI_AS NULL,
    DrawingNo           varchar(30)   COLLATE Korean_Wansung_CI_AS NULL,
    PalletQty           int           NULL,
    MaxPalletQty        int           NULL,
    ToteFlag            bit           NOT NULL DEFAULT ((0)),
    ActiveFlag          bit           NULL DEFAULT ((1)),
    CreatedBy           varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
    CreatedTS           datetime2(7)  NULL DEFAULT (sysdatetime()),
    ModifiedBy          varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
    ModifiedTS          datetime2(7)  NULL,
    CarType             varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
    PGN                 varchar(4)    COLLATE Korean_Wansung_CI_AS NULL,
    ALC                 varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
    MountPos            varchar(2)    COLLATE Korean_Wansung_CI_AS NULL,
    SparePartNo         nvarchar(80)  COLLATE Korean_Wansung_CI_AS NULL,
    ApplicableEquipment nvarchar(80)  COLLATE Korean_Wansung_CI_AS NULL,
    MakerName           nvarchar(80)  COLLATE Korean_Wansung_CI_AS NULL,
    LeadTimeDays        int           NULL,
    CONSTRAINT PK_MD_Item_new PRIMARY KEY CLUSTERED (ItemNo)
);

-- InjFlag 가 이미 있으면(순서만 다른 초판) 값을 옮기고, 없으면 0
SET @sql = N'INSERT INTO dbo.MD_Item_new
    (ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory, InjFlag, DefaultUOM, RoutingType,
     MinStock, MaxStock, SafetyStock, UnitCost, CustItemNoSAV, CustItemNoGEO, DrawingNo,
     PalletQty, MaxPalletQty, ToteFlag,
     ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS,
     CarType, PGN, ALC, MountPos, SparePartNo, ApplicableEquipment, MakerName, LeadTimeDays)
SELECT ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory, '
    + CASE WHEN COL_LENGTH('dbo.MD_Item', 'InjFlag') IS NULL THEN N'0' ELSE N'ISNULL(InjFlag, 0)' END + N', DefaultUOM, RoutingType,
       MinStock, MaxStock, SafetyStock, UnitCost, CustItemNoSAV, CustItemNoGEO, DrawingNo,
       PalletQty, MaxPalletQty, ToteFlag,
       ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS,
       CarType, PGN, ALC, MountPos, SparePartNo, ApplicableEquipment, MakerName, LeadTimeDays
FROM dbo.MD_Item;';
EXEC sp_executesql @sql;
DECLARE @moved int = @@ROWCOUNT;

DROP TABLE dbo.MD_Item;
EXEC sp_rename 'dbo.MD_Item_new', 'MD_Item';
EXEC sp_rename 'dbo.PK_MD_Item_new', 'PK_MD_Item', 'OBJECT';

-- 4) 설명 되살리기 + 새 컬럼 설명
DECLARE @col sysname, @name sysname, @val sql_variant;
DECLARE e CURSOR LOCAL FAST_FORWARD FOR SELECT ColName, Name, Value FROM @ep WHERE ColName IS NULL OR COL_LENGTH('dbo.MD_Item', ColName) IS NOT NULL;
OPEN e; FETCH NEXT FROM e INTO @col, @name, @val;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF @col IS NULL
        EXEC sys.sp_addextendedproperty @name = @name, @value = @val, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Item';
    ELSE
        EXEC sys.sp_addextendedproperty @name = @name, @value = @val, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Item', @level2type = N'COLUMN', @level2name = @col;
    FETCH NEXT FROM e INTO @col, @name, @val;
END
CLOSE e; DEALLOCATE e;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_Item') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_Item'), 'InjFlag', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'사출품 여부 — 품목 유형 SUB 만 1 · bit', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Item', @level2type = N'COLUMN', @level2name = N'InjFlag';

-- 5) FK 되살리기
DECLARE a CURSOR LOCAL FAST_FORWARD FOR SELECT AddSql FROM @fk;
OPEN a; FETCH NEXT FROM a INTO @sql;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC (@sql); FETCH NEXT FROM a INTO @sql; END
CLOSE a; DEALLOCATE a;

DECLARE @fkCount int = (SELECT COUNT(*) FROM @fk);
COMMIT;
PRINT CONCAT(N'✓ MD_Item 재생성 (', @moved, N' 행) — InjFlag 를 ItemCategory 뒤에, FK ', @fkCount, N' 개 복원');
GO

SELECT c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.MD_Item') AND c.column_id BETWEEN 4 AND 8 ORDER BY c.column_id;
SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS ParentTable, fk.is_not_trusted FROM sys.foreign_keys fk WHERE fk.referenced_object_id = OBJECT_ID('dbo.MD_Item');
SELECT COUNT(*) AS ItemRows, (SELECT COUNT(*) FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_Item')) AS Descriptions FROM dbo.MD_Item;
GO
