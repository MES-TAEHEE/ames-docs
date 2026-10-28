-- ════════════════════════════════════════════════════════════════════════
-- migrate_md_item_procure_type.sql  (10-20)
-- 품목 마스터에 조달형태 MD_Item.ProcureType(varchar(10) NULL)을 더하고 공통코드 PROCURE_TYPE 로 관리한다(사용자 요청).
--   INHOUSE 내작 · PURCHASE 매입 · PAID_SUP 유상사급 · FREE_SUP 무상사급
--   자리: SAPTransFlag 바로 앞(LeadTimeDays 다음). 기존 행은 NULL(자동으로 채우지 않음). 화면: MD-003 품목 목록 열·필터·등록/수정 콤보.
--
-- 방법: ALTER ADD 로 붙인 뒤(감사 컬럼 뒤로 감) SAPTransFlag 와 감사 컬럼을 하나씩 맨 뒤로 옮긴다
--       (migrate_md_sap_trans_flag.sql 과 같은 방식 — 임시 컬럼 추가 → 값 복사 → 원래 컬럼 삭제 → 이름 변경,
--        기본값 이름·NOT NULL·컬럼 설명·그 컬럼이 든 인덱스는 되살린다). 키·외래키는 그대로라 들어오는 참조도 영향 없다.
-- 재실행 안전 — 컬럼이 있으면 추가하지 않고, 이미 SAPTransFlag·감사 컬럼이 맨 뒤면 건너뛰며, 공통코드는 없는 행만 넣는다.
-- 신 Web 보다 먼저 적용한다 — 없으면 MD-003 목록과 ListItems 를 쓰는 화면(BOM·BOP·WO·출하계획·SCM)이 `Invalid column name 'ProcureType'` 로 예외.
--   구 Web·Pop·Api 는 이 컬럼을 읽지 않아 적용 뒤에도 그대로 동작한다.
--
-- 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <비밀번호> -C -f 65001 -I -b -i distmigrate_md_item_procure_type.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1) 공통코드 그룹·항목 — 없는 행만(MD-030 에서 고친 이름은 보존)
BEGIN TRAN;
MERGE dbo.MD_CodeGroup AS t
USING (SELECT 'PROCURE_TYPE' AS GroupCode, N'조달형태' AS GroupName, N'Procurement type' AS GroupNameEn,
              N'MD_Item.ProcureType — INHOUSE 내작·PURCHASE 매입·PAID_SUP 유상사급·FREE_SUP 무상사급' AS Description) s
   ON t.GroupCode = s.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.GroupCode, s.GroupName, s.GroupNameEn, s.Description, 1, 'system', SYSDATETIME());

MERGE dbo.MD_CodeItem AS t
USING (VALUES
        ('PROCURE_TYPE_INHOUSE',  'INHOUSE',  N'내작',     N'In-house',        10, N'자사 생산'),
        ('PROCURE_TYPE_PURCHASE', 'PURCHASE', N'매입',     N'Purchase',        20, N'협력업체에서 구매'),
        ('PROCURE_TYPE_PAID_SUP', 'PAID_SUP', N'유상사급', N'Paid supply',     30, N'고객사가 유상으로 공급'),
        ('PROCURE_TYPE_FREE_SUP', 'FREE_SUP', N'무상사급', N'Free supply',     40, N'고객사가 무상으로 공급')
      ) AS s (CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, Descr)
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, Description, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'PROCURE_TYPE', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, s.Descr, 'system', SYSDATETIME());
COMMIT;
GO

-- 2) 컬럼
IF COL_LENGTH(N'dbo.MD_Item', N'ProcureType') IS NULL
    ALTER TABLE dbo.MD_Item ADD ProcureType varchar(10) COLLATE DATABASE_DEFAULT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE class = 1 AND name = N'MS_Description' AND major_id = OBJECT_ID(N'dbo.MD_Item')
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_Item'), N'ProcureType', 'ColumnId'))
    EXEC sys.sp_addextendedproperty @name = N'MS_Description',
         @value = N'조달형태 — 공통코드 PROCURE_TYPE(INHOUSE 내작·PURCHASE 매입·PAID_SUP 유상사급·FREE_SUP 무상사급) · varchar(10)',
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Item',
         @level2type = N'COLUMN', @level2name = N'ProcureType';
GO

-- SAPTransFlag·감사 컬럼을 맨 뒤로(MD_Item 만) — ProcureType 이 그 앞에 남는다
DECLARE @audit TABLE (Ord int PRIMARY KEY, Name sysname COLLATE DATABASE_DEFAULT NOT NULL);
INSERT @audit VALUES (1, N'SAPTransFlag'), (2, N'SAPFlag'), (3, N'CreatedBy'), (4, N'CreatedTS'), (5, N'ModifiedBy'), (6, N'ModifiedTS');   -- SAPFlag = 10-21 개명 뒤 이름

DECLARE @tables TABLE (Seq int IDENTITY PRIMARY KEY, ObjectId int NOT NULL, TableName sysname COLLATE DATABASE_DEFAULT NOT NULL);
INSERT @tables (ObjectId, TableName)
SELECT t.object_id, t.name FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name = N'MD_Item'
ORDER BY t.name;

DECLARE @seq int = 0, @tid int, @t sysname, @qt nvarchar(300), @k int, @tail nvarchar(max), @want nvarchar(max),
        @dropIdx nvarchar(max), @createIdx nvarchar(max), @trig nvarchar(max), @sql nvarchar(max), @msg nvarchar(2048),
        @ord int, @c sysname, @type nvarchar(200), @nullable bit, @df sysname, @dfDef nvarchar(max), @mv sysname,
        @epName sysname, @epValue sql_variant;
DECLARE @ep TABLE (Name sysname, Value sql_variant);

WHILE 1 = 1
BEGIN
    SELECT TOP (1) @seq = Seq, @tid = ObjectId, @t = TableName FROM @tables WHERE Seq > @seq ORDER BY Seq;
    IF @@ROWCOUNT = 0 BREAK;
    SET @qt = N'dbo.' + QUOTENAME(@t);

    SELECT @k = COUNT(*) FROM sys.columns c JOIN @audit a ON a.Name = c.name COLLATE DATABASE_DEFAULT WHERE c.object_id = @tid;
    IF @k = 0 CONTINUE;
    SELECT @want = STRING_AGG(CAST(a.Name AS nvarchar(max)), N',') WITHIN GROUP (ORDER BY a.Ord)
    FROM @audit a JOIN sys.columns c ON c.name COLLATE DATABASE_DEFAULT = a.Name AND c.object_id = @tid;
    SELECT @tail = STRING_AGG(CAST(x.name COLLATE DATABASE_DEFAULT AS nvarchar(max)), N',') WITHIN GROUP (ORDER BY x.column_id)
    FROM (SELECT TOP (@k) c.name, c.column_id FROM sys.columns c WHERE c.object_id = @tid ORDER BY c.column_id DESC) x;
    IF @tail = @want CONTINUE;

    IF EXISTS (SELECT 1 FROM sys.indexes i
               JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
               JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               JOIN @audit a ON a.Name = c.name COLLATE DATABASE_DEFAULT
               WHERE i.object_id = @tid AND (i.is_primary_key = 1 OR i.is_unique_constraint = 1 OR i.type <> 2))
       OR EXISTS (SELECT 1 FROM sys.foreign_key_columns fc
                  JOIN sys.columns c ON (c.object_id = fc.parent_object_id AND c.column_id = fc.parent_column_id)
                                     OR (c.object_id = fc.referenced_object_id AND c.column_id = fc.referenced_column_id)
                  JOIN @audit a ON a.Name = c.name COLLATE DATABASE_DEFAULT
                  WHERE c.object_id = @tid)
    BEGIN
        SET @msg = @t + N': SAPTransFlag·감사 컬럼이 기본키·클러스터 인덱스·UNIQUE 제약·외래키에 들어 있어 옮기지 않는다 — 수동 처리 필요';
        THROW 50001, @msg, 1;
    END;

    SELECT @dropIdx = STRING_AGG(CAST(N'DROP INDEX ' + QUOTENAME(i.name) + N' ON ' + @qt + N';' AS nvarchar(max)), N' '),
           @createIdx = STRING_AGG(CAST(N'CREATE ' + CASE WHEN i.is_unique = 1 THEN N'UNIQUE ' ELSE N'' END
                + N'NONCLUSTERED INDEX ' + QUOTENAME(i.name) + N' ON ' + @qt + N' (' + k.Keys + N')'
                + ISNULL(N' INCLUDE (' + k.Incl + N')', N'')
                + ISNULL(N' WHERE ' + i.filter_definition, N'')
                + CASE WHEN i.fill_factor > 0 THEN N' WITH (FILLFACTOR = ' + CAST(i.fill_factor AS nvarchar(3)) + N')' ELSE N'' END
                + N';' AS nvarchar(max)), N' ')
    FROM sys.indexes i
    CROSS APPLY (SELECT
        (SELECT STRING_AGG(CAST(QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N' ASC' END AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY ic.key_ordinal)
         FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
         WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0) AS Keys,
        (SELECT STRING_AGG(CAST(QUOTENAME(c.name) AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY ic.index_column_id)
         FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
         WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1) AS Incl) k
    WHERE i.object_id = @tid AND i.type = 2
      AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  JOIN @audit a ON a.Name = c.name COLLATE DATABASE_DEFAULT
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id);

    SELECT @trig = STRING_AGG(CAST(QUOTENAME(tr.name) AS nvarchar(max)), N', ')
    FROM sys.triggers tr WHERE tr.parent_id = @tid AND tr.is_disabled = 0;

    BEGIN TRANSACTION;

    IF @trig IS NOT NULL BEGIN SET @sql = N'DISABLE TRIGGER ' + @trig + N' ON ' + @qt + N';'; EXEC (@sql); END;
    IF @dropIdx IS NOT NULL EXEC (@dropIdx);

    SET @ord = 0;
    WHILE 1 = 1
    BEGIN
        SELECT TOP (1) @ord = a.Ord, @c = c.name, @nullable = c.is_nullable,
               @type = CASE WHEN ty.is_user_defined = 1 THEN QUOTENAME(SCHEMA_NAME(ty.schema_id)) + N'.' + QUOTENAME(ty.name)
                            WHEN ty.name IN (N'varchar', N'char', N'varbinary', N'binary')
                                 THEN ty.name + N'(' + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS nvarchar(10)) END + N')'
                            WHEN ty.name IN (N'nvarchar', N'nchar')
                                 THEN ty.name + N'(' + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS nvarchar(10)) END + N')'
                            WHEN ty.name IN (N'decimal', N'numeric')
                                 THEN ty.name + N'(' + CAST(c.precision AS nvarchar(3)) + N',' + CAST(c.scale AS nvarchar(3)) + N')'
                            WHEN ty.name IN (N'datetime2', N'time', N'datetimeoffset')
                                 THEN ty.name + N'(' + CAST(c.scale AS nvarchar(3)) + N')'
                            ELSE ty.name END
                     + CASE WHEN c.collation_name IS NOT NULL THEN N' COLLATE ' + c.collation_name ELSE N'' END,
               @df = dc.name, @dfDef = dc.definition
        FROM @audit a
        JOIN sys.columns c ON c.object_id = @tid AND c.name COLLATE DATABASE_DEFAULT = a.Name
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
        WHERE a.Ord > @ord ORDER BY a.Ord;
        IF @@ROWCOUNT = 0 BREAK;

        DELETE @ep;
        INSERT @ep SELECT ep.name, ep.value FROM sys.extended_properties ep
        WHERE ep.class = 1 AND ep.major_id = @tid AND ep.minor_id = COLUMNPROPERTY(@tid, @c, 'ColumnId');

        SET @mv = @c + N'__mv';
        IF @df IS NOT NULL BEGIN SET @sql = N'ALTER TABLE ' + @qt + N' DROP CONSTRAINT ' + QUOTENAME(@df) + N';'; EXEC (@sql); END;
        SET @sql = N'ALTER TABLE ' + @qt + N' ADD ' + QUOTENAME(@mv) + N' ' + @type + N' NULL;'; EXEC (@sql);
        SET @sql = N'UPDATE ' + @qt + N' SET ' + QUOTENAME(@mv) + N' = ' + QUOTENAME(@c) + N';'; EXEC (@sql);
        SET @sql = N'ALTER TABLE ' + @qt + N' DROP COLUMN ' + QUOTENAME(@c) + N';'; EXEC (@sql);
        SET @sql = @qt + N'.' + QUOTENAME(@mv);
        EXEC sys.sp_rename @sql, @c, N'COLUMN';
        IF @nullable = 0 BEGIN SET @sql = N'ALTER TABLE ' + @qt + N' ALTER COLUMN ' + QUOTENAME(@c) + N' ' + @type + N' NOT NULL;'; EXEC (@sql); END;
        IF @df IS NOT NULL BEGIN SET @sql = N'ALTER TABLE ' + @qt + N' ADD CONSTRAINT ' + QUOTENAME(@df) + N' DEFAULT ' + @dfDef + N' FOR ' + QUOTENAME(@c) + N';'; EXEC (@sql); END;

        WHILE EXISTS (SELECT 1 FROM @ep)
        BEGIN
            SELECT TOP (1) @epName = Name, @epValue = Value FROM @ep ORDER BY Name;
            EXEC sys.sp_addextendedproperty @name = @epName, @value = @epValue,
                 @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = @t,
                 @level2type = N'COLUMN', @level2name = @c;
            DELETE @ep WHERE Name = @epName;
        END;
    END;

    IF @createIdx IS NOT NULL EXEC (@createIdx);
    IF @trig IS NOT NULL BEGIN SET @sql = N'ENABLE TRIGGER ' + @trig + N' ON ' + @qt + N';'; EXEC (@sql); END;

    COMMIT TRANSACTION;

    SET @sql = N'ALTER TABLE ' + @qt + N' REBUILD;'; EXEC (@sql);
    PRINT N'✓ ' + @t + N' — ' + @want + N' 를 맨 뒤로';
    SELECT @dropIdx = NULL, @createIdx = NULL, @trig = NULL;
END;
GO

-- 확인 — LeadTimeDays 뒤가 ProcureType · SAPTransFlag · 감사 컬럼 순서여야 한다
SELECT c.column_id, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS TypeName, c.max_length, c.is_nullable, dc.name AS DefaultName
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE c.object_id = OBJECT_ID(N'dbo.MD_Item')
  AND c.column_id >= COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_Item'), N'LeadTimeDays', 'ColumnId')
ORDER BY c.column_id;
SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'PROCURE_TYPE' ORDER BY SortOrder;
GO
