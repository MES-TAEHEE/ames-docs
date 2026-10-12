SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH('dbo.WH_PurchaseOrder', 'BoxContentUnit') IS NULL
        ALTER TABLE dbo.WH_PurchaseOrder ADD BoxContentUnit varchar(10) NULL;
    IF COL_LENGTH('dbo.WH_PurchaseOrder', 'BoxContentQty') IS NULL
        ALTER TABLE dbo.WH_PurchaseOrder ADD BoxContentQty decimal(18,3) NULL;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
