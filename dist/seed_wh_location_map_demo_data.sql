-- =====================================================================
-- Warehouse Location Map demo data
--
-- Safe to run repeatedly. It only inserts missing EOS demo master and
-- inventory rows; it never deletes or changes existing operational data.
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @Actor NVARCHAR(50) = N'wh-location-seed';

IF OBJECT_ID(N'dbo.WH_WarehouseMaster', N'U') IS NULL
    THROW 51000, 'dbo.WH_WarehouseMaster is required.', 1;

IF OBJECT_ID(N'dbo.WH_AreaMaster', N'U') IS NULL
    THROW 51000, 'dbo.WH_AreaMaster is required.', 1;

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NULL
    THROW 51000, 'dbo.MD_Location is required.', 1;

IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NULL
    THROW 51000, 'dbo.MD_Item is required.', 1;

IF OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NULL
    THROW 51000, 'dbo.WH_Inventory is required.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.WH_WarehouseMaster WHERE WhCode = 'EOS-WH-01')
    INSERT INTO dbo.WH_WarehouseMaster (WhCode, WhName, ActiveFlag, CreatedBy)
    VALUES ('EOS-WH-01', N'EOS Main Warehouse', 1, @Actor);

IF NOT EXISTS (SELECT 1 FROM dbo.WH_WarehouseMaster WHERE WhCode = 'EOS-WH-02')
    INSERT INTO dbo.WH_WarehouseMaster (WhCode, WhName, ActiveFlag, CreatedBy)
    VALUES ('EOS-WH-02', N'EOS Overflow Warehouse', 1, @Actor);

DECLARE @Areas TABLE (
    WhCode VARCHAR(20) NOT NULL,
    AreaCode VARCHAR(20) NOT NULL,
    AreaName NVARCHAR(120) NOT NULL
);

INSERT INTO @Areas (WhCode, AreaCode, AreaName)
VALUES
    ('EOS-WH-01', 'A1', N'Inbound Area'),
    ('EOS-WH-01', 'B1', N'Component Storage'),
    ('EOS-WH-01', 'C1', N'Release Staging'),
    ('EOS-WH-02', 'O1', N'Overflow Storage');

INSERT INTO dbo.WH_AreaMaster (WhCode, AreaCode, AreaName, ActiveFlag, CreatedBy)
SELECT A.WhCode, A.AreaCode, A.AreaName, 1, @Actor
FROM @Areas A
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.WH_AreaMaster T
    WHERE T.WhCode = A.WhCode
      AND T.AreaCode = A.AreaCode
);

DECLARE @Locations TABLE (
    LocationID VARCHAR(20) NOT NULL,
    LocationName NVARCHAR(120) NOT NULL,
    PlantCode VARCHAR(20) NOT NULL,
    ZoneCode VARCHAR(10) NOT NULL,
    LocationType VARCHAR(20) NOT NULL,
    Aisle VARCHAR(5) NOT NULL,
    Bay VARCHAR(5) NOT NULL,
    Slot VARCHAR(5) NOT NULL,
    Capacity DECIMAL(9, 3) NOT NULL
);

INSERT INTO @Locations
    (LocationID, LocationName, PlantCode, ZoneCode, LocationType, Aisle, Bay, Slot, Capacity)
VALUES
    ('EOS-A1-0101', N'Inbound Rack A1-01-01', 'EOS-WH-01', 'A1', 'INBOUND', '01', '01', '01', 500),
    ('EOS-A1-0102', N'Inbound Rack A1-01-02', 'EOS-WH-01', 'A1', 'INBOUND', '01', '01', '02', 500),
    ('EOS-A1-0201', N'Inbound Rack A1-02-01', 'EOS-WH-01', 'A1', 'INBOUND', '01', '02', '01', 500),
    ('EOS-A1-0202', N'Inbound Rack A1-02-02', 'EOS-WH-01', 'A1', 'INBOUND', '01', '02', '02', 500),
    ('EOS-B1-0101', N'Component Rack B1-01-01', 'EOS-WH-01', 'B1', 'STORAGE', '02', '01', '01', 800),
    ('EOS-B1-0102', N'Component Rack B1-01-02', 'EOS-WH-01', 'B1', 'STORAGE', '02', '01', '02', 800),
    ('EOS-B1-0201', N'Component Rack B1-02-01', 'EOS-WH-01', 'B1', 'STORAGE', '02', '02', '01', 800),
    ('EOS-B1-0202', N'Component Rack B1-02-02', 'EOS-WH-01', 'B1', 'STORAGE', '02', '02', '02', 800),
    ('EOS-C1-0101', N'Release Staging C1-01-01', 'EOS-WH-01', 'C1', 'RELEASE', '03', '01', '01', 300),
    ('EOS-C1-0201', N'Release Staging C1-02-01', 'EOS-WH-01', 'C1', 'RELEASE', '03', '02', '01', 300),
    ('EOS-O1-0101', N'Overflow Rack O1-01-01', 'EOS-WH-02', 'O1', 'STORAGE', '01', '01', '01', 1000),
    ('EOS-O1-0102', N'Overflow Rack O1-01-02', 'EOS-WH-02', 'O1', 'STORAGE', '01', '01', '02', 1000);

INSERT INTO dbo.MD_Location
    (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
SELECT L.LocationID, L.LocationName, L.ZoneCode, L.Aisle, L.Bay, L.Slot, L.Capacity,
       L.LocationType, L.PlantCode, 1, @Actor
FROM @Locations L
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.MD_Location T
    WHERE T.LocationID = L.LocationID
);

DECLARE @Items TABLE (
    ItemNo VARCHAR(20) NOT NULL,
    ItemName NVARCHAR(160) NOT NULL,
    CarType VARCHAR(10) NOT NULL,
    Uom VARCHAR(10) NOT NULL,
    MinStock DECIMAL(9, 3) NOT NULL,
    MaxStock DECIMAL(9, 3) NOT NULL
);

INSERT INTO @Items (ItemNo, ItemName, CarType, Uom, MinStock, MaxStock)
VALUES
    ('EOS-DEMO-001', N'EOS Demo Door Trim LH', 'EOS-A', 'EA', 80, 400),
    ('EOS-DEMO-002', N'EOS Demo Door Trim RH', 'EOS-A', 'EA', 100, 500),
    ('EOS-DEMO-003', N'EOS Demo Console Cover', 'EOS-B', 'EA', 60, 300),
    ('EOS-DEMO-004', N'EOS Demo Garnish Set', 'EOS-B', 'EA', 120, 600);

INSERT INTO dbo.MD_Item
    (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, MinStock, MaxStock, ActiveFlag, CreatedBy)
SELECT I.ItemNo, I.ItemName, 'RM', 'Warehouse Demo', I.CarType, I.Uom,
       I.MinStock, I.MaxStock, 1, @Actor
FROM @Items I
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.MD_Item T
    WHERE T.ItemNo = I.ItemNo
);

DECLARE @Inventory TABLE (
    ItemNo VARCHAR(20) NOT NULL,
    LocationID VARCHAR(20) NOT NULL,
    OnHandQty DECIMAL(9, 3) NOT NULL,
    ReceivedAt DATETIME2 NOT NULL
);

INSERT INTO @Inventory (ItemNo, LocationID, OnHandQty, ReceivedAt)
VALUES
    ('EOS-DEMO-001', 'EOS-A1-0101', 240, DATEADD(day, -5, SYSDATETIME())),
    ('EOS-DEMO-001', 'EOS-B1-0101', 120, DATEADD(day, -2, SYSDATETIME())),
    ('EOS-DEMO-002', 'EOS-B1-0102', 80,  DATEADD(day, -3, SYSDATETIME())),
    ('EOS-DEMO-003', 'EOS-B1-0201', 180, DATEADD(day, -7, SYSDATETIME())),
    ('EOS-DEMO-004', 'EOS-C1-0101', 60,  DATEADD(day, -1, SYSDATETIME())),
    ('EOS-DEMO-002', 'EOS-O1-0101', 300, DATEADD(day, -10, SYSDATETIME()));

INSERT INTO dbo.WH_Inventory
    (ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt, Status, CreatedBy)
SELECT I.ItemNo, I.LocationID, NULL, I.OnHandQty, 0, I.ReceivedAt, 'Received', @Actor
FROM @Inventory I
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.WH_Inventory T
    WHERE T.ItemNo = I.ItemNo
      AND T.LocationID = I.LocationID
      AND T.CreatedBy = @Actor
);

COMMIT TRANSACTION;

SELECT 'WH_WarehouseMaster' AS TableName, COUNT(*) AS DataRows
FROM dbo.WH_WarehouseMaster
WHERE CreatedBy = @Actor
UNION ALL
SELECT 'WH_AreaMaster', COUNT(*)
FROM dbo.WH_AreaMaster
WHERE CreatedBy = @Actor
UNION ALL
SELECT 'MD_Location', COUNT(*)
FROM dbo.MD_Location
WHERE CreatedBy = @Actor
UNION ALL
SELECT 'MD_Item', COUNT(*)
FROM dbo.MD_Item
WHERE CreatedBy = @Actor
UNION ALL
SELECT 'WH_Inventory', COUNT(*)
FROM dbo.WH_Inventory
WHERE CreatedBy = @Actor;
