/*
  FG shipment consolidation
  - Keeps one FG_ShipmentOrder row per shipment plan.
  - Moves former FG_ShipmentOrderLine rows into FG_ShipmentOrder.ItemsJSON.
  - Preserves delivery/loading metadata on the shipment order.
  - Moves legacy pick details into the common inventory transaction history.
  - Removes all retired FG shipment/location/day-end tables.
  This script does not delete shipment orders or their item data.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'ItemsJSON') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD ItemsJSON nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'ShipmentDocumentNo') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD ShipmentDocumentNo varchar(60) COLLATE Korean_Wansung_CI_AS NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'ShippedAt') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD ShippedAt datetime2 NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'LoadingNumber') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD LoadingNumber varchar(24) COLLATE Korean_Wansung_CI_AS NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'LicensePlate') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD LicensePlate varchar(20) COLLATE Korean_Wansung_CI_AS NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'DriverName') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD DriverName varchar(50) COLLATE Korean_Wansung_CI_AS NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'LoadingDockNo') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD LoadingDockNo varchar(10) COLLATE Korean_Wansung_CI_AS NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'ArrivalAt') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD ArrivalAt datetime2 NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'DepartureAt') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD DepartureAt datetime2 NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'SealNo') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD SealNo varchar(20) COLLATE Korean_Wansung_CI_AS NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'LoadingOTDStatus') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD LoadingOTDStatus varchar(10) COLLATE Korean_Wansung_CI_AS NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'ShipmentOperatorID') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD ShipmentOperatorID nvarchar(450) NULL;
IF COL_LENGTH(N'dbo.FG_ShipmentOrder', N'LoadingConfirmedAt') IS NULL
    ALTER TABLE dbo.FG_ShipmentOrder ADD LoadingConfirmedAt datetime2 NULL;

-- Keep migrated text columns aligned with the existing FG table collation.
ALTER TABLE dbo.FG_ShipmentOrder ALTER COLUMN ShipmentDocumentNo varchar(60) COLLATE Korean_Wansung_CI_AS NULL;
ALTER TABLE dbo.FG_ShipmentOrder ALTER COLUMN LoadingNumber varchar(24) COLLATE Korean_Wansung_CI_AS NULL;
ALTER TABLE dbo.FG_ShipmentOrder ALTER COLUMN LicensePlate varchar(20) COLLATE Korean_Wansung_CI_AS NULL;
ALTER TABLE dbo.FG_ShipmentOrder ALTER COLUMN DriverName varchar(50) COLLATE Korean_Wansung_CI_AS NULL;
ALTER TABLE dbo.FG_ShipmentOrder ALTER COLUMN LoadingDockNo varchar(10) COLLATE Korean_Wansung_CI_AS NULL;
ALTER TABLE dbo.FG_ShipmentOrder ALTER COLUMN SealNo varchar(20) COLLATE Korean_Wansung_CI_AS NULL;
ALTER TABLE dbo.FG_ShipmentOrder ALTER COLUMN LoadingOTDStatus varchar(10) COLLATE Korean_Wansung_CI_AS NULL;
GO

IF OBJECT_ID(N'dbo.FG_ShipmentOrderLine', N'U') IS NOT NULL
BEGIN
    UPDATE O
       SET ItemsJSON = J.ItemsJSON
    FROM dbo.FG_ShipmentOrder O
    CROSS APPLY
    (
        SELECT
            L.ShipmentOrderLineID AS shipmentOrderLineId,
            L.LineSeq AS lineSeq,
            L.ItemNo AS itemNo,
            L.OrderedQty AS orderedQty,
            L.AllocatedQty AS allocatedQty,
            L.StockID AS stockId,
            L.LotID AS lotId,
            L.Location AS location,
            L.ReservationStatus AS reservationStatus,
            L.ReservedAt AS reservedAt,
            L.ReleasedAt AS releasedAt
        FROM dbo.FG_ShipmentOrderLine L
        WHERE L.ShipmentOrderID = O.ShipmentOrderID
        ORDER BY L.LineSeq, L.ShipmentOrderLineID
        FOR JSON PATH
    ) J(ItemsJSON)
    WHERE EXISTS
    (
        SELECT 1
        FROM dbo.FG_ShipmentOrderLine L
        WHERE L.ShipmentOrderID = O.ShipmentOrderID
    );

    UPDATE dbo.FG_ShipmentOrder
       SET ItemsJSON = N'[]'
     WHERE ItemsJSON IS NULL OR ISJSON(ItemsJSON) <> 1;
END;

IF OBJECT_ID(N'dbo.FG_DeliveryNote', N'U') IS NOT NULL
BEGIN
    ;WITH Latest AS
    (
        SELECT D.ShipmentOrderID, D.DnNumber,
               COALESCE(D.CustomerAckTS, D.IssuedAt, D.CreatedTS) AS ShippedAt,
               ROW_NUMBER() OVER
               (
                   PARTITION BY D.ShipmentOrderID
                   ORDER BY COALESCE(D.CustomerAckTS, D.IssuedAt, D.CreatedTS) DESC,
                            D.DeliveryNoteID DESC
               ) AS RN
        FROM dbo.FG_DeliveryNote D
        WHERE D.ShipmentOrderID IS NOT NULL
    )
    UPDATE O
       SET ShipmentDocumentNo = COALESCE(NULLIF(L.DnNumber COLLATE DATABASE_DEFAULT, ''), O.ShipmentDocumentNo),
           ShippedAt = COALESCE(L.ShippedAt, O.ShippedAt)
    FROM dbo.FG_ShipmentOrder O
    JOIN Latest L ON L.ShipmentOrderID = O.ShipmentOrderID AND L.RN = 1;
END;

IF OBJECT_ID(N'dbo.FG_LoadingConfirm', N'U') IS NOT NULL
BEGIN
    UPDATE O
       SET LoadingNumber = COALESCE(NULLIF(L.LoadingNumber COLLATE DATABASE_DEFAULT, ''), O.LoadingNumber COLLATE DATABASE_DEFAULT),
           CarrierCode = COALESCE(NULLIF(L.CarrierCode COLLATE DATABASE_DEFAULT, ''), O.CarrierCode COLLATE DATABASE_DEFAULT),
           LicensePlate = COALESCE(NULLIF(L.LicensePlate COLLATE DATABASE_DEFAULT, ''), O.LicensePlate COLLATE DATABASE_DEFAULT),
           DriverName = COALESCE(NULLIF(L.DriverName COLLATE DATABASE_DEFAULT, ''), O.DriverName COLLATE DATABASE_DEFAULT),
           LoadingDockNo = COALESCE(NULLIF(L.DockNo COLLATE DATABASE_DEFAULT, ''), O.LoadingDockNo COLLATE DATABASE_DEFAULT),
           ArrivalAt = COALESCE(L.ArrivalTS, O.ArrivalAt),
           DepartureAt = COALESCE(L.DepartureTS, O.DepartureAt),
           SealNo = COALESCE(NULLIF(L.SealNo COLLATE DATABASE_DEFAULT, ''), O.SealNo COLLATE DATABASE_DEFAULT),
           LoadingOTDStatus = COALESCE(NULLIF(L.OTDStatus COLLATE DATABASE_DEFAULT, ''), O.LoadingOTDStatus COLLATE DATABASE_DEFAULT),
           ShipmentOperatorID = COALESCE(NULLIF(L.OperatorID COLLATE DATABASE_DEFAULT, ''), O.ShipmentOperatorID COLLATE DATABASE_DEFAULT),
           LoadingConfirmedAt = COALESCE(L.ConfirmedAt, O.LoadingConfirmedAt),
           ShippedAt = COALESCE(O.ShippedAt, L.DepartureTS, L.ConfirmedAt)
    FROM dbo.FG_ShipmentOrder O
    OUTER APPLY
    (
        SELECT TOP (1) C.*
        FROM dbo.FG_LoadingConfirm C
        WHERE C.ShipmentOrderID = O.ShipmentOrderID
        ORDER BY COALESCE(C.DepartureTS, C.ConfirmedAt, C.CreatedTS) DESC, C.LoadingID DESC
    ) L
    WHERE L.LoadingID IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.FG_PickingDetail', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FG_PickingFifo', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
BEGIN
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,ItemNo,LocationID,LotID,LotNo,QtyBefore,QtyChange,QtyAfter,
         ReasonCode,RefDocType,RefDocID,OperatorID,Note,CreatedBy,CreatedTS)
    SELECT COALESCE(P.EndTS,P.StartTS,D.CreatedTS,SYSDATETIME()),'OUT',D.ItemNo,D.Location,D.LotID,L.LotCode,
           NULL,-ABS(D.Qty),NULL,'LEGACY_PICK','FG_LEGACY_PICK',D.PickDetailID,
           P.PickerID,CONCAT('Migrated pick ',COALESCE(P.PickNumber,CONVERT(varchar(20),P.PickID))),
           LEFT(COALESCE(NULLIF(D.CreatedBy,''),'migration'),20),COALESCE(D.CreatedTS,SYSDATETIME())
    FROM dbo.FG_PickingDetail D
    JOIN dbo.FG_PickingFifo P ON P.PickID=D.PickID
    LEFT JOIN dbo.tbl_Lot L ON L.LotID=D.LotID
    WHERE D.Qty<>0
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.WH_InventoryTransaction T
          WHERE T.RefDocType='FG_LEGACY_PICK' AND T.RefDocID=D.PickDetailID
      );
END;

IF OBJECT_ID(N'dbo.FG_LoadingConfirm', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
BEGIN
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,ItemNo,LocationID,LotID,LotNo,QtyBefore,QtyChange,QtyAfter,
         ReasonCode,RefDocType,RefDocID,OperatorID,Note,CreatedBy,CreatedTS)
    SELECT COALESCE(C.DepartureTS,C.ConfirmedAt,C.CreatedTS,SYSDATETIME()),'OUT',J.ItemNo,J.Location,J.LotID,J.LotNo,
           NULL,-ABS(J.Qty),NULL,'LEGACY_LOAD','FG_LEGACY_LOAD',C.LoadingID,C.OperatorID,
           CONCAT('Migrated loading ',C.LoadingNumber,' / truck ',C.LicensePlate),
           LEFT(COALESCE(NULLIF(C.CreatedBy,''),'migration'),20),COALESCE(C.CreatedTS,SYSDATETIME())
    FROM dbo.FG_LoadingConfirm C
    CROSS APPLY OPENJSON(CASE WHEN ISJSON(C.PalletsLoadedJSON)=1 THEN C.PalletsLoadedJSON ELSE N'[]' END)
        WITH (ItemNo varchar(20) '$.itemNo',Location varchar(20) '$.location',LotID int '$.lotId',
              LotNo nvarchar(50) '$.lotNo',Qty decimal(14,3) '$.qty') J
    WHERE ISNULL(J.Qty,0)<>0
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.WH_InventoryTransaction T
          WHERE T.RefDocType='FG_LEGACY_LOAD' AND T.RefDocID=C.LoadingID
      )
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.FG_PickingDetail D
          WHERE D.PickID=C.PickID AND D.ItemNo=J.ItemNo AND ISNULL(D.LotID,-1)=ISNULL(J.LotID,-1)
      );
END;

DECLARE @DropForeignKeys nvarchar(max) = N'';
SELECT @DropForeignKeys +=
    N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(F.parent_object_id)) + N'.' +
    QUOTENAME(OBJECT_NAME(F.parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(F.name) + N';'
FROM sys.foreign_keys F
WHERE F.referenced_object_id IN
(
    OBJECT_ID(N'dbo.FG_ShipmentOrderLine'),
    OBJECT_ID(N'dbo.FG_DeliveryNote'),
    OBJECT_ID(N'dbo.FG_PickingDetail'),
    OBJECT_ID(N'dbo.FG_PickingFifo'),
    OBJECT_ID(N'dbo.FG_LoadingConfirm'),
    OBJECT_ID(N'dbo.FG_DayEndClose'),
    OBJECT_ID(N'dbo.FG_LocationMaster')
)
   OR F.parent_object_id IN
(
    OBJECT_ID(N'dbo.FG_ShipmentOrderLine'),
    OBJECT_ID(N'dbo.FG_DeliveryNote'),
    OBJECT_ID(N'dbo.FG_PickingDetail'),
    OBJECT_ID(N'dbo.FG_PickingFifo'),
    OBJECT_ID(N'dbo.FG_LoadingConfirm'),
    OBJECT_ID(N'dbo.FG_DayEndClose'),
    OBJECT_ID(N'dbo.FG_LocationMaster')
);
IF @DropForeignKeys <> N'' EXEC sys.sp_executesql @DropForeignKeys;

DROP TABLE IF EXISTS dbo.FG_PickingDetail;
DROP TABLE IF EXISTS dbo.FG_LoadingConfirm;
DROP TABLE IF EXISTS dbo.FG_PickingFifo;
DROP TABLE IF EXISTS dbo.FG_ShipmentOrderLine;
DROP TABLE IF EXISTS dbo.FG_DeliveryNote;
DROP TABLE IF EXISTS dbo.FG_DayEndClose;
DROP TABLE IF EXISTS dbo.FG_LocationMaster;

IF EXISTS
(
    SELECT 1 FROM sys.tables
    WHERE schema_id=SCHEMA_ID(N'dbo')
      AND name IN
      (
          N'FG_DayEndClose',N'FG_DeliveryNote',N'FG_LoadingConfirm',N'FG_LocationMaster',
          N'FG_PickingDetail',N'FG_PickingFifo',N'FG_ShipmentOrderLine'
      )
)
    THROW 51001, 'Retired FG tables were not fully removed.', 1;

DELETE FROM dbo.MD_CodeItem
WHERE GroupCode IN ('FG_SHIPMENT_SOURCE', 'FG_SHIPMENT_URL', 'FG_SHIPMENT_AUTH');
DELETE FROM dbo.MD_CodeGroup
WHERE GroupCode IN ('FG_SHIPMENT_SOURCE', 'FG_SHIPMENT_URL', 'FG_SHIPMENT_AUTH');

-- These procedures belonged to the removed order-line picking/truck-loading flow.
DROP PROCEDURE IF EXISTS dbo.FG_PDA_PICKING_SCAN;
DROP PROCEDURE IF EXISTS dbo.FG_PDA_PICKING_COMPLETE;
DROP PROCEDURE IF EXISTS dbo.FG_PDA_LOADING_ORDER_SCAN;
DROP PROCEDURE IF EXISTS dbo.FG_PDA_LOADING_TRUCK_SCAN;
DROP PROCEDURE IF EXISTS dbo.FG_PDA_LOADING_STOCK_SCAN;
DROP PROCEDURE IF EXISTS dbo.FG_PDA_LOADING_COMPLETE;

COMMIT TRANSACTION;
GO

-- Canonical LOT-based procedures are created by migrate_fg_inventory_consolidation.sql.
