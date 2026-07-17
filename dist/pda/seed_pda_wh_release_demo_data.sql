-- =====================================================================
--  seed_pda_wh_release_demo_data.sql
--  PDA Warehouse release/picking demo data
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\seed_pda_wh_release_demo_data.sql
-- =====================================================================
SET NOCOUNT ON;

-- Locations used by Release FIFO suggestions.
IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = 'REL010101')
        INSERT INTO dbo.MD_Location
            (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
        VALUES
            ('REL010101', N'Release Rack A-01-01', 'RA1', '01', '01', '01', 5000, 'RELEASE', 'PDA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = 'REL010201')
        INSERT INTO dbo.MD_Location
            (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
        VALUES
            ('REL010201', N'Release Rack A-02-01', 'RA1', '01', '02', '01', 5000, 'RELEASE', 'PDA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = 'REL020101')
        INSERT INTO dbo.MD_Location
            (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
        VALUES
            ('REL020101', N'Release Rack B-01-01', 'RB1', '02', '01', '01', 5000, 'RELEASE', 'PDA', 1, 'pda-seed');
END;

-- Keep items independently runnable after a fresh schedule seed.
IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'MAT-001')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('MAT-001', N'SW ASSY-RR HTR LH', 'RM', 'Warehouse Release Demo', 'MV1A', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'MAT-002')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('MAT-002', N'ARMREST GARNISH-RR DR LH', 'RM', 'Warehouse Release Demo', 'LQ2', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'MAT-003')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('MAT-003', N'COVER BLANKING', 'RM', 'Warehouse Release Demo', 'MQ4A', 'EA', 1, 'pda-seed');
END;

-- Release schedules used by WH001 Release tab and WH003 Release screen.
IF OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.WH_ReleaseSchedule WHERE ItemNo = 'MAT-001' AND CreatedBy = 'pda-seed')
        INSERT INTO dbo.WH_ReleaseSchedule (ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy)
        VALUES ('MAT-001', 120, 0, DATEADD(day, 1, SYSDATETIME()), 1, 'Open', 'pda-seed');
    ELSE
        UPDATE dbo.WH_ReleaseSchedule
           SET DemandQty = 120,
               PickedQty = 0,
               RequiredAt = DATEADD(day, 1, SYSDATETIME()),
               Priority = 1,
               Status = 'Open',
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE ItemNo = 'MAT-001'
           AND CreatedBy = 'pda-seed';

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_ReleaseSchedule WHERE ItemNo = 'MAT-002' AND CreatedBy = 'pda-seed')
        INSERT INTO dbo.WH_ReleaseSchedule (ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy)
        VALUES ('MAT-002', 80, 40, DATEADD(day, 2, SYSDATETIME()), 2, 'Partial', 'pda-seed');
    ELSE
        UPDATE dbo.WH_ReleaseSchedule
           SET DemandQty = 80,
               PickedQty = 40,
               RequiredAt = DATEADD(day, 2, SYSDATETIME()),
               Priority = 2,
               Status = 'Partial',
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE ItemNo = 'MAT-002'
           AND CreatedBy = 'pda-seed';

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_ReleaseSchedule WHERE ItemNo = 'MAT-003' AND CreatedBy = 'pda-seed')
        INSERT INTO dbo.WH_ReleaseSchedule (ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy)
        VALUES ('MAT-003', 60, 0, DATEADD(day, -1, SYSDATETIME()), 1, 'Open', 'pda-seed');
    ELSE
        UPDATE dbo.WH_ReleaseSchedule
           SET DemandQty = 60,
               PickedQty = 0,
               RequiredAt = DATEADD(day, -1, SYSDATETIME()),
               Priority = 1,
               Status = 'Open',
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE ItemNo = 'MAT-003'
           AND CreatedBy = 'pda-seed';
END;

-- LOTs and active inventory for FIFO tests.
IF OBJECT_ID(N'dbo.tbl_Lot', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'REL-MAT001-A')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES ('REL-MAT001-A', 'MAT-001', 'RELEASE', 60, 60, DATEADD(day, -10, SYSDATETIME()), 'Received', 'REL010101', 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = 'MAT-001', ProcessCode = 'RELEASE', BatchSize = 60, RemainingQty = 60,
               ProducedAt = DATEADD(day, -10, SYSDATETIME()), Status = 'Received',
               CurrentLocationID = 'REL010101', ModifiedBy = 'pda-seed', ModifiedTS = SYSDATETIME()
         WHERE LotCode = 'REL-MAT001-A';

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'REL-MAT001-B')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES ('REL-MAT001-B', 'MAT-001', 'RELEASE', 60, 60, DATEADD(day, -5, SYSDATETIME()), 'Received', 'REL010201', 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = 'MAT-001', ProcessCode = 'RELEASE', BatchSize = 60, RemainingQty = 60,
               ProducedAt = DATEADD(day, -5, SYSDATETIME()), Status = 'Received',
               CurrentLocationID = 'REL010201', ModifiedBy = 'pda-seed', ModifiedTS = SYSDATETIME()
         WHERE LotCode = 'REL-MAT001-B';

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'REL-MAT002-B')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES ('REL-MAT002-B', 'MAT-002', 'RELEASE', 40, 40, DATEADD(day, -6, SYSDATETIME()), 'Received', 'REL020101', 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = 'MAT-002', ProcessCode = 'RELEASE', BatchSize = 40, RemainingQty = 40,
               ProducedAt = DATEADD(day, -6, SYSDATETIME()), Status = 'Received',
               CurrentLocationID = 'REL020101', ModifiedBy = 'pda-seed', ModifiedTS = SYSDATETIME()
         WHERE LotCode = 'REL-MAT002-B';

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'REL-MAT003-A')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES ('REL-MAT003-A', 'MAT-003', 'RELEASE', 60, 60, DATEADD(day, -3, SYSDATETIME()), 'Received', 'REL010101', 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = 'MAT-003', ProcessCode = 'RELEASE', BatchSize = 60, RemainingQty = 60,
               ProducedAt = DATEADD(day, -3, SYSDATETIME()), Status = 'Received',
               CurrentLocationID = 'REL010101', ModifiedBy = 'pda-seed', ModifiedTS = SYSDATETIME()
         WHERE LotCode = 'REL-MAT003-A';
END;

IF OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NOT NULL
BEGIN
    DECLARE @Lots table (LotCode varchar(40), ItemNo varchar(20), LocationID varchar(20), Qty decimal(14,3), ReceivedDaysAgo int);

    INSERT INTO @Lots (LotCode, ItemNo, LocationID, Qty, ReceivedDaysAgo)
    VALUES
        ('REL-MAT001-A', 'MAT-001', 'REL010101', 60, 10),
        ('REL-MAT001-B', 'MAT-001', 'REL010201', 60, 5),
        ('REL-MAT002-B', 'MAT-002', 'REL020101', 40, 6),
        ('REL-MAT003-A', 'MAT-003', 'REL010101', 60, 3);

    MERGE dbo.WH_Inventory AS T
    USING
    (
        SELECT L.LotID, X.ItemNo, X.LocationID, X.Qty, X.ReceivedDaysAgo
        FROM @Lots X
        JOIN dbo.tbl_Lot L
          ON L.LotCode = X.LotCode
    ) AS S
    ON T.LotID = S.LotID
    WHEN MATCHED THEN
        UPDATE SET
            T.ItemNo = S.ItemNo,
            T.LocationID = S.LocationID,
            T.OnHandQty = S.Qty,
            T.ReservedQty = 0,
            T.LastReceivedAt = DATEADD(day, -S.ReceivedDaysAgo, SYSDATETIME()),
            T.Status = 'Received',
            T.ModifiedBy = 'pda-seed',
            T.ModifiedTS = SYSDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt, Status, CreatedBy)
        VALUES (S.ItemNo, S.LocationID, S.LotID, S.Qty, 0, DATEADD(day, -S.ReceivedDaysAgo, SYSDATETIME()), 'Received', 'pda-seed');
END;

SELECT 'WH_ReleaseSchedule' AS TableName, COUNT(*) AS DataRows FROM dbo.WH_ReleaseSchedule
UNION ALL
SELECT 'WH_Inventory', COUNT(*) FROM dbo.WH_Inventory
UNION ALL
SELECT 'tbl_Lot', COUNT(*) FROM dbo.tbl_Lot
UNION ALL
SELECT 'MD_Location', COUNT(*) FROM dbo.MD_Location;
