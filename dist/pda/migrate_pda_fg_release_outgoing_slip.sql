-- FG Release: scan an outgoing-slip barcode instead of a shipment-order number.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

IF COL_LENGTH('dbo.FG_ShipmentOrder', 'OutgoingSlipNumber') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD OutgoingSlipNumber varchar(24) NULL;
GO

UPDATE dbo.FG_ShipmentOrder
SET OutgoingSlipNumber = ShipOrderNumber
WHERE NULLIF(LTRIM(RTRIM(OutgoingSlipNumber)), '') IS NULL
  AND NULLIF(LTRIM(RTRIM(ShipOrderNumber)), '') IS NOT NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.FG_ShipmentOrder')
      AND name = 'UX_FG_ShipmentOrder_OutgoingSlipNumber')
BEGIN
    CREATE UNIQUE INDEX UX_FG_ShipmentOrder_OutgoingSlipNumber
        ON dbo.FG_ShipmentOrder (OutgoingSlipNumber)
        WHERE OutgoingSlipNumber IS NOT NULL;
END;
GO
