-- Normalize existing FG-area locations; inventory and location identifiers are unchanged.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRAN;
    UPDATE dbo.MD_Location
    SET LocationType = 'FG', ModifiedBy = 'FG-TYPE-MIGRATION', ModifiedTS = SYSDATETIME()
    WHERE UPPER(AreaCode) = 'FG_AREA'
      AND (LocationType IS NULL OR LocationType <> 'FG');
    SELECT @@ROWCOUNT AS UpdatedLocations;
    IF EXISTS (SELECT 1 FROM dbo.MD_Location
               WHERE UPPER(AreaCode) = 'FG_AREA' AND ISNULL(LocationType, '') <> 'FG')
        THROW 52072, 'FG area location type normalization failed.', 1;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
