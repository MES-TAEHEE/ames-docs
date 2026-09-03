SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FG_CustomerReturn', N'U') IS NULL
    THROW 50001, 'dbo.FG_CustomerReturn does not exist.', 1;

IF COL_LENGTH(N'dbo.FG_CustomerReturn', N'Note') IS NULL
    ALTER TABLE dbo.FG_CustomerReturn ADD [Note] NVARCHAR(500) NULL;

SELECT COL_LENGTH(N'dbo.FG_CustomerReturn', N'Note') AS NoteColumnBytes;
