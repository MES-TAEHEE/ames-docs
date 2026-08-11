-- =====================================================================
-- PDA Finished Goods six-screen demo data
-- Uses existing MD_Item and MD_Location masters without modifying them.
-- Screens: Waiting, Put-Away, Inventory, Release Picking, Loading, Return
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

;WITH Items AS
(
    SELECT ItemNo, ROW_NUMBER() OVER (ORDER BY ItemNo) AS RN
    FROM dbo.MD_Item
    WHERE ISNULL(ActiveFlag, 1) = 1
)
SELECT
    @Item1 = MAX(CASE WHEN RN = 1 THEN ItemNo END),
    @Item2 = MAX(CASE WHEN RN = 2 THEN ItemNo END),
    @Item3 = MAX(CASE WHEN RN = 3 THEN ItemNo END)
FROM Items WHERE RN <= 3;

;WITH Locations AS
(
    SELECT LocationID, ROW_NUMBER() OVER (ORDER BY LocationID) AS RN
    FROM dbo.MD_Location
    WHERE ISNULL(ActiveFlag, 1) = 1
)
SELECT
    @Loc1 = MAX(CASE WHEN RN = 1 THEN LocationID END),
    @Loc2 = MAX(CASE WHEN RN = 2 THEN LocationID END),
    @Loc3 = MAX(CASE WHEN RN = 3 THEN LocationID END),
    @Loc4 = MAX(CASE WHEN RN = 4 THEN LocationID END)
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
 (1, 'FG-DEMO-WO-001', 'FG-DEMO-WAIT-001', @Item1, 32, DATEADD(hour,-2,SYSDATETIME()),  NULL,  NULL),
 (2, 'FG-DEMO-WO-002', 'FG-DEMO-WAIT-002', @Item2, 24, DATEADD(day,-2,SYSDATETIME()),   NULL,  NULL),
 (3, 'FG-DEMO-WO-003', 'FG-DEMO-INV-001',  @Item1, 24, DATEADD(day,-10,SYSDATETIME()), @Loc1, 'Available'),
 (4, 'FG-DEMO-WO-004', 'FG-DEMO-INV-002',  @Item2, 16, DATEADD(day,-8,SYSDATETIME()),  @Loc2, 'Available'),
 (5, 'FG-DEMO-WO-005', 'FG-DEMO-INV-003',  @Item3, 20, DATEADD(day,-6,SYSDATETIME()),  @Loc3, 'Reserved'),
 (6, 'FG-DEMO-WO-006', 'FG-DEMO-INV-004',  @Item1, 12, DATEADD(day,-4,SYSDATETIME()),  @Loc4, 'Hold');

INSERT INTO dbo.PP_WorkOrder
    (WoNumber, ItemNo, OrderQty, OpenQty, CompletedQty, LineID, PlannedStart, PlannedEnd,
     ActualStart, ActualEnd, DueDate, Status, Priority, CreatedBy, CreatedTS)
SELECT WoNumber, ItemNo, Qty, 0, Qty, 'FG-DEMO', DATEADD(day,-1,ProducedAt), ProducedAt,
       DATEADD(hour,-4,ProducedAt), ProducedAt, CAST(ProducedAt AS date), 'Completed', 3,
       @SeedBy, SYSDATETIME()
FROM @Demo;

INSERT INTO dbo.tbl_Lot
    (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty, ProducedAt,
     Status, QualityFlag, CurrentLocationID, ExpiryDate, InventoryStatus, CreatedBy, CreatedTS)
SELECT d.LotCode, d.ItemNo, w.WoID, 'FG-DEMO', 'FINAL', d.Qty, d.Qty, d.ProducedAt,
       'Completed', 'PASS', d.LocationID, DATEADD(year,1,CAST(d.ProducedAt AS date)),
       COALESCE(d.InventoryStatus, 'QC_PASS'), @SeedBy, SYSDATETIME()
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
    (ShipOrderNumber, CustomerCode, CustomerPO, Source, ShipDate, CarrierCode, DestPlant,
     DestDock, ReceiverName, Status, PickslipID, OTDFlag, CreatedBy, CreatedTS)
VALUES
 ('FG-SO-DEMO-001', 'DEMO-CUSTOMER', 'PO-DEMO-001', 'PDA', DATEADD(day,1,CAST(GETDATE() AS date)), 'EOS-TRUCK', 'CUSTOMER-A', 'DOCK-A', 'Receiving A', 'Released', 'FG-PICK-DEMO-001', 'OnTime', @SeedBy, SYSDATETIME()),
 ('FG-SO-DEMO-002', 'DEMO-CUSTOMER', 'PO-DEMO-002', 'PDA', CAST(GETDATE() AS date),            'EOS-TRUCK', 'CUSTOMER-B', 'DOCK-B', 'Receiving B', 'Ready',    'FG-PICK-DEMO-002', 'OnTime', @SeedBy, SYSDATETIME()),
 ('FG-SO-DEMO-003', 'DEMO-CUSTOMER', 'PO-DEMO-003', 'PDA', DATEADD(day,2,CAST(GETDATE() AS date)), 'EOS-TRUCK', 'CUSTOMER-C', 'DOCK-C', 'Receiving C', 'Open',     'FG-PICK-DEMO-003', 'OnTime', @SeedBy, SYSDATETIME());

DECLARE @Order1 int = (SELECT ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber='FG-SO-DEMO-001' AND CreatedBy=@SeedBy);
DECLARE @Order2 int = (SELECT ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber='FG-SO-DEMO-002' AND CreatedBy=@SeedBy);
DECLARE @Order3 int = (SELECT ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber='FG-SO-DEMO-003' AND CreatedBy=@SeedBy);

INSERT INTO dbo.FG_ShipmentOrderLine
    (ShipmentOrderID, LineSeq, ItemNo, OrderedQty, AllocatedQty, StockID, LotID, Location,
     ReservationStatus, ReservedAt, CreatedBy, CreatedTS)
SELECT @Order1, 10, s.ItemNo, s.Qty, 0, s.StockID, s.LotID, s.Location, 'Open', NULL, @SeedBy, SYSDATETIME()
FROM dbo.FG_Inventory s WHERE s.StockNumber='FG-DEMO-STK-003' AND s.CreatedBy=@SeedBy
UNION ALL
SELECT @Order1, 20, s.ItemNo, s.Qty, 0, s.StockID, s.LotID, s.Location, 'Open', NULL, @SeedBy, SYSDATETIME()
FROM dbo.FG_Inventory s WHERE s.StockNumber='FG-DEMO-STK-004' AND s.CreatedBy=@SeedBy
UNION ALL
SELECT @Order2, 10, s.ItemNo, s.Qty, s.Qty, s.StockID, s.LotID, s.Location, 'Picked', SYSDATETIME(), @SeedBy, SYSDATETIME()
FROM dbo.FG_Inventory s WHERE s.StockNumber='FG-DEMO-STK-005' AND s.CreatedBy=@SeedBy
UNION ALL
SELECT @Order3, 10, @Item1, 12, 0, NULL, NULL, @Loc4, 'Open', NULL, @SeedBy, SYSDATETIME();

INSERT INTO dbo.FG_PickingFifo
    (PickNumber, PickslipID, ShipmentOrderID, PickerID, StartTS, EndTS, FifoViolations,
     OverrideCount, PickedQty, OrderedQty, Status, CreatedBy, CreatedTS)
VALUES
 ('FG-PICK-DEMO-002', 'FG-PICK-DEMO-002', @Order2, 'admin', DATEADD(minute,-30,SYSDATETIME()),
  DATEADD(minute,-20,SYSDATETIME()), 0, 0, 20, 20, 'Picked', @SeedBy, SYSDATETIME());

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
UNION ALL SELECT 'FG Release Shipment Order', 'FG-SO-DEMO-001'
UNION ALL SELECT 'FG Loading Shipment Order', 'FG-SO-DEMO-002'
UNION ALL SELECT 'FG Put-Away Location', @Loc1;
