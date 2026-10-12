SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH('dbo.PR_PopSession', 'BearerHash') IS NULL
        ALTER TABLE dbo.PR_PopSession ADD BearerHash char(64) NULL;
    IF COL_LENGTH('dbo.PR_PopSession', 'SessionJson') IS NULL
        ALTER TABLE dbo.PR_PopSession ADD SessionJson nvarchar(max) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PR_PopSession') AND name='UX_PR_PopSession_BearerHash')
        EXEC(N'CREATE UNIQUE INDEX UX_PR_PopSession_BearerHash ON dbo.PR_PopSession(BearerHash) WHERE BearerHash IS NOT NULL');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
