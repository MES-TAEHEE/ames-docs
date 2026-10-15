-- Read-only verification of actual shipments and return eligibility. No seed/reset/receive.
SET NOCOUNT ON;
SELECT TOP(20) S.ShipmentID,S.OutboundBarcode,S.CustomerID,S.ShippedAt,T.LotNo,T.PartNo,-T.QtyChange ShippedQty
FROM dbo.FG_Shipment S JOIN dbo.WH_InventoryTransaction T
    ON T.SourceType='FG_OUTBOUND' AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
ORDER BY S.ShippedAt DESC,T.TransactionID DESC;
DECLARE @Barcode varchar(80);
SELECT TOP(1) @Barcode=T.LotNo
FROM dbo.FG_Shipment S JOIN dbo.WH_InventoryTransaction T
    ON T.SourceType='FG_OUTBOUND' AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
WHERE NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WHERE R.ShipmentID=S.ShipmentID AND R.LotNo=T.LotNo)
  AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory W WHERE W.LotNo=T.LotNo AND W.Qty<>0)
ORDER BY S.ShippedAt DESC,T.TransactionID DESC;
IF @Barcode IS NOT NULL EXEC dbo.FG_PDA_RETURN_SCAN @Barcode;
ELSE SELECT 'No actual shipment is currently eligible for a return scan.' Result;
