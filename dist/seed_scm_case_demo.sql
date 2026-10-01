-- Additive CASE testing data for existing portal vendors. Never resets existing data.
-- PO-PACK-DEMO-01: adminext@ames.local / V1007
-- PO-PACK-DEMO-02: admintest@ames.local / SUP-CHEM
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.MD_Vendor WHERE VendorID='V1007' AND ActiveFlag=1)
    OR NOT EXISTS(SELECT 1 FROM dbo.MD_Vendor WHERE VendorID='SUP-CHEM' AND ActiveFlag=1)
    THROW 51450,'Expected CASE demo vendors are unavailable. No changes applied.',1;
DECLARE @Parts TABLE(ItemNo varchar(20),ItemName nvarchar(80),Qty decimal(18,3),PackingQty decimal(18,3),PoLineNo int);
INSERT @Parts VALUES
('CASE-DEMO-PART-01',N'CASE DEMO - BRACKET',120,20,1),
('CASE-DEMO-PART-02',N'CASE DEMO - CLIP',75,15,2);
IF EXISTS(SELECT 1 FROM dbo.MD_Item i JOIN @Parts p ON p.ItemNo=i.ItemNo WHERE i.CreatedBy<>'case-demo')
    THROW 51451,'CASE demo item number already belongs to other data.',1;
INSERT dbo.MD_Item(ItemNo,ItemName,ItemNameEN,ItemType,DefaultUOM,ActiveFlag,CreatedBy)
SELECT p.ItemNo,p.ItemName,p.ItemName,'MATERIAL','EA',1,'case-demo' FROM @Parts p
WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_Item i WITH(UPDLOCK,HOLDLOCK) WHERE i.ItemNo=p.ItemNo);
-- Correct only the two demo rows previously seeded with an unregistered item type.
UPDATE i SET ItemType='MATERIAL'
FROM dbo.MD_Item i JOIN @Parts p ON p.ItemNo=i.ItemNo
WHERE i.CreatedBy='case-demo' AND i.ItemType='RM';
DECLARE @Orders TABLE(PoNumber varchar(20),VendorID varchar(20),PortalUser nvarchar(450));
INSERT @Orders VALUES
('PO-PACK-DEMO-01','V1007','adminext@ames.local'),
('PO-PACK-DEMO-02','SUP-CHEM','admintest@ames.local');
IF EXISTS(SELECT 1 FROM dbo.WH_PurchaseOrder p JOIN @Orders o ON o.PoNumber=p.PoNumber
          WHERE p.CreatedBy<>'case-demo' OR p.VendorID<>o.VendorID)
    THROW 51452,'CASE demo PO number already belongs to other data.',1;
INSERT dbo.SCM_ItemVendor(ItemNo,VendorID,PackingQty,ActiveFlag,CreatedBy)
SELECT p.ItemNo,o.VendorID,p.PackingQty,1,'case-demo' FROM @Parts p CROSS JOIN @Orders o
WHERE NOT EXISTS(SELECT 1 FROM dbo.SCM_ItemVendor m WITH(UPDLOCK,HOLDLOCK)
                 WHERE m.ItemNo=p.ItemNo AND m.VendorID=o.VendorID);
INSERT dbo.WH_PurchaseOrder(PoNumber,PoLineNo,VendorID,ItemNo,OrderQty,ReceivedQty,UnitCode,
    UnitPrice,Currency,OrderDate,DueDate,Status,CreatedBy,DeliveryDestination,
    SupplierConfirmedAt,SupplierConfirmedBy,SupplierConfirmedUserID)
SELECT o.PoNumber,p.PoLineNo,o.VendorID,p.ItemNo,p.Qty,0,'EA',1,'USD',
    CONVERT(date,SYSDATETIME()),DATEADD(day,7,CONVERT(date,SYSDATETIME())),'Open','case-demo',N'EOS',
    SYSDATETIME(),'case-demo',o.PortalUser FROM @Orders o CROSS JOIN @Parts p
WHERE NOT EXISTS(SELECT 1 FROM dbo.WH_PurchaseOrder x WITH(UPDLOCK,HOLDLOCK)
                 WHERE x.PoNumber=o.PoNumber AND x.PoLineNo=p.PoLineNo);
COMMIT TRANSACTION;
