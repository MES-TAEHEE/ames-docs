/* ============================================================
   migrate_inspstd_activeflag.sql
   MD_InspectionStandard 의 Status(VARCHAR) → ActiveFlag(BIT) 전환
   - 가드형(재실행 안전)
   - Status 대문자 'ACTIVE' → 1, 그 외/NULL → 0
   - 접속: sqlcmd -S "localhost\MSSQLSERVER01" -U ames_app -P '!Dev2026' -d AMES_DEV -i dist\migrate_inspstd_activeflag.sql
   ============================================================ */
SET NOCOUNT ON;

IF COL_LENGTH('dbo.MD_InspectionStandard', 'ActiveFlag') IS NULL
BEGIN
    ALTER TABLE dbo.MD_InspectionStandard
        ADD ActiveFlag BIT NOT NULL CONSTRAINT DF_MD_InspectionStandard_ActiveFlag DEFAULT 1;
    PRINT 'MD_InspectionStandard.ActiveFlag 컬럼 추가';

    IF COL_LENGTH('dbo.MD_InspectionStandard', 'Status') IS NOT NULL
    BEGIN
        EXEC('UPDATE dbo.MD_InspectionStandard
                 SET ActiveFlag = CASE WHEN UPPER(LTRIM(RTRIM(Status))) = ''ACTIVE'' THEN 1 ELSE 0 END;');
        PRINT 'Status → ActiveFlag 값 이행';
    END
END
ELSE
    PRINT 'MD_InspectionStandard.ActiveFlag 이미 존재 — 스킵';
GO

IF COL_LENGTH('dbo.MD_InspectionStandard', 'Status') IS NOT NULL
BEGIN
    DECLARE @df sysname;
    SELECT @df = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.MD_InspectionStandard') AND c.name = 'Status';
    IF @df IS NOT NULL EXEC('ALTER TABLE dbo.MD_InspectionStandard DROP CONSTRAINT ' + @df);

    ALTER TABLE dbo.MD_InspectionStandard DROP COLUMN Status;
    PRINT 'MD_InspectionStandard.Status 컬럼 제거';
END
ELSE
    PRINT 'MD_InspectionStandard.Status 컬럼 없음 — 스킵';
GO
