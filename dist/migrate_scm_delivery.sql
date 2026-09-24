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

-- Restore missing baseline portal permissions for the order-to-delivery workflow; preserve existing overrides.
INSERT dbo.SYS_RolePermission(RoleID,RoleName,ModuleCode,ProcessCode,ScreenCode,PermissionLevel,IsSystemRole,EffectiveTS,CreatedBy,CreatedTS)
SELECT r.Id,r.Name,s.ModuleCode,s.ProcessCode,s.ScreenCode,
 CASE WHEN r.Name='Admin' THEN 'REA' WHEN s.ScreenCode IN ('PORTAL-001','PORTAL-003','PORTAL-004') THEN 'RE' ELSE 'R' END,
 CASE WHEN r.Name='Admin' THEN 1 ELSE 0 END,SYSDATETIME(),'scm-delivery',SYSDATETIME()
FROM dbo.SYS_Screen s CROSS JOIN dbo.AspNetRoles r
WHERE s.ScreenCode IN ('PORTAL-001','PORTAL-002','PORTAL-003','PORTAL-004','PORTAL-005') AND r.Name IN ('Admin','ExternalCustomer')
 AND NOT EXISTS(SELECT 1 FROM dbo.SYS_RolePermission p WHERE p.RoleName=r.Name AND p.ScreenCode=s.ScreenCode);

COMMIT;
