SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.WH_PurchaseOrder','DeliveryDestination') IS NULL
    ALTER TABLE dbo.WH_PurchaseOrder ADD DeliveryDestination nvarchar(200) NULL;
IF COL_LENGTH('dbo.WH_PurchaseOrder','ScmRowVersion') IS NULL
    ALTER TABLE dbo.WH_PurchaseOrder ADD ScmRowVersion rowversion;
COMMIT;
