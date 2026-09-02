/*
    cleanup_legacy_sis_test.sql

    Purpose
    -------
    Remove legacy AMES demo objects that were temporarily copied under the
    SIS_TEST schema during early SIS prototyping.

    The cleanup is intentionally schema-scoped. Dropping the SIS_TEST tables also
    removes the old seed/demo rows inside those tables, so local Docker databases
    and shared development databases can be brought to the same state.

    Safe scope
    ----------
    This script does not touch current AMES objects such as:
      - dbo.WH_*
      - dbo.MD_*
      - dbo.FG_*
      - dbo.tbl_Lot

    Usage examples
    --------------
      sqlcmd -S .\SQLEXPRESS -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\cleanup_legacy_sis_test.sql
      sqlcmd -S 192.168.1.100,1433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\cleanup_legacy_sis_test.sql
*/

SET NOCOUNT ON;

PRINT N'Legacy SIS_TEST cleanup started.';
PRINT N'Database: ' + DB_NAME();

IF SCHEMA_ID(N'SIS_TEST') IS NULL
BEGIN
    PRINT N'SIS_TEST schema does not exist. Nothing to clean.';
    RETURN;
END;

PRINT N'Legacy SIS_TEST table row counts before cleanup:';
SELECT
    QUOTENAME(SCHEMA_NAME(database_table.schema_id)) + N'.' + QUOTENAME(database_table.name) AS TableName,
    SUM(table_partition.rows) AS SeedRows
FROM sys.tables AS database_table
JOIN sys.partitions AS table_partition
    ON table_partition.object_id = database_table.object_id
   AND table_partition.index_id IN (0, 1)
WHERE database_table.schema_id = SCHEMA_ID(N'SIS_TEST')
GROUP BY database_table.schema_id, database_table.name
ORDER BY database_table.name;

DECLARE @sql nvarchar(max);

SET @sql = N'';
SELECT @sql = @sql +
    N'ALTER TABLE ' + QUOTENAME(parent_schema.name) + N'.' + QUOTENAME(parent_object.name) +
    N' DROP CONSTRAINT ' + QUOTENAME(foreign_key.name) + N';' + CHAR(13) + CHAR(10)
FROM sys.foreign_keys AS foreign_key
JOIN sys.objects AS parent_object
    ON parent_object.object_id = foreign_key.parent_object_id
JOIN sys.schemas AS parent_schema
    ON parent_schema.schema_id = parent_object.schema_id
JOIN sys.objects AS referenced_object
    ON referenced_object.object_id = foreign_key.referenced_object_id
JOIN sys.schemas AS referenced_schema
    ON referenced_schema.schema_id = referenced_object.schema_id
WHERE parent_schema.name = N'SIS_TEST'
   OR referenced_schema.name = N'SIS_TEST';

IF LEN(@sql) > 0
BEGIN
    PRINT N'Dropping foreign keys that reference SIS_TEST...';
    EXEC sys.sp_executesql @sql;
END;

SET @sql = N'';
SELECT @sql = @sql +
    N'DROP SYNONYM ' + QUOTENAME(synonym_schema.name) + N'.' + QUOTENAME(synonym.name) + N';' +
    CHAR(13) + CHAR(10)
FROM sys.synonyms AS synonym
JOIN sys.schemas AS synonym_schema
    ON synonym_schema.schema_id = synonym.schema_id
WHERE synonym_schema.name = N'SIS_TEST'
ORDER BY synonym.name;

IF LEN(@sql) > 0
BEGIN
    PRINT N'Dropping SIS_TEST synonyms...';
    EXEC sys.sp_executesql @sql;
END;

SET @sql = N'';
SELECT @sql = @sql +
    CASE database_object.type
        WHEN N'P' THEN N'DROP PROCEDURE '
        WHEN N'V' THEN N'DROP VIEW '
        WHEN N'FN' THEN N'DROP FUNCTION '
        WHEN N'IF' THEN N'DROP FUNCTION '
        WHEN N'TF' THEN N'DROP FUNCTION '
    END +
    QUOTENAME(database_schema.name) + N'.' + QUOTENAME(database_object.name) + N';' +
    CHAR(13) + CHAR(10)
FROM sys.objects AS database_object
JOIN sys.schemas AS database_schema
    ON database_schema.schema_id = database_object.schema_id
WHERE database_schema.name = N'SIS_TEST'
  AND database_object.type IN (N'P', N'V', N'FN', N'IF', N'TF')
ORDER BY
    CASE database_object.type
        WHEN N'V' THEN 1
        WHEN N'P' THEN 2
        ELSE 3
    END,
    database_object.name;

IF LEN(@sql) > 0
BEGIN
    PRINT N'Dropping SIS_TEST procedures, views, and functions...';
    EXEC sys.sp_executesql @sql;
END;

SET @sql = N'';
SELECT @sql = @sql +
    N'DROP SEQUENCE ' + QUOTENAME(sequence_schema.name) + N'.' + QUOTENAME(sequence.name) + N';' +
    CHAR(13) + CHAR(10)
FROM sys.sequences AS sequence
JOIN sys.schemas AS sequence_schema
    ON sequence_schema.schema_id = sequence.schema_id
WHERE sequence_schema.name = N'SIS_TEST'
ORDER BY sequence.name;

IF LEN(@sql) > 0
BEGIN
    PRINT N'Dropping SIS_TEST sequences...';
    EXEC sys.sp_executesql @sql;
END;

SET @sql = N'';
SELECT @sql = @sql +
    N'DROP TABLE ' + QUOTENAME(database_schema.name) + N'.' + QUOTENAME(database_table.name) + N';' +
    CHAR(13) + CHAR(10)
FROM sys.tables AS database_table
JOIN sys.schemas AS database_schema
    ON database_schema.schema_id = database_table.schema_id
WHERE database_schema.name = N'SIS_TEST'
ORDER BY database_table.name;

IF LEN(@sql) > 0
BEGIN
    PRINT N'Dropping SIS_TEST tables and seed data...';
    EXEC sys.sp_executesql @sql;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.objects
    WHERE schema_id = SCHEMA_ID(N'SIS_TEST')
)
BEGIN
    PRINT N'Dropping empty SIS_TEST schema...';
    DROP SCHEMA SIS_TEST;
END;
ELSE
BEGIN
    PRINT N'SIS_TEST schema still has objects. Review remaining dependencies manually.';
END;

IF SCHEMA_ID(N'SIS_TEST') IS NULL
BEGIN
    PRINT N'Legacy SIS_TEST cleanup complete. SIS_TEST schema was removed.';
END;
ELSE
BEGIN
    PRINT N'Legacy SIS_TEST cleanup finished with remaining schema. Check remaining non-object dependencies.';
END;
