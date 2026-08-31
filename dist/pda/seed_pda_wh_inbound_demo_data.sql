-- =====================================================================
--  seed_pda_wh_inbound_demo_data.sql
--  PDA Warehouse inbound demo data using production-like identifiers
--
--  Apply:
--    sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\pda\seed_pda_wh_inbound_demo_data.sql
--  Development/demo only. Set SQLCMDPASSWORD outside source control.
--  Re-running can update the sample PO, LOT and receiving records below.
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
IF OBJECT_ID(N'dbo.WH_InboundDocument', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_InboundPackage', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.WH_InboundDocument WHERE DocumentBarcode = '5011202608280001')
        INSERT INTO dbo.WH_InboundDocument
            (ReceiveType, DocumentBarcode, DocumentNo, VendorID, DeliveryDate, ArrivalDate, Status, CreatedBy)
        VALUES
            (N'LOCAL', '5011202608280001', '5011202608280001', 'V1007',
             CONVERT(date, DATEADD(day, 1, GETDATE())), CONVERT(date, GETDATE()), N'Open', 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_InboundDocument WHERE DocumentBarcode = 'CKD202608280001CASE00001')
        INSERT INTO dbo.WH_InboundDocument
            (ReceiveType, DocumentBarcode, DocumentNo, VendorID, CaseNo, InvoiceNo, ContainerNo,
             ShipDate, PackDate, DeliveryDate, ArrivalDate, Status, CreatedBy)
        VALUES
            (N'CKD', 'CKD202608280001CASE00001', 'CKD-DN-260828-01', 'V2003', 'CASE-260828-001',
             'INV-260828-0031', 'SEGU2608281', CONVERT(date, DATEADD(day, -7, GETDATE())),
             CONVERT(date, DATEADD(day, -8, GETDATE())), CONVERT(date, DATEADD(day, 2, GETDATE())),
             CONVERT(date, GETDATE()), N'Open', 'pda-seed');

    DECLARE @LocalDocumentID int =
        (SELECT InboundDocumentID FROM dbo.WH_InboundDocument WHERE DocumentBarcode = '5011202608280001');
    DECLARE @CkdDocumentID int =
        (SELECT InboundDocumentID FROM dbo.WH_InboundDocument WHERE DocumentBarcode = 'CKD202608280001CASE00001');
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
        (InboundDocumentID, BoxBarcode, LotID, ItemNo, PoID, Qty, UnitCode, ProductionDate, Status, CreatedBy)
    SELECT @LocalDocumentID, V.BoxBarcode, L.LotID, V.ItemNo, V.PoID, V.Qty, 'EA', CONVERT(date, L.ProducedAt), N'Open', 'pda-seed'
    FROM (VALUES
        ('5011LL260828000001', '81710-PI000NNB', @LocalPo1, CONVERT(decimal(14,3), 180)),
        ('5011LL260828000002', '81710-PI000NNB', @LocalPo1, CONVERT(decimal(14,3), 180)),
        ('5011LL260828000003', '81711-PI000YGN', @LocalPo2, CONVERT(decimal(14,3), 144))
    ) V(BoxBarcode, ItemNo, PoID, Qty)
    JOIN dbo.tbl_Lot L ON L.LotCode = V.BoxBarcode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.WH_InboundPackage P WHERE P.BoxBarcode = V.BoxBarcode);

    INSERT INTO dbo.WH_InboundPackage
        (InboundDocumentID, BoxBarcode, LotID, ItemNo, PoID, Qty, UnitCode, ProductionDate, Status, CreatedBy)
    SELECT @CkdDocumentID, V.BoxBarcode, L.LotID, V.ItemNo, @CkdPo, 300, 'EA', CONVERT(date, L.ProducedAt), N'Open', 'pda-seed'
    FROM (VALUES
        ('CKD260828000000001', '82301-PI000NNB'),
        ('CKD260828000000002', '82301-PI000NNB'),
        ('CKD260828000000003', '82301-PI000NNB')
    ) V(BoxBarcode, ItemNo)
    JOIN dbo.tbl_Lot L ON L.LotCode = V.BoxBarcode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.WH_InboundPackage P WHERE P.BoxBarcode = V.BoxBarcode);
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
SELECT 'WH_Inventory', COUNT(*) FROM dbo.WH_Inventory
UNION ALL
SELECT 'WH_Receiving', COUNT(*) FROM dbo.WH_Receiving;
