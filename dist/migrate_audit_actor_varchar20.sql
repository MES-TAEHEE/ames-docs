-- Normalize audit actor columns in the current database. No USE statement:
-- select the intended database explicitly. Application writers must send <=20-byte
-- employee/actor codes after this migration; identity UserID columns are unchanged.
-- Existing long values are preserved in dbo.SYS_AuditActorMap, never truncated.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_WARNINGS ON;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE #Targets (ID int IDENTITY PRIMARY KEY, SchemaName sysname,
        TableName sysname, ColumnName sysname, IsNullable bit);
    INSERT #Targets (SchemaName,TableName,ColumnName,IsNullable)
    SELECT s.name,t.name,c.name,c.is_nullable
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id=t.schema_id
    JOIN sys.columns c ON c.object_id=t.object_id
    WHERE t.is_ms_shipped=0
      AND c.name IN ('CreatedBy','ModifiedBy','ApprovedBy','RequestedBy');

    IF EXISTS (SELECT 1 FROM #Targets x JOIN sys.columns c
        ON c.object_id=OBJECT_ID(QUOTENAME(x.SchemaName)+'.'+QUOTENAME(x.TableName))
        AND c.name=x.ColumnName
        WHERE TYPE_NAME(c.user_type_id) NOT IN ('varchar','nvarchar') OR c.is_computed=1)
        THROW 51000, 'Unsupported audit column type; nothing changed.', 1;

    CREATE TABLE #Values (OriginalHash binary(32) PRIMARY KEY, OriginalValue nvarchar(900) NOT NULL);
    DECLARE @i int=1,@n int=(SELECT COUNT(*) FROM #Targets),@table nvarchar(517),
            @col nvarchar(258),@nullable bit,@sql nvarchar(max);
    WHILE @i<=@n
    BEGIN
        SELECT @table=QUOTENAME(SchemaName)+'.'+QUOTENAME(TableName),
               @col=QUOTENAME(ColumnName),@nullable=IsNullable FROM #Targets WHERE ID=@i;
        SET @sql=N'IF EXISTS(SELECT 1 FROM '+@table+N' WITH(TABLOCKX,HOLDLOCK)
            WHERE DATALENGTH(CONVERT(nvarchar(max),'+@col+N'))>1800)
            THROW 51000,''Actor value exceeds mapping capacity; nothing changed.'',1;
          INSERT #Values(OriginalHash,OriginalValue)
          SELECT DISTINCT HASHBYTES(''SHA2_256'',CONVERT(nvarchar(900),'+@col+N')),
                 CONVERT(nvarchar(900),'+@col+N')
          FROM '+@table+N' WITH(TABLOCKX,HOLDLOCK)
          WHERE '+@col+N' IS NOT NULL AND NOT EXISTS(SELECT 1 FROM #Values v
            WHERE v.OriginalHash=HASHBYTES(''SHA2_256'',CONVERT(nvarchar(900),'+@col+N')));';
        EXEC sys.sp_executesql @sql;
        SET @i+=1;
    END;

    IF OBJECT_ID(N'dbo.SYS_AuditActorMap',N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.SYS_AuditActorMap (
            OriginalHash binary(32) NOT NULL CONSTRAINT PK_SYS_AuditActorMap PRIMARY KEY,
            OriginalValue nvarchar(900) NOT NULL,
            ActorCode varchar(20) NOT NULL,
            MappingKind varchar(20) NOT NULL,
            RecordedAt datetime2(7) NOT NULL CONSTRAINT DF_SYS_AuditActorMap_RecordedAt DEFAULT SYSUTCDATETIME()
        );
    END;

    -- Use an existing employee number when the actor is a registered user ID.
    -- Otherwise preserve the original behind a deterministic 20-byte actor code.
    CREATE TABLE #NewMappings(OriginalHash binary(32) PRIMARY KEY);
    INSERT dbo.SYS_AuditActorMap(OriginalHash,OriginalValue,ActorCode,MappingKind)
    OUTPUT INSERTED.OriginalHash INTO #NewMappings
    SELECT v.OriginalHash,v.OriginalValue,
           CASE WHEN NULLIF(p.EmployeeNo,'') IS NOT NULL THEN p.EmployeeNo
                ELSE 'ACT-'+LEFT(CONVERT(varchar(64),v.OriginalHash,2),16) END,
           CASE WHEN NULLIF(p.EmployeeNo,'') IS NOT NULL THEN 'EMPLOYEE' ELSE 'ACTOR_ALIAS' END
    FROM #Values v
    LEFT JOIN dbo.SYS_UserProfile p ON p.UserID=v.OriginalValue
    WHERE (DATALENGTH(CONVERT(varchar(max),v.OriginalValue))>20
        OR CONVERT(varbinary(max),v.OriginalValue)<>
           CONVERT(varbinary(max),CONVERT(nvarchar(max),CONVERT(varchar(max),v.OriginalValue))))
      AND NOT EXISTS(SELECT 1 FROM dbo.SYS_AuditActorMap m WHERE m.OriginalHash=v.OriginalHash);

    IF EXISTS(SELECT 1 FROM dbo.SYS_AuditActorMap m JOIN #Values v
        ON v.OriginalValue=CONVERT(nvarchar(20),m.ActorCode)
        WHERE m.MappingKind='ACTOR_ALIAS' AND v.OriginalHash<>m.OriginalHash
          AND EXISTS(SELECT 1 FROM #NewMappings n WHERE n.OriginalHash=m.OriginalHash))
        THROW 51000, 'Generated actor code collides with an existing value.', 1;
    IF EXISTS(SELECT ActorCode FROM dbo.SYS_AuditActorMap
        GROUP BY ActorCode HAVING COUNT(*)>1 AND MAX(CASE WHEN MappingKind='ACTOR_ALIAS' THEN 1 ELSE 0 END)=1)
        THROW 51000, 'Generated actor code collision.', 1;

    CREATE TABLE #Results(TableName nvarchar(517),ColumnName nvarchar(258),ConvertedRows int);
    SET @i=1;
    WHILE @i<=@n
    BEGIN
        SELECT @table=QUOTENAME(SchemaName)+'.'+QUOTENAME(TableName),
               @col=QUOTENAME(ColumnName),@nullable=IsNullable FROM #Targets WHERE ID=@i;
        SET @sql=N'UPDATE t SET '+@col+N'=m.ActorCode FROM '+@table+N' t
          JOIN dbo.SYS_AuditActorMap m ON m.OriginalHash=HASHBYTES(''SHA2_256'',CONVERT(nvarchar(900),t.'+@col+N'));
          INSERT #Results VALUES(@TableName,@ColumnName,@@ROWCOUNT);
          IF EXISTS(SELECT 1 FROM '+@table+N' WHERE DATALENGTH(CONVERT(varchar(max),'+@col+N'))>20
              OR CONVERT(varbinary(max),CONVERT(nvarchar(max),'+@col+N'))<>
                 CONVERT(varbinary(max),CONVERT(nvarchar(max),CONVERT(varchar(max),'+@col+N'))))
             THROW 51000,''Actor conversion would lose data.'',1;
          ALTER TABLE '+@table+N' ALTER COLUMN '+@col+N' varchar(20) '+CASE WHEN @nullable=1 THEN N'NULL' ELSE N'NOT NULL' END+N';';
        EXEC sys.sp_executesql @sql,N'@TableName nvarchar(517),@ColumnName nvarchar(258)',@table,@col;
        SET @i+=1;
    END;
    COMMIT;
    SELECT COUNT(*) TargetColumns,SUM(ConvertedRows) ConvertedCells FROM #Results;
    SELECT * FROM #Results WHERE ConvertedRows>0 ORDER BY TableName,ColumnName;
    SELECT OriginalValue,ActorCode,MappingKind FROM dbo.SYS_AuditActorMap ORDER BY MappingKind,OriginalValue;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
