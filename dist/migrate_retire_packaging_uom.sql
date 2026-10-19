-- Deploy the code without BoxContent columns before applying this migration.
-- Existing item/order/inventory quantities and unit codes are not converted.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('dbo.MD_Item','BoxContentUnit') IS NOT NULL
        ALTER TABLE dbo.MD_Item DROP COLUMN BoxContentUnit;
    IF COL_LENGTH('dbo.MD_Item','BoxContentQty') IS NOT NULL
        ALTER TABLE dbo.MD_Item DROP COLUMN BoxContentQty;
    IF COL_LENGTH('dbo.WH_PurchaseOrder','BoxContentUnit') IS NOT NULL
        ALTER TABLE dbo.WH_PurchaseOrder DROP COLUMN BoxContentUnit;
    IF COL_LENGTH('dbo.WH_PurchaseOrder','BoxContentQty') IS NOT NULL
        ALTER TABLE dbo.WH_PurchaseOrder DROP COLUMN BoxContentQty;

    -- Keep historical unit labels while preventing further use.
    UPDATE dbo.MD_Uom
    SET ActiveFlag=0,ModifiedBy='uom-cleanup',ModifiedTS=SYSDATETIME()
    WHERE UPPER(LTRIM(RTRIM(UOMCode))) IN ('BOX','PLT','PALLET')
      AND ISNULL(ActiveFlag,1)=1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
