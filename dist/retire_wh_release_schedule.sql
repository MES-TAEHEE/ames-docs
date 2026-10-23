-- Target the intended database explicitly. Default is a read-only preview.
-- Set @Apply=1 only after approval. Never run the full rebuild/core migration for this cleanup.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Apply bit=0;
DECLARE @Rows bigint;

IF OBJECT_ID(N'dbo.WH_ReleaseSchedule',N'U') IS NULL
BEGIN
    SELECT N'WH_ReleaseSchedule is already absent; nothing changed.' AS Result;
    RETURN;
END;
IF OBJECT_ID(N'dbo.WH_PickSlip',N'U') IS NULL
    THROW 52002, 'WH_PickSlip is missing. Use the consolidation migration; do not discard the legacy table.', 1;

IF @Apply=0
BEGIN
    EXEC sys.sp_executesql N'SELECT @Rows=COUNT_BIG(*) FROM dbo.WH_ReleaseSchedule;', N'@Rows bigint OUTPUT', @Rows OUTPUT;
    SELECT DB_NAME() AS DatabaseName, N'dbo.WH_ReleaseSchedule' AS TableName, @Rows AS [Rows],
           N'Preview only. No table or data was changed.' AS Result;
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;
    -- Recheck under a lock so an insert cannot race the empty-table check.
    EXEC sys.sp_executesql N'SELECT @Rows=COUNT_BIG(*) FROM dbo.WH_ReleaseSchedule WITH(TABLOCKX,HOLDLOCK);',
        N'@Rows bigint OUTPUT', @Rows OUTPUT;
    IF @Rows<>0
        THROW 52002, 'WH_ReleaseSchedule contains rows. Nothing was deleted; review and migrate them first.', 1;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE parent_object_id=OBJECT_ID(N'dbo.WH_ReleaseSchedule') OR referenced_object_id=OBJECT_ID(N'dbo.WH_ReleaseSchedule'))
       OR EXISTS (SELECT 1 FROM sys.sql_expression_dependencies
                  WHERE referenced_id=OBJECT_ID(N'dbo.WH_ReleaseSchedule')
                    AND referencing_id<>OBJECT_ID(N'dbo.WH_ReleaseSchedule'))
        THROW 52002, 'WH_ReleaseSchedule still has database dependencies. Nothing was deleted.', 1;
    EXEC(N'DROP TABLE dbo.WH_ReleaseSchedule;');
    COMMIT TRANSACTION;
    SELECT N'Empty WH_ReleaseSchedule removed. WH_PickSlip and inventory were not changed.' AS Result;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
