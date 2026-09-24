SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.SCM_Delivery','U') IS NULL
BEGIN
 CREATE TABLE dbo.SCM_Delivery(
  DeliveryID int IDENTITY PRIMARY KEY, DeliveryNumber varchar(30) NOT NULL UNIQUE,
  RequestID uniqueidentifier NOT NULL UNIQUE, PoNumber varchar(20) NOT NULL,
  VendorID varchar(20) NOT NULL REFERENCES dbo.MD_Vendor(VendorID), DeliveryDate date NOT NULL,
  Status varchar(20) NOT NULL DEFAULT 'Registered' CHECK(Status IN ('Registered','Received','Cancelled')),
  CreatedBy varchar(20) NOT NULL, CreatedUserID nvarchar(450) NOT NULL,
  CreatedTS datetime2(7) NOT NULL DEFAULT SYSDATETIME());
 CREATE TABLE dbo.SCM_DeliveryLine(
  DeliveryLineID int IDENTITY PRIMARY KEY,
  DeliveryID int NOT NULL REFERENCES dbo.SCM_Delivery(DeliveryID),
  PoID int NOT NULL REFERENCES dbo.WH_PurchaseOrder(PoID),
  Quantity decimal(12,3) NOT NULL CHECK(Quantity>0),
  ReceivedQty decimal(12,3) NOT NULL DEFAULT 0,
  CONSTRAINT CK_SCM_DeliveryLine_Received CHECK(ReceivedQty>=0 AND ReceivedQty<=Quantity),
  CONSTRAINT UQ_SCM_DeliveryLine UNIQUE(DeliveryID,PoID));
 CREATE INDEX IX_SCM_DeliveryLine_PoID ON dbo.SCM_DeliveryLine(PoID);
END;

-- Portal access uses PortalAccess and SCM_PortalVendorUser, not screen RBAC.
COMMIT;
