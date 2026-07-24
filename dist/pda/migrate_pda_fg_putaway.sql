-- =====================================================================
-- AMES PDA Finished Goods / Put-Away database contract
-- Naming rule: dbo.FG_<BusinessEntity>, aligned with WH_Inventory style.
--
-- Main current-stock table:
--   dbo.FG_Inventory = finished goods inventory currently in warehouse
--
-- Compatibility:
--   dbo.FG_Stock is kept as a synonym when possible so older reports/code
--   can still read the same data while new PDA code uses FG_Inventory.
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.FG_Inventory', N'U') IS NULL
BEGIN
    IF OBJECT_ID(N'dbo.FG_Stock', N'U') IS NOT NULL
    BEGIN
        EXEC sp_rename N'dbo.FG_Stock', N'FG_Inventory';

        IF OBJECT_ID(N'dbo.PK_FG_Stock', N'PK') IS NOT NULL
            EXEC sp_rename N'dbo.PK_FG_Stock', N'PK_FG_Inventory', N'OBJECT';
    END
    ELSE
    BEGIN
        CREATE TABLE dbo.FG_Inventory
        (
            StockID int IDENTITY(1,1) NOT NULL,
            StockNumber varchar(24) NULL,
            FgTriggerID int NULL,
            WoID int NULL,
            ItemNo varchar(20) NULL,
            LotID int NULL,
            CustomerCode varchar(20) NULL,
            Qty decimal(12,3) NULL,
            Location varchar(20) NULL,
            Status varchar(20) NULL,
            HoldFlag bit NULL,
            HoldID int NULL,
            ReservationID int NULL,
            StockTS datetime2 NULL,
            CreatedBy varchar(50) NOT NULL CONSTRAINT DF_FG_Inventory_CreatedBy DEFAULT 'system',
            CreatedTS datetime2 NULL CONSTRAINT DF_FG_Inventory_CreatedTS DEFAULT SYSDATETIME(),
            ModifiedTS datetime2 NULL,
            ModifiedBy nvarchar(450) NULL,
            CONSTRAINT PK_FG_Inventory PRIMARY KEY CLUSTERED (StockID)
        );
    END
END;

IF OBJECT_ID(N'dbo.FG_PutAway', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.FG_PutAway', N'StorageMethod') IS NULL
        ALTER TABLE dbo.FG_PutAway ADD StorageMethod varchar(20) NULL;

    IF COL_LENGTH(N'dbo.FG_PutAway', N'ContainerType') IS NULL
        ALTER TABLE dbo.FG_PutAway ADD ContainerType varchar(20) NULL;

    IF COL_LENGTH(N'dbo.FG_PutAway', N'ContainerBarcode') IS NULL
        ALTER TABLE dbo.FG_PutAway ADD ContainerBarcode varchar(80) NULL;
END;

IF OBJECT_ID(N'dbo.FG_Inventory', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FG_Inventory_Lot' AND object_id = OBJECT_ID(N'dbo.FG_Inventory'))
        CREATE INDEX IX_FG_Inventory_Lot ON dbo.FG_Inventory (LotID, WoID, Status);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FG_Inventory_Location' AND object_id = OBJECT_ID(N'dbo.FG_Inventory'))
        CREATE INDEX IX_FG_Inventory_Location ON dbo.FG_Inventory (Location, Status);

    IF OBJECT_ID(N'dbo.FG_Stock', N'U') IS NULL
       AND OBJECT_ID(N'dbo.FG_Stock', N'V') IS NULL
       AND OBJECT_ID(N'dbo.FG_Stock', N'SN') IS NULL
    BEGIN
        CREATE SYNONYM dbo.FG_Stock FOR dbo.FG_Inventory;
    END
END;
