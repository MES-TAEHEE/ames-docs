/* ============================================================
   migrate_reasoncode_activeflag.sql
   MD_ReasonCode.Status(VARCHAR) → ActiveFlag(BIT) 전환
   - 가드형(재실행 안전), 수동 배포용
   - Status='ACTIVE' → 1, 그 외/NULL → 0
   - 접속: sqlcmd -S "localhost\MSSQLSERVER01" -U ames_app -P '!Dev2026' -d AMES_DEV -i dist\migrate_reasoncode_activeflag.sql
   ============================================================ */
SET NOCOUNT ON;

/* 1) ActiveFlag 컬럼 추가 + Status 값 이행 */
IF COL_LENGTH('dbo.MD_ReasonCode', 'ActiveFlag') IS NULL
BEGIN
    ALTER TABLE dbo.MD_ReasonCode
        ADD ActiveFlag BIT NOT NULL CONSTRAINT DF_MD_ReasonCode_ActiveFlag DEFAULT 1;
    PRINT 'MD_ReasonCode.ActiveFlag 컬럼 추가';

    IF COL_LENGTH('dbo.MD_ReasonCode', 'Status') IS NOT NULL
    BEGIN
        EXEC('UPDATE dbo.MD_ReasonCode
                 SET ActiveFlag = CASE WHEN UPPER(LTRIM(RTRIM(Status))) = ''ACTIVE'' THEN 1 ELSE 0 END;');
        PRINT 'Status → ActiveFlag 값 이행 완료';
    END
END
ELSE
    PRINT 'MD_ReasonCode.ActiveFlag 이미 존재 — 스킵';
GO

/* 2) Status 컬럼 제거 (기본 제약이 있으면 먼저 제거) */
IF COL_LENGTH('dbo.MD_ReasonCode', 'Status') IS NOT NULL
BEGIN
    DECLARE @df sysname;
    SELECT @df = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.MD_ReasonCode') AND c.name = 'Status';

    IF @df IS NOT NULL
        EXEC('ALTER TABLE dbo.MD_ReasonCode DROP CONSTRAINT ' + @df);

    ALTER TABLE dbo.MD_ReasonCode DROP COLUMN Status;
    PRINT 'MD_ReasonCode.Status 컬럼 제거';
END
ELSE
    PRINT 'MD_ReasonCode.Status 컬럼 없음 — 스킵';
GO
