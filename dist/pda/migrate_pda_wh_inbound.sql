-- =====================================================================
--  migrate_pda_wh_inbound.sql
--  PDA Warehouse inbound database contract
--
--  PDA-owned procedure names use the main AMES Warehouse style:
--    dbo.WH_PDA_INBOUND_...
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\migrate_pda_wh_inbound.sql
-- =====================================================================
SET NOCOUNT ON;
GO

-- =====================================================================
--  Inbound / LOT scan
--  Source: dbo.tbl_Lot, dbo.WH_PurchaseOrder, dbo.WH_Inventory
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_SCAN_LOT
    @ReceiveMode nvarchar(10),
    @LotBarcode nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
    DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));
    DECLARE @ActualMode nvarchar(10);

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N''
        THROW 51401, 'LOT barcode is required.', 1;

    SELECT TOP (1)
        @ActualMode = UPPER(LTRIM(RTRIM(COALESCE(ProcessCode, N''))))
    FROM dbo.tbl_Lot
    WHERE LotCode = @Barcode
    ORDER BY LotID DESC;

    IF @ActualMode IS NULL
        THROW 51402, 'LOT was not found in inbound source tables.', 1;
    IF @ActualMode <> @Mode
        THROW 51403, 'LOT receive mode does not match the selected tab.', 1;

    ;WITH MatchedLot AS
    (
        SELECT TOP (1)
            L.LotID,
            L.LotCode,
            L.ItemNo,
            L.BatchSize,
            L.RemainingQty,
            L.ProducedAt,
            L.Status AS LotStatus,
            L.CurrentLocationID,
            L.ExpiryDate,
            I.ItemName,
            I.CarType,
            I.DefaultUOM
        FROM dbo.tbl_Lot L
        LEFT JOIN dbo.MD_Item I
               ON I.ItemNo = L.ItemNo
        WHERE L.LotCode = @Barcode
        ORDER BY L.LotID DESC
    ),
    ActiveInventory AS
    (
        SELECT TOP (1)
            W.InventoryID,
            W.LotID,
            W.LocationID,
            W.OnHandQty,
            W.Status,
            W.LastReceivedAt
        FROM dbo.WH_Inventory W
        JOIN MatchedLot L
          ON L.LotID = W.LotID
        WHERE COALESCE(W.Status, 'Received') <> 'Canceled'
          AND COALESCE(W.OnHandQty, 0) > 0
        ORDER BY W.InventoryID DESC
    ),
    MatchedPo AS
    (
        SELECT TOP (1)
            P.PoID,
            P.PoNumber,
            P.PoLineNo,
            P.VendorID,
            P.OrderQty,
            P.ReceivedQty,
            P.UnitCode,
            P.OrderDate,
            P.DueDate,
            P.Status
        FROM dbo.WH_PurchaseOrder P
        JOIN MatchedLot L
          ON L.ItemNo = P.ItemNo
        ORDER BY
            CASE
                WHEN COALESCE(P.OrderQty, 0) > COALESCE(P.ReceivedQty, 0) THEN 0
                ELSE 1
            END,
            P.DueDate,
            P.PoID
    )
    SELECT
        @Mode AS RECEIVE_TYPE,
        CASE WHEN A.InventoryID IS NULL THEN N'Y' ELSE N'N' END AS YN,
        L.LotCode AS LOTNO,
        L.LotCode AS BARCODE,
        N'dbo.tbl_Lot/dbo.WH_PurchaseOrder' AS SOURCE_TABLE,
        P.PoNumber AS NOTENO,
        CASE WHEN @Mode = N'CKD' THEN L.LotCode ELSE NULL END AS CASE_BARCODE,
        CASE WHEN @Mode = N'CKD' THEN CONVERT(nvarchar(30), L.LotID) ELSE NULL END AS CASE_NO,
        CAST(NULL AS nvarchar(30)) AS INVOICE_NO,
        CAST(NULL AS nvarchar(30)) AS CONTAINER_NO,
        L.ItemNo AS PARTNO,
        L.ItemName AS PARTNM,
        COALESCE(A.OnHandQty, NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), P.OrderQty, 0) AS QTY,
        COALESCE(P.UnitCode, L.DefaultUOM) AS UNIT,
        P.PoNumber AS PONO,
        P.PoLineNo AS PONO_SEQ,
        P.VendorID AS VENDCD,
        COALESCE(V.VendorName, P.VendorID) AS VENDNM,
        CONVERT(date, L.ProducedAt) AS PROD_DATE,
        P.DueDate AS DELI_DATE,
        P.DueDate AS ARRIV_DATE,
        CAST(NULL AS date) AS SHIP_DATE,
        CAST(NULL AS date) AS PACK_DATE,
        COALESCE(A.LocationID, L.CurrentLocationID) AS RECEIVED_LOCATION,
        COALESCE(A.Status, L.LotStatus) AS RECEIVED_STATUS
    FROM MatchedLot L
    LEFT JOIN ActiveInventory A
           ON A.LotID = L.LotID
    LEFT JOIN MatchedPo P
           ON 1 = 1
    LEFT JOIN dbo.MD_Vendor V
           ON V.VendorID = P.VendorID;
END;
GO

-- =====================================================================
--  Inbound / Receive LOT
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_RECEIVE_LOT
    @ReceiveMode nvarchar(10),
    @LotBarcode nvarchar(50),
    @LocationId nvarchar(30),
    @UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
    DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));
    DECLARE @Location nvarchar(30) = LTRIM(RTRIM(ISNULL(@LocationId, N'')));
    DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51410, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N''
        THROW 51411, 'LOT barcode is required.', 1;
    IF @Location = N''
        THROW 51412, 'Location No is required.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = @Location AND COALESCE(ActiveFlag, 1) = 1)
        THROW 51413, 'Location No was not found.', 1;

    DECLARE
        @LotID int,
        @ItemNo varchar(20),
        @ActualMode nvarchar(10),
        @Qty decimal(14,3),
        @PoID int,
        @VendorID varchar(20),
        @ReceivingID int;

    SELECT TOP (1)
        @LotID = LotID,
        @ItemNo = ItemNo,
        @ActualMode = UPPER(LTRIM(RTRIM(COALESCE(ProcessCode, N'')))),
        @Qty = COALESCE(NULLIF(RemainingQty, 0), NULLIF(BatchSize, 0), 0)
    FROM dbo.tbl_Lot
    WHERE LotCode = @Barcode
    ORDER BY LotID DESC;

    IF @LotID IS NULL
        THROW 51414, 'LOT was not found in inbound source tables.', 1;
    IF @ActualMode <> @Mode
        THROW 51417, 'LOT receive mode does not match the selected tab.', 1;
    IF @Qty <= 0
        THROW 51415, 'LOT quantity must be greater than zero.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.WH_Inventory
        WHERE LotID = @LotID
          AND COALESCE(Status, 'Received') <> 'Canceled'
          AND COALESCE(OnHandQty, 0) > 0
    )
        THROW 51416, 'LOT is already received.', 1;

    SELECT TOP (1)
        @PoID = PoID,
        @VendorID = VendorID
    FROM dbo.WH_PurchaseOrder
    WHERE ItemNo = @ItemNo
    ORDER BY
        CASE WHEN COALESCE(OrderQty, 0) > COALESCE(ReceivedQty, 0) THEN 0 ELSE 1 END,
        DueDate,
        PoID;

    BEGIN TRANSACTION;

    DECLARE @InsertedReceiving TABLE (ReceivingID int NOT NULL);

    INSERT INTO dbo.WH_Receiving
    (
        ReceivingNo, PoID, ItemNo, VendorID, ReceivedQty, LocationID, LotCode,
        ReceivedAt, ReceivedBy, TerminalID, QcStatus, LabelPrinted, CreatedBy, CreatedTS
    )
    OUTPUT INSERTED.ReceivingID INTO @InsertedReceiving
    VALUES
    (
        CONCAT('RCV-', FORMAT(SYSDATETIME(), 'yyMMddHHmmssfff')),
        @PoID, @ItemNo, @VendorID, @Qty, @Location, @Barcode,
        SYSDATETIME(), @User, 'PDA', 'Received', 0, @User, SYSDATETIME()
    );

    SELECT TOP (1) @ReceivingID = ReceivingID FROM @InsertedReceiving;

    INSERT INTO dbo.WH_Inventory
    (
        ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt,
        ExpiryDate, Status, CreatedBy, CreatedTS
    )
    SELECT
        @ItemNo, @Location, @LotID, @Qty, 0, SYSDATETIME(),
        L.ExpiryDate, 'Received', @User, SYSDATETIME()
    FROM dbo.tbl_Lot L
    WHERE L.LotID = @LotID;

    UPDATE dbo.tbl_Lot
       SET CurrentLocationID = @Location,
           RemainingQty = @Qty,
           Status = 'Received',
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID;

    IF @PoID IS NOT NULL
    BEGIN
        UPDATE dbo.WH_PurchaseOrder
           SET ReceivedQty = COALESCE(ReceivedQty, 0) + @Qty,
               Status = CASE
                   WHEN COALESCE(ReceivedQty, 0) + @Qty >= COALESCE(OrderQty, 0) THEN 'Complete'
                   ELSE 'Open'
               END,
               ModifiedBy = @User,
               ModifiedTS = SYSDATETIME()
          WHERE PoID = @PoID;
    END;

    INSERT INTO dbo.WH_InventoryTransaction
    (
        TransactionType, ItemNo, LocationID, LotID, QtyBefore, QtyChange, QtyAfter,
        ReasonCode, RefDocType, RefDocID, OperatorID, Note, CreatedBy, CreatedTS
    )
    VALUES
    (
        'IN', @ItemNo, @Location, @LotID, 0, @Qty, @Qty,
        'INBOUND_RECEIVE', 'WH_Receiving', @ReceivingID, @User,
        CONCAT('PDA inbound receive ', @Barcode), @User, SYSDATETIME()
    );

    COMMIT TRANSACTION;

    EXEC dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode = @Mode, @LotBarcode = @Barcode;
END;
GO

-- =====================================================================
--  Inbound / Move received LOT location
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_MOVE_LOCATION
    @ReceiveMode nvarchar(10),
    @LotBarcode nvarchar(50),
    @LocationId nvarchar(30),
    @UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
    DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));
    DECLARE @Location nvarchar(30) = LTRIM(RTRIM(ISNULL(@LocationId, N'')));
    DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @LotID int;
    DECLARE @ActualMode nvarchar(10);
    DECLARE @CurrentLocation varchar(20);

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51420, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N''
        THROW 51421, 'LOT barcode is required.', 1;
    IF @Location = N''
        THROW 51422, 'Location No is required.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = @Location AND COALESCE(ActiveFlag, 1) = 1)
        THROW 51423, 'Location No was not found.', 1;

    SELECT TOP (1)
        @LotID = LotID,
        @ActualMode = UPPER(LTRIM(RTRIM(COALESCE(ProcessCode, N''))))
    FROM dbo.tbl_Lot
    WHERE LotCode = @Barcode
    ORDER BY LotID DESC;

    IF @LotID IS NULL
        THROW 51424, 'LOT was not found in inbound source tables.', 1;
    IF @ActualMode <> @Mode
        THROW 51427, 'LOT receive mode does not match the selected tab.', 1;

    SELECT TOP (1) @CurrentLocation = LocationID
    FROM dbo.WH_Inventory
    WHERE LotID = @LotID
      AND COALESCE(Status, 'Received') <> 'Canceled'
      AND COALESCE(OnHandQty, 0) > 0
    ORDER BY InventoryID DESC;

    IF @CurrentLocation IS NULL
        THROW 51425, 'LOT is not received yet.', 1;
    IF UPPER(@CurrentLocation) = UPPER(@Location)
        THROW 51426, 'LOT is already in this location.', 1;

    BEGIN TRANSACTION;

    UPDATE dbo.WH_Inventory
       SET LocationID = @Location,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID
       AND COALESCE(Status, 'Received') <> 'Canceled'
       AND COALESCE(OnHandQty, 0) > 0;

    UPDATE dbo.tbl_Lot
       SET CurrentLocationID = @Location,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID;

    UPDATE dbo.WH_Receiving
       SET LocationID = @Location,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE ReceivingID =
     (
        SELECT TOP (1) ReceivingID
        FROM dbo.WH_Receiving
        WHERE LotCode = @Barcode
        ORDER BY ReceivingID DESC
     );

    COMMIT TRANSACTION;

    EXEC dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode = @Mode, @LotBarcode = @Barcode;
END;
GO

-- =====================================================================
--  Inbound / Cancel receipt
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_CANCEL_RECEIPT
    @ReceiveMode nvarchar(10),
    @LotBarcode nvarchar(50),
    @UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
    DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));
    DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @LotID int;
    DECLARE @ActualMode nvarchar(10);
    DECLARE @Qty decimal(14,3);
    DECLARE @PoID int;

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51430, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N''
        THROW 51431, 'LOT barcode is required.', 1;

    SELECT TOP (1)
        @LotID = LotID,
        @ActualMode = UPPER(LTRIM(RTRIM(COALESCE(ProcessCode, N''))))
    FROM dbo.tbl_Lot
    WHERE LotCode = @Barcode
    ORDER BY LotID DESC;

    IF @LotID IS NULL
        THROW 51432, 'LOT was not found in inbound source tables.', 1;
    IF @ActualMode <> @Mode
        THROW 51436, 'LOT receive mode does not match the selected tab.', 1;

    SELECT TOP (1)
        @Qty = OnHandQty
    FROM dbo.WH_Inventory
    WHERE LotID = @LotID
      AND COALESCE(Status, 'Received') <> 'Canceled'
      AND COALESCE(OnHandQty, 0) > 0
    ORDER BY InventoryID DESC;

    IF COALESCE(@Qty, 0) <= 0
        THROW 51433, 'LOT is not received yet.', 1;

    SELECT TOP (1) @PoID = PoID
    FROM dbo.WH_Receiving
    WHERE LotCode = @Barcode
    ORDER BY ReceivingID DESC;

    BEGIN TRANSACTION;

    UPDATE dbo.WH_Inventory
       SET OnHandQty = 0,
           ReservedQty = 0,
           Status = 'Canceled',
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID
       AND COALESCE(Status, 'Received') <> 'Canceled';

    UPDATE dbo.WH_Receiving
       SET QcStatus = 'Canceled',
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE ReceivingID =
     (
        SELECT TOP (1) ReceivingID
        FROM dbo.WH_Receiving
        WHERE LotCode = @Barcode
        ORDER BY ReceivingID DESC
     );

    UPDATE dbo.tbl_Lot
       SET CurrentLocationID = NULL,
           RemainingQty = CASE WHEN COALESCE(RemainingQty, 0) <= 0 THEN @Qty ELSE RemainingQty END,
           Status = 'Open',
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID;

    IF @PoID IS NOT NULL
    BEGIN
        UPDATE dbo.WH_PurchaseOrder
           SET ReceivedQty = CASE
                   WHEN COALESCE(ReceivedQty, 0) - @Qty < 0 THEN 0
                   ELSE COALESCE(ReceivedQty, 0) - @Qty
               END,
               Status = 'Open',
               ModifiedBy = @User,
               ModifiedTS = SYSDATETIME()
         WHERE PoID = @PoID;
    END;

    COMMIT TRANSACTION;

    EXEC dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode = @Mode, @LotBarcode = @Barcode;
END;
GO
