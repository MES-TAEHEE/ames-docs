-- =====================================================================
--  PDA_SEED.sql
--  Consolidated Warehouse and Finished Goods demo/test data for the PDA
--
--  Apply after dist/pda/PDA_SCHEMA.sql:
--    sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\pda\PDA_SEED.sql
--
--  This file is rerunnable. It contains the current WH/FG PDA test set.
-- =====================================================================
USE [AMES_DEV];
GO

-- TEST / 0000: PDA scenario-runner account. Keep it unrestricted so every
-- Warehouse test screen can reuse the same login as scenario helpers expand.
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SYS_UserProfile', N'U') IS NOT NULL
BEGIN
    DECLARE @TestUserId nvarchar(450) = N'pda-test-user';
    DECLARE @TestPinHash nvarchar(200) = N'AQAAAAEAACcQAAAAEJFoD5NntyEZN/tZd1NHiMZtqlIJPCqGlrClvFmOcSGzPWghpal/Q1PscOkb3c9kyQ==';

    MERGE dbo.AspNetUsers AS T
    USING (SELECT @TestUserId AS Id) AS S ON T.Id = S.Id
    WHEN MATCHED THEN UPDATE SET
        UserName = N'TEST', NormalizedUserName = N'TEST',
        LockoutEnabled = 1, AccessFailedCount = 0
    WHEN NOT MATCHED THEN INSERT
        (Id, UserName, NormalizedUserName, SecurityStamp, ConcurrencyStamp,
         EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES
        (@TestUserId, N'TEST', N'TEST', REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''),
         REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 0, 0, 0, 1, 0);

    IF EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE EmployeeNo = 'TEST')
        UPDATE dbo.SYS_UserProfile
           SET UserID = @TestUserId, EmployeeName = N'Warehouse Test', Department = 'QA',
               AssignedLines = NULL, PinHash = @TestPinHash, AccountStatus = 'Active',
               FailedLoginCount = 0, ModifiedBy = N'pda-seed', ModifiedTS = SYSDATETIME()
         WHERE EmployeeNo = 'TEST';
    ELSE
        INSERT INTO dbo.SYS_UserProfile
            (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
             AssignedLines, PinHash, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
        VALUES
            (@TestUserId, 'TEST', N'Warehouse Test', 'QA', 'SEH-US-01', 'DAY',
             NULL, @TestPinHash, 'Active', 0, 'pda-seed', SYSDATETIME());
END;
GO

-- =====================================================================
--  WH Inbound
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

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = 'WH019901')
        INSERT INTO dbo.MD_Location
            (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
        VALUES
            ('WH019901', N'Inbound Blocked Test', 'A1', '01', '99', '01', 500, 'BLOCKED', 'PDA', 1, 'pda-scenario-seed');
    ELSE
        UPDATE dbo.MD_Location
           SET Capacity = 500, LocationType = 'BLOCKED', ActiveFlag = 1,
               ModifiedBy = 'pda-scenario-seed', ModifiedTS = SYSDATETIME()
         WHERE LocationID = 'WH019901';

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = 'WH019902')
        INSERT INTO dbo.MD_Location
            (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot, Capacity, LocationType, PlantCode, ActiveFlag, CreatedBy)
        VALUES
            ('WH019902', N'Inbound Capacity Test', 'A1', '01', '99', '02', 50, 'INBOUND', 'PDA', 1, 'pda-scenario-seed');
    ELSE
        UPDATE dbo.MD_Location
           SET Capacity = 50, LocationType = 'INBOUND', ActiveFlag = 1,
               ModifiedBy = 'pda-scenario-seed', ModifiedTS = SYSDATETIME()
         WHERE LocationID = 'WH019902';
END;

-- Keep this file independently runnable after a fresh schedule seed.
IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = '81710-PI000NNB')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('81710-PI000NNB', N'TRIM ASSY-TAIL GATE, LWR', 'ASSY', 'TRIM', 'NE1A', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = '81711-PI000YGN')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('81711-PI000YGN', N'TRIM - TAIL GATE LWR', 'SUB', 'TRIM', 'NE1A', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = '82301-PI000NNB')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('82301-PI000NNB', N'PNL ASSY-FR DR TRIM COMPL,LH', 'ASSY', 'TRIM', 'NE1A', 'EA', 1, 'pda-seed');
END;

IF OBJECT_ID(N'dbo.MD_Vendor', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Vendor WHERE VendorID = 'V1007')
        INSERT INTO dbo.MD_Vendor (VendorID, VendorName, VendorType, VendorCategory, ActiveFlag, CreatedBy)
        VALUES ('V1007', N'EOS Georgia Plant', 'LOCAL', N'Interior Trim', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Vendor WHERE VendorID = 'V2003')
        INSERT INTO dbo.MD_Vendor (VendorID, VendorName, VendorType, VendorCategory, ActiveFlag, CreatedBy)
        VALUES ('V2003', N'EOS Korea CKD', 'CKD', N'Interior Trim', 1, 'pda-seed');
END;

IF OBJECT_ID(N'dbo.WH_PurchaseOrder', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100151141' AND PoLineNo = 80)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('4100151141', 80, 'V1007', '81710-PI000NNB', 540, 0, 'EA',
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             CONVERT(date, DATEADD(day, 1, GETDATE())),
             'Open', 'pda-seed');
    ELSE
        UPDATE dbo.WH_PurchaseOrder
           SET ItemNo = '81710-PI000NNB',
               OrderQty = 540,
               UnitCode = 'EA',
               Status = CASE WHEN COALESCE(ReceivedQty, 0) >= 540 THEN 'Complete' ELSE 'Open' END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE PoNumber = '4100151141'
           AND PoLineNo = 80;

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100151142' AND PoLineNo = 10)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('4100151142', 10, 'V1007', '81711-PI000YGN', 144, 0, 'EA',
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             CONVERT(date, DATEADD(day, 1, GETDATE())),
             'Open', 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100151610' AND PoLineNo = 20)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('4100151610', 20, 'V2003', '82301-PI000NNB', 900, 0, 'EA',
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             CONVERT(date, DATEADD(day, 2, GETDATE())),
             'Open', 'pda-seed');
    ELSE
        UPDATE dbo.WH_PurchaseOrder
           SET VendorID = 'V2003',
               ItemNo = '82301-PI000NNB',
               OrderQty = 900,
               UnitCode = 'EA',
               Status = CASE WHEN COALESCE(ReceivedQty, 0) >= 900 THEN 'Complete' ELSE 'Open' END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE PoNumber = '4100151610'
           AND PoLineNo = 20;

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100152166' AND PoLineNo = 60)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('4100152166', 60, 'V1007', '81711-PI000YGN', 288, 288, 'EA',
             CONVERT(date, DATEADD(day, -2, GETDATE())),
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             'Complete', 'pda-seed');
    ELSE
        UPDATE dbo.WH_PurchaseOrder
           SET ItemNo = '81711-PI000YGN',
               OrderQty = 288,
               ReceivedQty = CASE WHEN COALESCE(ReceivedQty, 0) < 288 THEN 288 ELSE ReceivedQty END,
               UnitCode = 'EA',
               Status = 'Complete',
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE PoNumber = '4100152166'
           AND PoLineNo = 60;
END;

-- Production-like LOCAL delivery note / box barcodes and CKD case / box barcodes.
IF OBJECT_ID(N'dbo.WH_InboundPackage', N'U') IS NOT NULL
BEGIN
    DECLARE @LocalPo1 int =
        (SELECT TOP (1) PoID FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100151141' AND PoLineNo = 80);
    DECLARE @LocalPo2 int =
        (SELECT TOP (1) PoID FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100151142' AND PoLineNo = 10);
    DECLARE @CkdPo int =
        (SELECT TOP (1) PoID FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100151610' AND PoLineNo = 20);

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = '5011LL260828000001')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CreatedBy)
        VALUES ('5011LL260828000001', '81710-PI000NNB', 'LOCAL', 180, 180, DATEADD(hour, -6, SYSDATETIME()), 'Open', 'pda-seed');
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = '5011LL260828000002')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CreatedBy)
        VALUES ('5011LL260828000002', '81710-PI000NNB', 'LOCAL', 180, 180, DATEADD(hour, -5, SYSDATETIME()), 'Open', 'pda-seed');
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = '5011LL260828000003')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CreatedBy)
        VALUES ('5011LL260828000003', '81711-PI000YGN', 'LOCAL', 144, 144, DATEADD(hour, -4, SYSDATETIME()), 'Open', 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'CKD260828000000001')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CreatedBy)
        VALUES ('CKD260828000000001', '82301-PI000NNB', 'CKD', 300, 300, DATEADD(day, -8, SYSDATETIME()), 'Open', 'pda-seed');
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'CKD260828000000002')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CreatedBy)
        VALUES ('CKD260828000000002', '82301-PI000NNB', 'CKD', 300, 300, DATEADD(day, -8, SYSDATETIME()), 'Open', 'pda-seed');
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = 'CKD260828000000003')
        INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CreatedBy)
        VALUES ('CKD260828000000003', '82301-PI000NNB', 'CKD', 300, 300, DATEADD(day, -7, SYSDATETIME()), 'Open', 'pda-seed');

    INSERT INTO dbo.WH_InboundPackage
        (ReceiveType, DocumentBarcode, DocumentNo, VendorID, DeliveryDate, ArrivalDate,
         BoxBarcode, LotID, ItemNo, PoID, Qty, UnitCode, ProductionDate, Status, CreatedBy)
    SELECT N'LOCAL', '5011202608280001', '5011202608280001', 'V1007',
           CONVERT(date, DATEADD(day, 1, GETDATE())), CONVERT(date, GETDATE()),
           V.BoxBarcode, L.LotID, V.ItemNo, V.PoID, V.Qty, 'EA', CONVERT(date, L.ProducedAt), N'Open', 'pda-seed'
    FROM (VALUES
        ('5011LL260828000001', '81710-PI000NNB', @LocalPo1, CONVERT(decimal(14,3), 180)),
        ('5011LL260828000002', '81710-PI000NNB', @LocalPo1, CONVERT(decimal(14,3), 180)),
        ('5011LL260828000003', '81711-PI000YGN', @LocalPo2, CONVERT(decimal(14,3), 144))
    ) V(BoxBarcode, ItemNo, PoID, Qty)
    JOIN dbo.tbl_Lot L ON L.LotCode = V.BoxBarcode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.WH_InboundPackage P WHERE P.BoxBarcode = V.BoxBarcode);

    INSERT INTO dbo.WH_InboundPackage
        (ReceiveType, DocumentBarcode, DocumentNo, VendorID, CaseNo, InvoiceNo, ContainerNo,
         ShipDate, PackDate, DeliveryDate, ArrivalDate, BoxBarcode, LotID, ItemNo, PoID,
         Qty, UnitCode, ProductionDate, Status, CreatedBy)
    SELECT N'CKD', 'CKD202608280001CASE00001', 'CKD-DN-260828-01', 'V2003', 'CASE-260828-001',
           'INV-260828-0031', 'SEGU2608281', CONVERT(date, DATEADD(day, -7, GETDATE())),
           CONVERT(date, DATEADD(day, -8, GETDATE())), CONVERT(date, DATEADD(day, 2, GETDATE())),
           CONVERT(date, GETDATE()), V.BoxBarcode, L.LotID, V.ItemNo, @CkdPo,
           300, 'EA', CONVERT(date, L.ProducedAt), N'Open', 'pda-seed'
    FROM (VALUES
        ('CKD260828000000001', '82301-PI000NNB'),
        ('CKD260828000000002', '82301-PI000NNB'),
        ('CKD260828000000003', '82301-PI000NNB')
    ) V(BoxBarcode, ItemNo)
    JOIN dbo.tbl_Lot L ON L.LotCode = V.BoxBarcode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.WH_InboundPackage P WHERE P.BoxBarcode = V.BoxBarcode);
END;

-- Resettable LOCAL Inbound scenarios for direct PDA verification.
IF OBJECT_ID(N'dbo.WH_InboundPackage', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_PurchaseOrder', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.tbl_Lot', N'U') IS NOT NULL
BEGIN
    DECLARE @PdaInboundTestLotID int =
        (SELECT TOP (1) LotID FROM dbo.tbl_Lot WHERE LotCode = '5011LL260903900001');
    DECLARE @PdaInboundRollbackLotID int =
        (SELECT TOP (1) LotID FROM dbo.tbl_Lot WHERE LotCode = '5011LL260903900002');

    IF @PdaInboundTestLotID IS NOT NULL
       AND OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
        DELETE FROM dbo.WH_InventoryTransaction WHERE LotID = @PdaInboundTestLotID;
    IF @PdaInboundRollbackLotID IS NOT NULL
       AND OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
        DELETE FROM dbo.WH_InventoryTransaction WHERE LotID = @PdaInboundRollbackLotID;

    IF @PdaInboundTestLotID IS NOT NULL
       AND OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NOT NULL
        DELETE FROM dbo.WH_Inventory WHERE LotID = @PdaInboundTestLotID;
    IF @PdaInboundRollbackLotID IS NOT NULL
       AND OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NOT NULL
        DELETE FROM dbo.WH_Inventory WHERE LotID = @PdaInboundRollbackLotID;

    IF OBJECT_ID(N'dbo.WH_Receiving', N'U') IS NOT NULL
        DELETE FROM dbo.WH_Receiving WHERE LotCode IN ('5011LL260903900001', '5011LL260903900002');

    DELETE FROM dbo.WH_InboundPackage WHERE BoxBarcode IN ('5011LL260903900001', '5011LL260903900002');
    DELETE FROM dbo.tbl_Lot WHERE LotCode IN ('5011LL260903900001', '5011LL260903900002');
    DELETE FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100260903' AND PoLineNo = 90;

    INSERT INTO dbo.WH_PurchaseOrder
        (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode,
         OrderDate, DueDate, Status, CreatedBy)
    VALUES
        ('4100260903', 90, 'V1007', '81710-PI000NNB', 240, 0, 'EA',
         CONVERT(date, GETDATE()), CONVERT(date, DATEADD(day, 1, GETDATE())),
         'Open', 'pda-seed');

    DECLARE @PdaInboundTestPoID int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty,
         ProducedAt, Status, CurrentLocationID, CreatedBy)
    VALUES
        ('5011LL260903900001', '81710-PI000NNB', 'LOCAL', 120, 120,
         SYSDATETIME(), 'Open', NULL, 'pda-seed');

    DECLARE @PdaInboundNewLotID int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty,
         ProducedAt, Status, CurrentLocationID, CreatedBy)
    VALUES
        ('5011LL260903900002', '81710-PI000NNB', 'LOCAL', 120, 120,
         SYSDATETIME(), 'Open', NULL, 'pda-seed');

    DECLARE @PdaInboundRollbackNewLotID int = CONVERT(int, SCOPE_IDENTITY());

    INSERT INTO dbo.WH_InboundPackage
        (ReceiveType, DocumentBarcode, DocumentNo, VendorID, DeliveryDate, ArrivalDate,
         BoxBarcode, LotID, ItemNo, PoID, Qty, UnitCode, ProductionDate, Status, CreatedBy)
    VALUES
        (N'LOCAL', '5011202609039001', '5011202609039001', 'V1007',
         CONVERT(date, DATEADD(day, 1, GETDATE())), CONVERT(date, GETDATE()),
         '5011LL260903900001', @PdaInboundNewLotID, '81710-PI000NNB',
         @PdaInboundTestPoID, 120, 'EA', CONVERT(date, GETDATE()), N'Open', 'pda-seed'),
        (N'LOCAL', '5011202609039001', '5011202609039001', 'V1007',
         CONVERT(date, DATEADD(day, 1, GETDATE())), CONVERT(date, GETDATE()),
         '5011LL260903900002', @PdaInboundRollbackNewLotID, '81710-PI000NNB',
         @PdaInboundTestPoID, 120, 'EA', CONVERT(date, GETDATE()), N'Open', 'pda-seed');
END;

-- LOTs used by WH-002 scan.
IF OBJECT_ID(N'dbo.tbl_Lot', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = '260828001')
        INSERT INTO dbo.tbl_Lot
            (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES
            ('260828001', '81710-PI000NNB', 'LOCAL', 540, 540, SYSDATETIME(), 'Open', NULL, 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = '81710-PI000NNB',
               ProcessCode = 'LOCAL',
               BatchSize = 540,
               RemainingQty = CASE WHEN Status = 'Received' THEN RemainingQty ELSE 540 END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotCode = '260828001';

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = '260828002')
        INSERT INTO dbo.tbl_Lot
            (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES
            ('260828002', '82301-PI000NNB', 'CKD', 900, 900, SYSDATETIME(), 'Open', NULL, 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = '82301-PI000NNB',
               ProcessCode = 'CKD',
               BatchSize = 900,
               RemainingQty = CASE WHEN Status = 'Received' THEN RemainingQty ELSE 900 END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotCode = '260828002';

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = '260827014')
        INSERT INTO dbo.tbl_Lot
            (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt, Status, CurrentLocationID, CreatedBy)
        VALUES
            ('260827014', '81711-PI000YGN', 'LOCAL', 288, 288, DATEADD(day, -1, SYSDATETIME()), 'Received', 'WH010101', 'pda-seed');
    ELSE
        UPDATE dbo.tbl_Lot
           SET ItemNo = '81711-PI000YGN',
               ProcessCode = 'LOCAL',
               BatchSize = 288,
               RemainingQty = 288,
               Status = 'Received',
               CurrentLocationID = 'WH010101',
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotCode = '260827014';
END;

-- A pre-received LOT for change-location and cancel-incoming tests.
IF OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_Receiving', N'U') IS NOT NULL
BEGIN
    DECLARE @ReceivedLotID int = (SELECT TOP (1) LotID FROM dbo.tbl_Lot WHERE LotCode = '260827014');
    DECLARE @ReceivedPoID int = (SELECT TOP (1) PoID FROM dbo.WH_PurchaseOrder WHERE PoNumber = '4100152166' AND PoLineNo = 60 ORDER BY PoID);

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
            ('81711-PI000YGN', 'WH010101', @ReceivedLotID, 288, 0, SYSDATETIME(), 'Received', 'pda-seed');
    END;
    ELSE IF @ReceivedLotID IS NOT NULL
    BEGIN
        UPDATE dbo.WH_Inventory
           SET ItemNo = '81711-PI000YGN',
               LocationID = 'WH010101',
               OnHandQty = CASE WHEN COALESCE(OnHandQty, 0) <= 0 THEN 288 ELSE OnHandQty END,
               Status = CASE WHEN Status = 'Canceled' THEN 'Received' ELSE Status END,
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotID = @ReceivedLotID;
    END;

    IF @ReceivedLotID IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.WH_Receiving WHERE LotCode = '260827014')
    BEGIN
        INSERT INTO dbo.WH_Receiving
            (ReceivingNo, PoID, ItemNo, VendorID, ReceivedQty, LocationID, LotCode,
             ReceivedAt, ReceivedBy, TerminalID, QcStatus, LabelPrinted, CreatedBy)
        VALUES
            (CONCAT('RCV-', FORMAT(SYSDATETIME(), 'yyMMddHHmmssfff')),
             @ReceivedPoID, '81711-PI000YGN', 'V1007', 288, 'WH010101', '260827014',
             SYSDATETIME(), 'pda-seed', 'PDA', 'Received', 0, 'pda-seed');
    END;
    ELSE IF @ReceivedLotID IS NOT NULL
    BEGIN
        UPDATE dbo.WH_Receiving
           SET PoID = @ReceivedPoID,
               ItemNo = '81711-PI000YGN',
               VendorID = 'V1007',
               ReceivedQty = 288,
               LocationID = 'WH010101',
               QcStatus = 'Received',
               ModifiedBy = 'pda-seed',
               ModifiedTS = SYSDATETIME()
         WHERE LotCode = '260827014';
    END;
END;

SELECT 'MD_Location' AS TableName, COUNT(*) AS DataRows FROM dbo.MD_Location
UNION ALL
SELECT 'tbl_Lot', COUNT(*) FROM dbo.tbl_Lot
UNION ALL
SELECT 'WH_InboundPackage', COUNT(*) FROM dbo.WH_InboundPackage
UNION ALL
SELECT 'WH_Inventory', COUNT(*) FROM dbo.WH_Inventory
UNION ALL
SELECT 'WH_Receiving', COUNT(*) FROM dbo.WH_Receiving;
GO

-- =====================================================================
--  WH Release Base Demo
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
GO

-- =====================================================================
--  WH Inventory and Location Demo
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

IF OBJECT_ID(N'dbo.tbl_Lot', N'U') IS NULL
    THROW 51000, 'dbo.tbl_Lot is required.', 1;

IF OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NULL
    THROW 51000, 'dbo.WH_ReleaseSchedule is required. Run PDA_SCHEMA.sql first.', 1;

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PickSlipNo') IS NULL
    THROW 51000, 'WH_ReleaseSchedule Pick Slip columns are required. Run PDA_SCHEMA.sql first.', 1;

BEGIN TRANSACTION;

DECLARE @LegacyActor varchar(50) = 'wh-legacy-seed';
DECLARE @OldActor varchar(50) = 'wh-location-seed';
DECLARE @ScrollActor varchar(50) = 'CODEX_SAMPLE';

-- Remove only the previous EOS demo rows, then refresh this legacy-style set.
DELETE FROM dbo.WH_ReleaseSchedule WHERE CreatedBy = @LegacyActor;
DELETE FROM dbo.WH_Inventory WHERE CreatedBy IN (@OldActor, @LegacyActor, @ScrollActor);
DELETE FROM dbo.tbl_Lot WHERE CreatedBy IN (@LegacyActor, @ScrollActor);
DELETE FROM dbo.MD_Item WHERE CreatedBy IN (@OldActor, @ScrollActor);
DELETE FROM dbo.MD_Location WHERE CreatedBy IN (@OldActor, @LegacyActor);
DELETE FROM dbo.WH_AreaMaster WHERE CreatedBy IN (@OldActor, @LegacyActor);
DELETE FROM dbo.WH_WarehouseMaster WHERE CreatedBy IN (@OldActor, @LegacyActor);

IF NOT EXISTS (SELECT 1 FROM dbo.WH_WarehouseMaster WHERE WhCode = 'B')
    INSERT INTO dbo.WH_WarehouseMaster (WhCode, WhName, ActiveFlag, CreatedBy)
    VALUES ('B', N'Wiley W/H', 1, @LegacyActor);
ELSE
    UPDATE dbo.WH_WarehouseMaster
       SET WhName = N'Wiley W/H', ActiveFlag = 1
     WHERE WhCode = 'B';

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

UPDATE T
   SET T.LocationName = L.LocationName,
       T.ZoneCode = 'B0',
       T.Aisle = L.RackX,
       T.Bay = L.RackY,
       T.Slot = L.RackZ,
       T.Capacity = 500,
       T.LocationType = 'STORAGE',
       T.PlantCode = 'B',
       T.ActiveFlag = 1
FROM dbo.MD_Location T
INNER JOIN @Locations L ON L.LocationID = T.LocationID;

-- These are existing AMES_DEV material masters. The location distribution
-- deliberately creates up to three FIFO candidates for each requested part.
INSERT INTO dbo.MD_Item
    (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM,
     SafetyStock, ActiveFlag, CreatedBy, CreatedTS)
SELECT V.ItemNo, V.ItemName, 'ASSY', NULL, 'NE1A', 'EA',
       0, 1, 'pda-seed', SYSDATETIME()
FROM (VALUES
    (CONVERT(varchar(20), '81710-PI000NNB'), CONVERT(nvarchar(200), N'TRIM ASSY-TAIL GATE, LWR')),
    ('81710-PI000YGN', N'TRIM ASSY-TAIL GATE, LWR'),
    ('81710-PI010NNB', N'TRIM ASSY-TAIL GATE, LWR'),
    ('81710-PI010YGN', N'TRIM ASSY-TAIL GATE, LWR'),
    ('81711-PI000NNB', N'TRIM - TAIL GATE LWR'),
    ('81711-PI000YGN', N'TRIM - TAIL GATE LWR'),
    ('82301-PI000NNB', N'PNL ASSY-FR DR TRIM COMPL,LH'),
    ('82301-PI000YGU', N'PNL ASSY-FR DR TRIM COMPL,LH')
) V(ItemNo, ItemName)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Item I WHERE I.ItemNo = V.ItemNo);

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
    LotCode varchar(40) NOT NULL,
    ItemNo varchar(20) NOT NULL,
    LocationID varchar(20) NOT NULL,
    OnHandQty decimal(14, 3) NOT NULL,
    ReceivedAt datetime2 NOT NULL
);

INSERT INTO @Inventory (LotCode, ItemNo, LocationID, OnHandQty, ReceivedAt)
VALUES
    ('5011LL260804000001', '81710-PI000NNB', 'B0-09-D2', 120, DATEADD(day, -21, SYSDATETIME())),
    ('5011LL260811000002', '81710-PI000NNB', 'B0-09-C2',  80, DATEADD(day, -14, SYSDATETIME())),
    ('5011LL260818000003', '81710-PI000NNB', 'B0-09-B2',  60, DATEADD(day,  -7, SYSDATETIME())),
    ('5011LL260801000004', '81710-PI000NNB', 'B0-08-D1',  55, DATEADD(day, -24, SYSDATETIME())),
    ('5011LL260814000005', '81710-PI000NNB', 'B0-09-C1',  48, DATEADD(day, -11, SYSDATETIME())),
    ('5011LL260807000006', '81710-PI000YGN', 'B0-10-D1', 100, DATEADD(day, -18, SYSDATETIME())),
    ('5011LL260815000007', '81710-PI000YGN', 'B0-10-C1',  75, DATEADD(day, -10, SYSDATETIME())),
    ('5011LL260810000008', '81710-PI000YGN', 'B0-09-A1',  70, DATEADD(day, -15, SYSDATETIME())),
    ('5011LL260817000009', '81710-PI000YGN', 'B0-11-D1',  65, DATEADD(day,  -8, SYSDATETIME())),
    ('5011LL260809000010', '82301-PI000NNB', 'B0-10-B1',  90, DATEADD(day, -16, SYSDATETIME())),
    ('5011LL260820000011', '82301-PI000NNB', 'B0-10-A1',  45, DATEADD(day,  -5, SYSDATETIME())),
    ('5011LL260812000012', '82301-PI000NNB', 'B0-11-B1',  85, DATEADD(day, -13, SYSDATETIME())),
    ('5011LL260816000013', '82301-PI000YGU', 'B0-12-C1',  95, DATEADD(day,  -9, SYSDATETIME())),
    ('5011LL260813000014', '82301-PI000YGU', 'B0-09-A2', 110, DATEADD(day, -12, SYSDATETIME())),
    ('5011LL260901000099', '81710-PI000NNB', 'B0-12-A1',   0, DATEADD(day,  -3, SYSDATETIME()));

INSERT INTO dbo.tbl_Lot
    (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt,
     Status, InventoryStatus, QualityFlag, CurrentLocationID, CreatedBy)
SELECT I.LotCode, I.ItemNo, 'WH', I.OnHandQty, I.OnHandQty, I.ReceivedAt,
       'Received', 'STORED', 'PASS', I.LocationID, @LegacyActor
FROM @Inventory I;

INSERT INTO dbo.WH_Inventory
    (ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt, Status, CreatedBy)
SELECT I.ItemNo, I.LocationID, L.LotID, I.OnHandQty, 0, I.ReceivedAt, 'Received', @LegacyActor
FROM @Inventory I
INNER JOIN dbo.tbl_Lot L ON L.LotCode = I.LotCode;

INSERT INTO dbo.WH_ReleaseSchedule
    (PickSlipNo, ReqLocation, ReqSeqNo, ReqUserId,
     ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy, CreatedTS)
VALUES
    ('2026080601', 'B0-09-A2', 1, 'admin', '81710-PI000NNB', 2, 0, DATEADD(day, 1, SYSDATETIME()), 1, 'Open', @LegacyActor, SYSDATETIME()),
    ('2026080601', 'B0-09-A2', 2, 'admin', '81710-PI000YGN', 1, 0, DATEADD(day, 1, SYSDATETIME()), 1, 'Open', @LegacyActor, SYSDATETIME()),
    ('2026080601', 'B0-09-A2', 3, 'admin', '82301-PI000NNB', 3, 0, DATEADD(day, 1, SYSDATETIME()), 1, 'Open', @LegacyActor, SYSDATETIME()),
    ('2026080602', 'B0-10-A1', 1, 'admin', '82301-PI000YGU', 2, 0, DATEADD(day, 2, SYSDATETIME()), 2, 'Open', @LegacyActor, SYSDATETIME());

COMMIT TRANSACTION;

-- Repeatable history rows for WH006 filters and adjustment detail.
IF OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.WH_InventoryTransaction WHERE CreatedBy = 'pda-scenario-seed';

    DECLARE @ScenarioLotID int =
    (
        SELECT TOP (1) LotID
        FROM dbo.tbl_Lot
        WHERE LotCode = '5011LL260804000001'
    );

    INSERT INTO dbo.WH_InventoryTransaction
        (TransactionTime, TransactionType, ItemNo, LocationID, LotID,
         QtyBefore, QtyChange, QtyAfter, ReasonCode, RefDocType,
         OperatorID, ApproverID, Note, CreatedBy, CreatedTS)
    VALUES
        (DATEADD(day, -2, SYSDATETIME()), 'IN',  '81710-PI000NNB', 'B0-09-D2', @ScenarioLotID,
         0, 120, 120, 'INBOUND', 'WH_INBOUND', N'admin', NULL, N'Inbound scenario seed', 'pda-scenario-seed', SYSDATETIME()),
        (DATEADD(day, -1, SYSDATETIME()), 'OUT', '81710-PI000NNB', 'B0-09-D2', @ScenarioLotID,
         120, -1, 119, 'RELEASE', 'WH_RELEASE', N'admin', NULL, N'Release scenario seed', 'pda-scenario-seed', SYSDATETIME()),
        (DATEADD(hour, -2, SYSDATETIME()), 'ADJ', '81710-PI000NNB', 'B0-09-D2', @ScenarioLotID,
         119, 1, 120, 'COUNT_DIFF', 'WH_ADJUST', N'admin', N'admin', N'Adjustment scenario seed', 'pda-scenario-seed', SYSDATETIME());
END;

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
GO

-- =====================================================================
--  WH Release Scan Test
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

DECLARE @PickSlipNo nvarchar(40) = N'2026082801';
DECLARE @ItemNo varchar(20) = '81710-PI000NNB';
DECLARE @DirectItemNo varchar(20) = '82301-PI000NNB';

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @ItemNo)
    THROW 50001, 'Required test item 81710-PI000NNB was not found in MD_Item.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @DirectItemNo)
    THROW 50002, 'Required test item 82301-PI000NNB was not found in MD_Item.', 1;

DELETE FROM dbo.WH_ReleaseSchedule
WHERE PickSlipNo IN (@PickSlipNo, N'PDA-REL-TEST-01');

DELETE W
FROM dbo.WH_Inventory W
INNER JOIN dbo.tbl_Lot L ON L.LotID = W.LotID
WHERE L.LotCode IN
(
    'PDA-REL-LOT-001', 'PDA-REL-LOT-002', 'PDA-REL-LOT-003',
    '5011LL260701000001', '5011LL260715000002', '5011LL260801000003',
    '5011LL260820000010'
);

DELETE FROM dbo.tbl_Lot
WHERE LotCode IN
(
    'PDA-REL-LOT-001', 'PDA-REL-LOT-002', 'PDA-REL-LOT-003',
    '5011LL260701000001', '5011LL260715000002', '5011LL260801000003',
    '5011LL260820000010'
);

INSERT INTO dbo.WH_ReleaseSchedule
(
    WoID, ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status,
    CreatedBy, PickSlipNo, ReqLocation, ReqSeqNo, ReqUserId
)
VALUES
(
    NULL, @ItemNo, 3, 0, SYSDATETIME(), 2, 'Open',
    'pda-release-test', @PickSlipNo, N'LINE-A', 1, N'PDA TEST'
),
(
    NULL, @DirectItemNo, 1, 0, SYSDATETIME(), 2, 'Open',
    'pda-release-test', @PickSlipNo, N'LINE-A', 2, N'PDA TEST'
);

INSERT INTO dbo.tbl_Lot
(
    LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt,
    Status, InventoryStatus, QualityFlag, CurrentLocationID, CreatedBy
)
VALUES
('5011LL260701000001', @ItemNo, 'WH', 4, 4, '2026-07-01T08:00:00', 'Received', 'RECEIVED', 'PASS', 'B0-10-A1', 'pda-release-test'),
('5011LL260715000002', @ItemNo, 'WH', 4, 4, '2026-07-15T08:00:00', 'Received', 'RECEIVED', 'PASS', 'B0-10-B1', 'pda-release-test'),
('5011LL260801000003', @ItemNo, 'WH', 2, 2, '2026-08-01T08:00:00', 'Received', 'RECEIVED', 'PASS', 'B0-09-D2', 'pda-release-test'),
('5011LL260820000010', @DirectItemNo, 'WH', 24, 24, '2026-08-20T08:00:00', 'Received', 'RECEIVED', 'PASS', 'B0-10-A1', 'pda-release-test');

INSERT INTO dbo.WH_Inventory
(
    ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt,
    Status, CreatedBy
)
SELECT
    L.ItemNo,
    CASE L.LotCode
        WHEN '5011LL260701000001' THEN 'B0-10-A1'
        WHEN '5011LL260715000002' THEN 'B0-10-B1'
        WHEN '5011LL260801000003' THEN 'B0-09-D2'
        ELSE 'B0-10-A1'
    END,
    L.LotID,
    CASE L.LotCode
        WHEN '5011LL260701000001' THEN 4
        WHEN '5011LL260715000002' THEN 4
        WHEN '5011LL260801000003' THEN 2
        ELSE 24
    END,
    0,
    CASE L.LotCode
        WHEN '5011LL260701000001' THEN '2026-07-01T08:00:00'
        WHEN '5011LL260715000002' THEN '2026-07-15T08:00:00'
        WHEN '5011LL260801000003' THEN '2026-08-01T08:00:00'
        ELSE '2026-08-20T08:00:00'
    END,
    'Received',
    'pda-release-test'
FROM dbo.tbl_Lot L
WHERE L.LotCode IN
(
    '5011LL260701000001', '5011LL260715000002', '5011LL260801000003',
    '5011LL260820000010'
);

COMMIT TRANSACTION;

EXEC dbo.WH_PDA_RELEASE_SLIP_STATUS @PickSlipNo = @PickSlipNo;
EXEC dbo.WH_PDA_RELEASE_PICK_LINES @PickSlipNo = @PickSlipNo;
EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo = @PickSlipNo, @LotNo = N'5011LL260701000001';
EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo = @PickSlipNo, @LotNo = N'5011LL260715000002';
GO

-- =====================================================================
--  FG Six Screen Demo
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @SeedBy varchar(50) = 'pda-fg-six-demo';
DECLARE @Item1 varchar(20), @Item2 varchar(20), @Item3 varchar(20);
DECLARE @Loc1 varchar(20), @Loc2 varchar(20), @Loc3 varchar(20), @Loc4 varchar(20);

IF COL_LENGTH('dbo.FG_ShipmentOrder', 'OutgoingSlipNumber') IS NULL
    THROW 51000, 'Run PDA_SCHEMA.sql first.', 1;

;WITH Items AS
(
    SELECT ItemNo, ROW_NUMBER() OVER (ORDER BY ItemNo) AS RN
    FROM dbo.MD_Item
    WHERE ISNULL(ActiveFlag, 1) = 1
)
SELECT @Item1=MAX(CASE WHEN RN=1 THEN ItemNo END),
       @Item2=MAX(CASE WHEN RN=2 THEN ItemNo END),
       @Item3=MAX(CASE WHEN RN=3 THEN ItemNo END)
FROM Items WHERE RN <= 3;

;WITH Locations AS
(
    SELECT LocationID, ROW_NUMBER() OVER (ORDER BY LocationID) AS RN
    FROM dbo.MD_Location
    WHERE ISNULL(ActiveFlag, 1) = 1
)
SELECT @Loc1=MAX(CASE WHEN RN=1 THEN LocationID END),
       @Loc2=MAX(CASE WHEN RN=2 THEN LocationID END),
       @Loc3=MAX(CASE WHEN RN=3 THEN LocationID END),
       @Loc4=MAX(CASE WHEN RN=4 THEN LocationID END)
FROM Locations WHERE RN <= 4;

IF @Item3 IS NULL THROW 51000, 'At least three active MD_Item rows are required.', 1;
IF @Loc4 IS NULL THROW 51000, 'At least four active MD_Location rows are required.', 1;

BEGIN TRANSACTION;

-- Remove only this script's prior transactional demo rows.
DELETE FROM dbo.FG_LoadingConfirm WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.FG_PickingFifo WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.FG_CustomerReturn WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.FG_ShipmentOrderLine WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.FG_ShipmentOrder WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.FG_PutAway WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.FG_Inventory WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.QC_Inspection WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.tbl_Lot WHERE CreatedBy = @SeedBy;
DELETE FROM dbo.PP_WorkOrder WHERE CreatedBy = @SeedBy;

DECLARE @Demo TABLE
(
    Seq int PRIMARY KEY,
    WoNumber varchar(20),
    LotCode varchar(40),
    ItemNo varchar(20),
    Qty decimal(12,3),
    ProducedAt datetime2,
    LocationID varchar(20),
    InventoryStatus varchar(20)
);

INSERT INTO @Demo VALUES
 (1, 'FG-DEMO-WO-001', CONCAT('5011FG', CONVERT(char(6), DATEADD(hour,-2,SYSDATETIME()), 12), '000901'), @Item1, 32, DATEADD(hour,-2,SYSDATETIME()), NULL, NULL),
 (2, 'FG-DEMO-WO-002', CONCAT('5011FG', CONVERT(char(6), DATEADD(day,-2,SYSDATETIME()), 12), '000902'), @Item2, 24, DATEADD(day,-2,SYSDATETIME()), NULL, NULL),
 (3, 'FG-DEMO-WO-003', '5011FG260821000201', @Item1, 24, DATEADD(day,-10,SYSDATETIME()), @Loc1, 'Available'),
 (4, 'FG-DEMO-WO-004', '5011FG260822000202', @Item2, 16, DATEADD(day,-8,SYSDATETIME()),  @Loc2, 'Available'),
 (5, 'FG-DEMO-WO-005', '5011FG260823000203', @Item3, 20, DATEADD(day,-6,SYSDATETIME()),  @Loc3, 'Reserved'),
 (6, 'FG-DEMO-WO-006', '5011FG260824000204', @Item1, 12, DATEADD(day,-4,SYSDATETIME()),  @Loc4, 'Hold'),
 (7, 'FG-DEMO-WO-007', '5011FG260819000205', @Item1, 10, DATEADD(day,-12,SYSDATETIME()), @Loc1, 'Available'),
 (8, 'FG-DEMO-WO-008', '5011FG260820000206', @Item1, 14, DATEADD(day,-11,SYSDATETIME()), @Loc1, 'Available'),
 (9, 'FG-DEMO-WO-009', '5011FG260825000207', @Item2,  8, DATEADD(day,-5,SYSDATETIME()),  @Loc2, 'Reserved'),
 (10,'FG-DEMO-WO-010', '5011FG260826000208', @Item3,  6, DATEADD(day,-3,SYSDATETIME()),  @Loc3, 'Reserved');

INSERT INTO dbo.PP_WorkOrder
    (WoNumber, ItemNo, OrderQty, OpenQty, CompletedQty, LineID, PlannedStart, PlannedEnd,
     ActualStart, ActualEnd, DueDate, Status, Priority, CreatedBy, CreatedTS)
SELECT WoNumber, ItemNo, Qty, 0, Qty, 'FG-DEMO', DATEADD(day,-1,ProducedAt), ProducedAt,
       DATEADD(hour,-4,ProducedAt), ProducedAt, CAST(ProducedAt AS date), 'Completed', 3,
       @SeedBy, SYSDATETIME()
FROM @Demo;

INSERT INTO dbo.tbl_Lot
    (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty, ProducedAt,
     Status, QualityFlag, CurrentLocationID, ExpiryDate, CreatedBy, CreatedTS)
SELECT d.LotCode, d.ItemNo, w.WoID, 'FG-DEMO', 'FINAL', d.Qty, d.Qty, d.ProducedAt,
       'Completed', 'PASS', d.LocationID, DATEADD(year,1,CAST(d.ProducedAt AS date)),
       @SeedBy, SYSDATETIME()
FROM @Demo d
JOIN dbo.PP_WorkOrder w ON w.WoNumber = d.WoNumber AND w.CreatedBy = @SeedBy;

INSERT INTO dbo.QC_Inspection
    (InspectionNo, InspectionType, LotID, WoID, LineID, ItemNo, CustomerCode, Mode,
     SampleSize, BatchQty, CumulativeGood, DefectQtyTotal, Verdict, CriticalFlag,
     InspectorID, InsStartTS, InsEndTS, CreatedBy, CreatedTS)
SELECT CONCAT('FG-QC-DEMO-', RIGHT('000' + CAST(d.Seq AS varchar(3)),3)), 'FQC', l.LotID, w.WoID,
       'FG-DEMO', d.ItemNo, 'DEMO-CUSTOMER', 'Normal', 5, d.Qty, CONVERT(int,d.Qty), 0,
       'PASS', 0, 'admin', DATEADD(minute,-20,d.ProducedAt), DATEADD(minute,-5,d.ProducedAt),
       @SeedBy, SYSDATETIME()
FROM @Demo d
JOIN dbo.PP_WorkOrder w ON w.WoNumber = d.WoNumber AND w.CreatedBy = @SeedBy
JOIN dbo.tbl_Lot l ON l.LotCode = d.LotCode AND l.CreatedBy = @SeedBy;

INSERT INTO dbo.FG_Inventory
    (StockNumber, WoID, ItemNo, LotID, CustomerCode, Qty, Location, Status, HoldFlag,
     StockTS, CreatedBy, CreatedTS)
SELECT CONCAT('FG-DEMO-STK-', RIGHT('000' + CAST(d.Seq AS varchar(3)),3)), w.WoID, d.ItemNo,
       l.LotID, 'DEMO-CUSTOMER', d.Qty, d.LocationID, d.InventoryStatus,
       CASE WHEN d.InventoryStatus = 'Hold' THEN 1 ELSE 0 END,
       DATEADD(minute,30,d.ProducedAt), @SeedBy, SYSDATETIME()
FROM @Demo d
JOIN dbo.PP_WorkOrder w ON w.WoNumber = d.WoNumber AND w.CreatedBy = @SeedBy
JOIN dbo.tbl_Lot l ON l.LotCode = d.LotCode AND l.CreatedBy = @SeedBy
WHERE d.InventoryStatus IS NOT NULL;

INSERT INTO dbo.FG_ShipmentOrder
    (ShipOrderNumber, OutgoingSlipNumber, CustomerCode, CustomerPO, Source, ShipDate, CarrierCode, DestPlant,
     DestDock, ReceiverName, Status, PickslipID, OTDFlag, CreatedBy, CreatedTS)
VALUES
 ('FG-SO-DEMO-001', '2609020001', 'DEMO-CUSTOMER', 'PO-DEMO-001', 'PDA', DATEADD(day,1,CAST(GETDATE() AS date)), 'EOS-TRUCK', 'CUSTOMER-A', 'DOCK-A', 'Receiving A', 'Released', 'FG-PICK-DEMO-001', 'OnTime', @SeedBy, SYSDATETIME()),
 ('FG-SO-DEMO-002', '2609020002', 'DEMO-CUSTOMER', 'PO-DEMO-002', 'PDA', CAST(GETDATE() AS date),            'EOS-TRUCK', 'CUSTOMER-B', 'DOCK-B', 'Receiving B', 'Ready',    'FG-PICK-DEMO-002', 'OnTime', @SeedBy, SYSDATETIME()),
 ('FG-SO-DEMO-003', '2609020003', 'DEMO-CUSTOMER', 'PO-DEMO-003', 'PDA', DATEADD(day,2,CAST(GETDATE() AS date)), 'EOS-TRUCK', 'CUSTOMER-C', 'DOCK-C', 'Receiving C', 'Open',     'FG-PICK-DEMO-003', 'OnTime', @SeedBy, SYSDATETIME());

DECLARE @Order1 int = (SELECT ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber='FG-SO-DEMO-001' AND CreatedBy=@SeedBy);
DECLARE @Order2 int = (SELECT ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber='FG-SO-DEMO-002' AND CreatedBy=@SeedBy);
DECLARE @Order3 int = (SELECT ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber='FG-SO-DEMO-003' AND CreatedBy=@SeedBy);

INSERT INTO dbo.FG_ShipmentOrderLine
    (ShipmentOrderID, LineSeq, ItemNo, OrderedQty, AllocatedQty, StockID, LotID, Location,
     ReservationStatus, ReservedAt, CreatedBy, CreatedTS)
SELECT @Order1, 10, s.ItemNo, s.Qty, 0, NULL, NULL, s.Location, 'Open', NULL, @SeedBy, SYSDATETIME()
FROM dbo.FG_Inventory s WHERE s.StockNumber='FG-DEMO-STK-003' AND s.CreatedBy=@SeedBy
UNION ALL
SELECT @Order1, 20, s.ItemNo, s.Qty, 0, NULL, NULL, s.Location, 'Open', NULL, @SeedBy, SYSDATETIME()
FROM dbo.FG_Inventory s WHERE s.StockNumber='FG-DEMO-STK-004' AND s.CreatedBy=@SeedBy
UNION ALL
SELECT @Order2, 10, s.ItemNo, s.Qty, s.Qty, s.StockID, s.LotID, s.Location, 'Picked', SYSDATETIME(), @SeedBy, SYSDATETIME()
FROM dbo.FG_Inventory s WHERE s.StockNumber='FG-DEMO-STK-005' AND s.CreatedBy=@SeedBy
UNION ALL
SELECT @Order2, 20, s.ItemNo, s.Qty, s.Qty, s.StockID, s.LotID, s.Location, 'Picked', SYSDATETIME(), @SeedBy, SYSDATETIME()
FROM dbo.FG_Inventory s WHERE s.StockNumber='FG-DEMO-STK-009' AND s.CreatedBy=@SeedBy
UNION ALL
SELECT @Order2, 30, s.ItemNo, s.Qty, s.Qty, s.StockID, s.LotID, s.Location, 'Picked', SYSDATETIME(), @SeedBy, SYSDATETIME()
FROM dbo.FG_Inventory s WHERE s.StockNumber='FG-DEMO-STK-010' AND s.CreatedBy=@SeedBy
UNION ALL
SELECT @Order3, 10, @Item1, 12, 0, NULL, NULL, @Loc4, 'Open', NULL, @SeedBy, SYSDATETIME();

INSERT INTO dbo.FG_PickingFifo
    (PickNumber, PickslipID, ShipmentOrderID, PickerID, StartTS, EndTS, FifoViolations,
     OverrideCount, PickedQty, OrderedQty, Status, CreatedBy, CreatedTS)
VALUES
 ('FG-PICK-DEMO-002', 'FG-PICK-DEMO-002', @Order2, 'admin', DATEADD(minute,-30,SYSDATETIME()),
  DATEADD(minute,-20,SYSDATETIME()), 0, 0, 34, 34, 'Picked', @SeedBy, SYSDATETIME());

INSERT INTO dbo.FG_CustomerReturn
    (ReturnNumber, RMANo, CustomerCode, OriginalShipmentOrderID, ReturnReason, ItemsJSON,
     Status, ReceivedAt, ReceivedBy, CapaTriggered, CreatedBy, CreatedTS)
VALUES
 ('FG-RMA-DEMO-001', 'RMA-DEMO-001', 'DEMO-CUSTOMER', @Order2, 'Damaged in transit',
  CONCAT('[{"itemNo":"', @Item3, '","qty":2}]'), 'Open', DATEADD(hour,-3,SYSDATETIME()),
  'admin', 0, @SeedBy, SYSDATETIME()),
 ('FG-RMA-DEMO-002', 'RMA-DEMO-002', 'DEMO-CUSTOMER', @Order1, 'Wrong item',
  CONCAT('[{"itemNo":"', @Item2, '","qty":1}]'), 'Inspecting', DATEADD(day,-1,SYSDATETIME()),
  'admin', 0, @SeedBy, SYSDATETIME());

COMMIT TRANSACTION;

SELECT 'FG Waiting / Put-Away LOT' AS DemoType, CONCAT('FGLOT:', LotCode) AS ScanValue
FROM @Demo WHERE InventoryStatus IS NULL
UNION ALL SELECT 'FG Inventory LOT', LotCode FROM @Demo WHERE InventoryStatus IS NOT NULL
UNION ALL SELECT 'FG Release Outgoing Slip', '2609020001'
UNION ALL SELECT 'FG Loading Truck', 'TRUCK:GA-260902-01'
UNION ALL SELECT 'FG Loading Shipment Order', 'FG-SO-DEMO-002'
UNION ALL SELECT 'FG Loading Stock', 'FG-DEMO-STK-005'
UNION ALL SELECT 'FG Loading Stock', 'FG-DEMO-STK-009'
UNION ALL SELECT 'FG Loading Stock', 'FG-DEMO-STK-010'
UNION ALL SELECT 'FG Put-Away Location', @Loc1;
GO

-- =====================================================================
--  FG QC Waiting Demo
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @SeedBy varchar(50) = 'pda-fg-qc-waiting-demo';
DECLARE @Now datetime2 = SYSDATETIME();
DECLARE @Samples TABLE
(
    Seq int PRIMARY KEY,
    LotCode varchar(40),
    ItemNo varchar(20),
    Qty decimal(12,3),
    AgeHours int
);

-- Age margins keep all four color bands visible across local/UTC clock differences.
INSERT INTO @Samples VALUES
    (1, '5011FG260831000101', '81710-PI000NNB', 32, 12),
    (2, '5011FG260831000102', '81710-PI000YGN', 24, 54),
    (3, '5011FG260831000103', '81710-PI010NNB', 48, 102),
    (4, '5011FG260831000104', '81710-PI010YGN', 36, 150),
    (5, '5011FG260831000105', '82301-PI000NNB', 40, 198),
    (6, '5011FG260831000106', '82301-PI000YGU', 28, 270),
    (7, '5011FG260831000107', '81711-PI000NNB', 60, 318),
    (8, '5011FG260831000108', '81711-PI000YGN', 56, 366);

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE l SET LotCode = CONCAT('5011FG', CONVERT(char(6), l.ProducedAt, 12),
        CASE l.LotCode WHEN 'FG-DEMO-WAIT-001' THEN '000901' ELSE '000902' END)
    FROM dbo.tbl_Lot l
    WHERE l.CreatedBy = 'pda-fg-six-demo'
      AND l.LotCode IN ('FG-DEMO-WAIT-001', 'FG-DEMO-WAIT-002')
      AND l.ProducedAt IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.FG_Inventory f WHERE f.LotID = l.LotID)
      AND NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot other WHERE other.LotCode =
          CONCAT('5011FG', CONVERT(char(6), l.ProducedAt, 12),
              CASE l.LotCode WHEN 'FG-DEMO-WAIT-001' THEN '000901' ELSE '000902' END));

    IF EXISTS
    (
        SELECT 1 FROM @Samples s
        WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Item i WHERE i.ItemNo = s.ItemNo)
    ) THROW 51000, 'Required existing part masters are missing. No masters will be created.', 1;

    IF EXISTS
    (
        SELECT 1 FROM @Samples s
        JOIN dbo.tbl_Lot l ON l.LotCode = s.LotCode
        WHERE l.CreatedBy <> @SeedBy
    ) THROW 51000, 'A sample LOT number is already owned by other data.', 1;

    IF EXISTS
    (
        SELECT 1 FROM @Samples s
        JOIN dbo.QC_Inspection q ON q.InspectionNo = CONCAT('FGWAIT-QC-', s.Seq)
        WHERE q.CreatedBy <> @SeedBy
    ) THROW 51000, 'A sample inspection number is already owned by other data.', 1;

    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt,
         Status, QualityFlag, InventoryStatus, ExpiryDate, CreatedBy, CreatedTS)
    SELECT s.LotCode, s.ItemNo, 'FINAL', s.Qty, s.Qty,
           DATEADD(hour, -s.AgeHours - 2, @Now), 'Completed', 'PASS', 'QC_PASS',
           DATEADD(year, 1, CAST(DATEADD(hour, -s.AgeHours - 2, @Now) AS date)), @SeedBy, @Now
    FROM @Samples s
    WHERE NOT EXISTS
        (SELECT 1 FROM dbo.tbl_Lot l WITH (UPDLOCK, HOLDLOCK) WHERE l.LotCode = s.LotCode);

    INSERT INTO dbo.QC_Inspection
        (InspectionNo, InspectionType, LotID, ItemNo, Mode, SampleSize,
         BatchQty, CumulativeGood, DefectQtyTotal, Verdict, CriticalFlag,
         InspectorID, InsStartTS, InsEndTS, CreatedBy, CreatedTS)
    SELECT CONCAT('FGWAIT-QC-', s.Seq), 'FQC', l.LotID, s.ItemNo, 'Normal', 5,
           s.Qty, CONVERT(int, s.Qty), 0, 'PASS', 0, 'admin',
           DATEADD(hour, 1, l.ProducedAt), DATEADD(hour, 2, l.ProducedAt), @SeedBy, @Now
    FROM @Samples s
    JOIN dbo.tbl_Lot l ON l.LotCode = s.LotCode AND l.CreatedBy = @SeedBy
    WHERE NOT EXISTS
        (SELECT 1 FROM dbo.QC_Inspection q WITH (UPDLOCK, HOLDLOCK)
         WHERE q.InspectionNo = CONCAT('FGWAIT-QC-', s.Seq))
      AND NOT EXISTS (SELECT 1 FROM dbo.QC_Inspection q WHERE q.LotID = l.LotID)
      AND NOT EXISTS (SELECT 1 FROM dbo.FG_Inventory f WHERE f.LotID = l.LotID);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT l.LotCode, l.ItemNo, i.ItemName, l.BatchSize AS Qty, i.DefaultUOM AS Unit,
       q.InsEndTS AS QcPassedAt
FROM dbo.tbl_Lot l
JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
JOIN dbo.QC_Inspection q ON q.LotID = l.LotID AND q.CreatedBy = @SeedBy
WHERE l.CreatedBy = @SeedBy
ORDER BY q.InsEndTS, l.LotID;
GO

-- =====================================================================
--  FG Customer Return Demo
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.FG_Inventory WHERE StockNumber = 'FG-DEMO-RETURN-002')
BEGIN
    INSERT INTO dbo.FG_Inventory
        (StockNumber, WoID, ItemNo, CustomerCode, Qty, Location, Status,
         HoldFlag, StockTS, CreatedBy, CreatedTS)
    VALUES
        ('FG-DEMO-RETURN-002', 3, '81710-PI000NNB', 'DEMO-CUSTOMER', 1,
         'REL010101', 'Shipped', 0, DATEADD(day, -1, SYSDATETIME()),
         'pda-return-test', SYSDATETIME());
END;

IF NOT EXISTS (SELECT 1 FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber = 'FG-SO-RETURN-002')
BEGIN
    INSERT INTO dbo.FG_ShipmentOrder
        (ShipOrderNumber, CustomerCode, CustomerPO, Source, ShipDate,
         CarrierCode, DestPlant, DestDock, Status, PickslipID, OTDFlag,
         ConfirmedBy, ConfirmedAt, OutgoingSlipNumber, CreatedBy, CreatedTS)
    VALUES
        ('FG-SO-RETURN-002', 'DEMO-CUSTOMER', 'PO-RETURN-002', 'PDA',
         DATEADD(day, -1, CAST(SYSDATETIME() AS date)), 'EOS-TRUCK',
         'DEMO-CUSTOMER', 'RETURN', 'Shipped', 'FG-PICK-RETURN-002', 'OnTime',
         'admin', DATEADD(day, -1, SYSDATETIME()), 'FG-SO-RETURN-002',
         'pda-return-test', SYSDATETIME());
END;

DECLARE @StockID int =
    (SELECT TOP (1) StockID FROM dbo.FG_Inventory WHERE StockNumber = 'FG-DEMO-RETURN-002' ORDER BY StockID DESC);
DECLARE @ShipmentOrderID int =
    (SELECT TOP (1) ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber = 'FG-SO-RETURN-002' ORDER BY ShipmentOrderID DESC);

IF NOT EXISTS
(
    SELECT 1 FROM dbo.FG_ShipmentOrderLine
    WHERE ShipmentOrderID = @ShipmentOrderID AND StockID = @StockID
)
BEGIN
    INSERT INTO dbo.FG_ShipmentOrderLine
        (ShipmentOrderID, LineSeq, ItemNo, OrderedQty, AllocatedQty,
         StockID, Location, ReservationStatus, ReservedAt, ReleasedAt,
         CreatedBy, CreatedTS)
    VALUES
        (@ShipmentOrderID, 10, '81710-PI000NNB', 1, 1,
         @StockID, 'REL010101', 'Loaded', DATEADD(day, -1, SYSDATETIME()),
         DATEADD(day, -1, SYSDATETIME()), 'pda-return-test', SYSDATETIME());
END;

IF NOT EXISTS
(
    SELECT 1 FROM dbo.FG_LoadingConfirm
    WHERE ShipmentOrderID = @ShipmentOrderID AND DepartureTS IS NOT NULL
)
BEGIN
    INSERT INTO dbo.FG_LoadingConfirm
        (LoadingNumber, ShipmentOrderID, LicensePlate, CarrierCode, DockNo,
         ArrivalTS, DepartureTS, OTDStatus, OperatorID, ConfirmedAt,
         CreatedBy, CreatedTS)
    VALUES
        ('FG-LOAD-RETURN-002', @ShipmentOrderID, 'GA-EOS-RT2', 'EOS-TRUCK', 'D01',
         DATEADD(minute, -90, SYSDATETIME()), DATEADD(minute, -60, SYSDATETIME()),
         'OnTime', 'admin', DATEADD(minute, -90, SYSDATETIME()),
         'pda-return-test', SYSDATETIME());
END;

COMMIT TRANSACTION;

SELECT S.StockNumber AS Barcode, O.ShipOrderNumber, L.ItemNo, C.DepartureTS
FROM dbo.FG_Inventory S
JOIN dbo.FG_ShipmentOrderLine L ON L.StockID = S.StockID
JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID = L.ShipmentOrderID
JOIN dbo.FG_LoadingConfirm C ON C.ShipmentOrderID = O.ShipmentOrderID
WHERE S.StockNumber = 'FG-DEMO-RETURN-002' AND C.DepartureTS IS NOT NULL;
GO


-- =====================================================================
--  WH Inventory Threshold Demo
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.MD_Item
       SET MinStock = CASE ItemNo
            WHEN 'MAT-001' THEN 130
            WHEN 'MAT-002' THEN 90
            WHEN 'MAT-003' THEN 40
            WHEN 'INB-MAT-002' THEN 5
            ELSE MinStock
           END,
           MaxStock = CASE ItemNo
            WHEN 'MAT-001' THEN 220
            WHEN 'MAT-002' THEN 180
            WHEN 'MAT-003' THEN 50
            WHEN 'INB-MAT-002' THEN 40
            ELSE MaxStock
           END,
           ModifiedBy = N'pda-seed',
           ModifiedTS = SYSDATETIME()
     WHERE ItemNo IN ('MAT-001', 'MAT-002', 'MAT-003', 'INB-MAT-002');
END;
GO

-- =====================================================================
--  WH Legacy Location Menu Cleanup
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.SYS_RolePermission', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.SYS_RolePermission
    WHERE ScreenCode = 'WH-001';
END;

IF OBJECT_ID(N'dbo.SYS_Screen', N'U') IS NOT NULL
BEGIN
    DECLARE @DeleteWh001Sql nvarchar(max) = N'
        DELETE FROM dbo.SYS_Screen
        WHERE ScreenCode = N''WH-001''
          AND ModuleCode = N''WEB''';
    IF COL_LENGTH(N'dbo.SYS_Screen', N'ProcessCode') IS NOT NULL
        SET @DeleteWh001Sql += N' AND ProcessCode = N''WH''';
    EXEC sys.sp_executesql @DeleteWh001Sql;
END;
GO

-- =====================================================================
--  FG Web Shipment History Demo
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SeedBy varchar(50) = 'fg-web-demo';

IF EXISTS
(
    SELECT 1
    FROM dbo.FG_ShipmentOrder
    WHERE ShipOrderNumber IN ('FG-SO-WEB-001', 'FG-SO-WEB-002')
      AND CreatedBy <> @SeedBy
)
    THROW 51000, 'A FG web shipment demo number is owned by other data.', 1;

DELETE FROM dbo.FG_DeliveryNote
WHERE DnNumber = 'FG-DN-DEMO-001' AND CreatedBy = @SeedBy;

DELETE FROM dbo.FG_LoadingConfirm
WHERE LoadingNumber IN ('FG-LOAD-DEMO-001', 'FG-LOAD-DEMO-002') AND CreatedBy = @SeedBy;

DELETE L
FROM dbo.FG_ShipmentOrderLine L
JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID = L.ShipmentOrderID
WHERE O.CreatedBy = @SeedBy
  AND O.ShipOrderNumber IN ('FG-SO-WEB-001', 'FG-SO-WEB-002');

DELETE FROM dbo.FG_ShipmentOrder
WHERE CreatedBy = @SeedBy
  AND ShipOrderNumber IN ('FG-SO-WEB-001', 'FG-SO-WEB-002');

INSERT INTO dbo.FG_ShipmentOrder
    (ShipOrderNumber, OutgoingSlipNumber, CustomerCode, CustomerPO, Source, ShipDate,
     CarrierCode, DestPlant, DestDock, ReceiverName, Status, PickslipID, OTDFlag,
     ConfirmedBy, ConfirmedAt, CreatedBy, CreatedTS)
VALUES
    ('FG-SO-WEB-001', 'WEB2608110001', 'DEMO-CUSTOMER', 'PO-WEB-001', 'WEB', '2026-08-11',
     'EOS-TRUCK', 'CUSTOMER-A', 'DOCK-A', 'Receiving A', 'Loaded', 'FG-PICK-WEB-001', 'OnTime',
     'admin@ames.local', '2026-08-11T08:00:00', @SeedBy, '2026-08-11T08:00:00'),
    ('FG-SO-WEB-002', 'WEB2608110002', 'DEMO-CUSTOMER', 'PO-WEB-002', 'WEB', '2026-08-11',
     'EOS-TRUCK', 'CUSTOMER-B', 'DOCK-B', 'Receiving B', 'Shipped', 'FG-PICK-WEB-002', 'OnTime',
     'admin@ames.local', '2026-08-11T08:35:00', @SeedBy, '2026-08-11T08:35:00');

DECLARE @LoadedOrderID int = (SELECT ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber = 'FG-SO-WEB-001' AND CreatedBy = @SeedBy);
DECLARE @ShippedOrderID int = (SELECT ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber = 'FG-SO-WEB-002' AND CreatedBy = @SeedBy);

INSERT INTO dbo.FG_LoadingConfirm
    (LoadingNumber, ShipmentOrderID, LicensePlate, CarrierCode, DriverID, DriverName,
     DockNo, ArrivalTS, DepartureTS, SealNo, OTDStatus, OperatorID, ConfirmedAt, CreatedBy, CreatedTS)
VALUES
    ('FG-LOAD-DEMO-001', @LoadedOrderID, 'GA-EOS-2601', 'EOS-TRUCK', 'DRV-001', 'Alex Morgan',
     'D01', '2026-08-11T07:40:00', NULL, 'SEAL-260811-A', 'OnTime', 'admin@ames.local',
     '2026-08-11T08:00:00', @SeedBy, '2026-08-11T08:00:00'),
    ('FG-LOAD-DEMO-002', @ShippedOrderID, 'GA-EOS-2602', 'EOS-TRUCK', 'DRV-002', 'Jordan Lee',
     'D02', '2026-08-11T08:10:00', '2026-08-11T08:35:00', 'SEAL-260811-B', 'OnTime',
     'admin@ames.local', '2026-08-11T08:30:00', @SeedBy, '2026-08-11T08:30:00');

DECLARE @ShippedLoadingID int = (SELECT TOP 1 LoadingID FROM dbo.FG_LoadingConfirm WHERE LoadingNumber = 'FG-LOAD-DEMO-002');

IF NOT EXISTS (SELECT 1 FROM dbo.FG_DeliveryNote WHERE DnNumber = 'FG-DN-DEMO-001')
BEGIN
    INSERT INTO dbo.FG_DeliveryNote
        (DnNumber, ShipmentOrderID, LoadingID, CustomerCode, FormatTemplate, Revision,
         IssuedAt, IssuedBy, EdiStatus, CreatedBy, CreatedTS)
    SELECT
        'FG-DN-DEMO-001', O.ShipmentOrderID, @ShippedLoadingID, O.CustomerCode, 'STANDARD', 1,
        '2026-08-11T08:36:00', 'admin@ames.local', 'Sent', @SeedBy, '2026-08-11T08:36:00'
    FROM dbo.FG_ShipmentOrder O
    WHERE O.ShipmentOrderID = @ShippedOrderID;
END;

SELECT O.ShipOrderNumber, O.Status, L.LoadingNumber, L.LicensePlate, L.DriverName,
       L.DockNo, L.ConfirmedAt, L.DepartureTS, D.DnNumber
FROM dbo.FG_ShipmentOrder O
JOIN dbo.FG_LoadingConfirm L ON L.ShipmentOrderID = O.ShipmentOrderID
LEFT JOIN dbo.FG_DeliveryNote D ON D.LoadingID = L.LoadingID
WHERE L.LoadingNumber IN ('FG-LOAD-DEMO-001', 'FG-LOAD-DEMO-002')
ORDER BY L.LoadingNumber;
GO
