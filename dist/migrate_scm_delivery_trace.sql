-- Delivery-line traceability; existing rows default to their delivery date.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.SCM_DeliveryLine','VendorLotNo') IS NULL
    ALTER TABLE dbo.SCM_DeliveryLine ADD VendorLotNo nvarchar(30) NULL;
IF COL_LENGTH('dbo.SCM_DeliveryLine','ProductionDate') IS NULL
    ALTER TABLE dbo.SCM_DeliveryLine ADD ProductionDate date NULL;
EXEC(N'UPDATE l SET VendorLotNo=COALESCE(l.VendorLotNo,CONVERT(char(8),d.DeliveryDate,112)),
    ProductionDate=COALESCE(l.ProductionDate,d.DeliveryDate)
    FROM dbo.SCM_DeliveryLine l JOIN dbo.SCM_Delivery d ON d.DeliveryID=l.DeliveryID
    WHERE l.VendorLotNo IS NULL OR l.ProductionDate IS NULL;');
COMMIT;
