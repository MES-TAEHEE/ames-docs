-- =====================================================================
--  seed_pda_wh_inbound_demo_data.sql
--  PDA Warehouse inbound demo data
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\seed_pda_wh_inbound_demo_data.sql
-- =====================================================================
SET NOCOUNT ON;

-- Locations used by WH-002 location scan.
IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = 'WH010101')
        INSERT INTO dbo.MD_Location
            (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
        VALUES
            ('WH010101', N'Inbound Rack A-01-01', 'A1', '01', '01', '01', 5000, 'INBOUND', 'PDA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = 'WH010201')
        INSERT INTO dbo.MD_Location
            (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
        VALUES
            ('WH010201', N'Inbound Rack A-02-01', 'A1', '01', '02', '01', 5000, 'INBOUND', 'PDA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = 'WH020101')
        INSERT INTO dbo.MD_Location
            (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
        VALUES
            ('WH020101', N'Inbound Rack B-01-01', 'B1', '02', '01', '01', 5000, 'INBOUND', 'PDA', 1, 'pda-seed');
END;

-- Keep this file independently runnable after a fresh schedule seed.
IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'INB-MAT-001')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('INB-MAT-001', N'PDA INBOUND LOCAL DEMO PART', 'RM', 'Warehouse Inbound Demo', 'MV1A', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'INB-MAT-002')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('INB-MAT-002', N'PDA INBOUND RECEIVED DEMO PART', 'RM', 'Warehouse Inbound Demo', 'LQ2', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'INB-MAT-003')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('INB-MAT-003', N'PDA INBOUND CKD DEMO PART', 'RM', 'Warehouse Inbound Demo', 'MQ4A', 'EA', 1, 'pda-seed');
END;

IF OBJECT_ID(N'dbo.MD_Vendor', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Vendor WHERE VendorID = 'V-PDA')
        INSERT INTO dbo.MD_Vendor (VendorID, VendorName, VendorType, VendorCategory, ActiveFlag, CreatedBy)
        VALUES ('V-PDA', N'PDA Demo Supplier', 'LOCAL', N'Demo', 1, 'pda-seed');
END;

IF OBJECT_ID(N'dbo.WH_PurchaseOrder', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = 'INB2607001' AND PoLineNo = 10)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('INB2607001', 10, 'V-PDA', 'INB-MAT-001', 72, 0, 'EA',
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             CONVERT(date, DATEADD(day, 1, GETDATE())),
             'Open', 'pda-seed');
    ELSE
        UPDATE dbo.WH_PurchaseOrder
           SET ItemNo = 'INB-MAT-001',
               OrderQty = 72,
               UnitCode = 'EA',
               Status = CASE WHEN COALESCE(ReceivedQty, 0) >= 72 THEN 'Complete' ELSE 'Open' END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE PoNumber = 'INB2607001'
           AND PoLineNo = 10;

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = 'INB2607002' AND PoLineNo = 20)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('INB2607002', 20, 'V-PDA', 'INB-MAT-003', 100, 0, 'EA',
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             CONVERT(date, DATEADD(day, 2, GETDATE())),
             'Open', 'pda-seed');
    ELSE
        UPDATE dbo.WH_PurchaseOrder
           SET ItemNo = 'INB-MAT-003',
               OrderQty = 100,
               UnitCode = 'EA',
               Status = CASE WHEN COALESCE(ReceivedQty, 0) >= 100 THEN 'Complete' ELSE 'Open' END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE PoNumber = 'INB2607002'
           AND PoLineNo = 20;

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = 'INB2607003' AND PoLineNo = 30)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('INB2607003', 30, 'V-PDA', 'INB-MAT-002', 40, 40, 'EA',
             CONVERT(date, DATEADD(day, -2, GETDATE())),
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             'Complete', 'pda-seed');
    ELSE
        UPDATE dbo.WH_PurchaseOrder
           SET ItemNo = 'INB-MAT-002',
               OrderQty = 40,
               ReceivedQty = CASE WHEN COALESCE(ReceivedQty, 0) < 40 THEN 40 ELSE ReceivedQty END,
               UnitCode = 'EA',
               Status = 'Complete',
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE PoNumber = 'INB2607003'
           AND PoLineNo = 30;
END;

-- LOTs used by WH-002 scan.
IF OBJECT_ID(N'dbo.tbl_Lot', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'LOT-LOCAL-001')
        INSERT INTO dbo.tbl_Lot
            (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES
            ('LOT-LOCAL-001', 'INB-MAT-001', 'LOCAL', 72, 72, SYSDATETIME(), 'Open', NULL, 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = 'INB-MAT-001',
               ProcessCode = 'LOCAL',
               BatchSize = 72,
               RemainingQty = CASE WHEN Status = 'Received' THEN RemainingQty ELSE 72 END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotCode = 'LOT-LOCAL-001';

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'LOT-CKD-001')
        INSERT INTO dbo.tbl_Lot
            (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES
            ('LOT-CKD-001', 'INB-MAT-003', 'CKD', 100, 100, SYSDATETIME(), 'Open', NULL, 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = 'INB-MAT-003',
               ProcessCode = 'CKD',
               BatchSize = 100,
               RemainingQty = CASE WHEN Status = 'Received' THEN RemainingQty ELSE 100 END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotCode = 'LOT-CKD-001';

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'LOT-LOCAL-RECV')
        INSERT INTO dbo.tbl_Lot
            (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES
            ('LOT-LOCAL-RECV', 'INB-MAT-002', 'LOCAL', 40, 40, DATEADD(day, -1, SYSDATETIME()), 'Received', 'WH010101', 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = 'INB-MAT-002',
               ProcessCode = 'LOCAL',
               BatchSize = 40,
               RemainingQty = 40,
               Status = 'Received',
               CurrentLocationID = COALESCE(CurrentLocationID, 'WH010101'),
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotCode = 'LOT-LOCAL-RECV';
END;

-- A pre-received LOT for change-location and cancel-incoming tests.
IF OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_Receiving', N'U') IS NOT NULL
BEGIN
    DECLARE @ReceivedLotID int = (SELECT TOP (1) LotID FROM dbo.tbl_Lot WHERE LotCode = 'LOT-LOCAL-RECV');
    DECLARE @ReceivedPoID int = (SELECT TOP (1) PoID FROM dbo.WH_PurchaseOrder WHERE PoNumber = 'INB2607003' AND PoLineNo = 30 ORDER BY PoID);

    IF @ReceivedLotID IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM dbo.WH_Inventory
           WHERE LotID = @ReceivedLotID
             AND COALESCE(Status, 'Received') <> 'Canceled'
             AND COALESCE(OnHandQty, 0) > 0
       )
    BEGIN
        INSERT INTO dbo.WH_Inventory
            (ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt, Status, CreatedBy)
        VALUES
            ('INB-MAT-002', 'WH010101', @ReceivedLotID, 40, 0, SYSDATETIME(), 'Received', 'pda-seed');
    END;
    ELSE IF @ReceivedLotID IS NOT NULL
    BEGIN
        UPDATE dbo.WH_Inventory
           SET ItemNo = 'INB-MAT-002',
               LocationID = COALESCE(NULLIF(LocationID, ''), 'WH010101'),
               OnHandQty = CASE WHEN COALESCE(OnHandQty, 0) <= 0 THEN 40 ELSE OnHandQty END,
               Status = CASE WHEN Status = 'Canceled' THEN 'Received' ELSE Status END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotID = @ReceivedLotID;
    END;

    IF @ReceivedLotID IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.WH_Receiving WHERE LotCode = 'LOT-LOCAL-RECV')
    BEGIN
        INSERT INTO dbo.WH_Receiving
            (ReceivingNo, PoID, ItemNo, VendorID, ReceivedQty, LocationID, LotCode,
             ReceivedAt, ReceivedBy, TerminalID, QcStatus, LabelPrinted, CreatedBy)
        VALUES
            (CONCAT('RCV-', FORMAT(SYSDATETIME(), 'yyMMddHHmmssfff')),
             @ReceivedPoID, 'INB-MAT-002', 'V-PDA', 40, 'WH010101', 'LOT-LOCAL-RECV',
             SYSDATETIME(), 'pda-seed', 'PDA', 'Received', 0, 'pda-seed');
    END;
    ELSE IF @ReceivedLotID IS NOT NULL
    BEGIN
        UPDATE dbo.WH_Receiving
           SET PoID = @ReceivedPoID,
               ItemNo = 'INB-MAT-002',
               VendorID = 'V-PDA',
               ReceivedQty = 40,
               LocationID = 'WH010101',
               QcStatus = 'Received',
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotCode = 'LOT-LOCAL-RECV';
    END;
END;

SELECT 'MD_Location' AS TableName, COUNT(*) AS DataRows FROM dbo.MD_Location
UNION ALL
SELECT 'tbl_Lot', COUNT(*) FROM dbo.tbl_Lot
UNION ALL
SELECT 'WH_Inventory', COUNT(*) FROM dbo.WH_Inventory
UNION ALL
SELECT 'WH_Receiving', COUNT(*) FROM dbo.WH_Receiving;
