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

IF OBJECT_ID(N'dbo.WH_InboundDocument', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_InboundDocument
    (
        InboundDocumentID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WH_InboundDocument PRIMARY KEY,
        ReceiveType nvarchar(10) NOT NULL,
        DocumentBarcode nvarchar(50) NOT NULL,
        DocumentNo nvarchar(50) NULL,
        VendorID varchar(20) NULL,
        CaseNo nvarchar(50) NULL,
        InvoiceNo nvarchar(50) NULL,
        ContainerNo nvarchar(50) NULL,
        ShipDate date NULL,
        PackDate date NULL,
        DeliveryDate date NULL,
        ArrivalDate date NULL,
        Status nvarchar(20) NOT NULL CONSTRAINT DF_WH_InboundDocument_Status DEFAULT N'Open',
        CreatedBy nvarchar(40) NULL,
        CreatedTS datetime2(0) NOT NULL CONSTRAINT DF_WH_InboundDocument_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy nvarchar(40) NULL,
        ModifiedTS datetime2(0) NULL,
        CONSTRAINT UQ_WH_InboundDocument_Barcode UNIQUE (DocumentBarcode),
        CONSTRAINT CK_WH_InboundDocument_Type CHECK (ReceiveType IN (N'LOCAL', N'CKD'))
    );
END;
GO

IF OBJECT_ID(N'dbo.WH_InboundPackage', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_InboundPackage
    (
        InboundPackageID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WH_InboundPackage PRIMARY KEY,
        InboundDocumentID int NOT NULL,
        BoxBarcode nvarchar(50) NOT NULL,
        LotID int NOT NULL,
        ItemNo varchar(20) NOT NULL,
        PoID int NULL,
        Qty decimal(14,3) NOT NULL,
        UnitCode varchar(10) NULL,
        ProductionDate date NULL,
        Status nvarchar(20) NOT NULL CONSTRAINT DF_WH_InboundPackage_Status DEFAULT N'Open',
        ReceivedAt datetime2(0) NULL,
        ReceivedBy nvarchar(40) NULL,
        CreatedBy nvarchar(40) NULL,
        CreatedTS datetime2(0) NOT NULL CONSTRAINT DF_WH_InboundPackage_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy nvarchar(40) NULL,
        ModifiedTS datetime2(0) NULL,
        CONSTRAINT UQ_WH_InboundPackage_Barcode UNIQUE (BoxBarcode),
        CONSTRAINT FK_WH_InboundPackage_Document FOREIGN KEY (InboundDocumentID)
            REFERENCES dbo.WH_InboundDocument(InboundDocumentID)
    );

    CREATE INDEX IX_WH_InboundPackage_Document
        ON dbo.WH_InboundPackage(InboundDocumentID, ItemNo);
    CREATE INDEX IX_WH_InboundPackage_Lot
        ON dbo.WH_InboundPackage(LotID);
END;
GO

-- LOCAL: delivery note header/detail. CKD: case header/detail.
CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_DOCUMENT_INFO
    @ReceiveMode nvarchar(10),
    @DocumentBarcode nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
    DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@DocumentBarcode, N'')));
    DECLARE @DocumentID int;
    DECLARE @ActualMode nvarchar(10);

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;

    SELECT TOP (1)
        @DocumentID = InboundDocumentID,
        @ActualMode = ReceiveType
    FROM dbo.WH_InboundDocument
    WHERE DocumentBarcode = @Barcode;

    IF @DocumentID IS NOT NULL AND @ActualMode <> @Mode
        THROW 51403, 'Barcode receive mode does not match the selected tab.', 1;

    SELECT
        D.ReceiveType AS RECEIVE_TYPE,
        D.InboundDocumentID AS INBOUND_DOCUMENT_ID,
        D.DocumentBarcode AS DOCUMENT_BARCODE,
        D.DocumentNo AS DOCUMENT_NO,
        D.VendorID AS VENDCD,
        COALESCE(V.VendorName, D.VendorID) AS VENDNM,
        D.CaseNo AS CASE_NO,
        D.InvoiceNo AS INVOICE_NO,
        D.ContainerNo AS CONTAINER_NO,
        D.ShipDate AS SHIP_DATE,
        D.PackDate AS PACK_DATE,
        D.DeliveryDate AS DELI_DATE,
        D.ArrivalDate AS ARRIV_DATE,
        COUNT(P.InboundPackageID) AS TOTAL_BOXES,
        SUM(CASE WHEN AI.LotID IS NULL THEN 0 ELSE 1 END) AS SCANNED_BOXES,
        CASE WHEN COUNT(P.InboundPackageID) > 0
                   AND COUNT(P.InboundPackageID) = SUM(CASE WHEN AI.LotID IS NULL THEN 0 ELSE 1 END)
             THEN N'Y' ELSE N'N' END AS YN
    FROM dbo.WH_InboundDocument D
    LEFT JOIN dbo.MD_Vendor V ON V.VendorID = D.VendorID
    LEFT JOIN dbo.WH_InboundPackage P ON P.InboundDocumentID = D.InboundDocumentID
    LEFT JOIN
    (
        SELECT DISTINCT LotID
        FROM dbo.WH_Inventory
        WHERE COALESCE(Status, 'Received') <> 'Canceled'
          AND COALESCE(OnHandQty, 0) > 0
    ) AI ON AI.LotID = P.LotID
    WHERE D.InboundDocumentID = @DocumentID
    GROUP BY D.ReceiveType, D.InboundDocumentID, D.DocumentBarcode, D.DocumentNo,
             D.VendorID, V.VendorName, D.CaseNo, D.InvoiceNo, D.ContainerNo,
             D.ShipDate, D.PackDate, D.DeliveryDate, D.ArrivalDate;

    SELECT
        P.ItemNo AS PARTNO,
        I.ItemName AS PARTNM,
        COUNT(P.InboundPackageID) AS BOX_COUNT,
        SUM(CASE WHEN AI.LotID IS NULL THEN 0 ELSE 1 END) AS SCAN_COUNT,
        CASE WHEN COUNT(P.InboundPackageID) = SUM(CASE WHEN AI.LotID IS NULL THEN 0 ELSE 1 END)
             THEN N'Y' ELSE N'N' END AS YN
    FROM dbo.WH_InboundPackage P
    LEFT JOIN dbo.MD_Item I ON I.ItemNo = P.ItemNo
    LEFT JOIN
    (
        SELECT DISTINCT LotID
        FROM dbo.WH_Inventory
        WHERE COALESCE(Status, 'Received') <> 'Canceled'
          AND COALESCE(OnHandQty, 0) > 0
    ) AI ON AI.LotID = P.LotID
    WHERE P.InboundDocumentID = @DocumentID
    GROUP BY P.ItemNo, I.ItemName
    ORDER BY P.ItemNo;

    SELECT
        P.ItemNo AS PARTNO,
        P.BoxBarcode AS BOX_BARCODE,
        L.LotCode AS LOTNO,
        P.Qty AS QTY,
        P.UnitCode AS UNIT,
        CASE WHEN AI.LotID IS NULL THEN N'N' ELSE N'Y' END AS YN
    FROM dbo.WH_InboundPackage P
    LEFT JOIN dbo.tbl_Lot L ON L.LotID = P.LotID
    LEFT JOIN
    (
        SELECT DISTINCT LotID
        FROM dbo.WH_Inventory
        WHERE COALESCE(Status, 'Received') <> 'Canceled'
          AND COALESCE(OnHandQty, 0) > 0
    ) AI ON AI.LotID = P.LotID
    WHERE P.InboundDocumentID = @DocumentID
    ORDER BY P.ItemNo, P.InboundPackageID;
END;
GO

-- =====================================================================
--  Inbound / box barcode scan
--  Source: dbo.WH_InboundPackage, dbo.tbl_Lot, dbo.WH_PurchaseOrder
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
    DECLARE @LotID int;
    DECLARE @PackageID int;
    DECLARE @DocumentID int;

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N''
        THROW 51401, 'Box barcode is required.', 1;

    SELECT TOP (1)
        @PackageID = P.InboundPackageID,
        @DocumentID = P.InboundDocumentID,
        @LotID = P.LotID,
        @ActualMode = D.ReceiveType
    FROM dbo.WH_InboundPackage P
    JOIN dbo.WH_InboundDocument D ON D.InboundDocumentID = P.InboundDocumentID
    WHERE P.BoxBarcode = @Barcode;

    IF @LotID IS NULL
    BEGIN
        SELECT TOP (1)
            @LotID = LotID,
            @ActualMode = UPPER(LTRIM(RTRIM(COALESCE(ProcessCode, N''))))
        FROM dbo.tbl_Lot
        WHERE LotCode = @Barcode
        ORDER BY LotID DESC;
    END;

    IF @ActualMode IS NULL
        THROW 51402, 'Barcode was not found in inbound source tables.', 1;
    IF @ActualMode <> @Mode
        THROW 51403, 'Barcode receive mode does not match the selected tab.', 1;

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
        WHERE L.LotID = @LotID
        ORDER BY L.LotID DESC
    ),
    MatchedPackage AS
    (
        SELECT TOP (1)
            P.InboundPackageID,
            P.InboundDocumentID,
            P.BoxBarcode,
            P.PoID,
            P.Qty,
            P.UnitCode,
            P.ProductionDate
        FROM dbo.WH_InboundPackage P
        WHERE P.InboundPackageID = @PackageID
    ),
    MatchedDocument AS
    (
        SELECT TOP (1) D.*
        FROM dbo.WH_InboundDocument D
        WHERE D.InboundDocumentID = @DocumentID
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
        JOIN MatchedLot L ON L.ItemNo = P.ItemNo
        LEFT JOIN MatchedPackage MP ON 1 = 1
        WHERE MP.PoID IS NULL OR P.PoID = MP.PoID
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
        @Barcode AS BARCODE,
        CASE WHEN MP.InboundPackageID IS NULL
             THEN N'dbo.tbl_Lot/dbo.WH_PurchaseOrder'
             ELSE N'dbo.WH_InboundDocument/dbo.WH_InboundPackage' END AS SOURCE_TABLE,
        CASE WHEN @Mode = N'LOCAL' THEN D.DocumentNo ELSE P.PoNumber END AS NOTENO,
        CASE WHEN @Mode = N'CKD' THEN D.DocumentBarcode ELSE NULL END AS CASE_BARCODE,
        CASE WHEN @Mode = N'CKD' THEN D.CaseNo ELSE NULL END AS CASE_NO,
        D.InvoiceNo AS INVOICE_NO,
        D.ContainerNo AS CONTAINER_NO,
        L.ItemNo AS PARTNO,
        L.ItemName AS PARTNM,
        COALESCE(A.OnHandQty, NULLIF(MP.Qty, 0), NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), P.OrderQty, 0) AS QTY,
        COALESCE(MP.UnitCode, P.UnitCode, L.DefaultUOM) AS UNIT,
        P.PoNumber AS PONO,
        P.PoLineNo AS PONO_SEQ,
        P.VendorID AS VENDCD,
        COALESCE(V.VendorName, P.VendorID) AS VENDNM,
        COALESCE(MP.ProductionDate, CONVERT(date, L.ProducedAt)) AS PROD_DATE,
        COALESCE(D.DeliveryDate, P.DueDate) AS DELI_DATE,
        COALESCE(D.ArrivalDate, P.DueDate) AS ARRIV_DATE,
        D.ShipDate AS SHIP_DATE,
        D.PackDate AS PACK_DATE,
        COALESCE(A.LocationID, L.CurrentLocationID) AS RECEIVED_LOCATION,
        COALESCE(A.Status, L.LotStatus) AS RECEIVED_STATUS
    FROM MatchedLot L
    LEFT JOIN MatchedPackage MP ON 1 = 1
    LEFT JOIN MatchedDocument D ON 1 = 1
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
        @InboundDocumentID int,
        @VendorID varchar(20),
        @ReceivingID int;

    SELECT TOP (1)
        @LotID = P.LotID,
        @ItemNo = P.ItemNo,
        @ActualMode = D.ReceiveType,
        @Qty = P.Qty,
        @PoID = P.PoID,
        @InboundDocumentID = P.InboundDocumentID
    FROM dbo.WH_InboundPackage P
    JOIN dbo.WH_InboundDocument D ON D.InboundDocumentID = P.InboundDocumentID
    WHERE P.BoxBarcode = @Barcode;

    IF @LotID IS NULL
    BEGIN
        SELECT TOP (1)
            @LotID = LotID,
            @ItemNo = ItemNo,
            @ActualMode = UPPER(LTRIM(RTRIM(COALESCE(ProcessCode, N'')))),
            @Qty = COALESCE(NULLIF(RemainingQty, 0), NULLIF(BatchSize, 0), 0)
        FROM dbo.tbl_Lot
        WHERE LotCode = @Barcode
        ORDER BY LotID DESC;
    END;

    IF @LotID IS NULL
        THROW 51414, 'Barcode was not found in inbound source tables.', 1;
    IF @ActualMode <> @Mode
        THROW 51417, 'Barcode receive mode does not match the selected tab.', 1;
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

    IF @PoID IS NOT NULL
        SELECT @VendorID = VendorID FROM dbo.WH_PurchaseOrder WHERE PoID = @PoID;
    ELSE
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

    UPDATE dbo.WH_InboundPackage
       SET Status = N'Received',
           ReceivedAt = SYSDATETIME(),
           ReceivedBy = @User,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE BoxBarcode = @Barcode;

    IF @InboundDocumentID IS NOT NULL
    BEGIN
        UPDATE dbo.WH_InboundDocument
           SET Status = CASE WHEN EXISTS
               (
                   SELECT 1
                   FROM dbo.WH_InboundPackage P
                   WHERE P.InboundDocumentID = @InboundDocumentID
                     AND P.Status <> N'Received'
               ) THEN N'InProgress' ELSE N'Complete' END,
               ModifiedBy = @User,
               ModifiedTS = SYSDATETIME()
         WHERE InboundDocumentID = @InboundDocumentID;
    END;

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
        @LotID = P.LotID,
        @ActualMode = D.ReceiveType
    FROM dbo.WH_InboundPackage P
    JOIN dbo.WH_InboundDocument D ON D.InboundDocumentID = P.InboundDocumentID
    WHERE P.BoxBarcode = @Barcode;

    IF @LotID IS NULL
        SELECT TOP (1)
            @LotID = LotID,
            @ActualMode = UPPER(LTRIM(RTRIM(COALESCE(ProcessCode, N''))))
        FROM dbo.tbl_Lot
        WHERE LotCode = @Barcode
        ORDER BY LotID DESC;

    IF @LotID IS NULL
        THROW 51424, 'Barcode was not found in inbound source tables.', 1;
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
        WHERE LotCode IN (@Barcode, (SELECT LotCode FROM dbo.tbl_Lot WHERE LotID = @LotID))
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
    DECLARE @InboundDocumentID int;

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51430, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N''
        THROW 51431, 'LOT barcode is required.', 1;

    SELECT TOP (1)
        @LotID = P.LotID,
        @ActualMode = D.ReceiveType,
        @PoID = P.PoID,
        @InboundDocumentID = P.InboundDocumentID
    FROM dbo.WH_InboundPackage P
    JOIN dbo.WH_InboundDocument D ON D.InboundDocumentID = P.InboundDocumentID
    WHERE P.BoxBarcode = @Barcode;

    IF @LotID IS NULL
        SELECT TOP (1)
            @LotID = LotID,
            @ActualMode = UPPER(LTRIM(RTRIM(COALESCE(ProcessCode, N''))))
        FROM dbo.tbl_Lot
        WHERE LotCode = @Barcode
        ORDER BY LotID DESC;

    IF @LotID IS NULL
        THROW 51432, 'Barcode was not found in inbound source tables.', 1;
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

    IF @PoID IS NULL
        SELECT TOP (1) @PoID = PoID
        FROM dbo.WH_Receiving
        WHERE LotCode IN (@Barcode, (SELECT LotCode FROM dbo.tbl_Lot WHERE LotID = @LotID))
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
        WHERE LotCode IN (@Barcode, (SELECT LotCode FROM dbo.tbl_Lot WHERE LotID = @LotID))
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

    UPDATE dbo.WH_InboundPackage
       SET Status = N'Open',
           ReceivedAt = NULL,
           ReceivedBy = NULL,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE BoxBarcode = @Barcode;

    IF @InboundDocumentID IS NOT NULL
    BEGIN
        UPDATE dbo.WH_InboundDocument
           SET Status = CASE WHEN EXISTS
               (
                   SELECT 1
                   FROM dbo.WH_InboundPackage
                   WHERE InboundDocumentID = @InboundDocumentID
                     AND Status = N'Received'
               ) THEN N'InProgress' ELSE N'Open' END,
               ModifiedBy = @User,
               ModifiedTS = SYSDATETIME()
         WHERE InboundDocumentID = @InboundDocumentID;
    END;

    COMMIT TRANSACTION;

    EXEC dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode = @Mode, @LotBarcode = @Barcode;
END;
GO
