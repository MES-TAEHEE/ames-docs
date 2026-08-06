-- =====================================================================
-- Warehouse legacy-style location and Pick Slip demo data
--
-- Replaces only the earlier wh-location-seed demo rows with locations
-- following the legacy SIS pattern: B0-09-D2, B0-10-A1, and so on.
-- Existing master, inventory, and operational rows are not changed.
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.WH_WarehouseMaster', N'U') IS NULL
    THROW 51000, 'dbo.WH_WarehouseMaster is required.', 1;

IF OBJECT_ID(N'dbo.WH_AreaMaster', N'U') IS NULL
    THROW 51000, 'dbo.WH_AreaMaster is required.', 1;

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NULL
    THROW 51000, 'dbo.MD_Location is required.', 1;

IF OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NULL
    THROW 51000, 'dbo.WH_Inventory is required.', 1;

IF OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NULL
    THROW 51000, 'dbo.WH_ReleaseSchedule is required. Run migrate_wh_picking_slip.sql first.', 1;

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PickSlipNo') IS NULL
    THROW 51000, 'WH_ReleaseSchedule Pick Slip columns are required. Run migrate_wh_picking_slip.sql first.', 1;

BEGIN TRANSACTION;

DECLARE @LegacyActor varchar(50) = 'wh-legacy-seed';
DECLARE @OldActor varchar(50) = 'wh-location-seed';

-- Remove only the previous EOS demo rows, then refresh this legacy-style set.
DELETE FROM dbo.WH_ReleaseSchedule WHERE CreatedBy = @LegacyActor;
DELETE FROM dbo.WH_Inventory WHERE CreatedBy IN (@OldActor, @LegacyActor);
DELETE FROM dbo.MD_Item WHERE CreatedBy = @OldActor;
DELETE FROM dbo.MD_Location WHERE CreatedBy IN (@OldActor, @LegacyActor);
DELETE FROM dbo.WH_AreaMaster WHERE CreatedBy IN (@OldActor, @LegacyActor);
DELETE FROM dbo.WH_WarehouseMaster WHERE CreatedBy IN (@OldActor, @LegacyActor);

IF NOT EXISTS (SELECT 1 FROM dbo.WH_WarehouseMaster WHERE WhCode = 'B')
    INSERT INTO dbo.WH_WarehouseMaster (WhCode, WhName, ActiveFlag, CreatedBy)
    VALUES ('B', N'EOS Warehouse B', 1, @LegacyActor);

IF NOT EXISTS (SELECT 1 FROM dbo.WH_AreaMaster WHERE WhCode = 'B' AND AreaCode = 'B0')
    INSERT INTO dbo.WH_AreaMaster (WhCode, AreaCode, AreaName, ActiveFlag, CreatedBy)
    VALUES ('B', 'B0', N'Component Storage Area', 1, @LegacyActor);

DECLARE @Locations TABLE
(
    LocationID varchar(20) NOT NULL,
    LocationName nvarchar(120) NOT NULL,
    RackX varchar(5) NOT NULL,
    RackY varchar(5) NOT NULL,
    RackZ varchar(5) NOT NULL
);

INSERT INTO @Locations (LocationID, LocationName, RackX, RackY, RackZ)
VALUES
    ('B0-09-D2', N'Rack 09 / Bay D / Level 2', '09', 'D', '2'),
    ('B0-09-C2', N'Rack 09 / Bay C / Level 2', '09', 'C', '2'),
    ('B0-09-B2', N'Rack 09 / Bay B / Level 2', '09', 'B', '2'),
    ('B0-09-A2', N'Rack 09 / Bay A / Level 2', '09', 'A', '2'),
    ('B0-08-D1', N'Rack 08 / Bay D / Level 1', '08', 'D', '1'),
    ('B0-08-C1', N'Rack 08 / Bay C / Level 1', '08', 'C', '1'),
    ('B0-08-B1', N'Rack 08 / Bay B / Level 1', '08', 'B', '1'),
    ('B0-08-A1', N'Rack 08 / Bay A / Level 1', '08', 'A', '1'),
    ('B0-09-D1', N'Rack 09 / Bay D / Level 1', '09', 'D', '1'),
    ('B0-09-C1', N'Rack 09 / Bay C / Level 1', '09', 'C', '1'),
    ('B0-09-B1', N'Rack 09 / Bay B / Level 1', '09', 'B', '1'),
    ('B0-09-A1', N'Rack 09 / Bay A / Level 1', '09', 'A', '1'),
    ('B0-10-D1', N'Rack 10 / Bay D / Level 1', '10', 'D', '1'),
    ('B0-10-C1', N'Rack 10 / Bay C / Level 1', '10', 'C', '1'),
    ('B0-10-B1', N'Rack 10 / Bay B / Level 1', '10', 'B', '1'),
    ('B0-10-A1', N'Rack 10 / Bay A / Level 1', '10', 'A', '1'),
    ('B0-11-D1', N'Rack 11 / Bay D / Level 1', '11', 'D', '1'),
    ('B0-11-C1', N'Rack 11 / Bay C / Level 1', '11', 'C', '1'),
    ('B0-11-B1', N'Rack 11 / Bay B / Level 1', '11', 'B', '1'),
    ('B0-11-A1', N'Rack 11 / Bay A / Level 1', '11', 'A', '1'),
    ('B0-12-D1', N'Rack 12 / Bay D / Level 1', '12', 'D', '1'),
    ('B0-12-C1', N'Rack 12 / Bay C / Level 1', '12', 'C', '1'),
    ('B0-12-B1', N'Rack 12 / Bay B / Level 1', '12', 'B', '1'),
    ('B0-12-A1', N'Rack 12 / Bay A / Level 1', '12', 'A', '1');

INSERT INTO dbo.MD_Location
    (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
SELECT L.LocationID, L.LocationName, 'B0', L.RackX, L.RackY, L.RackZ,
       500, 'STORAGE', 'B', 1, @LegacyActor
FROM @Locations L
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Location T WHERE T.LocationID = L.LocationID);

-- These are existing AMES_DEV material masters. The location distribution
-- deliberately creates up to three FIFO candidates for each requested part.
IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = '81710-PI000NNB')
    THROW 51000, 'Required material 81710-PI000NNB was not found in MD_Item.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = '81710-PI000YGN')
    THROW 51000, 'Required material 81710-PI000YGN was not found in MD_Item.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = '82301-PI000NNB')
    THROW 51000, 'Required material 82301-PI000NNB was not found in MD_Item.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = '82301-PI000YGU')
    THROW 51000, 'Required material 82301-PI000YGU was not found in MD_Item.', 1;

DECLARE @Inventory TABLE
(
    ItemNo varchar(20) NOT NULL,
    LocationID varchar(20) NOT NULL,
    OnHandQty decimal(14, 3) NOT NULL,
    ReceivedAt datetime2 NOT NULL
);

INSERT INTO @Inventory (ItemNo, LocationID, OnHandQty, ReceivedAt)
VALUES
    ('81710-PI000NNB', 'B0-09-D2', 120, DATEADD(day, -21, SYSDATETIME())),
    ('81710-PI000NNB', 'B0-09-C2',  80, DATEADD(day, -14, SYSDATETIME())),
    ('81710-PI000NNB', 'B0-09-B2',  60, DATEADD(day,  -7, SYSDATETIME())),
    ('81710-PI000NNB', 'B0-08-D1',  55, DATEADD(day, -24, SYSDATETIME())),
    ('81710-PI000NNB', 'B0-09-C1',  48, DATEADD(day, -11, SYSDATETIME())),
    ('81710-PI000YGN', 'B0-10-D1', 100, DATEADD(day, -18, SYSDATETIME())),
    ('81710-PI000YGN', 'B0-10-C1',  75, DATEADD(day, -10, SYSDATETIME())),
    ('81710-PI000YGN', 'B0-09-A1',  70, DATEADD(day, -15, SYSDATETIME())),
    ('81710-PI000YGN', 'B0-11-D1',  65, DATEADD(day,  -8, SYSDATETIME())),
    ('82301-PI000NNB', 'B0-10-B1',  90, DATEADD(day, -16, SYSDATETIME())),
    ('82301-PI000NNB', 'B0-10-A1',  45, DATEADD(day,  -5, SYSDATETIME())),
    ('82301-PI000NNB', 'B0-11-B1',  85, DATEADD(day, -13, SYSDATETIME())),
    ('82301-PI000YGU', 'B0-12-C1',  95, DATEADD(day,  -9, SYSDATETIME())),
    ('82301-PI000YGU', 'B0-09-A2', 110, DATEADD(day, -12, SYSDATETIME()));

INSERT INTO dbo.WH_Inventory
    (ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt, Status, CreatedBy)
SELECT I.ItemNo, I.LocationID, NULL, I.OnHandQty, 0, I.ReceivedAt, 'Received', @LegacyActor
FROM @Inventory I;

INSERT INTO dbo.WH_ReleaseSchedule
    (PickSlipNo, ReqLocation, ReqSeqNo, ReqUserId,
     ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy, CreatedTS)
VALUES
    ('2026080601', 'B0-09-A2', 1, 'admin', '81710-PI000NNB', 2, 0, DATEADD(day, 1, SYSDATETIME()), 1, 'Open', @LegacyActor, SYSDATETIME()),
    ('2026080601', 'B0-09-A2', 2, 'admin', '81710-PI000YGN', 1, 0, DATEADD(day, 1, SYSDATETIME()), 1, 'Open', @LegacyActor, SYSDATETIME()),
    ('2026080601', 'B0-09-A2', 3, 'admin', '82301-PI000NNB', 3, 0, DATEADD(day, 1, SYSDATETIME()), 1, 'Open', @LegacyActor, SYSDATETIME()),
    ('2026080602', 'B0-10-A1', 1, 'admin', '82301-PI000YGU', 2, 0, DATEADD(day, 2, SYSDATETIME()), 2, 'Open', @LegacyActor, SYSDATETIME());

COMMIT TRANSACTION;

SELECT 'WH_WarehouseMaster' AS TableName, COUNT(*) AS DataRows
FROM dbo.WH_WarehouseMaster
WHERE CreatedBy = @LegacyActor
UNION ALL
SELECT 'WH_AreaMaster', COUNT(*)
FROM dbo.WH_AreaMaster
WHERE CreatedBy = @LegacyActor
UNION ALL
SELECT 'MD_Location', COUNT(*)
FROM dbo.MD_Location
WHERE CreatedBy = @LegacyActor
UNION ALL
SELECT 'WH_Inventory', COUNT(*)
FROM dbo.WH_Inventory
WHERE CreatedBy = @LegacyActor
UNION ALL
SELECT 'WH_ReleaseSchedule', COUNT(*)
FROM dbo.WH_ReleaseSchedule
WHERE CreatedBy = @LegacyActor;
