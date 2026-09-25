/* ------------------------------------------------------------------
   migrate_md_equipment_type_tonnage.sql  (2026-09-26)

   1) 공통코드 EQUIP_TYPE 신설 — 설비 유형: INJ 사출 · WRAP 감싸기 · PNT 도장
   2) MD_Equipment.EquipType · MD_PmTemplate.EquipType 의 기존 값을 새 코드로 변환
        INJ · INJ_MACHINE · INJECTION*          → INJ
        IMG · WRAP_PRESS · WRAP*                 → WRAP   (IMG = 원단/래핑 공정)
        PNT · PNT_ROBOT · OVEN_UNIT · SPRAY_BOOTH · PAINT* → PNT (도장 라인 설비 — 경화 오븐 포함)
      그 밖의 값은 건드리지 않는다(재실행 안전, 운영자가 새로 넣은 코드 보존).
   3) MD_Equipment.Tonnage INT NULL(MD_Mold.Tonnage 와 같은 형)을 MoldCompatJSON 바로 앞에 추가.
      사출(INJ) 설비만 값을 가진다 — MD-014 가 INJ 에서만 입력받고 저장 시 그 밖의 유형은 비운다.
      컬럼 순서 때문에 테이블을 재생성하며, 컬럼·테이블 설명(MS_Description)은 DB 에서 읽어 되살린다
      (들어오는 FK 는 없다 — 있으면 함께 되살린다).

   · 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · 신 Web(MD-014)은 Tonnage 를 읽으므로 이 마이그레이션 없이 올리면 설비 목록이 예외.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

-- 1) 공통코드 그룹·항목 (없는 행만 넣는다 — MD-26 에서 고친 값은 보존)
MERGE dbo.MD_CodeGroup AS t
USING (SELECT 'EQUIP_TYPE' AS GroupCode, N'설비 유형' AS GroupName, N'Equipment type' AS GroupNameEn,
              N'MD_Equipment·MD_PmTemplate.EquipType — INJ 사출·WRAP 감싸기·PNT 도장' AS Description) s
   ON t.GroupCode = s.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.GroupCode, s.GroupName, s.GroupNameEn, s.Description, 1, 'system', SYSDATETIME());

MERGE dbo.MD_CodeItem AS t
USING (VALUES
        ('EQUIP_TYPE_INJ',  'INJ',  N'사출',   N'Injection', 10),
        ('EQUIP_TYPE_WRAP', 'WRAP', N'감싸기', N'Wrapping',  20),
        ('EQUIP_TYPE_PNT',  'PNT',  N'도장',   N'Painting',  30)
      ) AS s (CodeID, CodeValue, CodeName, CodeNameEn, SortOrder)
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'EQUIP_TYPE', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, 'system', SYSDATETIME());

-- 2) 기존 값 변환
UPDATE dbo.MD_Equipment
SET    EquipType = CASE
           WHEN EquipType IN ('INJ_MACHINE') OR EquipType LIKE 'INJECT%'                           THEN 'INJ'
           WHEN EquipType IN ('IMG', 'WRAP_PRESS') OR EquipType LIKE 'WRAP_%' OR EquipType LIKE 'WRAPP%' THEN 'WRAP'
           WHEN EquipType IN ('PNT_ROBOT', 'OVEN_UNIT', 'SPRAY_BOOTH') OR EquipType LIKE 'PAINT%'    THEN 'PNT'
       END,
       ModifiedBy = 'system', ModifiedTS = SYSDATETIME()
WHERE  EquipType IN ('INJ_MACHINE', 'IMG', 'WRAP_PRESS', 'PNT_ROBOT', 'OVEN_UNIT', 'SPRAY_BOOTH')
    OR EquipType LIKE 'INJECT%' OR EquipType LIKE 'WRAP[_]%' OR EquipType LIKE 'WRAPP%' OR EquipType LIKE 'PAINT%';
PRINT N'· MD_Equipment.EquipType 변환 ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'행';

UPDATE dbo.MD_PmTemplate
SET    EquipType = CASE
           WHEN EquipType IN ('INJ_MACHINE') OR EquipType LIKE 'INJECT%'                           THEN 'INJ'
           WHEN EquipType IN ('IMG', 'WRAP_PRESS') OR EquipType LIKE 'WRAP_%' OR EquipType LIKE 'WRAPP%' THEN 'WRAP'
           WHEN EquipType IN ('PNT_ROBOT', 'OVEN_UNIT', 'SPRAY_BOOTH') OR EquipType LIKE 'PAINT%'    THEN 'PNT'
       END,
       ModifiedBy = 'system', ModifiedTS = SYSDATETIME()
WHERE  EquipType IN ('INJ_MACHINE', 'IMG', 'WRAP_PRESS', 'PNT_ROBOT', 'OVEN_UNIT', 'SPRAY_BOOTH')
    OR EquipType LIKE 'INJECT%' OR EquipType LIKE 'WRAP[_]%' OR EquipType LIKE 'WRAPP%' OR EquipType LIKE 'PAINT%';
PRINT N'· MD_PmTemplate.EquipType 변환 ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'행';

-- 컬럼 설명: 공통코드 기준으로
DECLARE @d1 nvarchar(200) = N'설비 유형 — 공통코드 EQUIP_TYPE (INJ 사출·WRAP 감싸기·PNT 도장).  · varchar(16)';
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_Equipment') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_Equipment'), 'EquipType', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = @d1, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Equipment', @level2type = N'COLUMN', @level2name = N'EquipType';
ELSE
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @d1, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Equipment', @level2type = N'COLUMN', @level2name = N'EquipType';
DECLARE @d2 nvarchar(200) = N'설비 유형 — 공통코드 EQUIP_TYPE (INJ 사출·WRAP 감싸기·PNT 도장), MD_Equipment.EquipType 과 같은 코드.  · varchar(16)';
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_PmTemplate') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_PmTemplate'), 'EquipType', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = @d2, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_PmTemplate', @level2type = N'COLUMN', @level2name = N'EquipType';
ELSE
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @d2, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_PmTemplate', @level2type = N'COLUMN', @level2name = N'EquipType';

COMMIT;
GO

-- 3) MD_Equipment.Tonnage — MoldCompatJSON 바로 앞(= TargetOEE 바로 뒤). 이미 그 자리에 있으면 건너뛴다
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.MD_Equipment', 'Tonnage') IS NOT NULL
   AND (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_Equipment') AND name = 'Tonnage')
     = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_Equipment') AND name = 'MoldCompatJSON') - 1
BEGIN
    PRINT N'· MD_Equipment.Tonnage 이미 MoldCompatJSON 앞에 있음';
    RETURN;
END

BEGIN TRAN;

-- 들어오는 FK 저장 (현재는 없지만 다른 개발자가 추가했을 수 있다)
DECLARE @fk TABLE (Name sysname, DropSql nvarchar(max), AddSql nvarchar(max));
INSERT @fk (Name, DropSql, AddSql)
SELECT fk.name,
       N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name),
       N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
         + CASE WHEN fk.is_not_trusted = 1 THEN N' WITH NOCHECK' ELSE N' WITH CHECK' END
         + N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ('
         + (SELECT STRING_AGG(QUOTENAME(COL_NAME(fc.parent_object_id, fc.parent_column_id)), N',') WITHIN GROUP (ORDER BY fc.constraint_column_id)
            FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id = fk.object_id)
         + N') REFERENCES [dbo].[MD_Equipment] ('
         + (SELECT STRING_AGG(QUOTENAME(COL_NAME(fc.referenced_object_id, fc.referenced_column_id)), N',') WITHIN GROUP (ORDER BY fc.constraint_column_id)
            FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id = fk.object_id)
         + N')'
         + CASE fk.delete_referential_action WHEN 1 THEN N' ON DELETE CASCADE' WHEN 2 THEN N' ON DELETE SET NULL' WHEN 3 THEN N' ON DELETE SET DEFAULT' ELSE N'' END
         + CASE fk.update_referential_action WHEN 1 THEN N' ON UPDATE CASCADE' WHEN 2 THEN N' ON UPDATE SET NULL' WHEN 3 THEN N' ON UPDATE SET DEFAULT' ELSE N'' END
         + CASE WHEN fk.is_disabled = 1 THEN N'; ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) ELSE N'' END
FROM   sys.foreign_keys fk
JOIN   sys.tables t ON t.object_id = fk.parent_object_id
WHERE  fk.referenced_object_id = OBJECT_ID('dbo.MD_Equipment');

-- 컬럼·테이블 설명 저장
DECLARE @ep TABLE (ColName sysname NULL, Name sysname, Value sql_variant);
INSERT @ep (ColName, Name, Value)
SELECT CASE WHEN ep.minor_id = 0 THEN NULL ELSE COL_NAME(ep.major_id, ep.minor_id) END, ep.name, ep.value
FROM   sys.extended_properties ep
WHERE  ep.class = 1 AND ep.major_id = OBJECT_ID('dbo.MD_Equipment');

DECLARE @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT DropSql FROM @fk;
OPEN c; FETCH NEXT FROM c INTO @sql;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC (@sql); FETCH NEXT FROM c INTO @sql; END
CLOSE c; DEALLOCATE c;

IF OBJECT_ID('dbo.MD_Equipment_new', 'U') IS NOT NULL DROP TABLE dbo.MD_Equipment_new;
CREATE TABLE dbo.MD_Equipment_new (
    EquipID          varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
    EquipName        nvarchar(50)  COLLATE Korean_Wansung_CI_AS NULL,
    EquipType        varchar(16)   COLLATE Korean_Wansung_CI_AS NULL,
    LineID           varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
    WCID             varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
    MakerModel       nvarchar(60)  COLLATE Korean_Wansung_CI_AS NULL,
    InstallDate      date          NULL,
    TheoreticalCycle decimal(8,2)  NULL,
    TargetOEE        decimal(5,2)  NULL,
    Tonnage          int           NULL,
    MoldCompatJSON   nvarchar(max) COLLATE Korean_Wansung_CI_AS NULL,
    PlcAddress       varchar(40)   COLLATE Korean_Wansung_CI_AS NULL,
    Status           varchar(8)    COLLATE Korean_Wansung_CI_AS NULL,
    ActiveFlag       bit           NULL DEFAULT ((1)),
    CreatedBy        varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
    CreatedTS        datetime2(7)  NULL DEFAULT (sysdatetime()),
    ModifiedBy       varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
    ModifiedTS       datetime2(7)  NULL,
    CONSTRAINT PK_MD_Equipment_new PRIMARY KEY CLUSTERED (EquipID)
);

-- 이미 Tonnage 가 있으면(순서만 다른 초판) 값을 옮기고, 없으면 NULL
SET @sql = N'INSERT INTO dbo.MD_Equipment_new
    (EquipID, EquipName, EquipType, LineID, WCID, MakerModel, InstallDate, TheoreticalCycle, TargetOEE,
     Tonnage, MoldCompatJSON, PlcAddress, Status, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
SELECT EquipID, EquipName, EquipType, LineID, WCID, MakerModel, InstallDate, TheoreticalCycle, TargetOEE, '
    + CASE WHEN COL_LENGTH('dbo.MD_Equipment', 'Tonnage') IS NULL THEN N'NULL' ELSE N'Tonnage' END + N',
       MoldCompatJSON, PlcAddress, Status, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
FROM dbo.MD_Equipment;';
EXEC sp_executesql @sql;
DECLARE @moved int = @@ROWCOUNT;

DROP TABLE dbo.MD_Equipment;
EXEC sp_rename 'dbo.MD_Equipment_new', 'MD_Equipment';
EXEC sp_rename 'dbo.PK_MD_Equipment_new', 'PK_MD_Equipment', 'OBJECT';

-- 설명 되살리기 + 새 컬럼 설명
DECLARE @col sysname, @name sysname, @val sql_variant;
DECLARE e CURSOR LOCAL FAST_FORWARD FOR SELECT ColName, Name, Value FROM @ep WHERE ColName IS NULL OR COL_LENGTH('dbo.MD_Equipment', ColName) IS NOT NULL;
OPEN e; FETCH NEXT FROM e INTO @col, @name, @val;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF @col IS NULL
        EXEC sys.sp_addextendedproperty @name = @name, @value = @val, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Equipment';
    ELSE
        EXEC sys.sp_addextendedproperty @name = @name, @value = @val, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Equipment', @level2type = N'COLUMN', @level2name = @col;
    FETCH NEXT FROM e INTO @col, @name, @val;
END
CLOSE e; DEALLOCATE e;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_Equipment') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_Equipment'), 'Tonnage', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'톤수 — 사출(INJ) 설비만 (MD_Mold.Tonnage 와 같은 형).  · int', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Equipment', @level2type = N'COLUMN', @level2name = N'Tonnage';

-- FK 되살리기
DECLARE a CURSOR LOCAL FAST_FORWARD FOR SELECT AddSql FROM @fk;
OPEN a; FETCH NEXT FROM a INTO @sql;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC (@sql); FETCH NEXT FROM a INTO @sql; END
CLOSE a; DEALLOCATE a;

DECLARE @fkCount int = (SELECT COUNT(*) FROM @fk);
COMMIT;
PRINT N'· MD_Equipment 재생성 — ' + CAST(@moved AS nvarchar(10)) + N'행, FK ' + CAST(@fkCount AS nvarchar(10)) + N'개 복원';
GO

-- 확인
SELECT c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.MD_Equipment') AND c.name IN ('TargetOEE', 'Tonnage', 'MoldCompatJSON') ORDER BY c.column_id;
SELECT 'MD_Equipment' AS Tbl, EquipType, COUNT(*) AS n FROM dbo.MD_Equipment GROUP BY EquipType
UNION ALL SELECT 'MD_PmTemplate', EquipType, COUNT(*) FROM dbo.MD_PmTemplate GROUP BY EquipType;
SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'EQUIP_TYPE' ORDER BY SortOrder;
GO
