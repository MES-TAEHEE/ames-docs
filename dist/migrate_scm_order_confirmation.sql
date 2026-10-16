SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.WH_PurchaseOrder','SupplierConfirmedAt') IS NULL
    ALTER TABLE dbo.WH_PurchaseOrder ADD SupplierConfirmedAt datetime2(7) NULL,
        SupplierConfirmedBy varchar(20) NULL, SupplierConfirmedUserID nvarchar(450) NULL;
COMMIT;
