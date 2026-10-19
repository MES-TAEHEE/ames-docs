-- ════════════════════════════════════════════════════════════════════════
-- migrate_md_bomversion_sap_trans_flag.sql  (10-20)
-- BOM 버전 헤더에 SAP 전송 대상 여부 SAPTransFlag(bit NOT NULL DEFAULT 0)를 더한다(사용자 요청 — 품목·협력업체·로케이션과 같은 규칙).
--   MD_BomVersion 만 — 라인(MD_Bom)에는 두지 않는다(라인 일부만 보내면 SAP 의 BOM 이 틀어진다, 전송 단위 = 버전).
--   자리: 업무 컬럼의 맨 뒤(감사 컬럼 바로 앞). 기존 행은 0(전송 안 함). 화면: MD-004 버전 목록 아이콘·헤더·등록/수정 체크박스.
--   값을 저장·표시만 한다 — SAP 로 보내는 기능은 아직 없다.
--
-- 방법: ALTER ADD 로 붙인 뒤(감사 컬럼 뒤로 감) 감사 컬럼을 하나씩 맨 뒤로 옮긴다
--       (migrate_md_sap_trans_flag.sql 과 같은 방식 — 기본값 이름·NOT NULL·컬럼 설명·인덱스 보존, 키 그대로).
-- 재실행 안전 — 컬럼이 있으면 추가하지 않고, 감사 컬럼이 이미 맨 뒤면 건너뛴다.
-- 신 Web 보다 먼저 적용한다 — 없으면 MD-004 BOM 과 ListBomVersions 를 쓰는 화면(MD-005 BOP 등)이 `Invalid column name 'SAPTransFlag'` 로 예외.
--   구 Web·Pop·Api 는 이 컬럼을 읽지 않아 적용 뒤에도 그대로 동작한다.
--
-- 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <비밀번호> -C -f 65001 -I -b -i dist\migrate_md_bomversion_sap_trans_flag.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.MD_BomVersion', N'SAPTransFlag') IS NULL
    ALTER TABLE dbo.MD_BomVersion ADD SAPTransFlag bit NOT NULL CONSTRAINT DF_MD_BomVersion_SAPTransFlag DEFAULT (0) WITH VALUES;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE class = 1 AND name = N'MS_Description' AND major_id = OBJECT_ID(N'dbo.MD_BomVersion')
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_BomVersion'), N'SAPTransFlag', 'ColumnId'))
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'SAP 전송 대상 여부 — 1 이면 이 버전(유효할 때)을 SAP BOM 으로 보낸다 · bit',
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_BomVersion',
         @level2type = N'COLUMN', @level2name = N'SAPTransFlag';
GO

-- 감사 컬럼을 맨 뒤로(MD_BomVersion 만)
DECLARE @audit TABLE (Ord int PRIMARY KEY, Name sysname COLLATE DATABASE_DEFAULT NOT NULL);
INSERT @audit VALUES (1, N'CreatedBy'), (2, N'CreatedTS'), (3, N'ModifiedBy'), (4, N'ModifiedTS');

DECLARE @tables TABLE (Seq int IDENTITY PRIMARY KEY, ObjectId int NOT NULL, TableName sysname COLLATE DATABASE_DEFAULT NOT NULL);
INSERT @tables (ObjectId, TableName)
SELECT t.object_id, t.name FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name = N'MD_BomVersion'
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
        SET @msg = @t + N': 감사 컬럼이 기본키·클러스터 인덱스·UNIQUE 제약·외래키에 들어 있어 옮기지 않는다 — 수동 처리 필요';
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
    PRINT N'✓ ' + @t + N' — 감사 컬럼 ' + @want + N' 를 맨 뒤로';
    SELECT @dropIdx = NULL, @createIdx = NULL, @trig = NULL;
END;
GO

-- 확인 — SAPTransFlag 바로 뒤가 감사 컬럼이어야 한다
SELECT c.column_id, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS TypeName, c.is_nullable, dc.name AS DefaultName
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE c.object_id = OBJECT_ID(N'dbo.MD_BomVersion')
  AND c.column_id >= COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_BomVersion'), N'Status', 'ColumnId')
ORDER BY c.column_id;
GO
