-- ════════════════════════════════════════════════════════════════════════
-- migrate_audit_columns_last.sql  (10-08)
-- 감사 컬럼을 테이블 맨 뒤에 CreatedBy → CreatedTS → ModifiedBy → ModifiedTS 순서로 둔다.
--
-- 대상: dbo 의 사용자 테이블 중 감사 컬럼(넷 중 하나라도)이 있고, 그 컬럼들이 맨 뒤에 위 순서로
--       있지 않은 테이블 (TEST_*·sysdiagrams 제외). 감사 컬럼이 없는 테이블은 건드리지 않는다.
--       일부만 있는 테이블은 있는 것만 같은 순서로 맨 뒤에 둔다.
-- 방법: 테이블 재생성 대신 감사 컬럼만 하나씩 맨 뒤로 옮긴다
--       (임시 컬럼 추가 → 값 복사 → 원래 컬럼 삭제 → 이름 변경) — 키·외래키·다른 인덱스·
--       IDENTITY 는 그대로이고, 기본값(같은 이름)·NOT NULL·컬럼 설명·그 컬럼이 든 인덱스는 되살린다.
--       값 복사 중에는 그 테이블의 켜진 트리거를 껐다가 다시 켠다.
--       테이블마다 트랜잭션 하나 — 실패하면 그 테이블은 원래대로 남고 스크립트가 멈춘다.
-- 중단: 감사 컬럼이 기본키·클러스터 인덱스·UNIQUE 제약·외래키에 들어 있으면 THROW
--       (그런 테이블은 없다 — 10-08 개발 DB 기준 감사 컬럼 인덱스는 IX_tbl_Lot_Line_Created 하나).
-- 재실행 안전 — 이미 맞는 테이블은 건너뛴다. 다른 마이그레이션이 컬럼을 맨 뒤에 붙여도
--   다시 돌리면 정리되므로 rebuild_db.sh 에서는 맨 끝에 둔다.
-- 앱 영향 없음 — 코드는 컬럼을 이름으로만 쓴다(SELECT * 순번 읽기·컬럼 목록 없는 INSERT 없음).
--
-- 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <비밀번호> -C -f 65001 -I -b -i dist\migrate_audit_columns_last.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @audit TABLE (Ord int PRIMARY KEY, Name sysname COLLATE DATABASE_DEFAULT NOT NULL);
INSERT @audit VALUES (1, N'CreatedBy'), (2, N'CreatedTS'), (3, N'ModifiedBy'), (4, N'ModifiedTS');

DECLARE @tables TABLE (Seq int IDENTITY PRIMARY KEY, ObjectId int NOT NULL, TableName sysname COLLATE DATABASE_DEFAULT NOT NULL);
INSERT @tables (ObjectId, TableName)
SELECT t.object_id, t.name
FROM sys.tables t
WHERE t.is_ms_shipped = 0 AND t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name NOT LIKE N'TEST[_]%' AND t.name <> N'sysdiagrams'
  AND EXISTS (SELECT 1 FROM sys.columns c JOIN @audit a ON a.Name = c.name COLLATE DATABASE_DEFAULT WHERE c.object_id = t.object_id)
ORDER BY t.name;

DECLARE @seq int = 0, @tid int, @t sysname, @qt nvarchar(300), @k int, @tail nvarchar(max), @want nvarchar(max),
        @dropIdx nvarchar(max), @createIdx nvarchar(max), @trig nvarchar(max), @sql nvarchar(max), @msg nvarchar(2048),
        @moved int = 0, @ord int, @c sysname, @type nvarchar(200), @nullable bit, @df sysname, @dfDef nvarchar(max), @mv sysname,
        @epName sysname, @epValue sql_variant;
DECLARE @ep TABLE (Name sysname, Value sql_variant);

WHILE 1 = 1
BEGIN
    SELECT TOP (1) @seq = Seq, @tid = ObjectId, @t = TableName FROM @tables WHERE Seq > @seq ORDER BY Seq;
    IF @@ROWCOUNT = 0 BREAK;
    SET @qt = N'dbo.' + QUOTENAME(@t);

    SELECT @k = COUNT(*) FROM sys.columns c JOIN @audit a ON a.Name = c.name COLLATE DATABASE_DEFAULT WHERE c.object_id = @tid;
    SELECT @want = STRING_AGG(CAST(a.Name AS nvarchar(max)), N',') WITHIN GROUP (ORDER BY a.Ord)
    FROM @audit a JOIN sys.columns c ON c.name COLLATE DATABASE_DEFAULT = a.Name AND c.object_id = @tid;
    SELECT @tail = STRING_AGG(CAST(x.name COLLATE DATABASE_DEFAULT AS nvarchar(max)), N',') WITHIN GROUP (ORDER BY x.column_id)
    FROM (SELECT TOP (@k) c.name, c.column_id FROM sys.columns c WHERE c.object_id = @tid ORDER BY c.column_id DESC) x;
    IF @tail = @want CONTINUE;

    -- 감사 컬럼이 키·제약에 들어 있으면 옮기지 않는다
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

    -- 감사 컬럼이 든 비클러스터 인덱스는 지웠다가 같은 정의로 다시 만든다
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

    -- 켜져 있는 트리거만 껐다가 다시 켠다(값 복사 UPDATE 가 트리거를 부르지 않게)
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

    -- 지운 컬럼이 차지하던 공간을 정리한다
    SET @sql = N'ALTER TABLE ' + @qt + N' REBUILD;'; EXEC (@sql);

    SET @moved += 1;
    PRINT N'✓ ' + @t + N' — 감사 컬럼 ' + @want + N' 를 맨 뒤로';
    SELECT @dropIdx = NULL, @createIdx = NULL, @trig = NULL;
END;

PRINT N'감사 컬럼 정리: ' + CAST(@moved AS nvarchar(10)) + N'개 테이블';
GO

-- 확인 — 아래 결과가 0 행이어야 한다
SELECT t.name AS TableName,
       (SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY c.column_id) FROM sys.columns c
        WHERE c.object_id = t.object_id AND c.name IN (N'CreatedBy', N'CreatedTS', N'ModifiedBy', N'ModifiedTS')) AS AuditColumns
FROM sys.tables t
WHERE t.is_ms_shipped = 0 AND t.schema_id = SCHEMA_ID(N'dbo') AND t.name NOT LIKE N'TEST[_]%' AND t.name <> N'sysdiagrams'
  AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name IN (N'CreatedBy', N'CreatedTS', N'ModifiedBy', N'ModifiedTS'))
  AND EXISTS (SELECT 1 FROM sys.columns c
              WHERE c.object_id = t.object_id AND c.name NOT IN (N'CreatedBy', N'CreatedTS', N'ModifiedBy', N'ModifiedTS')
                AND c.column_id > (SELECT MIN(c2.column_id) FROM sys.columns c2
                                   WHERE c2.object_id = t.object_id AND c2.name IN (N'CreatedBy', N'CreatedTS', N'ModifiedBy', N'ModifiedTS')))
UNION ALL
SELECT t.name, x.Cols
FROM sys.tables t
CROSS APPLY (SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY c.column_id) AS Cols FROM sys.columns c
             WHERE c.object_id = t.object_id AND c.name IN (N'CreatedBy', N'CreatedTS', N'ModifiedBy', N'ModifiedTS')) x
CROSS APPLY (SELECT STRING_AGG(v.n, N',') WITHIN GROUP (ORDER BY v.o) AS Want FROM (VALUES (1, N'CreatedBy'), (2, N'CreatedTS'), (3, N'ModifiedBy'), (4, N'ModifiedTS')) v(o, n)
             WHERE COL_LENGTH(N'dbo.' + QUOTENAME(t.name), v.n) IS NOT NULL) w
WHERE t.is_ms_shipped = 0 AND t.schema_id = SCHEMA_ID(N'dbo') AND t.name NOT LIKE N'TEST[_]%' AND t.name <> N'sysdiagrams'
  AND x.Cols IS NOT NULL AND x.Cols <> w.Want;
GO
