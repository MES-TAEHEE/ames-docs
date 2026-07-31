/* ============================================================
   migrate_defect_activeflag.sql
   MD_DefectCode / MD_DefectCause 의 Status(VARCHAR) → ActiveFlag(BIT) 전환
   - 가드형(재실행 안전), 수동 배포용
   - Status 대문자 'ACTIVE' → 1, 그 외/NULL → 0
   - 접속: sqlcmd -S "localhost\MSSQLSERVER01" -U ames_app -P '!Dev2026' -d AMES_DEV -i dist\migrate_defect_activeflag.sql
   ============================================================ */
SET NOCOUNT ON;

DECLARE @tbl sysname, @sql nvarchar(max);
DECLARE tbls CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES ('MD_DefectCode'), ('MD_DefectCause')) AS t(name);

OPEN tbls;
FETCH NEXT FROM tbls INTO @tbl;
WHILE @@FETCH_STATUS = 0
BEGIN
    /* 1) ActiveFlag 추가 + Status 값 이행 */
    IF COL_LENGTH('dbo.' + @tbl, 'ActiveFlag') IS NULL
    BEGIN
        SET @sql = 'ALTER TABLE dbo.' + QUOTENAME(@tbl) +
                   ' ADD ActiveFlag BIT NOT NULL CONSTRAINT DF_' + @tbl + '_ActiveFlag DEFAULT 1;';
        EXEC sp_executesql @sql;
        PRINT @tbl + '.ActiveFlag 컬럼 추가';

        IF COL_LENGTH('dbo.' + @tbl, 'Status') IS NOT NULL
        BEGIN
            SET @sql = 'UPDATE dbo.' + QUOTENAME(@tbl) +
                       ' SET ActiveFlag = CASE WHEN UPPER(LTRIM(RTRIM(Status))) = ''ACTIVE'' THEN 1 ELSE 0 END;';
            EXEC sp_executesql @sql;
            PRINT @tbl + ': Status → ActiveFlag 값 이행';
        END
    END
    ELSE
        PRINT @tbl + '.ActiveFlag 이미 존재 — 스킵';

    /* 2) Status 컬럼 제거 (기본 제약이 있으면 먼저 제거) */
    IF COL_LENGTH('dbo.' + @tbl, 'Status') IS NOT NULL
    BEGIN
        DECLARE @df sysname;
        SELECT @df = dc.name
        FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID('dbo.' + @tbl) AND c.name = 'Status';

        IF @df IS NOT NULL
            EXEC('ALTER TABLE dbo.' + @tbl + ' DROP CONSTRAINT ' + @df);

        SET @sql = 'ALTER TABLE dbo.' + QUOTENAME(@tbl) + ' DROP COLUMN Status;';
        EXEC sp_executesql @sql;
        PRINT @tbl + '.Status 컬럼 제거';
    END

    FETCH NEXT FROM tbls INTO @tbl;
END
CLOSE tbls;
DEALLOCATE tbls;
GO
