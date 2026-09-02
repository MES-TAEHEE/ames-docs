/*
  Web FG shipment demo data
  -------------------------
  Creates one truck-loaded order and one shipped order with a delivery note.
  Safe to run repeatedly.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SeedBy varchar(50) = 'fg-web-demo';
DECLARE @LoadedOrderID int = (SELECT TOP 1 ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber = 'FG-SO-DEMO-001');
DECLARE @ShippedOrderID int = (SELECT TOP 1 ShipmentOrderID FROM dbo.FG_ShipmentOrder WHERE ShipOrderNumber = 'FG-SO-DEMO-002');

IF @LoadedOrderID IS NULL OR @ShippedOrderID IS NULL
    THROW 51000, 'Run the FG six-screen demo seed first. FG-SO-DEMO-001/002 are required.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.FG_LoadingConfirm WHERE LoadingNumber = 'FG-LOAD-DEMO-001')
BEGIN
    INSERT INTO dbo.FG_LoadingConfirm
        (LoadingNumber, ShipmentOrderID, LicensePlate, CarrierCode, DriverID, DriverName,
         DockNo, ArrivalTS, SealNo, OTDStatus, OperatorID, ConfirmedAt, CreatedBy, CreatedTS)
    VALUES
        ('FG-LOAD-DEMO-001', @LoadedOrderID, 'GA-EOS-2601', 'EOS-TRUCK', 'DRV-001', 'Alex Morgan',
         'D01', '2026-08-11T07:40:00', 'SEAL-260811-A', 'OnTime', 'admin@ames.local',
         '2026-08-11T08:00:00', @SeedBy, '2026-08-11T08:00:00');
END;

UPDATE dbo.FG_ShipmentOrder
SET Status = 'Loaded', ConfirmedBy = 'admin@ames.local', ConfirmedAt = '2026-08-11T08:00:00',
    ModifiedBy = 'admin@ames.local', ModifiedTS = '2026-08-11T08:00:00'
WHERE ShipmentOrderID = @LoadedOrderID;

IF NOT EXISTS (SELECT 1 FROM dbo.FG_LoadingConfirm WHERE LoadingNumber = 'FG-LOAD-DEMO-002')
BEGIN
    INSERT INTO dbo.FG_LoadingConfirm
        (LoadingNumber, ShipmentOrderID, LicensePlate, CarrierCode, DriverID, DriverName,
         DockNo, ArrivalTS, DepartureTS, SealNo, OTDStatus, OperatorID, ConfirmedAt, CreatedBy, CreatedTS)
    VALUES
        ('FG-LOAD-DEMO-002', @ShippedOrderID, 'GA-EOS-2602', 'EOS-TRUCK', 'DRV-002', 'Jordan Lee',
         'D02', '2026-08-11T08:10:00', '2026-08-11T08:35:00', 'SEAL-260811-B', 'OnTime',
         'admin@ames.local', '2026-08-11T08:30:00', @SeedBy, '2026-08-11T08:30:00');
END;

UPDATE dbo.FG_ShipmentOrder
SET Status = 'Shipped', ConfirmedBy = 'admin@ames.local', ConfirmedAt = '2026-08-11T08:35:00',
    ModifiedBy = 'admin@ames.local', ModifiedTS = '2026-08-11T08:35:00'
WHERE ShipmentOrderID = @ShippedOrderID;

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
