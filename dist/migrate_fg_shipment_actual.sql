-- Actual barcode shipments only. No stock deletion, rebuilding, or shipment seeds.
-- Apply to an explicitly selected database. Deploy matching API/Web/PDA afterwards.
-- PDA business commands are maintained in PdaFinishedGoodsSql.cs; do not recreate retired procedures.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
GO
IF OBJECT_ID(N'dbo.FG_ShipmentOrder',N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.FG_Shipment',N'U') IS NOT NULL
        THROW 52100,'Both shipment tables exist. Resolve the migration state first.',1;
    IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn)
        THROW 52101,'Existing customer returns require an explicit shipment mapping before migration.',1;
    IF EXISTS(SELECT 1 FROM dbo.FG_ShipmentOrder WITH(UPDLOCK,HOLDLOCK)
        WHERE CreatedBy IS NULL OR CreatedBy NOT IN('fg-seed','pda-return-test','pda-ppt-fg-release',
            'pda-ppt-fg-loading','pda-ppt-fg-return','pda-ppt-fg-history','pda-fg-six-demo','fg-web-demo'))
        THROW 52102,'Non-demo shipment orders exist. Do not discard real orders.',1;
    IF EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction WITH(UPDLOCK,HOLDLOCK) WHERE SourceType='FG_OUTBOUND')
        THROW 52103,'Existing actual outbound transactions require migration before retiring shipment orders.',1;
    DECLARE @Sql nvarchar(max)=N'';
    SELECT @Sql=@Sql+N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(parent_object_id))
        +N' DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.foreign_keys WHERE referenced_object_id=OBJECT_ID(N'dbo.FG_ShipmentOrder');
    EXEC sys.sp_executesql @Sql;
    DELETE FROM dbo.FG_ShipmentOrder;
    EXEC sys.sp_rename N'dbo.FG_ShipmentOrder',N'FG_Shipment';
    EXEC sys.sp_rename N'dbo.FG_Shipment.ShipmentOrderID',N'ShipmentID',N'COLUMN';
    SET @Sql=N'';
    SELECT @Sql=@Sql+N'DROP INDEX '+QUOTENAME(name)+N' ON dbo.FG_Shipment;'
    FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_Shipment') AND is_primary_key=0 AND index_id>0;
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_Shipment DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FG_Shipment');
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_Shipment DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FG_Shipment');
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_Shipment DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.FG_Shipment');
    EXEC sys.sp_executesql @Sql;
    SET @Sql=N'';
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_Shipment DROP COLUMN '+QUOTENAME(name)+N';'
    FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FG_Shipment') AND name<>N'ShipmentID';
    EXEC sys.sp_executesql @Sql;
    IF OBJECT_ID(N'dbo.PK_FG_ShipmentOrder',N'PK') IS NOT NULL
        EXEC sys.sp_rename N'dbo.PK_FG_ShipmentOrder',N'PK_FG_Shipment',N'OBJECT';
END;
GO
IF OBJECT_ID(N'dbo.FG_Shipment',N'U') IS NULL
    CREATE TABLE dbo.FG_Shipment(ShipmentID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FG_Shipment PRIMARY KEY);
IF COL_LENGTH(N'dbo.FG_Shipment',N'OutboundBarcode') IS NULL AND COL_LENGTH(N'dbo.FG_Shipment',N'LotNo') IS NULL
    ALTER TABLE dbo.FG_Shipment ADD OutboundBarcode nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
        UnitType varchar(10) COLLATE DATABASE_DEFAULT NOT NULL,
        CustomerID varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
        ShippedAt datetime2 NOT NULL CONSTRAINT DF_FG_Shipment_ShippedAt DEFAULT SYSDATETIME(),
        ShippedBy varchar(20) COLLATE DATABASE_DEFAULT NOT NULL;
GO
IF COL_LENGTH(N'dbo.FG_CustomerReturn',N'ShipmentOrderID') IS NOT NULL
    EXEC sys.sp_rename N'dbo.FG_CustomerReturn.ShipmentOrderID',N'ShipmentID',N'COLUMN';
GO
-- Customer/shipment existence is validated by PdaFinishedGoodsSql in the save transaction.
-- Do not recreate foreign keys: existing databases use migrate_fg_shipment_no_foreign_keys.sql.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_Shipment') AND name=N'IX_FG_Shipment_ShippedAt')
BEGIN
    DECLARE @ShipmentIndexSql nvarchar(max)=N'CREATE INDEX IX_FG_Shipment_ShippedAt ON dbo.FG_Shipment(ShippedAt DESC,ShipmentID DESC) INCLUDE(CustomerID,'+
        CASE WHEN COL_LENGTH(N'dbo.FG_Shipment',N'LotNo') IS NOT NULL THEN N'LotNo' ELSE N'OutboundBarcode' END+N',UnitType,ShippedBy);';
    EXEC(@ShipmentIndexSql);
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction') AND name=N'IX_WH_InventoryTransaction_Source')
    CREATE INDEX IX_WH_InventoryTransaction_Source ON dbo.WH_InventoryTransaction(SourceType,SourceID,TransactionType)
        INCLUDE(LotNo,PartNo,QtyChange,LocationNo,TransactionTime);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'UX_FG_CustomerReturn_ShipmentLot')
    CREATE UNIQUE INDEX UX_FG_CustomerReturn_ShipmentLot ON dbo.FG_CustomerReturn(ShipmentID,LotNo) WHERE LotNo IS NOT NULL;
GO
-- Retired scenario reset must never manufacture actual shipments or returns.
CREATE OR ALTER PROCEDURE dbo.FG_PDA_PPT_TEST_RESET @Screen varchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51700,'Legacy FG scenario seeding is retired. Use actual barcode operations.',1;
END;
GO
COMMIT TRANSACTION;
GO
