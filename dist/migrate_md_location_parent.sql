-- ════════════════════════════════════════════════════════════════════════
-- migrate_md_location_parent.sql  (10-22)
-- 로케이션 2단계 구성 — MD_Location 에 상위 로케이션 ParentLocationID 와 단계 LocationLevel 을 더한다
--   LocationLevel 1 = 1차 로케이션(SAP 에서 받거나(SAPFlag 1) MES 에서 등록(SAPFlag 0)) — 재고를 두지 않고 하위 상세를 묶는다
--                 2 = 상세 로케이션(MES 전용) — 재고 위치(WH_Inventory.LocationNo). ParentLocationID = 1차 LocationID, NULL = 상위 미지정
--   기존 행은 모두 2(상세, 상위 미지정)라 재고·위치 동작은 그대로다. 단계 규칙(1차는 상위 없음·상세의 상위는 1차·1차는 재고 없음)은
--   MasterDataRepository 가 검사한다(외래키 없음 — 원칙).
--
-- 방법: ALTER ADD 로 붙인 뒤 SAPFlag·감사 컬럼(CreatedBy → CreatedTS → ModifiedBy → ModifiedTS)을 하나씩 맨 뒤로 옮긴다
--       (migrate_audit_columns_last.sql 과 같은 방식 — 임시 컬럼 추가 → 값 복사 → 원래 컬럼 삭제 → 이름 변경,
--        기본값 이름·NOT NULL·컬럼 설명·그 컬럼이 든 인덱스는 되살린다). 다른 테이블은 건드리지 않는다.
--       뒤에 ALTER ADD 로 붙은 LineID·PartNo·MinimumQty 도 이 이동으로 SAPFlag 앞에 오게 된다.
-- 재실행 안전 — 컬럼·제약·인덱스가 있으면 만들지 않고, 꼬리 컬럼이 이미 맨 뒤면 건너뛴다.
-- 전제: migrate_md_sap_flag_rename.sql 뒤(옛 이름 SAPTransFlag 가 남아 있으면 중단).
-- 신 Web 보다 먼저 적용한다 — 없으면 로케이션 목록(ListLocations — MD-018·WH 로케이션 지도)이 `Invalid column name 'ParentLocationID'` 로 예외.
--   구 Web·Api·Pda 는 이 컬럼을 읽지 않아 먼저 적용해도 안전하다.
--
-- 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <비밀번호> -C -f 65001 -I -b -i dist\migrate_md_location_parent.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.MD_Location', N'SAPTransFlag') IS NOT NULL
    THROW 50001, N'MD_Location.SAPTransFlag 이 남아 있다 — migrate_md_sap_flag_rename.sql 을 먼저 적용할 것', 1;
GO

IF COL_LENGTH(N'dbo.MD_Location', N'ParentLocationID') IS NULL
    ALTER TABLE dbo.MD_Location ADD ParentLocationID varchar(20) COLLATE Korean_Wansung_CI_AS NULL;
IF COL_LENGTH(N'dbo.MD_Location', N'LocationLevel') IS NULL
    ALTER TABLE dbo.MD_Location ADD LocationLevel tinyint NOT NULL CONSTRAINT DF_MD_Location_LocationLevel DEFAULT (2) WITH VALUES;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_MD_Location_LocationLevel' AND parent_object_id = OBJECT_ID(N'dbo.MD_Location'))
    ALTER TABLE dbo.MD_Location ADD CONSTRAINT CK_MD_Location_LocationLevel CHECK (LocationLevel IN (1, 2));
-- 필터 인덱스는 두지 않는다 — MD_Location 을 고치는 모든 연결·프로시저에 QUOTED_IDENTIFIER ON 을 요구해(Msg 1934) 옛 스크립트·프로시저가 멈출 수 있다
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MD_Location_Parent' AND object_id = OBJECT_ID(N'dbo.MD_Location') AND has_filter = 1)
    DROP INDEX IX_MD_Location_Parent ON dbo.MD_Location;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MD_Location_Parent' AND object_id = OBJECT_ID(N'dbo.MD_Location'))
    CREATE NONCLUSTERED INDEX IX_MD_Location_Parent ON dbo.MD_Location (ParentLocationID);
GO

DECLARE @cols TABLE (Name sysname, Descr nvarchar(300));
INSERT @cols VALUES
    (N'ParentLocationID', N'상위(1차) 로케이션 ID — 상세(LocationLevel 2)만, NULL = 상위 미지정 · varchar(20)'),
    (N'LocationLevel',    N'로케이션 단계 — 1 = 1차(SAP 수신 또는 MES 등록, 재고 없음·하위 상세를 묶음), 2 = 상세(MES 전용, 재고 위치) · tinyint');
DECLARE @c sysname, @d nvarchar(300);
WHILE EXISTS (SELECT 1 FROM @cols)
BEGIN
    SELECT TOP (1) @c = Name, @d = Descr FROM @cols ORDER BY Name;
    IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
                   WHERE class = 1 AND name = N'MS_Description' AND major_id = OBJECT_ID(N'dbo.MD_Location')
                     AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_Location'), @c, 'ColumnId'))
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @d,
             @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Location',
             @level2type = N'COLUMN', @level2name = @c;
    DELETE @cols WHERE Name = @c;
END;
GO

-- SAPFlag·감사 컬럼을 맨 뒤로(MD_Location 만)
DECLARE @audit TABLE (Ord int PRIMARY KEY, Name sysname COLLATE DATABASE_DEFAULT NOT NULL);
INSERT @audit VALUES (0, N'SAPFlag'), (1, N'CreatedBy'), (2, N'CreatedTS'), (3, N'ModifiedBy'), (4, N'ModifiedTS');
DECLARE @tables TABLE (Seq int IDENTITY PRIMARY KEY, ObjectId int NOT NULL, TableName sysname COLLATE DATABASE_DEFAULT NOT NULL);
INSERT @tables (ObjectId, TableName)
SELECT t.object_id, t.name FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name IN (N'MD_Location')
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
        SET @msg = @t + N': SAPFlag·감사 컬럼이 기본키·클러스터 인덱스·UNIQUE 제약·외래키에 들어 있어 옮기지 않는다 — 수동 처리 필요';
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

    SET @ord = -1;
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
    PRINT N'✓ ' + @t + N' — 꼬리 컬럼 ' + @want + N' 를 맨 뒤로';
    SELECT @dropIdx = NULL, @createIdx = NULL, @trig = NULL;
END;
GO

-- 확인 — LocationLevel 은 모두 1 또는 2, SAPFlag 바로 뒤가 감사 컬럼(CreatedBy → CreatedTS → ModifiedBy → ModifiedTS)이어야 한다
SELECT LocationLevel, COUNT(*) AS Cnt, SUM(CASE WHEN ParentLocationID IS NOT NULL THEN 1 ELSE 0 END) AS WithParent
FROM dbo.MD_Location GROUP BY LocationLevel ORDER BY LocationLevel;
SELECT c.column_id, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS TypeName, c.is_nullable, dc.name AS DefaultName
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE c.object_id = OBJECT_ID(N'dbo.MD_Location')
  AND c.column_id >= (SELECT c2.column_id FROM sys.columns c2 WHERE c2.object_id = c.object_id AND c2.name = N'ParentLocationID')
ORDER BY c.column_id;
GO
