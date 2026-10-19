-- Additive migration; no stock deletion or table rebuild. Deploy Web and API together.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.FG_Shipment','LotNo') IS NULL
    EXEC sys.sp_rename 'dbo.FG_Shipment.OutboundBarcode','LotNo','COLUMN';
IF COL_LENGTH('dbo.MD_Location','LineID') IS NULL
    ALTER TABLE dbo.MD_Location ADD LineID varchar(20) NULL, PartNo varchar(20) NULL,
        MinimumQty decimal(18,3) NULL;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.MD_Location') AND name='IX_MD_Location_Line' AND has_filter=1)
    DROP INDEX IX_MD_Location_Line ON dbo.MD_Location;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.MD_Location') AND name='IX_MD_Location_Line')
    EXEC('CREATE INDEX IX_MD_Location_Line ON dbo.MD_Location(LineID,PartNo) INCLUDE(MinimumQty)');
IF OBJECT_ID('dbo.FG_ShippingOrder','U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_ShippingOrder(
        OrderID int IDENTITY PRIMARY KEY, OrderNo varchar(30) NOT NULL UNIQUE,
        CustomerID varchar(20) NOT NULL, ShipDate date NOT NULL,
        Status varchar(20) NOT NULL DEFAULT 'Open', DeliveryID int NULL,
        CreatedBy varchar(20) NOT NULL, CreatedTS datetime2 NOT NULL DEFAULT SYSDATETIME(),
        ModifiedBy varchar(20) NULL, ModifiedTS datetime2 NULL);
    CREATE TABLE dbo.FG_ShippingOrderLine(
        OrderLineID int IDENTITY PRIMARY KEY, OrderID int NOT NULL, SoID int NOT NULL,
        PartNo varchar(20) NOT NULL, Qty decimal(18,3) NOT NULL CHECK(Qty>0));
    CREATE INDEX IX_FG_ShippingOrderLine_So ON dbo.FG_ShippingOrderLine(SoID,OrderID);
    CREATE TABLE dbo.FG_Delivery(
        DeliveryID int IDENTITY PRIMARY KEY, DeliveryNo varchar(30) NOT NULL UNIQUE,
        CustomerID varchar(20) NOT NULL, ShipDate date NOT NULL, TrailerNo nvarchar(50) NULL,
        Status varchar(20) NOT NULL DEFAULT 'Open',
        CreatedBy varchar(20) NOT NULL, CreatedTS datetime2 NOT NULL DEFAULT SYSDATETIME(),
        ShippedBy varchar(20) NULL, ShippedAt datetime2 NULL);
    CREATE TABLE dbo.FG_DeliveryLoad(
        LoadID int IDENTITY PRIMARY KEY, DeliveryID int NOT NULL, RootLotNo nvarchar(50) NOT NULL,
        LotNo nvarchar(50) NOT NULL, PartNo varchar(20) NOT NULL, Qty decimal(18,3) NOT NULL CHECK(Qty>0),
        LoadedBy varchar(20) NOT NULL, LoadedAt datetime2 NOT NULL DEFAULT SYSDATETIME(),
        ShipmentID int NULL);
    CREATE UNIQUE INDEX UX_FG_DeliveryLoad_Lot ON dbo.FG_DeliveryLoad(LotNo) WHERE ShipmentID IS NULL;
    CREATE INDEX IX_FG_DeliveryLoad_Delivery ON dbo.FG_DeliveryLoad(DeliveryID,RootLotNo);
END;
IF NOT EXISTS(SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode='FG-05')
    INSERT dbo.SYS_Screen(ScreenCode,ModuleCode,ProcessCode,SubProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,PermissionCriteria,SortOrder,IsVisible,CreatedBy,CreatedTS)
    VALUES('FG-05','WEB','FG',NULL,N'출하 오더','Shipment Orders','fg/shipping-orders','FG-05','__X',25,1,'migration',SYSDATETIME());
INSERT dbo.SYS_RolePermission(RoleID,RoleName,ModuleCode,ScreenCode,PermissionLevel,IsSystemRole,EffectiveTS,CreatedBy,CreatedTS)
SELECT P.RoleID,P.RoleName,'WEB','FG-05',P.PermissionLevel,P.IsSystemRole,SYSDATETIME(),'migration',SYSDATETIME()
FROM dbo.SYS_RolePermission P WHERE P.ScreenCode='FG-02'
    AND NOT EXISTS(SELECT 1 FROM dbo.SYS_RolePermission X WHERE X.ScreenCode='FG-05' AND X.RoleID=P.RoleID);
-- Shipment Plan is read-only; its RXX cannot grant creation on the new order screen.
UPDATE dbo.SYS_RolePermission SET PermissionLevel='REX'
WHERE ScreenCode='FG-05' AND RoleID='ROLE-SYSADMIN';
COMMIT;
