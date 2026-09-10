-- =====================================================================
--  PDA_SCHEMA.sql
--  Consolidated Warehouse and Finished Goods schema for the PDA
--
--  Apply after dist/AMES_Schema.sql:
--    sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\pda\PDA_SCHEMA.sql
--
--  Keep all PDA WH/FG tables, columns, indexes and procedures in this file.
-- =====================================================================
USE [AMES_DEV];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

-- =====================================================================
--  WH Transactions
-- =====================================================================
SET NOCOUNT ON;
GO

-- =====================================================================
--  Inventory transaction table
-- =====================================================================
IF OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_InventoryTransaction
    (
        TransactionID bigint IDENTITY(1,1) NOT NULL,
        TransactionTime datetime2 NOT NULL CONSTRAINT DF_WH_InventoryTransaction_Time DEFAULT SYSDATETIME(),
        TransactionType varchar(10) NOT NULL,
        ItemNo varchar(20) NULL,
        LocationID varchar(20) NULL,
        LotID int NULL,
        QtyBefore decimal(14,3) NULL,
        QtyChange decimal(14,3) NOT NULL CONSTRAINT DF_WH_InventoryTransaction_QtyChange DEFAULT 0,
        QtyAfter decimal(14,3) NULL,
        ReasonCode varchar(30) NULL,
        RefDocType varchar(30) NULL,
        RefDocID int NULL,
        OperatorID nvarchar(450) NULL,
        ApproverID nvarchar(450) NULL,
        Note nvarchar(500) NULL,
        CreatedBy varchar(50) NOT NULL CONSTRAINT DF_WH_InventoryTransaction_CreatedBy DEFAULT 'system',
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_WH_InventoryTransaction_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedTS datetime2 NULL,
        ModifiedBy nvarchar(450) NULL,
        CONSTRAINT PK_WH_InventoryTransaction PRIMARY KEY CLUSTERED (TransactionID),
        CONSTRAINT CK_WH_InventoryTransaction_Type CHECK (TransactionType IN ('IN', 'OUT', 'ADJ'))
    );
END;
GO

IF OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_TransactionHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.WH_InventoryTransaction)
BEGIN
    INSERT INTO dbo.WH_InventoryTransaction
    (
        TransactionTime, TransactionType, ItemNo, LocationID, LotID,
        QtyBefore, QtyChange, QtyAfter, ReasonCode, RefDocType, RefDocID,
        OperatorID, ApproverID, Note, CreatedBy, CreatedTS, ModifiedTS, ModifiedBy
    )
    SELECT
        COALESCE(TxnTime, CreatedTS, SYSDATETIME()),
        CASE WHEN TxnType IN ('IN', 'OUT', 'ADJ') THEN TxnType ELSE 'ADJ' END,
        ItemNo,
        LocationID,
        LotID,
        QtyBefore,
        COALESCE(Delta, 0),
        QtyAfter,
        ReasonCode,
        RefDocType,
        RefDocID,
        OperatorID,
        ApproverID,
        Note,
        COALESCE(CreatedBy, 'system'),
        COALESCE(CreatedTS, SYSDATETIME()),
        ModifiedTS,
        ModifiedBy
    FROM dbo.WH_TransactionHistory;
END;
GO

-- WH adjustment history consolidation. Preserve legacy reference IDs for audit;
-- new adjustments are recorded directly as ADJ transactions without approval.
IF OBJECT_ID(N'dbo.WH_InventoryAdjust', N'U') IS NOT NULL
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime, TransactionType, ItemNo, LocationID, LotID,
         QtyBefore, QtyChange, QtyAfter, ReasonCode, RefDocType, RefDocID,
         OperatorID, ApproverID, Note, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT COALESCE(A.CreatedTS, SYSDATETIME()), 'ADJ', A.ItemNo, A.LocationID, A.LotID,
           A.QtyBefore, COALESCE(A.Delta, 0), A.QtyAfter, A.ReasonCode,
           'WH_InventoryAdjust', A.AdjustID, COALESCE(A.RequestedBy, A.CreatedBy),
           A.ApprovedBy, A.ReasonNote, A.CreatedBy, COALESCE(A.CreatedTS, SYSDATETIME()),
           A.ModifiedBy, A.ModifiedTS
    FROM dbo.WH_InventoryAdjust A
    WHERE NOT EXISTS
        (SELECT 1 FROM dbo.WH_InventoryTransaction T
         WHERE T.RefDocType='WH_InventoryAdjust' AND T.RefDocID=A.AdjustID);
    DROP TABLE dbo.WH_InventoryAdjust;
    COMMIT TRANSACTION;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InventoryTransaction_Time' AND object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    CREATE INDEX IX_WH_InventoryTransaction_Time ON dbo.WH_InventoryTransaction (TransactionTime DESC, TransactionID DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InventoryTransaction_Search' AND object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    CREATE INDEX IX_WH_InventoryTransaction_Search ON dbo.WH_InventoryTransaction (TransactionType, ItemNo, LocationID, LotID);
GO

IF OBJECT_ID(N'dbo.WH_PDA_OPERATION_LOG_WRITE', N'P') IS NOT NULL
    DROP PROCEDURE dbo.WH_PDA_OPERATION_LOG_WRITE;
GO

IF OBJECT_ID(N'dbo.WH_WEB_LOG_HISTORY_LIST', N'P') IS NOT NULL
    DROP PROCEDURE dbo.WH_WEB_LOG_HISTORY_LIST;
GO

IF OBJECT_ID(N'dbo.WH_OperationLog', N'U') IS NOT NULL
    DROP TABLE dbo.WH_OperationLog;
GO

IF OBJECT_ID(N'dbo.WH_InventorySnapshot', N'U') IS NOT NULL
    DROP TABLE dbo.WH_InventorySnapshot;
GO

-- =====================================================================
--  PDA transaction list
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_TRANSACTION_LIST
    @SearchText nvarchar(120) = NULL,
    @DateFrom date = NULL,
    @DateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Search nvarchar(120) = NULLIF(LTRIM(RTRIM(@SearchText)), N'');
    DECLARE @Like nvarchar(130) = CASE WHEN @Search IS NULL THEN NULL ELSE N'%' + @Search + N'%' END;
    DECLARE @From date = COALESCE(@DateFrom, DATEADD(day, -30, CONVERT(date, SYSDATETIME())));
    DECLARE @To date = COALESCE(@DateTo, CONVERT(date, SYSDATETIME()));

    SELECT
        ROW_NUMBER() OVER (ORDER BY T.TransactionTime DESC, T.TransactionID DESC) AS ROW_NO,
        L.LotCode AS LOTNO,
        T.ItemNo AS PARTNO,
        CONVERT(nvarchar(10), T.TransactionTime, 23) AS WDATE,
        CONVERT(nvarchar(8), T.TransactionTime, 108) AS WTIME,
        T.LocationID AS LOCATION_NO,
        CASE WHEN T.TransactionType IN ('IN', 'OUT') THEN ABS(T.QtyChange)
             ELSE T.QtyChange END AS QTY,
        I.DefaultUOM AS UNIT,
        CASE T.TransactionType
            WHEN 'IN' THEN N'In'
            WHEN 'OUT' THEN N'Out'
            WHEN 'ADJ' THEN N'Adjust'
            ELSE T.TransactionType
        END AS STATUS,
        T.TransactionType AS DIRECTION,
        T.OperatorID AS WORKER_ID,
        T.ReasonCode AS REASON_CODE,
        T.Note AS REASON_NOTE,
        T.ApproverID AS SUPERVISOR,
        T.QtyBefore AS BEFORE_QTY,
        T.QtyChange AS DELTA_QTY,
        T.QtyAfter AS AFTER_QTY,
        CASE WHEN T.TransactionType = 'ADJ' THEN N'QTY BEFORE' ELSE NULL END AS BEFORE_STATUS,
        CASE WHEN T.TransactionType = 'ADJ' THEN N'QTY AFTER' ELSE NULL END AS AFTER_STATUS,
        T.LocationID AS BEFORE_LOCATION,
        T.LocationID AS AFTER_LOCATION,
        N'WH_InventoryTransaction' AS SOURCE,
        T.Note AS NOTE
    FROM dbo.WH_InventoryTransaction T
    LEFT JOIN dbo.tbl_Lot L
           ON L.LotID = T.LotID
    LEFT JOIN dbo.MD_Item I
           ON I.ItemNo = T.ItemNo
    WHERE T.TransactionTime >= @From
      AND T.TransactionTime < DATEADD(day, 1, @To)
      AND (@Like IS NULL
           OR L.LotCode LIKE @Like
           OR T.ItemNo LIKE @Like
           OR T.LocationID LIKE @Like
           OR EXISTS (
               SELECT 1
               FROM dbo.WH_InboundPackage P
               WHERE P.LotID = T.LotID
                 AND (P.BoxBarcode LIKE @Like
                      OR P.CaseNo LIKE @Like
                      OR (P.ReceiveType = N'CKD' AND P.DocumentBarcode LIKE @Like))))
    ORDER BY T.TransactionTime DESC, T.TransactionID DESC;
END;
GO

IF OBJECT_ID(N'dbo.SYS_Screen', N'U') IS NOT NULL
BEGIN
    DECLARE @UpdateWh004Sql nvarchar(max) = N'
        UPDATE dbo.SYS_Screen
           SET ScreenName = N''Log History'',
               ScreenNameEn = N''Log History'',
               HRef = N''wh/log-history'',
               ModifiedBy = N''PDA_SCHEMA'',
               ModifiedTS = SYSDATETIME()
         WHERE ModuleCode = N''WEB''
           AND ScreenCode = N''WH-004''';

    IF COL_LENGTH(N'dbo.SYS_Screen', N'ProcessCode') IS NOT NULL
        SET @UpdateWh004Sql += N' AND ProcessCode = N''WH''';

    EXEC sys.sp_executesql @UpdateWh004Sql;
END;
GO
GO

-- =====================================================================
--  WH Inbound
-- =====================================================================
-- =====================================================================
--  PDA Warehouse Inbound database contract
-- =====================================================================
IF OBJECT_ID(N'dbo.WH_InboundPackage', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_InboundPackage
    (
        InboundPackageID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WH_InboundPackage PRIMARY KEY,
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
        CONSTRAINT CK_WH_InboundPackage_Type CHECK (ReceiveType IN (N'LOCAL', N'CKD'))
    );

    CREATE INDEX IX_WH_InboundPackage_DocumentBarcode
        ON dbo.WH_InboundPackage(DocumentBarcode, ItemNo);
    CREATE INDEX IX_WH_InboundPackage_Lot
        ON dbo.WH_InboundPackage(LotID);
END;
GO

IF COL_LENGTH(N'dbo.WH_InboundPackage', N'ReceiveType') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD ReceiveType nvarchar(10) NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'DocumentBarcode') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD DocumentBarcode nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'DocumentNo') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD DocumentNo nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'VendorID') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD VendorID varchar(20) NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'CaseNo') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD CaseNo nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'InvoiceNo') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD InvoiceNo nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'ContainerNo') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD ContainerNo nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'ShipDate') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD ShipDate date NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'PackDate') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD PackDate date NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'DeliveryDate') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD DeliveryDate date NULL;
IF COL_LENGTH(N'dbo.WH_InboundPackage', N'ArrivalDate') IS NULL
    ALTER TABLE dbo.WH_InboundPackage ADD ArrivalDate date NULL;
GO

IF OBJECT_ID(N'dbo.WH_InboundDocument', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.WH_InboundPackage', N'InboundDocumentID') IS NOT NULL
BEGIN
    UPDATE P
       SET ReceiveType = D.ReceiveType,
           DocumentBarcode = D.DocumentBarcode,
           DocumentNo = D.DocumentNo,
           VendorID = D.VendorID,
           CaseNo = D.CaseNo,
           InvoiceNo = D.InvoiceNo,
           ContainerNo = D.ContainerNo,
           ShipDate = D.ShipDate,
           PackDate = D.PackDate,
           DeliveryDate = D.DeliveryDate,
           ArrivalDate = D.ArrivalDate
    FROM dbo.WH_InboundPackage P
    JOIN dbo.WH_InboundDocument D ON D.InboundDocumentID = P.InboundDocumentID;
END;
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_WH_InboundPackage_Document')
    ALTER TABLE dbo.WH_InboundPackage DROP CONSTRAINT FK_WH_InboundPackage_Document;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InboundPackage_Document' AND object_id = OBJECT_ID(N'dbo.WH_InboundPackage'))
    DROP INDEX IX_WH_InboundPackage_Document ON dbo.WH_InboundPackage;
GO

IF COL_LENGTH(N'dbo.WH_InboundPackage', N'InboundDocumentID') IS NOT NULL
    ALTER TABLE dbo.WH_InboundPackage DROP COLUMN InboundDocumentID;
GO

IF OBJECT_ID(N'dbo.WH_InboundDocument', N'U') IS NOT NULL
    DROP TABLE dbo.WH_InboundDocument;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InboundPackage_DocumentBarcode' AND object_id = OBJECT_ID(N'dbo.WH_InboundPackage'))
    CREATE INDEX IX_WH_InboundPackage_DocumentBarcode ON dbo.WH_InboundPackage(DocumentBarcode, ItemNo);
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
    DECLARE @ActualMode nvarchar(10);

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;

    SELECT TOP (1) @ActualMode = ReceiveType
    FROM dbo.WH_InboundPackage
    WHERE DocumentBarcode = @Barcode;

    IF @ActualMode IS NOT NULL AND @ActualMode <> @Mode
        THROW 51403, 'Barcode receive mode does not match the selected tab.', 1;

    SELECT
        MAX(P.ReceiveType) AS RECEIVE_TYPE,
        MIN(P.InboundPackageID) AS INBOUND_DOCUMENT_ID,
        MAX(P.DocumentBarcode) AS DOCUMENT_BARCODE,
        MAX(P.DocumentNo) AS DOCUMENT_NO,
        MAX(P.VendorID) AS VENDCD,
        MAX(COALESCE(V.VendorName, P.VendorID)) AS VENDNM,
        MAX(P.CaseNo) AS CASE_NO,
        MAX(P.InvoiceNo) AS INVOICE_NO,
        MAX(P.ContainerNo) AS CONTAINER_NO,
        MAX(P.ShipDate) AS SHIP_DATE,
        MAX(P.PackDate) AS PACK_DATE,
        MAX(P.DeliveryDate) AS DELI_DATE,
        MAX(P.ArrivalDate) AS ARRIV_DATE,
        COUNT(P.InboundPackageID) AS TOTAL_BOXES,
        SUM(CASE WHEN AI.LotID IS NULL THEN 0 ELSE 1 END) AS SCANNED_BOXES,
        CASE WHEN COUNT(P.InboundPackageID) > 0
                   AND COUNT(P.InboundPackageID) = SUM(CASE WHEN AI.LotID IS NULL THEN 0 ELSE 1 END)
             THEN N'Y' ELSE N'N' END AS YN
    FROM dbo.WH_InboundPackage P
    LEFT JOIN dbo.MD_Vendor V ON V.VendorID = P.VendorID
    LEFT JOIN
    (
        SELECT DISTINCT LotID
        FROM dbo.WH_Inventory
        WHERE COALESCE(Status, 'Received') <> 'Canceled'
          AND COALESCE(OnHandQty, 0) > 0
    ) AI ON AI.LotID = P.LotID
    WHERE P.DocumentBarcode = @Barcode
    HAVING COUNT(P.InboundPackageID) > 0;

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
    WHERE P.DocumentBarcode = @Barcode
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
    WHERE P.DocumentBarcode = @Barcode
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

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N''
        THROW 51401, 'Box barcode is required.', 1;

    SELECT TOP (1)
        @PackageID = P.InboundPackageID,
        @LotID = P.LotID,
        @ActualMode = P.ReceiveType
    FROM dbo.WH_InboundPackage P
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
            P.ReceiveType,
            P.DocumentBarcode,
            P.DocumentNo,
            P.VendorID,
            P.CaseNo,
            P.InvoiceNo,
            P.ContainerNo,
            P.ShipDate,
            P.PackDate,
            P.DeliveryDate,
            P.ArrivalDate,
            P.BoxBarcode,
            P.PoID,
            P.Qty,
            P.UnitCode,
            P.ProductionDate
        FROM dbo.WH_InboundPackage P
        WHERE P.InboundPackageID = @PackageID
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
             ELSE N'dbo.WH_InboundPackage' END AS SOURCE_TABLE,
        CASE WHEN @Mode = N'LOCAL' THEN MP.DocumentNo ELSE P.PoNumber END AS NOTENO,
        CASE WHEN @Mode = N'CKD' THEN MP.DocumentBarcode ELSE NULL END AS CASE_BARCODE,
        CASE WHEN @Mode = N'CKD' THEN MP.CaseNo ELSE NULL END AS CASE_NO,
        MP.InvoiceNo AS INVOICE_NO,
        MP.ContainerNo AS CONTAINER_NO,
        L.ItemNo AS PARTNO,
        L.ItemName AS PARTNM,
        COALESCE(A.OnHandQty, NULLIF(MP.Qty, 0), NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), P.OrderQty, 0) AS QTY,
        COALESCE(MP.UnitCode, P.UnitCode, L.DefaultUOM) AS UNIT,
        P.PoNumber AS PONO,
        P.PoLineNo AS PONO_SEQ,
        COALESCE(P.VendorID, MP.VendorID) AS VENDCD,
        COALESCE(V.VendorName, P.VendorID, MP.VendorID) AS VENDNM,
        COALESCE(MP.ProductionDate, CONVERT(date, L.ProducedAt)) AS PROD_DATE,
        COALESCE(MP.DeliveryDate, P.DueDate) AS DELI_DATE,
        COALESCE(MP.ArrivalDate, P.DueDate) AS ARRIV_DATE,
        MP.ShipDate AS SHIP_DATE,
        MP.PackDate AS PACK_DATE,
        COALESCE(A.LocationID, L.CurrentLocationID) AS RECEIVED_LOCATION,
        COALESCE(A.Status, L.LotStatus) AS RECEIVED_STATUS
    FROM MatchedLot L
    LEFT JOIN MatchedPackage MP ON 1 = 1
    LEFT JOIN ActiveInventory A
           ON A.LotID = L.LotID
    LEFT JOIN MatchedPo P
           ON 1 = 1
    LEFT JOIN dbo.MD_Vendor V
           ON V.VendorID = COALESCE(P.VendorID, MP.VendorID);
END;
GO

-- =====================================================================
--  Inbound / Receive LOT
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_PPT_TEST_RESET
    @Screen varchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Screen NOT IN ('release', 'inventory', 'adjust', 'history')
        THROW 51522, 'Unknown PPT test screen.', 1;
    BEGIN TRANSACTION;
    DECLARE @Lots TABLE (LotID int, LotCode varchar(40), ItemNo varchar(20), Qty decimal(14,3), LocationID varchar(20), ProducedAt datetime2);
    INSERT INTO @Lots
    SELECT LotID, LotCode, ItemNo, BatchSize,
           CASE WHEN LotCode IN ('5011LL260908810002', '5011LL260908810003', '5011LL260908840001')
                THEN 'B0-10-B1' ELSE 'B0-10-A1' END, ProducedAt
    FROM dbo.tbl_Lot WITH (UPDLOCK, HOLDLOCK)
    WHERE CreatedBy = CONCAT('pda-ppt-', @Screen)
      AND LotCode IN ('5011LL260908810001', '5011LL260908810002', '5011LL260908810003', '5011LL260908810004',
          '5011LL260908820001', '5011LL260908820002', '5011LL260908820003', '5011LL260908830001', '5011LL260908840001');
    IF (SELECT COUNT(*) FROM @Lots) <> CASE @Screen WHEN 'release' THEN 4 WHEN 'inventory' THEN 3 ELSE 1 END
        THROW 51523, 'PPT test data is missing. Apply PDA_SEED.sql first.', 1;

    DELETE FROM dbo.WH_InventoryTransaction WHERE LotID IN (SELECT LotID FROM @Lots);
    DELETE FROM dbo.WH_ReleasePicking WHERE LotID IN (SELECT LotID FROM @Lots);
    DELETE FROM dbo.WH_Inventory WHERE LotID IN (SELECT LotID FROM @Lots);
    INSERT INTO dbo.WH_Inventory
        (ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt, Status, CreatedBy)
    SELECT ItemNo, LocationID, LotID, Qty, 0, ProducedAt, 'Received', CONCAT('pda-ppt-', @Screen) FROM @Lots;
    UPDATE Lot
    SET RemainingQty = Sample.Qty, CurrentLocationID = Sample.LocationID, Status = 'Received',
        InventoryStatus = 'RECEIVED', ModifiedBy = 'TEST1', ModifiedTS = SYSDATETIME()
    FROM dbo.tbl_Lot Lot JOIN @Lots Sample ON Sample.LotID = Lot.LotID;

    IF @Screen = 'release'
    BEGIN
        IF (SELECT COUNT(*) FROM dbo.WH_ReleaseSchedule WHERE PickSlipNo = 'PS-PPT-WH-01' AND CreatedBy = 'pda-ppt-release') <> 2
            THROW 51524, 'PPT Pick Slip is missing. Apply PDA_SEED.sql first.', 1;
        UPDATE dbo.WH_ReleaseSchedule
        SET PickedQty = 0, Status = 'Open', CloseDate = NULL, CloseUserId = NULL,
            RequiredAt = SYSDATETIME(), ModifiedBy = 'TEST1', ModifiedTS = SYSDATETIME()
        WHERE PickSlipNo = 'PS-PPT-WH-01' AND CreatedBy = 'pda-ppt-release';
    END;
    IF @Screen = 'history'
    BEGIN
        DECLARE @Today datetime2 = CONVERT(date, SYSDATETIME());
        INSERT INTO dbo.WH_InventoryTransaction
            (TransactionTime, TransactionType, ItemNo, LocationID, LotID, QtyBefore, QtyChange, QtyAfter,
             ReasonCode, OperatorID, ApproverID, Note, CreatedBy, CreatedTS)
        SELECT DATEADD(second, Sample.OffsetSeconds, @Today), Sample.Kind, Lot.ItemNo, Lot.LocationID, Lot.LotID,
               Sample.BeforeQty, Sample.DeltaQty, Sample.AfterQty, Sample.Reason, 'TEST1',
               NULL, Sample.Note, 'pda-ppt-history', DATEADD(second, Sample.OffsetSeconds, @Today)
        FROM @Lots Lot CROSS JOIN (VALUES
            (1, 'IN', 0, 20, 20, 'INBOUND_RECEIVE', N'PPT inbound 20 EA'),
            (2, 'OUT', 20, -4, 16, 'PRODUCTION', N'PPT outgoing 4 EA'),
            (3, 'ADJ', 16, 2, 18, 'COUNT_DIFF', N'PPT count correction +2 EA')
        ) Sample(OffsetSeconds, Kind, BeforeQty, DeltaQty, AfterQty, Reason, Note);
    END;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_SIMPLE_TEST_RESET
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @Lots TABLE (LotID int, Barcode varchar(40), PoID int);
    INSERT INTO @Lots
    SELECT Package.LotID, Package.BoxBarcode, Package.PoID
    FROM dbo.WH_InboundPackage Package WITH (UPDLOCK, HOLDLOCK)
    JOIN dbo.tbl_Lot Lot ON Lot.LotID = Package.LotID
    JOIN dbo.WH_PurchaseOrder Purchase ON Purchase.PoID = Package.PoID
    WHERE Package.CreatedBy = 'pda-simple-inbound'
      AND Lot.CreatedBy = 'pda-simple-inbound'
      AND Purchase.CreatedBy = 'pda-simple-inbound'
      AND Purchase.PoNumber = 'PPT-INBOUND'
      AND Package.BoxBarcode IN
        ('5011LL260908800001', '5011LL260908800002', '5011LL260908800003',
         'CKD260908800000001', 'CKD260908800000002', 'CKD260908800000003');

    IF (SELECT COUNT(*) FROM @Lots) <> 6
        THROW 51521, 'PPT Inbound test data is missing. Apply PDA_SEED.sql first.', 1;

    DELETE FROM dbo.WH_InventoryTransaction WHERE LotID IN (SELECT LotID FROM @Lots);
    DELETE FROM dbo.WH_Inventory WHERE LotID IN (SELECT LotID FROM @Lots);
    DELETE FROM dbo.WH_Receiving WHERE LotCode IN (SELECT Barcode FROM @Lots);
    UPDATE dbo.tbl_Lot
    SET Status = 'Open', CurrentLocationID = NULL, RemainingQty = BatchSize,
        InventoryStatus = NULL, ModifiedBy = 'TEST1', ModifiedTS = SYSDATETIME()
    WHERE LotID IN (SELECT LotID FROM @Lots);
    UPDATE dbo.WH_InboundPackage
    SET Status = N'Open', ReceivedAt = NULL, ReceivedBy = NULL,
        ModifiedBy = 'TEST1', ModifiedTS = SYSDATETIME()
    WHERE LotID IN (SELECT LotID FROM @Lots);
    UPDATE dbo.WH_PurchaseOrder
    SET ReceivedQty = 0, Status = 'Open', ModifiedBy = 'TEST1', ModifiedTS = SYSDATETIME()
    WHERE PoID IN (SELECT PoID FROM @Lots);
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_RECEIVE_LOT
    @ReceiveMode nvarchar(10),
    @LotBarcode nvarchar(50),
    @LocationId nvarchar(30),
    @UserId nvarchar(40),
    @SimulateFailure bit = 0
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
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = @Location)
        THROW 51413, 'Location No was not found.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.MD_Location
        WHERE LocationID = @Location
          AND
          (
              COALESCE(ActiveFlag, 1) = 0
              OR UPPER(COALESCE(LocationType, N'')) IN (N'BLOCKED', N'HOLD', N'QUARANTINE', N'INACTIVE')
          )
    )
        THROW 51418, 'Location is blocked for inbound.', 1;

    DECLARE
        @LotID int,
        @ItemNo varchar(20),
        @ActualMode nvarchar(10),
        @Qty decimal(14,3),
        @PoID int,
        @VendorID varchar(20),
        @ReceivingID int;

    SELECT TOP (1)
        @LotID = P.LotID,
        @ItemNo = P.ItemNo,
        @ActualMode = P.ReceiveType,
        @Qty = P.Qty,
        @PoID = P.PoID,
        @VendorID = P.VendorID
    FROM dbo.WH_InboundPackage P
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
        FROM dbo.MD_Location L
        OUTER APPLY
        (
            SELECT SUM(COALESCE(W.OnHandQty, 0)) AS CurrentQty
            FROM dbo.WH_Inventory W
            WHERE W.LocationID = L.LocationID
              AND COALESCE(W.OnHandQty, 0) > 0
              AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
        ) S
        WHERE L.LocationID = @Location
          AND COALESCE(L.Capacity, 0) > 0
          AND COALESCE(S.CurrentQty, 0) + @Qty > L.Capacity
    )
        THROW 51419, 'Location capacity would be exceeded.', 1;
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

    IF @SimulateFailure = 1
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51518, 'Simulated receive API failure. Database transaction was rolled back.', 1;
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
        @ActualMode = P.ReceiveType
    FROM dbo.WH_InboundPackage P
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

    IF @Mode NOT IN (N'LOCAL', N'CKD')
        THROW 51430, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N''
        THROW 51431, 'LOT barcode is required.', 1;

    SELECT TOP (1)
        @LotID = P.LotID,
        @ActualMode = P.ReceiveType,
        @PoID = P.PoID
    FROM dbo.WH_InboundPackage P
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

    COMMIT TRANSACTION;

    EXEC dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode = @Mode, @LotBarcode = @Barcode;
END;
GO
GO

-- =====================================================================
--  WH Inventory
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_STATUS_LIST
    @SearchText nvarchar(80) = NULL,
    @StockDateFrom date = NULL,
    @StockDateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Q nvarchar(80) = NULLIF(LTRIM(RTRIM(@SearchText)), N'');

    ;WITH ActiveStock AS
    (
        SELECT
            W.ItemNo,
            MIN(W.InventoryID) AS INVENTORY_ID,
            SUM(COALESCE(W.OnHandQty, 0)) AS SUM_QTY,
            SUM(COALESCE(W.ReservedQty, 0)) AS RESERVED_QTY,
            MAX(W.LastReceivedAt) AS LAST_RECEIVED_DATE,
            COUNT(DISTINCT CASE WHEN COALESCE(W.OnHandQty, 0) > 0 THEN W.LotID END) AS LOT_COUNT,
            COUNT(DISTINCT CASE WHEN COALESCE(W.OnHandQty, 0) > 0 THEN W.LocationID END) AS LOCATION_COUNT
        FROM dbo.WH_Inventory W
        WHERE W.ItemNo IS NOT NULL
          AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
          AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
          AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
        GROUP BY W.ItemNo
    ),
    InventoryBase AS
    (
        SELECT
            COALESCE(S.INVENTORY_ID, 0) AS INVENTORY_ID,
            I.ItemNo AS PARTNO,
            I.ItemName AS PARTNM,
            PRI.LotCode AS LOTNO,
            COALESCE(PRI.LocationID, N'-') AS PRIMARY_LOCATION,
            COALESCE(S.SUM_QTY, 0) AS SUM_QTY,
            COALESCE(S.RESERVED_QTY, 0) AS RESERVED_QTY,
            S.LAST_RECEIVED_DATE,
            I.CarType AS VINCD,
            I.DefaultUOM AS UNIT,
            CAST(NULL AS decimal(18,3)) AS MIN_INV_DAY,
            COALESCE(I.MinStock, 0) AS MIN_INV_QTY,
            CAST(NULL AS decimal(18,3)) AS MAX_INV_DAY,
            COALESCE(I.MaxStock, 0) AS MAX_INV_QTY,
            COALESCE(S.LOT_COUNT, 0) AS LOT_COUNT,
            COALESCE(S.LOCATION_COUNT, 0) AS LOCATION_COUNT
        FROM dbo.MD_Item I
        LEFT JOIN ActiveStock S
               ON S.ItemNo = I.ItemNo
        OUTER APPLY
        (
            SELECT TOP (1)
                W.LocationID,
                LOT.LotCode
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Location L
                   ON L.LocationID = W.LocationID
            LEFT JOIN dbo.tbl_Lot LOT
                   ON LOT.LotID = W.LotID
            WHERE W.ItemNo = I.ItemNo
              AND COALESCE(W.OnHandQty, 0) > 0
              AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
              AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
              AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
            ORDER BY
                CASE
                    WHEN @Q IS NOT NULL
                     AND
                     (
                         W.LocationID LIKE N'%' + @Q + N'%'
                         OR L.LocationName LIKE N'%' + @Q + N'%'
                         OR L.ZoneCode LIKE N'%' + @Q + N'%'
                         OR L.Aisle LIKE N'%' + @Q + N'%'
                         OR L.Bay LIKE N'%' + @Q + N'%'
                         OR L.Slot LIKE N'%' + @Q + N'%'
                     )
                    THEN 0
                    ELSE 1
                END,
                W.LastReceivedAt DESC,
                W.InventoryID DESC
        ) PRI
        WHERE COALESCE(I.ActiveFlag, 1) = 1
          AND
          (
              S.ItemNo IS NOT NULL
              OR COALESCE(I.MinStock, 0) > 0
              OR COALESCE(I.MaxStock, 0) > 0
          )
          AND
          (
              @Q IS NULL
              OR I.ItemNo LIKE N'%' + @Q + N'%'
              OR I.ItemName LIKE N'%' + @Q + N'%'
              OR I.CarType LIKE N'%' + @Q + N'%'
              OR EXISTS
              (
                  SELECT 1
                  FROM dbo.tbl_Lot L
                  WHERE L.ItemNo = I.ItemNo
                    AND L.LotCode LIKE N'%' + @Q + N'%'
              )
              OR EXISTS
              (
                  SELECT 1
                  FROM dbo.WH_Inventory W
                  LEFT JOIN dbo.MD_Location L
                         ON L.LocationID = W.LocationID
                  WHERE W.ItemNo = I.ItemNo
                    AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
                    AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
                    AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
                    AND
                    (
                        W.LocationID LIKE N'%' + @Q + N'%'
                        OR L.LocationName LIKE N'%' + @Q + N'%'
                        OR L.ZoneCode LIKE N'%' + @Q + N'%'
                        OR L.Aisle LIKE N'%' + @Q + N'%'
                        OR L.Bay LIKE N'%' + @Q + N'%'
                        OR L.Slot LIKE N'%' + @Q + N'%'
                    )
              )
          )
    ),
    Statused AS
    (
        SELECT *,
            CASE
                WHEN SUM_QTY <= 0 THEN N'OUT'
                WHEN MIN_INV_QTY > 0 AND SUM_QTY < MIN_INV_QTY THEN N'BELOW_MIN'
                WHEN MAX_INV_QTY > 0 AND SUM_QTY > MAX_INV_QTY THEN N'OVER_MAX'
                ELSE N'NORMAL'
            END AS STATUS
        FROM InventoryBase
    )
    SELECT TOP (300)
        INVENTORY_ID,
        PARTNO,
        PARTNM,
        LOTNO,
        PRIMARY_LOCATION,
        SUM_QTY,
        RESERVED_QTY,
        LAST_RECEIVED_DATE,
        VINCD,
        UNIT,
        MIN_INV_DAY,
        MIN_INV_QTY,
        MAX_INV_DAY,
        MAX_INV_QTY,
        LOT_COUNT,
        LOCATION_COUNT,
        STATUS,
        CASE STATUS
            WHEN N'OUT' THEN N'Out'
            WHEN N'BELOW_MIN' THEN N'Below Min'
            WHEN N'OVER_MAX' THEN N'Over Max'
            ELSE N'Normal'
        END AS STATUSNM
    FROM Statused
    ORDER BY
        CASE STATUS
            WHEN N'OUT' THEN 1
            WHEN N'BELOW_MIN' THEN 2
            WHEN N'OVER_MAX' THEN 3
            ELSE 4
        END,
        PARTNO;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_SCAN_LOOKUP
    @ScanText nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Scan nvarchar(80) = NULLIF(LTRIM(RTRIM(@ScanText)), N'');

    IF @Scan IS NULL
    BEGIN
        SELECT N'TEXT' AS SEARCH_KIND, N'' AS SEARCH_TEXT, CAST(NULL AS nvarchar(120)) AS DISPLAY_TEXT;
        RETURN;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MD_Location L
        WHERE UPPER(L.LocationID) = UPPER(@Scan)
          AND COALESCE(L.ActiveFlag, 1) = 1
    )
    BEGIN
        SELECT TOP (1)
            N'LOCATION' AS SEARCH_KIND,
            CAST(L.LocationID AS nvarchar(80)) AS SEARCH_TEXT,
            L.LocationName AS DISPLAY_TEXT
        FROM dbo.MD_Location L
        WHERE UPPER(L.LocationID) = UPPER(@Scan)
          AND COALESCE(L.ActiveFlag, 1) = 1;
        RETURN;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.tbl_Lot L
        WHERE UPPER(L.LotCode) = UPPER(@Scan)
           OR UPPER(L.ItemNo) = UPPER(@Scan)
    )
    BEGIN
        SELECT TOP (1)
            N'PART' AS SEARCH_KIND,
            CAST(L.ItemNo AS nvarchar(80)) AS SEARCH_TEXT,
            I.ItemName AS DISPLAY_TEXT
        FROM dbo.tbl_Lot L
        LEFT JOIN dbo.MD_Item I
               ON I.ItemNo = L.ItemNo
        WHERE UPPER(L.LotCode) = UPPER(@Scan)
           OR UPPER(L.ItemNo) = UPPER(@Scan)
        ORDER BY
            CASE WHEN UPPER(L.LotCode) = UPPER(@Scan) THEN 0 ELSE 1 END,
            L.LotID DESC;
        RETURN;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MD_Item I
        WHERE UPPER(I.ItemNo) = UPPER(@Scan)
          AND COALESCE(I.ActiveFlag, 1) = 1
    )
    BEGIN
        SELECT TOP (1)
            N'PART' AS SEARCH_KIND,
            CAST(I.ItemNo AS nvarchar(80)) AS SEARCH_TEXT,
            I.ItemName AS DISPLAY_TEXT
        FROM dbo.MD_Item I
        WHERE UPPER(I.ItemNo) = UPPER(@Scan)
          AND COALESCE(I.ActiveFlag, 1) = 1;
        RETURN;
    END;

    SELECT N'TEXT' AS SEARCH_KIND, @Scan AS SEARCH_TEXT, CAST(NULL AS nvarchar(120)) AS DISPLAY_TEXT;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_LOCATION_LIST
    @ItemNo nvarchar(40),
    @StockDateFrom date = NULL,
    @StockDateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PartNo nvarchar(40) = NULLIF(LTRIM(RTRIM(@ItemNo)), N'');

    SELECT
        ROW_NUMBER() OVER (ORDER BY COALESCE(L.LocationID, W.LocationID)) AS ROW_NO,
        W.ItemNo AS PARTNO,
        COALESCE(L.LocationID, W.LocationID, N'-') AS LOCATION_NO,
        L.LocationName AS LOCATION_NM,
        L.PlantCode AS WHCD,
        L.PlantCode AS WHNM,
        L.ZoneCode AS AREACD,
        L.ZoneCode AS AREANM,
        L.ZoneCode AS ZONECD,
        L.LocationName AS ZONENM,
        L.Aisle AS RACK_X,
        L.Bay AS RACK_Y,
        L.Slot AS RACK_Z,
        SUM(COALESCE(W.OnHandQty, 0)) AS SUM_QTY
    FROM dbo.WH_Inventory W
    LEFT JOIN dbo.MD_Location L
           ON L.LocationID = W.LocationID
    WHERE @PartNo IS NOT NULL
      AND W.ItemNo = @PartNo
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
      AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
      AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
    GROUP BY
        W.ItemNo,
        W.LocationID,
        L.LocationID,
        L.LocationName,
        L.PlantCode,
        L.ZoneCode,
        L.Aisle,
        L.Bay,
        L.Slot
    ORDER BY COALESCE(L.LocationID, W.LocationID);
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_LOCATION_CONTENTS
    @LocationId nvarchar(40),
    @StockDateFrom date = NULL,
    @StockDateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LocationNo nvarchar(40) = NULLIF(LTRIM(RTRIM(@LocationId)), N'');

    SELECT
        COALESCE(LOT.LotCode, CONCAT(N'LOT-', W.LotID), N'-') AS LOTNO,
        W.ItemNo AS PARTNO,
        I.ItemName AS PARTNM,
        SUM(COALESCE(W.OnHandQty, 0)) AS QTY,
        I.DefaultUOM AS UNIT,
        COALESCE(W.Status, N'Received') AS INV_STATUS,
        CONVERT(nvarchar(10), MAX(W.LastReceivedAt), 23) AS WORK_DATE,
        CONVERT(nvarchar(8), MAX(W.LastReceivedAt), 108) AS WORK_TIME
    FROM dbo.WH_Inventory W
    LEFT JOIN dbo.tbl_Lot LOT
           ON LOT.LotID = W.LotID
    LEFT JOIN dbo.MD_Item I
           ON I.ItemNo = W.ItemNo
    WHERE @LocationNo IS NOT NULL
      AND UPPER(W.LocationID) = UPPER(@LocationNo)
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
      AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
      AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
    GROUP BY
        COALESCE(LOT.LotCode, CONCAT(N'LOT-', W.LotID), N'-'),
        W.ItemNo,
        I.ItemName,
        I.DefaultUOM,
        COALESCE(W.Status, N'Received')
    ORDER BY W.ItemNo, LOTNO;
END;
GO
GO

-- =====================================================================
--  WH Adjust
-- =====================================================================
SET NOCOUNT ON;
GO

-- =====================================================================
--  Adjust / scan current stock
--  Source: dbo.WH_Inventory, dbo.tbl_Lot, dbo.MD_Item
--  ScanText accepts only LOT No, resolving directly to that inventory LOT.
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_ADJUST_SCAN_STOCK
    @ScanText nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Scan nvarchar(80) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
    DECLARE @LotID int;

    IF @Scan = N''
        THROW 51500, 'Lot No is required.', 1;

    IF OBJECT_ID(N'dbo.FG_Inventory', N'U') IS NOT NULL
       AND EXISTS
       (
           SELECT 1
           FROM dbo.FG_Inventory F
           LEFT JOIN dbo.tbl_Lot FL ON FL.LotID = F.LotID
           WHERE (UPPER(COALESCE(FL.LotCode, N'')) = UPPER(@Scan)
               OR UPPER(COALESCE(F.StockNumber, N'')) = UPPER(@Scan))
             AND UPPER(COALESCE(F.Status, N'Available')) NOT IN
                 (N'CANCELED', N'CANCELLED', N'SHIPPED', N'DELIVERED', N'CLOSED')
       )
        THROW 51505, 'Finished goods cannot be adjusted in Warehouse Adjust.', 1;

    -- Known inventory LOTs can also be selected from a location list (including imported LOT codes).
    -- Unknown scans still follow the legacy barcode lengths and character validation.
    IF @Scan COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9-]%'
       OR NOT (LEN(@Scan) IN (15, 18, 50)
           OR (LEN(@Scan) = 9 AND @Scan COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')
           OR EXISTS (SELECT 1 FROM dbo.WH_Inventory W JOIN dbo.tbl_Lot L ON L.LotID = W.LotID
                      WHERE L.LotCode = @Scan))
        THROW 51504, 'The barcode format is invalid.', 1;

    SELECT TOP (1)
        @LotID = L.LotID
    FROM dbo.tbl_Lot L
    WHERE UPPER(L.LotCode) = UPPER(@Scan)
    ORDER BY L.LotID DESC;

    IF @LotID IS NULL
        THROW 51501, 'The specified Lot No could not be found.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.WH_Inventory W
        WHERE W.LotID = @LotID
          AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    )
        THROW 51503, 'The scanned LOT is not in current inventory.', 1;

    ;WITH CurrentStock AS
    (
        SELECT TOP (1)
            W.InventoryID,
            W.ItemNo,
            W.LocationID,
            W.LotID,
            COALESCE(W.OnHandQty, 0) AS OnHandQty,
            W.Status AS InventoryStatus,
            W.LastReceivedAt,
            W.ExpiryDate,
            L.LotCode,
            L.ProcessCode,
            L.ProducedAt,
            L.Status AS LotStatus,
            I.ItemName,
            I.DefaultUOM
        FROM dbo.WH_Inventory W
        JOIN dbo.tbl_Lot L
          ON L.LotID = W.LotID
        LEFT JOIN dbo.MD_Item I
          ON I.ItemNo = W.ItemNo
        WHERE W.LotID = @LotID
          AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
        ORDER BY
            CASE WHEN COALESCE(W.OnHandQty, 0) > 0 THEN 0 ELSE 1 END,
            W.InventoryID DESC
    )
    SELECT
        CASE
            WHEN UPPER(COALESCE(ProcessCode, N'')) = N'CKD' THEN N'CKD'
            ELSE N'LOCAL'
        END AS RECEIVE_TYPE,
        N'N' AS YN,
        LotCode AS LOTNO,
        LotCode AS BARCODE,
        N'dbo.WH_Inventory/dbo.tbl_Lot' AS SOURCE_TABLE,
        CAST(NULL AS nvarchar(50)) AS NOTENO,
        CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
        CAST(NULL AS nvarchar(30)) AS CASE_NO,
        CAST(NULL AS nvarchar(30)) AS INVOICE_NO,
        CAST(NULL AS nvarchar(30)) AS CONTAINER_NO,
        ItemNo AS PARTNO,
        ItemName AS PARTNM,
        OnHandQty AS QTY,
        DefaultUOM AS UNIT,
        CAST(NULL AS nvarchar(30)) AS PONO,
        CAST(NULL AS int) AS PONO_SEQ,
        CAST(NULL AS nvarchar(30)) AS VENDCD,
        CAST(NULL AS nvarchar(100)) AS VENDNM,
        CONVERT(date, ProducedAt) AS PROD_DATE,
        CAST(NULL AS date) AS DELI_DATE,
        CONVERT(date, LastReceivedAt) AS ARRIV_DATE,
        CAST(NULL AS date) AS SHIP_DATE,
        CAST(NULL AS date) AS PACK_DATE,
        LocationID AS RECEIVED_LOCATION,
        COALESCE(InventoryStatus, LotStatus, N'Received') AS RECEIVED_STATUS
    FROM CurrentStock;
END;
GO

-- =====================================================================
--  Adjust / save quantity change
--  Target: dbo.WH_Inventory, dbo.tbl_Lot
--  Audit:  dbo.WH_InventoryTransaction only (no separate approval workflow)
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_ADJUST_SAVE_QTY
    @ScanText nvarchar(80),
    @DeltaQty decimal(18,3),
    @ReasonCode nvarchar(30),
    @ReasonNote nvarchar(500) = NULL,
    @UserId nvarchar(40),
    @SimulateFailure bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Scan nvarchar(80) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
    DECLARE @Reason nvarchar(30) = UPPER(LTRIM(RTRIM(ISNULL(@ReasonCode, N''))));
    DECLARE @Note nvarchar(500) = NULLIF(LTRIM(RTRIM(@ReasonNote)), N'');
    DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @LotID int;

    IF @Scan = N''
        THROW 51510, 'Lot No is required.', 1;
    IF OBJECT_ID(N'dbo.FG_Inventory', N'U') IS NOT NULL
       AND EXISTS
       (
           SELECT 1
           FROM dbo.FG_Inventory F
           LEFT JOIN dbo.tbl_Lot FL ON FL.LotID = F.LotID
           WHERE (UPPER(COALESCE(FL.LotCode, N'')) = UPPER(@Scan)
               OR UPPER(COALESCE(F.StockNumber, N'')) = UPPER(@Scan))
             AND UPPER(COALESCE(F.Status, N'Available')) NOT IN
                 (N'CANCELED', N'CANCELLED', N'SHIPPED', N'DELIVERED', N'CLOSED')
       )
        THROW 51519, 'Finished goods cannot be adjusted in Warehouse Adjust.', 1;
    IF @Scan COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9-]%'
       OR NOT (LEN(@Scan) IN (15, 18, 50)
           OR (LEN(@Scan) = 9 AND @Scan COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')
           OR EXISTS (SELECT 1 FROM dbo.WH_Inventory W JOIN dbo.tbl_Lot L ON L.LotID = W.LotID
                      WHERE L.LotCode = @Scan))
        THROW 51518, 'The barcode format is invalid.', 1;
    IF COALESCE(@DeltaQty, 0) = 0
        THROW 51511, 'Adjustment quantity must be different from zero.', 1;
    IF @Reason = N''
        THROW 51512, 'Reason code is required.', 1;
    IF NOT EXISTS
       (SELECT 1 FROM dbo.MD_CodeItem
        WHERE GroupCode='INV_ADJUST_REASON' AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
        THROW 51520, 'Unsupported inventory adjustment reason.', 1;

    SELECT TOP (1)
        @LotID = L.LotID
    FROM dbo.tbl_Lot L
    WHERE UPPER(L.LotCode) = UPPER(@Scan)
    ORDER BY L.LotID DESC;

    IF @LotID IS NULL
        THROW 51514, 'The specified Lot No could not be found.', 1;

    DECLARE
        @InventoryID int,
        @ItemNo varchar(20),
        @LocationID varchar(20),
        @LotCode varchar(40),
        @BeforeQty decimal(18,3),
        @AfterQty decimal(18,3);

    BEGIN TRAN;

    SELECT TOP (1)
        @InventoryID = W.InventoryID,
        @ItemNo = W.ItemNo,
        @LocationID = W.LocationID,
        @BeforeQty = COALESCE(W.OnHandQty, 0),
        @LotCode = L.LotCode
    FROM dbo.WH_Inventory W WITH (UPDLOCK, ROWLOCK)
    JOIN dbo.tbl_Lot L
      ON L.LotID = W.LotID
    WHERE W.LotID = @LotID
      AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ORDER BY
        CASE WHEN COALESCE(W.OnHandQty, 0) > 0 THEN 0 ELSE 1 END,
        W.InventoryID DESC;

    IF @InventoryID IS NULL
        THROW 51516, 'The scanned LOT is not in current inventory.', 1;

    SET @AfterQty = @BeforeQty + @DeltaQty;

    IF @AfterQty < 0
        THROW 51517, 'After Qty cannot be below zero.', 1;
    IF @AfterQty <> FLOOR(@AfterQty) OR @AfterQty > 999999999
        THROW 51521, 'New quantity must be a whole number from 0 to 999999999.', 1;

    UPDATE dbo.WH_Inventory
       SET OnHandQty = @AfterQty,
           Status = N'Received',
           ModifiedTS = SYSDATETIME(),
           ModifiedBy = @User
     WHERE InventoryID = @InventoryID;

    UPDATE dbo.tbl_Lot
       SET RemainingQty = @AfterQty,
           CurrentLocationID = COALESCE(@LocationID, CurrentLocationID),
           Status = N'Received',
           ModifiedTS = SYSDATETIME(),
           ModifiedBy = @User
     WHERE LotID = @LotID;

    IF @SimulateFailure = 1
        THROW 51520, 'Simulated Adjust API failure. Database transaction was rolled back.', 1;

    INSERT INTO dbo.WH_InventoryTransaction
        (TransactionType, ItemNo, LocationID, LotID, QtyBefore, QtyChange, QtyAfter,
         ReasonCode, RefDocType, RefDocID, OperatorID, ApproverID, Note, CreatedBy, CreatedTS)
    VALUES
        (N'ADJ', @ItemNo, @LocationID, @LotID, @BeforeQty, @DeltaQty, @AfterQty,
         CONVERT(varchar(30), @Reason), N'LOT', @LotID,
         @User, NULL, @Note, @User, SYSDATETIME());

    COMMIT TRAN;

    EXEC dbo.WH_PDA_ADJUST_SCAN_STOCK @ScanText = @LotCode;
END;
GO
GO

-- =====================================================================
--  WH Release
-- =====================================================================
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PickSlipNo') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD PickSlipNo nvarchar(40) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqLocation') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqLocation nvarchar(40) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqSeqNo') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqSeqNo int NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqUserId') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqUserId nvarchar(80) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PrintDate') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD PrintDate datetime2 NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'CloseDate') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD CloseDate datetime2 NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'CloseUserId') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD CloseUserId nvarchar(80) NULL;
GO

UPDATE dbo.WH_ReleaseSchedule
   SET PickSlipNo = CONCAT(N'RS-', ReleaseScheduleID)
 WHERE NULLIF(PickSlipNo, N'') IS NULL;
GO

UPDATE dbo.WH_ReleaseSchedule
   SET Status = 'Closed',
       CloseDate = COALESCE(CloseDate, ModifiedTS, SYSDATETIME())
 WHERE UPPER(COALESCE(Status, N'')) IN (N'RELEASED', N'PICKED')
   AND COALESCE(PickedQty, 0) >= COALESCE(DemandQty, 0);
GO

IF OBJECT_ID(N'dbo.WH_RELEASE_BUILD_PICK_ALLOCATIONS', N'P') IS NOT NULL
    DROP PROCEDURE dbo.WH_RELEASE_BUILD_PICK_ALLOCATIONS;
GO

IF OBJECT_ID(N'dbo.WH_ReleasePickAllocation', N'U') IS NOT NULL
    DROP TABLE dbo.WH_ReleasePickAllocation;
GO

-- =====================================================================
--  LOT warehouse lifecycle status
--  Production status (tbl_Lot.Status) remains separate from warehouse state.
-- =====================================================================
IF COL_LENGTH(N'dbo.tbl_Lot', N'InventoryStatus') IS NULL
    ALTER TABLE dbo.tbl_Lot ADD InventoryStatus varchar(30) NULL;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints DC
    INNER JOIN sys.columns C
        ON C.object_id = DC.parent_object_id AND C.column_id = DC.parent_column_id
    WHERE DC.parent_object_id = OBJECT_ID(N'dbo.tbl_Lot')
      AND C.name = N'InventoryStatus'
)
    ALTER TABLE dbo.tbl_Lot
        ADD CONSTRAINT DF_tbl_Lot_InventoryStatus DEFAULT ('CREATED') FOR InventoryStatus;
GO

UPDATE L
   SET InventoryStatus =
       CASE
           WHEN UPPER(COALESCE(L.Status, '')) = 'RELEASED'
             OR COALESCE(W.OnHandQty, 0) <= 0 AND W.InventoryID IS NOT NULL THEN 'RELEASED'
           WHEN W.InventoryID IS NOT NULL AND NULLIF(W.LocationID, '') IS NOT NULL THEN 'STORED'
           WHEN W.InventoryID IS NOT NULL THEN 'RECEIVED'
           ELSE 'CREATED'
       END
FROM dbo.tbl_Lot L
OUTER APPLY
(
    SELECT TOP (1) I.InventoryID, I.OnHandQty, I.LocationID
    FROM dbo.WH_Inventory I
    WHERE I.LotID = L.LotID
    ORDER BY I.InventoryID DESC
) W
WHERE NULLIF(L.InventoryStatus, '') IS NULL;
GO

IF OBJECT_ID(N'dbo.WH_LotStatusHistory', N'U') IS NOT NULL
    DROP TABLE dbo.WH_LotStatusHistory;
GO

CREATE OR ALTER PROCEDURE dbo.WH_SET_LOT_STATUS
    @LotNo nvarchar(50),
    @Status varchar(30),
    @ReasonCode varchar(30) = NULL,
    @ReferenceNo nvarchar(40) = NULL,
    @ChangedBy nvarchar(80) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NormalizedStatus varchar(30) = UPPER(LTRIM(RTRIM(@Status)));
    IF NOT EXISTS
       (SELECT 1 FROM dbo.MD_CodeItem
        WHERE GroupCode='WH_INV_STATUS' AND CodeValue=@NormalizedStatus AND ISNULL(UseFlag,1)=1)
        THROW 51610, 'Unsupported LOT inventory status.', 1;

    DECLARE @LotID int, @BeforeStatus varchar(30);
    SELECT TOP (1)
        @LotID = LotID,
        @BeforeStatus = COALESCE(NULLIF(InventoryStatus, ''), 'CREATED')
    FROM dbo.tbl_Lot WITH (UPDLOCK, HOLDLOCK)
    WHERE UPPER(LotCode) = UPPER(LTRIM(RTRIM(@LotNo)));

    IF @LotID IS NULL
        THROW 51611, 'LOT was not found.', 1;

    IF @BeforeStatus = @NormalizedStatus
        RETURN;

    UPDATE dbo.tbl_Lot
       SET InventoryStatus = @NormalizedStatus,
           ModifiedBy = COALESCE(NULLIF(@ChangedBy, ''), ModifiedBy),
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID;

END;
GO

-- FIFO uses production time first, then receipt time and LotID as deterministic ties.
CREATE OR ALTER PROCEDURE dbo.WH_PDA_FIFO_VIEW
    @LotNo nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LotID int, @ItemNo varchar(20), @ProducedAt datetime2, @ReceivedAt datetime2;
    SELECT TOP (1)
        @LotID = L.LotID,
        @ItemNo = L.ItemNo,
        @ProducedAt = L.ProducedAt,
        @ReceivedAt = W.LastReceivedAt
    FROM dbo.tbl_Lot L
    LEFT JOIN dbo.WH_Inventory W ON W.LotID = L.LotID
    WHERE UPPER(L.LotCode) = UPPER(LTRIM(RTRIM(@LotNo)));

    SELECT
        Older.LotID AS LOT_ID,
        Older.LotCode AS LOTNO,
        Older.ItemNo AS PARTNO,
        W.LocationID AS LOCATION_NO,
        COALESCE(W.OnHandQty, 0) AS QTY,
        W.LastReceivedAt AS RCV_DATE,
        Older.InventoryStatus AS LOT_STATUS
    FROM dbo.tbl_Lot Older
    INNER JOIN dbo.WH_Inventory W ON W.LotID = Older.LotID
    WHERE Older.ItemNo = @ItemNo
      AND Older.LotID <> @LotID
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(Older.InventoryStatus, 'STORED')) IN ('RECEIVED','STORED','RETURN_RECEIVED','RELEASE_CANCELLED')
      AND
      (
          COALESCE(Older.ProducedAt, CONVERT(datetime2, '9999-12-31'))
              < COALESCE(@ProducedAt, CONVERT(datetime2, '9999-12-31'))
          OR
          (
              COALESCE(Older.ProducedAt, CONVERT(datetime2, '9999-12-31'))
                  = COALESCE(@ProducedAt, CONVERT(datetime2, '9999-12-31'))
              AND COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31'))
                  < COALESCE(@ReceivedAt, CONVERT(datetime2, '9999-12-31'))
          )
          OR
          (
              COALESCE(Older.ProducedAt, CONVERT(datetime2, '9999-12-31'))
                  = COALESCE(@ProducedAt, CONVERT(datetime2, '9999-12-31'))
              AND COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31'))
                  = COALESCE(@ReceivedAt, CONVERT(datetime2, '9999-12-31'))
              AND Older.LotID < @LotID
          )
      )
    ORDER BY COALESCE(Older.ProducedAt, CONVERT(datetime2, '9999-12-31')),
             COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31')),
             Older.LotID;
END;
GO

-- =====================================================================
--  Release / Pick Slip status
--  Source: dbo.WH_ReleaseSchedule
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_SLIP_STATUS
    @PickSlipNo nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    DECLARE @ReleaseScheduleID int;

    IF @Slip = N''
    BEGIN
        SELECT
            @Slip AS PICK_SLIPNO,
            CAST(0 AS bit) AS EXISTS_FLAG,
            CAST(0 AS bit) AS IS_CLOSED,
            CAST(0 AS int) AS LINE_COUNT,
            CAST(NULL AS nvarchar(40)) AS REQ_LOCATION,
            CAST(NULL AS date) AS REQ_DATE,
            CAST(NULL AS datetime2) AS CLOSE_DATE,
            N'Pick Slip No is required.' AS MESSAGE;
        RETURN;
    END;

    SELECT TOP (1)
        @ReleaseScheduleID = RS.ReleaseScheduleID
    FROM dbo.WH_ReleaseSchedule RS
    WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = @Slip
       OR UPPER(CONCAT(N'RS-', RS.ReleaseScheduleID)) = @Slip
       OR RS.ReleaseScheduleID = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''))
    ORDER BY RS.ReleaseScheduleID;

    IF @ReleaseScheduleID IS NULL
    BEGIN
        SELECT
            @Slip AS PICK_SLIPNO,
            CAST(0 AS bit) AS EXISTS_FLAG,
            CAST(0 AS bit) AS IS_CLOSED,
            CAST(0 AS int) AS LINE_COUNT,
            CAST(NULL AS nvarchar(40)) AS REQ_LOCATION,
            CAST(NULL AS date) AS REQ_DATE,
            CAST(NULL AS datetime2) AS CLOSE_DATE,
            N'Pick Slip was not found.' AS MESSAGE;
        RETURN;
    END;

    SELECT
        COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) AS PICK_SLIPNO,
        CAST(1 AS bit) AS EXISTS_FLAG,
        CASE WHEN UPPER(COALESCE(RS.Status, N'')) IN (N'CLOSED', N'RELEASED', N'PICKED', N'CANCELED', N'CANCELLED')
                  OR RS.CloseDate IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IS_CLOSED,
        (
            SELECT COUNT(*)
            FROM dbo.WH_ReleaseSchedule L
            WHERE COALESCE(NULLIF(L.PickSlipNo, N''), CONCAT(N'RS-', L.ReleaseScheduleID))
                = COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))
        ) AS LINE_COUNT,
        RS.ReqLocation AS REQ_LOCATION,
        CONVERT(date, RS.RequiredAt) AS REQ_DATE,
        RS.CloseDate AS CLOSE_DATE,
        CASE
            WHEN UPPER(COALESCE(RS.Status, N'')) IN (N'CLOSED', N'RELEASED', N'PICKED', N'CANCELED', N'CANCELLED')
                 OR RS.CloseDate IS NOT NULL THEN N'This Pick Slip has already been processed.'
            ELSE N'Pick Slip is ready.'
        END AS MESSAGE
    FROM dbo.WH_ReleaseSchedule RS
    WHERE RS.ReleaseScheduleID = @ReleaseScheduleID;
END;
GO

-- =====================================================================
--  Release / Pick Slip lines
--  Source: dbo.WH_ReleaseSchedule, dbo.WH_Inventory
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_PICK_LINES
    @PickSlipNo nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    DECLARE @PickSlipKey nvarchar(40);

    SELECT TOP (1)
        @PickSlipKey = COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))
    FROM dbo.WH_ReleaseSchedule RS
    WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = @Slip
       OR UPPER(CONCAT(N'RS-', RS.ReleaseScheduleID)) = @Slip
       OR RS.ReleaseScheduleID = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''))
    ORDER BY RS.ReleaseScheduleID;

    IF @PickSlipKey IS NULL
        RETURN;

    ;WITH RequiredParts AS
    (
        SELECT
            RS.ReleaseScheduleID,
            COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) AS PICK_SLIPNO,
            RS.ItemNo,
            COALESCE(I.ItemName, RS.ItemNo) AS ItemName,
            COALESCE(RS.DemandQty, 0) AS DemandQty,
            COALESCE(RS.PickedQty, 0) AS PickedQty,
            COALESCE(NULLIF(RS.ReqUserId, N''), RS.CreatedBy) AS RequestUserId
        FROM dbo.WH_ReleaseSchedule RS
        LEFT JOIN dbo.MD_Item I
               ON I.ItemNo = RS.ItemNo
        WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
    ),
    PickedPhysical AS
    (
        SELECT P.ReleaseScheduleID, SUM(COALESCE(P.PickedQty, 0)) AS PickedQty
        FROM dbo.WH_ReleasePicking P
        INNER JOIN RequiredParts R
                ON R.ReleaseScheduleID = P.ReleaseScheduleID
        GROUP BY P.ReleaseScheduleID
    )
    SELECT
        R.PICK_SLIPNO,
        R.ItemNo AS PARTNO,
        R.ItemName AS PARTNM,
        R.DemandQty AS REQ_BOX_QTY,
        R.PickedQty AS PICKED_BOX_QTY,
        COALESCE(P.PickedQty, 0) AS PICKED_QTY,
        R.RequestUserId AS REQ_USERID,
        CAST(NULL AS varchar(20)) AS LOC_01,
        CAST(NULL AS varchar(20)) AS LOC_02,
        CAST(NULL AS varchar(20)) AS LOC_03,
        CASE
            WHEN R.DemandQty > 0 AND R.PickedQty >= R.DemandQty THEN N'Picked'
            WHEN R.PickedQty > 0 THEN N'Partial'
            ELSE N'Open'
        END AS STATUS
    FROM RequiredParts R
    LEFT JOIN PickedPhysical P
           ON P.ReleaseScheduleID = R.ReleaseScheduleID
    ORDER BY R.ItemNo;
END;
GO

-- =====================================================================
--  Release / LOT scan validation
--  Source: dbo.WH_ReleaseSchedule, dbo.WH_Inventory, dbo.tbl_Lot
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_SCAN_LOT
    @PickSlipNo nvarchar(40),
    @LotNo nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    DECLARE @Lot nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotNo, N'')));
    DECLARE @ScanText nvarchar(50) = @Lot;
    DECLARE @PickSlipKey nvarchar(40);
    DECLARE @PickSlipOut nvarchar(40) = @Slip;

    DECLARE
        @RequestedItemNo varchar(20),
        @DemandQty decimal(14,3),
        @PickedQty decimal(14,3),
        @SlipStatus varchar(20),
        @LotID int,
        @InventoryID int,
        @ItemNo varchar(20),
        @ItemName nvarchar(100),
        @Qty decimal(14,3),
        @Unit varchar(10),
        @LocationID varchar(20),
        @LocationName nvarchar(100),
        @ZoneCode varchar(20),
        @InventoryStatus varchar(20),
        @ProducedAt datetime2,
        @ReceivedAt datetime2,
        @OldestLot varchar(40);

    IF @Slip = N''
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'Pick Slip No is required.' AS MESSAGE;
        RETURN;
    END;

    IF @Lot = N''
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'LOT No is required.' AS MESSAGE;
        RETURN;
    END;

    SELECT TOP (1)
        @PickSlipKey = COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)),
        @PickSlipOut = COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))
    FROM dbo.WH_ReleaseSchedule RS
    WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = @Slip
       OR UPPER(CONCAT(N'RS-', RS.ReleaseScheduleID)) = @Slip
       OR RS.ReleaseScheduleID = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''))
    ORDER BY RS.ReleaseScheduleID;

    IF @PickSlipKey IS NULL
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'Pick Slip was not found.' AS MESSAGE;
        RETURN;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.WH_ReleaseSchedule RS
        WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
          AND UPPER(COALESCE(RS.Status, N'OPEN')) NOT IN (N'CLOSED', N'RELEASED', N'PICKED', N'CANCELED', N'CANCELLED')
    )
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, N'Closed' AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'This Pick Slip has already been processed.' AS MESSAGE;
        RETURN;
    END;

    -- A material label can carry either a LOT No or a requested Part No.
    -- Resolve a Part No scan to its FIFO-eligible LOT before standard validation.
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = @Lot)
       AND EXISTS
       (
           SELECT 1
           FROM dbo.WH_ReleaseSchedule RS
           WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
             AND UPPER(RS.ItemNo) = UPPER(@ScanText)
       )
    BEGIN
        SET @Lot = NULL;

        SELECT TOP (1) @Lot = L.LotCode
        FROM dbo.WH_Inventory W
        INNER JOIN dbo.tbl_Lot L
                ON L.LotID = W.LotID
        WHERE W.ItemNo = @ScanText
          AND COALESCE(W.OnHandQty, 0) > 0
          AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
        ORDER BY COALESCE(L.ProducedAt, CONVERT(datetime2, '9999-12-31')),
                 COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31')),
                 L.LotID;

        IF @Lot IS NULL
        BEGIN
            SELECT @PickSlipOut AS PICK_SLIPNO, @ScanText AS LOTNO, @ScanText AS PARTNO, NULL AS PARTNM,
                CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
                NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
                CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
                N'No available LOT was found for this Part No.' AS MESSAGE;
            RETURN;
        END;
    END;

    SELECT TOP (1)
        @InventoryID = W.InventoryID,
        @LotID = L.LotID,
        @ItemNo = W.ItemNo,
        @ItemName = I.ItemName,
        @Qty = COALESCE(W.OnHandQty, 0),
        @Unit = I.DefaultUOM,
        @LocationID = W.LocationID,
        @LocationName = LOC.LocationName,
        @ZoneCode = LOC.ZoneCode,
        @InventoryStatus = W.Status,
        @ProducedAt = L.ProducedAt,
        @ReceivedAt = W.LastReceivedAt
    FROM dbo.tbl_Lot L
    LEFT JOIN dbo.WH_Inventory W
           ON W.LotID = L.LotID
          AND COALESCE(W.OnHandQty, 0) > 0
          AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    LEFT JOIN dbo.MD_Item I
           ON I.ItemNo = COALESCE(W.ItemNo, L.ItemNo)
    LEFT JOIN dbo.MD_Location LOC
           ON LOC.LocationID = W.LocationID
    WHERE L.LotCode = @Lot
    ORDER BY W.InventoryID DESC, L.LotID DESC;

    IF @LotID IS NULL
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'LOT was not found.' AS MESSAGE;
        RETURN;
    END;

    IF @InventoryID IS NULL
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, @Unit AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
            NULL AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'LOT is not available for release.' AS MESSAGE;
        RETURN;
    END;

    SELECT TOP (1)
        @RequestedItemNo = RS.ItemNo,
        @DemandQty = COALESCE(RS.DemandQty, 0),
        @PickedQty = COALESCE(RS.PickedQty, 0),
        @SlipStatus = RS.Status
    FROM dbo.WH_ReleaseSchedule RS
    WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
      AND RS.ItemNo = @ItemNo
    ORDER BY RS.ReleaseScheduleID;

    IF @RequestedItemNo IS NULL OR @ItemNo <> @RequestedItemNo
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
            @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
            @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
            CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            N'Wrong item. This LOT is not requested by the selected Pick Slip.' AS MESSAGE;
        RETURN;
    END;

    IF @DemandQty > 0 AND @PickedQty >= @DemandQty
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
            @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
            @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
            CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            N'This item is already fully picked for the selected Pick Slip.' AS MESSAGE;
        RETURN;
    END;

    SELECT TOP (1)
        @OldestLot = L.LotCode
    FROM dbo.WH_Inventory W
    INNER JOIN dbo.tbl_Lot L
            ON L.LotID = W.LotID
    WHERE W.ItemNo = @ItemNo
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ORDER BY COALESCE(L.ProducedAt, CONVERT(datetime2, '9999-12-31')),
             COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31')),
             L.LotID;

    IF @OldestLot IS NOT NULL AND UPPER(@OldestLot) <> UPPER(@Lot)
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
            @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
            @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
            CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            CONCAT(N'FIFO violation. Pick LOT ', @OldestLot, N' first.') AS MESSAGE;
        RETURN;
    END;

    SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
        @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
        @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
        CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(1 AS bit) AS IS_FIFO_SUGGESTED,
        CAST(1 AS bit) AS IS_VALID, N'LOT is ready to pick.' AS MESSAGE;
END;
GO

-- =====================================================================
--  Release / Pick LOT
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_PICK_LOT
    @PickSlipNo nvarchar(40),
    @LotNo nvarchar(50),
    @UserId nvarchar(80),
    @TerminalId nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    DECLARE @Lot nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotNo, N'')));
    DECLARE @User nvarchar(80) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @Terminal nvarchar(80) = COALESCE(NULLIF(LTRIM(RTRIM(@TerminalId)), N''), N'PDA');
    DECLARE @ReleaseScheduleID int;
    DECLARE @PickSlipKey nvarchar(40);

    DECLARE @Validation table
    (
        PICK_SLIPNO nvarchar(40),
        LOTNO nvarchar(50),
        PARTNO varchar(20) NULL,
        PARTNM nvarchar(100) NULL,
        QTY decimal(18,3),
        UNIT varchar(10) NULL,
        LOCATION_NO varchar(20) NULL,
        LOCATION_NM nvarchar(100) NULL,
        ZONECD varchar(20) NULL,
        INV_STATUS varchar(20) NULL,
        PROD_DATE nvarchar(20) NULL,
        RCV_DATE nvarchar(20) NULL,
        IS_FIFO_SUGGESTED bit,
        IS_VALID bit,
        MESSAGE nvarchar(200)
    );

    INSERT INTO @Validation
    EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo = @Slip, @LotNo = @Lot;

    IF NOT EXISTS (SELECT 1 FROM @Validation WHERE IS_VALID = 1)
    BEGIN
        SELECT TOP (1) * FROM @Validation;
        RETURN;
    END;

    DECLARE
        @LotID int,
        @InventoryID int,
        @ItemNo varchar(20),
        @LocationID varchar(20),
        @Qty decimal(18,3),
        @InventoryQty decimal(18,3),
        @ResolvedLot nvarchar(50),
        @BeforeStatus varchar(20),
        @PickedTotal decimal(18,3),
        @DemandQty decimal(18,3);

    SELECT TOP (1)
        @ResolvedLot = LOTNO,
        @Qty = QTY
    FROM @Validation
    WHERE IS_VALID = 1;

    SELECT TOP (1)
        @LotID = L.LotID,
        @InventoryID = W.InventoryID,
        @ItemNo = W.ItemNo,
        @LocationID = W.LocationID,
        @InventoryQty = W.OnHandQty,
        @BeforeStatus = W.Status
    FROM dbo.tbl_Lot L
    INNER JOIN dbo.WH_Inventory W
            ON W.LotID = L.LotID
    WHERE L.LotCode = @ResolvedLot
      AND W.ItemNo = (SELECT TOP (1) PARTNO FROM @Validation)
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ORDER BY W.InventoryID DESC;

    IF @InventoryID IS NULL
    BEGIN
        SELECT TOP (1)
            PICK_SLIPNO, LOTNO, PARTNO, PARTNM, QTY, UNIT, LOCATION_NO, LOCATION_NM,
            ZONECD, INV_STATUS, PROD_DATE, RCV_DATE, IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            N'LOT status changed before picking. Scan again.' AS MESSAGE
        FROM @Validation;
        RETURN;
    END;

    SELECT TOP (1)
        @PickSlipKey = V.PICK_SLIPNO
    FROM @Validation V
    WHERE V.IS_VALID = 1;

    SELECT TOP (1)
        @ReleaseScheduleID = RS.ReleaseScheduleID
    FROM dbo.WH_ReleaseSchedule RS
    WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
      AND RS.ItemNo = @ItemNo
    ORDER BY RS.ReleaseScheduleID;

    IF @ReleaseScheduleID IS NULL
    BEGIN
        SELECT TOP (1)
            PICK_SLIPNO, LOTNO, PARTNO, PARTNM, QTY, UNIT, LOCATION_NO, LOCATION_NM,
            ZONECD, INV_STATUS, PROD_DATE, RCV_DATE, IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            N'Pick Slip line was not found for this LOT.' AS MESSAGE
        FROM @Validation;
        RETURN;
    END;

    BEGIN TRANSACTION;

    UPDATE dbo.WH_Inventory
       SET OnHandQty = OnHandQty - @Qty,
           ReservedQty = 0,
           Status = CASE WHEN OnHandQty <= @Qty THEN 'Released' ELSE 'Received' END,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE InventoryID = @InventoryID;

    UPDATE dbo.tbl_Lot
       SET RemainingQty = CASE WHEN COALESCE(RemainingQty, 0) <= @Qty THEN 0 ELSE RemainingQty - @Qty END,
           Status = CASE WHEN COALESCE(RemainingQty, 0) <= @Qty THEN 'Released' ELSE 'Received' END,
           CurrentLocationID = CASE WHEN COALESCE(RemainingQty, 0) <= @Qty THEN NULL ELSE CurrentLocationID END,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID;

    INSERT INTO dbo.WH_ReleasePicking
    (
        PickingNo, ReleaseScheduleID, ItemNo, LocationID, LotID, PickedQty,
        PickedAt, PickedBy, TerminalID, FifoOverride, CreatedBy, CreatedTS
    )
    VALUES
    (
        CONCAT('PICK-', FORMAT(SYSDATETIME(), 'yyMMddHHmmssfff')),
        @ReleaseScheduleID, @ItemNo, @LocationID, @LotID, @Qty,
        SYSDATETIME(), @User, @Terminal, 0, @User, SYSDATETIME()
    );

    UPDATE dbo.WH_ReleaseSchedule
       SET PickedQty = COALESCE(PickedQty, 0) + 1,
            Status = CASE
                WHEN COALESCE(PickedQty, 0) + 1 >= COALESCE(DemandQty, 0) THEN 'Picked'
               ELSE 'Partial'
           END,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE ReleaseScheduleID = @ReleaseScheduleID;

    SELECT
        @PickedTotal = COALESCE(PickedQty, 0),
        @DemandQty = COALESCE(DemandQty, 0)
    FROM dbo.WH_ReleaseSchedule
    WHERE ReleaseScheduleID = @ReleaseScheduleID;

    INSERT INTO dbo.WH_InventoryTransaction
    (
        TransactionTime, TransactionType, ItemNo, LocationID, LotID, QtyBefore, QtyChange, QtyAfter,
        ReasonCode, RefDocType, RefDocID, OperatorID, Note, CreatedBy, CreatedTS
    )
    VALUES
    (
        SYSDATETIME(), 'OUT', @ItemNo, @LocationID, @LotID, @InventoryQty, -@Qty, @InventoryQty - @Qty,
        'RELEASE_PICK', 'PICK_SLIP', @ReleaseScheduleID, @User,
        CONCAT('PDA release pick ', @PickSlipKey), @User, SYSDATETIME()
    );

    COMMIT TRANSACTION;

    SELECT TOP (1)
        PICK_SLIPNO,
        LOTNO,
        PARTNO,
        PARTNM,
        QTY,
        UNIT,
        LOCATION_NO,
        LOCATION_NM,
        ZONECD,
        'Released' AS INV_STATUS,
        PROD_DATE,
        RCV_DATE,
        CAST(1 AS bit) AS IS_FIFO_SUGGESTED,
        CAST(1 AS bit) AS IS_VALID,
        N'Release pick completed.' AS MESSAGE
    FROM @Validation;
END;
GO
GO

-- =====================================================================
--  FG Put-Away
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.FG_Inventory', N'U') IS NULL
BEGIN
    IF OBJECT_ID(N'dbo.FG_Stock', N'U') IS NOT NULL
    BEGIN
        EXEC sp_rename N'dbo.FG_Stock', N'FG_Inventory';

        IF OBJECT_ID(N'dbo.PK_FG_Stock', N'PK') IS NOT NULL
            EXEC sp_rename N'dbo.PK_FG_Stock', N'PK_FG_Inventory', N'OBJECT';
    END
    ELSE
    BEGIN
        CREATE TABLE dbo.FG_Inventory
        (
            StockID int IDENTITY(1,1) NOT NULL,
            StockNumber varchar(24) NULL,
            FgTriggerID int NULL,
            WoID int NULL,
            ItemNo varchar(20) NULL,
            LotID int NULL,
            CustomerCode varchar(20) NULL,
            Qty decimal(12,3) NULL,
            Location varchar(20) NULL,
            Status varchar(20) NULL,
            HoldFlag bit NULL,
            HoldID int NULL,
            ReservationID int NULL,
            StockTS datetime2 NULL,
            CreatedBy varchar(50) NOT NULL CONSTRAINT DF_FG_Inventory_CreatedBy DEFAULT 'system',
            CreatedTS datetime2 NULL CONSTRAINT DF_FG_Inventory_CreatedTS DEFAULT SYSDATETIME(),
            ModifiedTS datetime2 NULL,
            ModifiedBy nvarchar(450) NULL,
            CONSTRAINT PK_FG_Inventory PRIMARY KEY CLUSTERED (StockID)
        );
    END
END;

IF OBJECT_ID(N'dbo.FG_PutAway', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.FG_PutAway', N'StorageMethod') IS NULL
        ALTER TABLE dbo.FG_PutAway ADD StorageMethod varchar(20) NULL;

    IF COL_LENGTH(N'dbo.FG_PutAway', N'ContainerType') IS NULL
        ALTER TABLE dbo.FG_PutAway ADD ContainerType varchar(20) NULL;

    IF COL_LENGTH(N'dbo.FG_PutAway', N'ContainerBarcode') IS NULL
        ALTER TABLE dbo.FG_PutAway ADD ContainerBarcode varchar(80) NULL;
END;

IF OBJECT_ID(N'dbo.FG_Inventory', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FG_Inventory_Lot' AND object_id = OBJECT_ID(N'dbo.FG_Inventory'))
        CREATE INDEX IX_FG_Inventory_Lot ON dbo.FG_Inventory (LotID, WoID, Status);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FG_Inventory_Location' AND object_id = OBJECT_ID(N'dbo.FG_Inventory'))
        CREATE INDEX IX_FG_Inventory_Location ON dbo.FG_Inventory (Location, Status);

    IF OBJECT_ID(N'dbo.FG_Stock', N'U') IS NULL
       AND OBJECT_ID(N'dbo.FG_Stock', N'V') IS NULL
       AND OBJECT_ID(N'dbo.FG_Stock', N'SN') IS NULL
    BEGIN
        CREATE SYNONYM dbo.FG_Stock FOR dbo.FG_Inventory;
    END
END;
GO

-- =====================================================================
--  FG Adjust
-- =====================================================================
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.FG_InventoryAdjust', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_InventoryAdjust
    (
        AdjustID int IDENTITY(1,1) NOT NULL,
        AdjustNo varchar(24) NULL,
        StockID int NOT NULL,
        ItemNo varchar(20) NULL,
        Location varchar(20) NULL,
        LotID int NULL,
        QtyBefore decimal(14,3) NOT NULL,
        Delta decimal(14,3) NOT NULL,
        QtyAfter decimal(14,3) NOT NULL,
        ReasonCode varchar(30) NULL,
        ReasonNote nvarchar(500) NULL,
        Status varchar(20) NULL,
        RequestedBy nvarchar(450) NULL,
        ApprovedBy nvarchar(450) NULL,
        CreatedBy varchar(50) NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_FG_InventoryAdjust_CreatedTS DEFAULT SYSDATETIME(),
        CONSTRAINT PK_FG_InventoryAdjust PRIMARY KEY CLUSTERED (AdjustID)
    );

    CREATE INDEX IX_FG_InventoryAdjust_Stock
        ON dbo.FG_InventoryAdjust (StockID, CreatedTS DESC);
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_ADJUST_SCAN_STOCK
    @ScanText nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Scan nvarchar(80) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
    DECLARE @StockID int;

    IF @Scan = N''
        THROW 51600, 'Finished goods Lot No is required.', 1;
    IF LEN(@Scan) < 3
       OR @Scan COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9-]%'
        THROW 51604, 'The barcode format is invalid.', 1;

    SELECT TOP (1)
        @StockID = F.StockID
    FROM dbo.FG_Inventory F
    LEFT JOIN dbo.tbl_Lot L ON L.LotID = F.LotID
    WHERE (UPPER(COALESCE(L.LotCode, N'')) = UPPER(@Scan)
        OR UPPER(COALESCE(F.StockNumber, N'')) = UPPER(@Scan))
      AND UPPER(COALESCE(F.Status, N'Available')) NOT IN
          (N'CANCELED', N'CANCELLED', N'SHIPPED', N'DELIVERED', N'CLOSED')
    ORDER BY CASE WHEN COALESCE(F.Qty, 0) > 0 THEN 0 ELSE 1 END, F.StockID DESC;

    IF @StockID IS NULL
       AND EXISTS
       (
           SELECT 1
           FROM dbo.WH_Inventory W
           JOIN dbo.tbl_Lot WL ON WL.LotID = W.LotID
           WHERE UPPER(WL.LotCode) = UPPER(@Scan)
             AND UPPER(COALESCE(W.Status, N'Received')) NOT IN
                 (N'CANCELED', N'CANCELLED', N'RELEASED', N'PICKED')
       )
        THROW 51605, 'Warehouse material cannot be adjusted in Finished Goods Adjust.', 1;

    IF @StockID IS NULL
        THROW 51601, 'The specified finished goods Lot No could not be found.', 1;

    SELECT
        N'FG' AS RECEIVE_TYPE,
        N'N' AS YN,
        COALESCE(NULLIF(L.LotCode, N''), F.StockNumber) AS LOTNO,
        COALESCE(NULLIF(L.LotCode, N''), F.StockNumber) AS BARCODE,
        N'dbo.FG_Inventory' AS SOURCE_TABLE,
        CAST(NULL AS nvarchar(50)) AS NOTENO,
        CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
        CAST(NULL AS nvarchar(30)) AS CASE_NO,
        CAST(NULL AS nvarchar(30)) AS INVOICE_NO,
        CAST(NULL AS nvarchar(30)) AS CONTAINER_NO,
        F.ItemNo AS PARTNO,
        I.ItemName AS PARTNM,
        COALESCE(F.Qty, 0) AS QTY,
        I.DefaultUOM AS UNIT,
        CAST(NULL AS nvarchar(30)) AS PONO,
        CAST(NULL AS int) AS PONO_SEQ,
        CAST(NULL AS nvarchar(30)) AS VENDCD,
        CAST(NULL AS nvarchar(100)) AS VENDNM,
        CONVERT(date, L.ProducedAt) AS PROD_DATE,
        CAST(NULL AS date) AS DELI_DATE,
        CONVERT(date, F.StockTS) AS ARRIV_DATE,
        CAST(NULL AS date) AS SHIP_DATE,
        CAST(NULL AS date) AS PACK_DATE,
        F.Location AS RECEIVED_LOCATION,
        COALESCE(F.Status, N'Available') AS RECEIVED_STATUS
    FROM dbo.FG_Inventory F
    LEFT JOIN dbo.tbl_Lot L ON L.LotID = F.LotID
    LEFT JOIN dbo.MD_Item I ON I.ItemNo = F.ItemNo
    WHERE F.StockID = @StockID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_ADJUST_SAVE_QTY
    @ScanText nvarchar(80),
    @DeltaQty decimal(18,3),
    @ReasonCode nvarchar(30),
    @ReasonNote nvarchar(500) = NULL,
    @SupervisorUserId nvarchar(450) = NULL,
    @SupervisorEmployeeNo nvarchar(40) = NULL,
    @UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Scan nvarchar(80) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
    DECLARE @Reason nvarchar(30) = UPPER(LTRIM(RTRIM(ISNULL(@ReasonCode, N''))));
    DECLARE @Note nvarchar(500) = NULLIF(LTRIM(RTRIM(@ReasonNote)), N'');
    DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @Supervisor nvarchar(450) = COALESCE(
        NULLIF(LTRIM(RTRIM(@SupervisorEmployeeNo)), N''),
        NULLIF(LTRIM(RTRIM(@SupervisorUserId)), N''),
        @User
    );

    IF @Scan = N''
        THROW 51610, 'Finished goods Lot No is required.', 1;
    IF COALESCE(@DeltaQty, 0) = 0
        THROW 51611, 'Adjustment quantity must be different from zero.', 1;
    IF @Reason = N''
        THROW 51612, 'Reason code is required.', 1;
    IF NOT EXISTS
       (SELECT 1 FROM dbo.MD_CodeItem
        WHERE GroupCode='INV_ADJUST_REASON' AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
        THROW 51619, 'Unsupported inventory adjustment reason.', 1;

    DECLARE
        @StockID int,
        @ItemNo varchar(20),
        @Location varchar(20),
        @LotID int,
        @BeforeQty decimal(18,3),
        @AfterQty decimal(18,3),
        @LotCode varchar(80);

    BEGIN TRAN;

    SELECT TOP (1)
        @StockID = F.StockID,
        @ItemNo = F.ItemNo,
        @Location = F.Location,
        @LotID = F.LotID,
        @BeforeQty = COALESCE(F.Qty, 0),
        @LotCode = COALESCE(NULLIF(L.LotCode, N''), F.StockNumber)
    FROM dbo.FG_Inventory F WITH (UPDLOCK, ROWLOCK)
    LEFT JOIN dbo.tbl_Lot L ON L.LotID = F.LotID
    WHERE (UPPER(COALESCE(L.LotCode, N'')) = UPPER(@Scan)
        OR UPPER(COALESCE(F.StockNumber, N'')) = UPPER(@Scan))
      AND UPPER(COALESCE(F.Status, N'Available')) NOT IN
          (N'CANCELED', N'CANCELLED', N'SHIPPED', N'DELIVERED', N'CLOSED')
    ORDER BY CASE WHEN COALESCE(F.Qty, 0) > 0 THEN 0 ELSE 1 END, F.StockID DESC;

    IF @StockID IS NULL
       AND EXISTS
       (
           SELECT 1
           FROM dbo.WH_Inventory W
           JOIN dbo.tbl_Lot WL ON WL.LotID = W.LotID
           WHERE UPPER(WL.LotCode) = UPPER(@Scan)
             AND UPPER(COALESCE(W.Status, N'Received')) NOT IN
                 (N'CANCELED', N'CANCELLED', N'RELEASED', N'PICKED')
       )
        THROW 51615, 'Warehouse material cannot be adjusted in Finished Goods Adjust.', 1;

    IF @StockID IS NULL
        THROW 51614, 'The specified finished goods Lot No could not be found.', 1;

    SET @AfterQty = @BeforeQty + @DeltaQty;
    IF @AfterQty < 0
        THROW 51617, 'After Qty cannot be below zero.', 1;
    IF @AfterQty <> FLOOR(@AfterQty) OR @AfterQty > 999999999
        THROW 51618, 'New quantity must be a whole number from 0 to 999999999.', 1;

    UPDATE dbo.FG_Inventory
       SET Qty = @AfterQty,
           Status = N'AVAILABLE',
           ModifiedTS = SYSDATETIME(),
           ModifiedBy = @User
     WHERE StockID = @StockID;

    IF @LotID IS NOT NULL
    BEGIN
        UPDATE dbo.tbl_Lot
           SET RemainingQty =
               (
                   SELECT COALESCE(SUM(COALESCE(F.Qty, 0)), 0)
                   FROM dbo.FG_Inventory F
                   WHERE F.LotID = @LotID
                     AND UPPER(COALESCE(F.Status, N'Available')) NOT IN
                         (N'CANCELED', N'CANCELLED', N'SHIPPED', N'DELIVERED', N'CLOSED')
               ),
               ModifiedTS = SYSDATETIME(),
               ModifiedBy = @User
         WHERE LotID = @LotID;
    END;

    INSERT INTO dbo.FG_InventoryAdjust
        (AdjustNo, StockID, ItemNo, Location, LotID, QtyBefore, Delta, QtyAfter,
         ReasonCode, ReasonNote, Status, RequestedBy, ApprovedBy, CreatedBy, CreatedTS)
    VALUES
        (CONCAT('FGADJ-', FORMAT(SYSDATETIME(), 'yyMMddHHmmss')),
         @StockID, @ItemNo, @Location, @LotID, @BeforeQty, @DeltaQty, @AfterQty,
         CONVERT(varchar(30), @Reason), @Note, N'Posted', @User, @Supervisor, @User, SYSDATETIME());

    COMMIT TRAN;

    EXEC dbo.FG_PDA_ADJUST_SCAN_STOCK @ScanText = @LotCode;
END;
GO
GO

-- =====================================================================
--  FG Release Outgoing Slip
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

IF OBJECT_ID(N'dbo.FG_PickingDetail', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_PickingDetail
    (
        PickDetailID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FG_PickingDetail PRIMARY KEY,
        PickID int NOT NULL,
        ShipmentOrderLineID int NOT NULL,
        StockID int NOT NULL,
        LotID int NULL,
        ItemNo varchar(20) NOT NULL,
        Qty decimal(12,3) NOT NULL,
        Location varchar(20) NULL,
        PickSeq int NOT NULL,
        CreatedBy varchar(50) NOT NULL,
        CreatedTS datetime2 NULL CONSTRAINT DF_FG_PickingDetail_CreatedTS DEFAULT SYSDATETIME()
    );
END;
GO

-- Preserve the one useful legacy value before removing unused picking columns.
IF COL_LENGTH(N'dbo.FG_PickingFifo', N'PickslipID') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE O
           SET PickslipID=COALESCE(NULLIF(O.PickslipID, ''''), P.PickslipID)
        FROM dbo.FG_ShipmentOrder O
        JOIN dbo.FG_PickingFifo P ON P.ShipmentOrderID=O.ShipmentOrderID
        WHERE NULLIF(P.PickslipID, '''') IS NOT NULL;';
    ALTER TABLE dbo.FG_PickingFifo DROP COLUMN PickslipID;
END;
GO
IF COL_LENGTH(N'dbo.FG_PickingFifo', N'FifoViolations') IS NOT NULL
    ALTER TABLE dbo.FG_PickingFifo DROP COLUMN FifoViolations;
IF COL_LENGTH(N'dbo.FG_PickingFifo', N'OverrideCount') IS NOT NULL
    ALTER TABLE dbo.FG_PickingFifo DROP COLUMN OverrideCount;
IF COL_LENGTH(N'dbo.FG_PickingFifo', N'OverrideApprovedBy') IS NOT NULL
    ALTER TABLE dbo.FG_PickingFifo DROP COLUMN OverrideApprovedBy;
IF COL_LENGTH(N'dbo.FG_PickingFifo', N'PartialPicksJSON') IS NOT NULL
    ALTER TABLE dbo.FG_PickingFifo DROP COLUMN PartialPicksJSON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_ShipmentOrderLine') AND name=N'IX_FG_ShipmentOrderLine_Order')
    CREATE INDEX IX_FG_ShipmentOrderLine_Order ON dbo.FG_ShipmentOrderLine(ShipmentOrderID, LineSeq, ShipmentOrderLineID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_Inventory') AND name=N'IX_FG_Inventory_Picking')
    CREATE INDEX IX_FG_Inventory_Picking ON dbo.FG_Inventory(ItemNo, Status, HoldFlag, StockTS, StockID)
        INCLUDE (LotID, CustomerCode, Qty, Location, StockNumber);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_PickingFifo') AND name=N'IX_FG_PickingFifo_Order')
    CREATE INDEX IX_FG_PickingFifo_Order ON dbo.FG_PickingFifo(ShipmentOrderID, EndTS DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_PickingFifo') AND name=N'UX_FG_PickingFifo_PickNumber')
    CREATE UNIQUE INDEX UX_FG_PickingFifo_PickNumber ON dbo.FG_PickingFifo(PickNumber) WHERE PickNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_PickingDetail') AND name=N'UX_FG_PickingDetail_Pick_Stock')
    CREATE UNIQUE INDEX UX_FG_PickingDetail_Pick_Stock ON dbo.FG_PickingDetail(PickID, StockID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_PickingDetail') AND name=N'IX_FG_PickingDetail_Stock')
    CREATE INDEX IX_FG_PickingDetail_Stock ON dbo.FG_PickingDetail(StockID, PickID);
IF EXISTS (SELECT 1 FROM dbo.FG_LoadingConfirm WHERE LoadingNumber IS NULL OR ShipmentOrderID IS NULL OR LicensePlate IS NULL)
    THROW 51920, 'FG_LoadingConfirm contains rows without loading number, shipment order, or truck.', 1;
ALTER TABLE dbo.FG_LoadingConfirm ALTER COLUMN LoadingNumber varchar(24) NOT NULL;
ALTER TABLE dbo.FG_LoadingConfirm ALTER COLUMN ShipmentOrderID int NOT NULL;
ALTER TABLE dbo.FG_LoadingConfirm ALTER COLUMN LicensePlate varchar(20) NOT NULL;
IF COL_LENGTH(N'dbo.FG_LoadingConfirm', N'DriverID') IS NOT NULL
    ALTER TABLE dbo.FG_LoadingConfirm DROP COLUMN DriverID;
IF COL_LENGTH(N'dbo.FG_LoadingConfirm', N'DriverPhone') IS NOT NULL
    ALTER TABLE dbo.FG_LoadingConfirm DROP COLUMN DriverPhone;
IF COL_LENGTH(N'dbo.FG_LoadingConfirm', N'DriverSigURL') IS NOT NULL
    ALTER TABLE dbo.FG_LoadingConfirm DROP COLUMN DriverSigURL;
IF COL_LENGTH(N'dbo.FG_LoadingConfirm', N'DriverPhotoURL') IS NOT NULL
    ALTER TABLE dbo.FG_LoadingConfirm DROP COLUMN DriverPhotoURL;
IF COL_LENGTH(N'dbo.FG_LoadingConfirm', N'GPSCoord') IS NOT NULL
    ALTER TABLE dbo.FG_LoadingConfirm DROP COLUMN GPSCoord;
UPDATE C SET PickID=P.PickID
FROM dbo.FG_LoadingConfirm C
OUTER APPLY
(
    SELECT TOP(1) F.PickID FROM dbo.FG_PickingFifo F
    WHERE F.ShipmentOrderID=C.ShipmentOrderID
    ORDER BY ISNULL(F.EndTS,F.CreatedTS) DESC,F.PickID DESC
) P
WHERE C.PickID IS NULL AND P.PickID IS NOT NULL;
UPDATE C SET PalletsLoadedJSON=J.Items
FROM dbo.FG_LoadingConfirm C
CROSS APPLY
(
    SELECT L.StockID AS stockId,L.ShipmentOrderLineID AS shipmentOrderLineId,L.ItemNo AS itemNo,
        LOT.LotCode AS lotNo,S.StockNumber AS stockNumber,COALESCE(NULLIF(L.AllocatedQty,0),S.Qty,L.OrderedQty) AS qty,
        I.DefaultUOM AS unit,COALESCE(L.Location,S.Location) AS location
    FROM dbo.FG_ShipmentOrderLine L
    JOIN dbo.FG_Inventory S ON S.StockID=L.StockID
    LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=COALESCE(L.LotID,S.LotID)
    LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
    WHERE L.ShipmentOrderID=C.ShipmentOrderID
    ORDER BY L.LineSeq,L.ShipmentOrderLineID FOR JSON PATH
) J(Items)
WHERE ISNULL(ISJSON(C.PalletsLoadedJSON),0)<>1 AND J.Items<>N'[]';
UPDATE C SET DepartureTS=COALESCE(C.ConfirmedAt,C.CreatedTS,SYSDATETIME())
FROM dbo.FG_LoadingConfirm C
JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=C.ShipmentOrderID
WHERE C.DepartureTS IS NULL AND UPPER(ISNULL(O.Status,'')) IN ('LOADED','SHIPPED','CLOSED');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_LoadingConfirm') AND name=N'UX_FG_LoadingConfirm_LoadingNumber')
    CREATE UNIQUE INDEX UX_FG_LoadingConfirm_LoadingNumber ON dbo.FG_LoadingConfirm(LoadingNumber);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_LoadingConfirm') AND name=N'UX_FG_LoadingConfirm_Order')
    CREATE UNIQUE INDEX UX_FG_LoadingConfirm_Order ON dbo.FG_LoadingConfirm(ShipmentOrderID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_LoadingConfirm') AND name=N'IX_FG_LoadingConfirm_Truck')
    CREATE INDEX IX_FG_LoadingConfirm_Truck ON dbo.FG_LoadingConfirm(LicensePlate, DepartureTS DESC)
        INCLUDE (LoadingNumber, OTDStatus, ConfirmedAt);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_ShipmentOrderLine_Order')
    ALTER TABLE dbo.FG_ShipmentOrderLine WITH CHECK ADD CONSTRAINT FK_FG_ShipmentOrderLine_Order
        FOREIGN KEY(ShipmentOrderID) REFERENCES dbo.FG_ShipmentOrder(ShipmentOrderID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_ShipmentOrderLine_Item')
    ALTER TABLE dbo.FG_ShipmentOrderLine WITH CHECK ADD CONSTRAINT FK_FG_ShipmentOrderLine_Item
        FOREIGN KEY(ItemNo) REFERENCES dbo.MD_Item(ItemNo);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_ShipmentOrderLine_Stock')
    ALTER TABLE dbo.FG_ShipmentOrderLine WITH CHECK ADD CONSTRAINT FK_FG_ShipmentOrderLine_Stock
        FOREIGN KEY(StockID) REFERENCES dbo.FG_Inventory(StockID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_ShipmentOrderLine_Lot')
    ALTER TABLE dbo.FG_ShipmentOrderLine WITH CHECK ADD CONSTRAINT FK_FG_ShipmentOrderLine_Lot
        FOREIGN KEY(LotID) REFERENCES dbo.tbl_Lot(LotID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_ShipmentOrderLine_Location')
    ALTER TABLE dbo.FG_ShipmentOrderLine WITH CHECK ADD CONSTRAINT FK_FG_ShipmentOrderLine_Location
        FOREIGN KEY(Location) REFERENCES dbo.MD_Location(LocationID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_Inventory_Item')
    ALTER TABLE dbo.FG_Inventory WITH CHECK ADD CONSTRAINT FK_FG_Inventory_Item
        FOREIGN KEY(ItemNo) REFERENCES dbo.MD_Item(ItemNo);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_Inventory_Lot')
    ALTER TABLE dbo.FG_Inventory WITH CHECK ADD CONSTRAINT FK_FG_Inventory_Lot
        FOREIGN KEY(LotID) REFERENCES dbo.tbl_Lot(LotID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_Inventory_Location')
    ALTER TABLE dbo.FG_Inventory WITH CHECK ADD CONSTRAINT FK_FG_Inventory_Location
        FOREIGN KEY(Location) REFERENCES dbo.MD_Location(LocationID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_PickingFifo_Order')
    ALTER TABLE dbo.FG_PickingFifo WITH CHECK ADD CONSTRAINT FK_FG_PickingFifo_Order
        FOREIGN KEY(ShipmentOrderID) REFERENCES dbo.FG_ShipmentOrder(ShipmentOrderID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_PickingDetail_Pick')
    ALTER TABLE dbo.FG_PickingDetail WITH CHECK ADD CONSTRAINT FK_FG_PickingDetail_Pick
        FOREIGN KEY(PickID) REFERENCES dbo.FG_PickingFifo(PickID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_PickingDetail_Line')
    ALTER TABLE dbo.FG_PickingDetail WITH CHECK ADD CONSTRAINT FK_FG_PickingDetail_Line
        FOREIGN KEY(ShipmentOrderLineID) REFERENCES dbo.FG_ShipmentOrderLine(ShipmentOrderLineID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_PickingDetail_Stock')
    ALTER TABLE dbo.FG_PickingDetail WITH CHECK ADD CONSTRAINT FK_FG_PickingDetail_Stock
        FOREIGN KEY(StockID) REFERENCES dbo.FG_Inventory(StockID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_PickingDetail_Lot')
    ALTER TABLE dbo.FG_PickingDetail WITH CHECK ADD CONSTRAINT FK_FG_PickingDetail_Lot
        FOREIGN KEY(LotID) REFERENCES dbo.tbl_Lot(LotID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_PickingDetail_Item')
    ALTER TABLE dbo.FG_PickingDetail WITH CHECK ADD CONSTRAINT FK_FG_PickingDetail_Item
        FOREIGN KEY(ItemNo) REFERENCES dbo.MD_Item(ItemNo);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_PickingDetail_Location')
    ALTER TABLE dbo.FG_PickingDetail WITH CHECK ADD CONSTRAINT FK_FG_PickingDetail_Location
        FOREIGN KEY(Location) REFERENCES dbo.MD_Location(LocationID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_LoadingConfirm_Order')
    ALTER TABLE dbo.FG_LoadingConfirm WITH CHECK ADD CONSTRAINT FK_FG_LoadingConfirm_Order
        FOREIGN KEY(ShipmentOrderID) REFERENCES dbo.FG_ShipmentOrder(ShipmentOrderID);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_LoadingConfirm_Pick')
    ALTER TABLE dbo.FG_LoadingConfirm WITH CHECK ADD CONSTRAINT FK_FG_LoadingConfirm_Pick
        FOREIGN KEY(PickID) REFERENCES dbo.FG_PickingFifo(PickID);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_FG_LoadingConfirm_LoadedJSON')
    ALTER TABLE dbo.FG_LoadingConfirm WITH CHECK ADD CONSTRAINT CK_FG_LoadingConfirm_LoadedJSON
        CHECK(PalletsLoadedJSON IS NULL OR ISJSON(PalletsLoadedJSON)=1);
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_PICKING_SCAN
    @OutgoingSlipID int,
    @LotBarcode varchar(80),
    @ScannedLots nvarchar(max)=N'[]'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status varchar(15), @OrderCustomer varchar(20);
    SELECT @Status=UPPER(ISNULL(Status,'')), @OrderCustomer=CustomerCode
    FROM dbo.FG_ShipmentOrder
    WHERE ShipmentOrderID=@OutgoingSlipID AND NULLIF(OutgoingSlipNumber,'') IS NOT NULL;
    IF @Status IS NULL THROW 51800, 'Outgoing slip was not found.', 1;
    IF @Status<>'RELEASED'
    BEGIN
        DECLARE @StatusMessage nvarchar(2048)=CONCAT('Only RELEASED outgoing slips can be picked. Current status: ',@Status,'.');
        THROW 51801, @StatusMessage, 1;
    END;
    IF ISJSON(COALESCE(@ScannedLots,N''))<>1 THROW 51803, 'The scanned LOT list is invalid.', 1;

    DECLARE @Scanned TABLE(OutgoingSlipLineID int NOT NULL,StockID int NOT NULL PRIMARY KEY,Qty decimal(12,3) NOT NULL);
    IF EXISTS
    (
        SELECT StockID FROM OPENJSON(@ScannedLots)
        WITH(StockID int '$.StockId') GROUP BY StockID HAVING StockID IS NULL OR COUNT(*)>1
    ) THROW 51803, 'This FG LOT is already scanned.', 1;
    INSERT @Scanned
    SELECT OutgoingSlipLineID,StockID,Qty
    FROM OPENJSON(@ScannedLots)
    WITH(OutgoingSlipLineID int '$.OutgoingSlipLineId',StockID int '$.StockId',Qty decimal(12,3) '$.Qty');

    DECLARE @StockID int,@StockNumber varchar(24),@ItemNo varchar(20),@LotID int,@LotNo varchar(40),
            @StockCustomer varchar(20),@Qty decimal(12,3),@Location varchar(20),@StockStatus varchar(15),
            @HoldFlag bit,@StockTS datetime2;
    SELECT TOP(1) @StockID=S.StockID,@StockNumber=S.StockNumber,@ItemNo=S.ItemNo,@LotID=S.LotID,
        @LotNo=L.LotCode,@StockCustomer=S.CustomerCode,@Qty=ISNULL(S.Qty,0),@Location=S.Location,
        @StockStatus=UPPER(ISNULL(S.Status,'')),@HoldFlag=ISNULL(S.HoldFlag,0),@StockTS=S.StockTS
    FROM dbo.tbl_Lot L JOIN dbo.FG_Inventory S ON S.LotID=L.LotID
    WHERE UPPER(ISNULL(L.LotCode,''))=UPPER(LTRIM(RTRIM(@LotBarcode)))
    ORDER BY S.StockID DESC;
    IF @StockID IS NULL THROW 51802, 'The scanned FG LOT was not found.', 1;
    IF EXISTS(SELECT 1 FROM @Scanned WHERE StockID=@StockID) THROW 51803, 'This FG LOT is already scanned.', 1;
    IF @StockStatus<>'AVAILABLE' OR @Qty<=0 THROW 51804, 'This FG LOT is not available.', 1;
    IF @HoldFlag=1 THROW 51805, 'This FG LOT is on hold.', 1;
    IF NULLIF(@StockCustomer,'') IS NOT NULL AND ISNULL(@OrderCustomer,'')<>@StockCustomer
        THROW 51806, 'This FG LOT belongs to a different customer.', 1;

    DECLARE @LineID int,@RequiredQty decimal(12,3),@AlreadyQty decimal(12,3),@RemainingTotal decimal(12,3);
    SELECT TOP(1) @LineID=L.ShipmentOrderLineID,@RequiredQty=ISNULL(L.OrderedQty,0),@AlreadyQty=ISNULL(P.Qty,0)
    FROM dbo.FG_ShipmentOrderLine L
    OUTER APPLY(SELECT SUM(S.Qty) Qty FROM @Scanned S WHERE S.OutgoingSlipLineID=L.ShipmentOrderLineID) P
    WHERE L.ShipmentOrderID=@OutgoingSlipID AND L.ItemNo=@ItemNo
      AND ISNULL(L.OrderedQty,0)-ISNULL(P.Qty,0)>=@Qty
    ORDER BY ISNULL(L.LineSeq,0),L.ShipmentOrderLineID;
    SELECT @RemainingTotal=SUM(ISNULL(L.OrderedQty,0)-ISNULL(P.Qty,0))
    FROM dbo.FG_ShipmentOrderLine L
    OUTER APPLY(SELECT SUM(S.Qty) Qty FROM @Scanned S WHERE S.OutgoingSlipLineID=L.ShipmentOrderLineID) P
    WHERE L.ShipmentOrderID=@OutgoingSlipID AND L.ItemNo=@ItemNo;
    IF @RemainingTotal IS NULL THROW 51807, 'This part is not required by the outgoing slip.', 1;
    IF @LineID IS NULL THROW 51808, 'LOT quantity exceeds the remaining part quantity.', 1;

    DECLARE @FifoStockID int,@FifoLotNo varchar(40),@FifoLocation varchar(20);
    SELECT TOP(1) @FifoStockID=F.StockID,@FifoLotNo=FL.LotCode,@FifoLocation=F.Location
    FROM dbo.FG_Inventory F
    LEFT JOIN dbo.tbl_Lot FL ON FL.LotID=F.LotID
    LEFT JOIN @Scanned Seen ON Seen.StockID=F.StockID
    WHERE F.ItemNo=@ItemNo AND UPPER(ISNULL(F.Status,''))='AVAILABLE'
      AND ISNULL(F.HoldFlag,0)=0 AND ISNULL(F.Qty,0)>0 AND Seen.StockID IS NULL
      AND (NULLIF(F.CustomerCode,'') IS NULL OR F.CustomerCode=@OrderCustomer)
      AND EXISTS
      (
          SELECT 1 FROM dbo.FG_ShipmentOrderLine L
          OUTER APPLY(SELECT SUM(S.Qty) Qty FROM @Scanned S WHERE S.OutgoingSlipLineID=L.ShipmentOrderLineID) P
          WHERE L.ShipmentOrderID=@OutgoingSlipID AND L.ItemNo=@ItemNo
            AND ISNULL(L.OrderedQty,0)-ISNULL(P.Qty,0)>=ISNULL(F.Qty,0)
      )
    ORDER BY ISNULL(F.StockTS,'9999-12-31'),F.StockID;
    IF @FifoStockID<>@StockID
    BEGIN
        DECLARE @FifoMessage nvarchar(2048)=CONCAT('Scan ',COALESCE(@FifoLotNo,'-'),' first. Location: ',COALESCE(@FifoLocation,'-'),'.');
        THROW 51809, @FifoMessage, 1;
    END;

    SELECT S.StockID,S.StockNumber,S.ItemNo,I.ItemName,S.LotID,L.LotCode AS LotNo,S.CustomerCode,
        ISNULL(S.Qty,0) Qty,I.DefaultUOM AS Unit,S.Location,S.Status,S.StockTS,@LineID AS OutgoingSlipLineID
    FROM dbo.FG_Inventory S
    LEFT JOIN dbo.tbl_Lot L ON L.LotID=S.LotID
    LEFT JOIN dbo.MD_Item I ON I.ItemNo=S.ItemNo
    WHERE S.StockID=@StockID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_PICKING_COMPLETE
    @OutgoingSlipID int,
    @Lots nvarchar(max),
    @OperatorID nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Status varchar(15),@OrderCustomer varchar(20);
        SELECT @Status=UPPER(ISNULL(Status,'')),@OrderCustomer=CustomerCode
        FROM dbo.FG_ShipmentOrder WITH(UPDLOCK,HOLDLOCK)
        WHERE ShipmentOrderID=@OutgoingSlipID AND NULLIF(OutgoingSlipNumber,'') IS NOT NULL;
        IF @Status IS NULL THROW 51820, 'Outgoing slip was not found.', 1;
        IF @Status<>'RELEASED' THROW 51821, 'Only RELEASED outgoing slips can be completed.', 1;
        IF ISJSON(COALESCE(@Lots,N''))<>1 THROW 51822, 'The scanned LOT list is invalid.', 1;

        DECLARE @Scanned TABLE(OutgoingSlipLineID int NOT NULL,StockID int NOT NULL PRIMARY KEY,Qty decimal(12,3) NOT NULL);
        IF EXISTS
        (
            SELECT StockID FROM OPENJSON(@Lots)
            WITH(StockID int '$.StockId') GROUP BY StockID HAVING StockID IS NULL OR COUNT(*)>1
        ) THROW 51822, 'The scanned LOT list contains a duplicate or invalid LOT.', 1;
        INSERT @Scanned
        SELECT OutgoingSlipLineID,StockID,Qty
        FROM OPENJSON(@Lots)
        WITH(OutgoingSlipLineID int '$.OutgoingSlipLineId',StockID int '$.StockId',Qty decimal(12,3) '$.Qty');
        IF NOT EXISTS(SELECT 1 FROM @Scanned) THROW 51822, 'Scan every listed LOT before completing.', 1;

        IF EXISTS
        (
            SELECT 1 FROM dbo.FG_ShipmentOrderLine L
            LEFT JOIN(SELECT OutgoingSlipLineID,SUM(Qty) Qty FROM @Scanned GROUP BY OutgoingSlipLineID) P
              ON P.OutgoingSlipLineID=L.ShipmentOrderLineID
            WHERE L.ShipmentOrderID=@OutgoingSlipID AND ISNULL(P.Qty,0)<>ISNULL(L.OrderedQty,0)
        ) THROW 51823, 'Scanned LOT quantities must equal every listed part quantity.', 1;
        IF EXISTS
        (
            SELECT 1 FROM @Scanned P LEFT JOIN dbo.FG_ShipmentOrderLine L
              ON L.ShipmentOrderID=@OutgoingSlipID AND L.ShipmentOrderLineID=P.OutgoingSlipLineID
            WHERE L.ShipmentOrderLineID IS NULL
        ) THROW 51824, 'A scanned LOT is not listed on this outgoing slip.', 1;
        IF EXISTS
        (
            SELECT 1 FROM @Scanned P
            JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderID=@OutgoingSlipID AND L.ShipmentOrderLineID=P.OutgoingSlipLineID
            LEFT JOIN dbo.FG_Inventory S WITH(UPDLOCK,HOLDLOCK) ON S.StockID=P.StockID
            WHERE S.StockID IS NULL OR UPPER(ISNULL(S.Status,''))<>'AVAILABLE' OR ISNULL(S.HoldFlag,0)=1
               OR P.Qty<=0 OR P.Qty<>S.Qty OR ISNULL(S.ItemNo,'')<>ISNULL(L.ItemNo,'')
               OR (NULLIF(S.CustomerCode,'') IS NOT NULL AND ISNULL(@OrderCustomer,'')<>S.CustomerCode)
        ) THROW 51825, 'A scanned LOT customer, part, quantity, hold, or inventory status is invalid.', 1;
        IF EXISTS
        (
            SELECT 1 FROM @Scanned P
            JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderID=@OutgoingSlipID AND L.ShipmentOrderLineID=P.OutgoingSlipLineID
            JOIN dbo.FG_Inventory Chosen ON Chosen.StockID=P.StockID
            JOIN dbo.FG_Inventory Older WITH(UPDLOCK,HOLDLOCK)
              ON Older.ItemNo=L.ItemNo AND UPPER(ISNULL(Older.Status,''))='AVAILABLE'
             AND ISNULL(Older.HoldFlag,0)=0 AND ISNULL(Older.Qty,0)>0
             AND (NULLIF(Older.CustomerCode,'') IS NULL OR Older.CustomerCode=@OrderCustomer)
             AND (ISNULL(Older.StockTS,'9999-12-31')<ISNULL(Chosen.StockTS,'9999-12-31')
               OR (ISNULL(Older.StockTS,'9999-12-31')=ISNULL(Chosen.StockTS,'9999-12-31') AND Older.StockID<Chosen.StockID))
            LEFT JOIN @Scanned Earlier ON Earlier.StockID=Older.StockID
            WHERE Earlier.StockID IS NULL
              AND EXISTS(SELECT 1 FROM dbo.FG_ShipmentOrderLine E
                         WHERE E.ShipmentOrderID=@OutgoingSlipID AND E.ItemNo=Older.ItemNo
                           AND ISNULL(E.OrderedQty,0)>=ISNULL(Older.Qty,0))
        ) THROW 51826, 'A scanned LOT violates FIFO order.', 1;

        DECLARE @Qty decimal(12,3)=(SELECT SUM(Qty) FROM @Scanned);
        INSERT dbo.FG_PickingFifo(PickNumber,ShipmentOrderID,PickerID,StartTS,EndTS,PicksJSON,PickedQty,OrderedQty,Status,CreatedBy,CreatedTS)
        VALUES(CONCAT('PICK-',FORMAT(SYSDATETIME(),'yyMMddHHmmssfff'),RIGHT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),4)),
               @OutgoingSlipID,@OperatorID,SYSDATETIME(),SYSDATETIME(),@Lots,@Qty,@Qty,'Picked','pda',SYSDATETIME());
        DECLARE @PickID int=CONVERT(int,SCOPE_IDENTITY());

        INSERT dbo.FG_PickingDetail(PickID,ShipmentOrderLineID,StockID,LotID,ItemNo,Qty,Location,PickSeq,CreatedBy,CreatedTS)
        SELECT @PickID,P.OutgoingSlipLineID,P.StockID,S.LotID,S.ItemNo,P.Qty,S.Location,
               ROW_NUMBER() OVER(ORDER BY ISNULL(L.LineSeq,0),ISNULL(S.StockTS,'9999-12-31'),S.StockID),
               'pda',SYSDATETIME()
        FROM @Scanned P JOIN dbo.FG_Inventory S ON S.StockID=P.StockID
        JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderLineID=P.OutgoingSlipLineID;

        UPDATE S SET Status='RESERVED',ModifiedBy=@OperatorID,ModifiedTS=SYSDATETIME()
        FROM dbo.FG_Inventory S JOIN @Scanned P ON P.StockID=S.StockID;
        UPDATE L SET AllocatedQty=OrderedQty,ReservationStatus='Picked',ReservedAt=SYSDATETIME(),
            ModifiedBy=@OperatorID,ModifiedTS=SYSDATETIME()
        FROM dbo.FG_ShipmentOrderLine L WHERE L.ShipmentOrderID=@OutgoingSlipID;
        UPDATE dbo.FG_ShipmentOrder SET Status='PICKED',ModifiedBy=@OperatorID,ModifiedTS=SYSDATETIME()
        WHERE ShipmentOrderID=@OutgoingSlipID;
        COMMIT TRANSACTION;
        SELECT @PickID;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_LOADING_ORDER_SCAN
    @OrderNumber varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OrderID int,@PickID int,@Status varchar(15),@CustomerCode varchar(20),
            @ShipOrderNumber varchar(40),@ShipDate date,@Destination varchar(30);
    SELECT TOP(1) @OrderID=ShipmentOrderID,@ShipOrderNumber=ShipOrderNumber,
        @CustomerCode=CustomerCode,@ShipDate=ShipDate,@Destination=DestPlant,
        @Status=UPPER(ISNULL(Status,''))
    FROM dbo.FG_ShipmentOrder
    WHERE UPPER(ISNULL(ShipOrderNumber,''))=UPPER(LTRIM(RTRIM(@OrderNumber)))
    ORDER BY ShipmentOrderID DESC;
    IF @OrderID IS NULL THROW 51900, 'Shipment order barcode was not found.', 1;
    IF @Status<>'PICKED' THROW 51901, 'Only PICKED shipment orders can be loaded.', 1;
    IF EXISTS(SELECT 1 FROM dbo.FG_LoadingConfirm WHERE ShipmentOrderID=@OrderID)
        THROW 51902, 'This shipment order was already loaded.', 1;

    SELECT TOP(1) @PickID=PickID
    FROM dbo.FG_PickingFifo
    WHERE ShipmentOrderID=@OrderID AND UPPER(ISNULL(Status,''))='PICKED'
    ORDER BY ISNULL(EndTS,CreatedTS) DESC,PickID DESC;
    IF @PickID IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.FG_PickingDetail WHERE PickID=@PickID)
        THROW 51903, 'No completed picking detail exists for this shipment order.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.FG_PickingDetail D
        JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderLineID=D.ShipmentOrderLineID
        LEFT JOIN dbo.FG_Inventory S ON S.StockID=D.StockID
        WHERE D.PickID=@PickID
          AND (L.ShipmentOrderID<>@OrderID OR S.StockID IS NULL
               OR UPPER(ISNULL(L.ReservationStatus,''))<>'PICKED'
               OR UPPER(ISNULL(S.Status,''))<>'RESERVED' OR ISNULL(S.HoldFlag,0)=1
               OR D.Qty<=0 OR D.Qty<>S.Qty OR D.ItemNo<>S.ItemNo OR D.ItemNo<>L.ItemNo
               OR (NULLIF(S.CustomerCode,'') IS NOT NULL AND ISNULL(@CustomerCode,'')<>S.CustomerCode))
    ) THROW 51904, 'A picked product changed status, quantity, hold, customer, or part before loading.', 1;

    SELECT @OrderID AS ShipmentOrderID,@ShipOrderNumber AS Barcode,
        @ShipOrderNumber AS ShipOrderNumber,@CustomerCode AS CustomerCode,
        @ShipDate AS ShipDate,@Destination AS Destination;
    SELECT S.StockID,D.ShipmentOrderLineID,@OrderID AS ShipmentOrderID,
        @ShipOrderNumber AS ShipOrderNumber,@CustomerCode AS CustomerCode,
        D.ItemNo,I.ItemName,LOT.LotCode AS LotNo,S.StockNumber,D.Qty,
        I.DefaultUOM AS Unit,D.Location
    FROM dbo.FG_PickingDetail D
    JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderLineID=D.ShipmentOrderLineID
    JOIN dbo.FG_Inventory S ON S.StockID=D.StockID
    LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=D.LotID
    LEFT JOIN dbo.MD_Item I ON I.ItemNo=D.ItemNo
    WHERE D.PickID=@PickID
    ORDER BY D.PickSeq,D.PickDetailID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_LOADING_STOCK_SCAN
    @Barcode varchar(80),
    @ShipmentOrderID int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status varchar(15),@CustomerCode varchar(20),@ShipOrderNumber varchar(40),@PickID int;
    SELECT @Status=UPPER(ISNULL(Status,'')),@CustomerCode=CustomerCode,@ShipOrderNumber=ShipOrderNumber
    FROM dbo.FG_ShipmentOrder WHERE ShipmentOrderID=@ShipmentOrderID;
    IF @Status IS NULL THROW 51900, 'Shipment order was not found.', 1;
    IF @Status<>'PICKED' THROW 51901, 'Only PICKED shipment orders can be loaded.', 1;
    IF EXISTS(SELECT 1 FROM dbo.FG_LoadingConfirm WHERE ShipmentOrderID=@ShipmentOrderID)
        THROW 51902, 'This shipment order was already loaded.', 1;
    SELECT TOP(1) @PickID=PickID FROM dbo.FG_PickingFifo
    WHERE ShipmentOrderID=@ShipmentOrderID AND UPPER(ISNULL(Status,''))='PICKED'
    ORDER BY ISNULL(EndTS,CreatedTS) DESC,PickID DESC;
    IF @PickID IS NULL THROW 51903, 'No completed picking detail exists for this shipment order.', 1;

    DECLARE @Normalized varchar(80)=LTRIM(RTRIM(@Barcode));
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.FG_Inventory S LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=S.LotID
        WHERE UPPER(ISNULL(S.StockNumber,''))=UPPER(@Normalized)
           OR UPPER(ISNULL(LOT.LotCode,''))=UPPER(@Normalized)
    ) THROW 51905, 'This barcode does not match an FG LOT or stock record.', 1;

    DECLARE @StockID int,@LineID int,@ItemNo varchar(20),@ItemName nvarchar(120),
            @LotNo varchar(80),@StockNumber varchar(80),@Qty decimal(12,3),@InventoryQty decimal(12,3),
            @Unit varchar(10),@Location varchar(20),@StockStatus varchar(15),
            @Hold bit,@StockCustomer varchar(20),@StockItemNo varchar(20),@LineItemNo varchar(20),@LineStatus varchar(15);
    SELECT TOP(1) @StockID=S.StockID,@LineID=D.ShipmentOrderLineID,@ItemNo=D.ItemNo,
        @ItemName=I.ItemName,@LotNo=LOT.LotCode,@StockNumber=S.StockNumber,@Qty=D.Qty,@InventoryQty=S.Qty,
        @Unit=I.DefaultUOM,@Location=D.Location,@StockStatus=UPPER(ISNULL(S.Status,'')),
        @Hold=ISNULL(S.HoldFlag,0),@StockCustomer=S.CustomerCode,@StockItemNo=S.ItemNo,
        @LineItemNo=L.ItemNo,@LineStatus=UPPER(ISNULL(L.ReservationStatus,''))
    FROM dbo.FG_PickingDetail D
    JOIN dbo.FG_Inventory S ON S.StockID=D.StockID
    JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderLineID=D.ShipmentOrderLineID
    LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=S.LotID
    LEFT JOIN dbo.MD_Item I ON I.ItemNo=D.ItemNo
    WHERE D.PickID=@PickID
      AND (UPPER(ISNULL(S.StockNumber,''))=UPPER(@Normalized)
        OR UPPER(ISNULL(LOT.LotCode,''))=UPPER(@Normalized))
    ORDER BY D.PickSeq,D.PickDetailID;
    IF @StockID IS NULL
    BEGIN
        IF EXISTS
        (
            SELECT 1 FROM dbo.FG_PickingDetail D
            JOIN dbo.FG_PickingFifo P ON P.PickID=D.PickID
            JOIN dbo.FG_Inventory S ON S.StockID=D.StockID
            LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=S.LotID
            WHERE P.ShipmentOrderID<>@ShipmentOrderID
              AND (UPPER(ISNULL(S.StockNumber,''))=UPPER(@Normalized)
                OR UPPER(ISNULL(LOT.LotCode,''))=UPPER(@Normalized))
        ) THROW 51906, 'This product belongs to a different shipment order.', 1;
        THROW 51907, 'This product has not completed release picking for this shipment order.', 1;
    END;
    IF @StockStatus<>'RESERVED' THROW 51908, 'This picked product is no longer reserved.', 1;
    IF @Hold=1 THROW 51909, 'This picked product is on hold.', 1;
    IF NULLIF(@StockCustomer,'') IS NOT NULL AND ISNULL(@CustomerCode,'')<>@StockCustomer
        THROW 51910, 'This picked product belongs to a different customer.', 1;
    IF @Qty<>@InventoryQty OR @ItemNo<>@StockItemNo OR @ItemNo<>@LineItemNo OR @LineStatus<>'PICKED'
        THROW 51904, 'This picked product changed quantity, part, or line status before loading.', 1;

    SELECT @StockID AS StockID,@LineID AS ShipmentOrderLineID,@ShipmentOrderID AS ShipmentOrderID,
        @ShipOrderNumber AS ShipOrderNumber,@CustomerCode AS CustomerCode,@ItemNo AS ItemNo,
        @ItemName AS ItemName,@LotNo AS LotNo,@StockNumber AS StockNumber,@Qty AS Qty,
        @Unit AS Unit,@Location AS Location;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_LOADING_COMPLETE
    @LicensePlate varchar(20),
    @ShipmentOrderID int,
    @StockIDs nvarchar(max),
    @OperatorID nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Status varchar(15),@CustomerCode varchar(20),@ShipDate date,@PickID int;
        SELECT @Status=UPPER(ISNULL(Status,'')),@CustomerCode=CustomerCode,@ShipDate=ShipDate
        FROM dbo.FG_ShipmentOrder WITH(UPDLOCK,HOLDLOCK)
        WHERE ShipmentOrderID=@ShipmentOrderID;
        IF @Status IS NULL THROW 51900, 'Shipment order was not found.', 1;
        IF @Status<>'PICKED' THROW 51901, 'Only PICKED shipment orders can be loaded.', 1;
        IF EXISTS(SELECT 1 FROM dbo.FG_LoadingConfirm WITH(UPDLOCK,HOLDLOCK) WHERE ShipmentOrderID=@ShipmentOrderID)
            THROW 51902, 'This shipment order was already loaded.', 1;
        IF ISJSON(COALESCE(@StockIDs,N''))<>1 THROW 51911, 'The scanned stock list is invalid.', 1;

        DECLARE @Scanned TABLE(StockID int NOT NULL PRIMARY KEY);
        IF EXISTS
        (
            SELECT TRY_CONVERT(int,[value]) StockID FROM OPENJSON(@StockIDs)
            GROUP BY TRY_CONVERT(int,[value]) HAVING TRY_CONVERT(int,[value]) IS NULL OR COUNT(*)>1
        ) THROW 51911, 'The scanned stock list contains a duplicate or invalid product.', 1;
        INSERT @Scanned SELECT TRY_CONVERT(int,[value]) FROM OPENJSON(@StockIDs);
        IF NOT EXISTS(SELECT 1 FROM @Scanned) THROW 51911, 'Scan every picked product before confirming.', 1;

        SELECT TOP(1) @PickID=PickID FROM dbo.FG_PickingFifo WITH(UPDLOCK,HOLDLOCK)
        WHERE ShipmentOrderID=@ShipmentOrderID AND UPPER(ISNULL(Status,''))='PICKED'
        ORDER BY ISNULL(EndTS,CreatedTS) DESC,PickID DESC;
        IF @PickID IS NULL THROW 51903, 'No completed picking detail exists for this shipment order.', 1;

        DECLARE @Expected TABLE
        (
            StockID int NOT NULL PRIMARY KEY,ShipmentOrderLineID int NOT NULL,LotID int NULL,
            ItemNo varchar(20) NOT NULL,LotNo varchar(80) NULL,StockNumber varchar(80) NULL,
            Qty decimal(12,3) NOT NULL,InventoryQty decimal(12,3) NOT NULL,Unit varchar(10) NULL,Location varchar(20) NULL,
            StockStatus varchar(15) NULL,HoldFlag bit NOT NULL,StockCustomer varchar(20) NULL,
            LineStatus varchar(15) NULL,LineItemNo varchar(20) NULL,StockItemNo varchar(20) NULL,PickSeq int NOT NULL
        );
        INSERT @Expected
        SELECT S.StockID,D.ShipmentOrderLineID,D.LotID,D.ItemNo,LOT.LotCode,S.StockNumber,D.Qty,S.Qty,
            I.DefaultUOM,D.Location,UPPER(ISNULL(S.Status,'')),ISNULL(S.HoldFlag,0),S.CustomerCode,
            UPPER(ISNULL(L.ReservationStatus,'')),L.ItemNo,S.ItemNo,D.PickSeq
        FROM dbo.FG_PickingDetail D
        JOIN dbo.FG_ShipmentOrderLine L WITH(UPDLOCK,HOLDLOCK) ON L.ShipmentOrderLineID=D.ShipmentOrderLineID
        JOIN dbo.FG_Inventory S WITH(UPDLOCK,HOLDLOCK) ON S.StockID=D.StockID
        LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=D.LotID
        LEFT JOIN dbo.MD_Item I ON I.ItemNo=D.ItemNo
        WHERE D.PickID=@PickID AND L.ShipmentOrderID=@ShipmentOrderID;
        IF NOT EXISTS(SELECT 1 FROM @Expected) THROW 51903, 'No completed picking detail exists for this shipment order.', 1;
        IF EXISTS
        (
            SELECT 1 FROM @Expected
            WHERE StockStatus<>'RESERVED' OR HoldFlag=1 OR Qty<=0
               OR ItemNo<>LineItemNo OR ItemNo<>StockItemNo
               OR LineStatus<>'PICKED'
               OR (NULLIF(StockCustomer,'') IS NOT NULL AND ISNULL(@CustomerCode,'')<>StockCustomer)
               OR Qty<>InventoryQty
        ) THROW 51904, 'A picked product changed status, quantity, hold, customer, or part before loading.', 1;
        IF EXISTS(SELECT 1 FROM @Expected E LEFT JOIN @Scanned S ON S.StockID=E.StockID WHERE S.StockID IS NULL)
            THROW 51912, 'Scan all picked products before confirming.', 1;
        IF EXISTS(SELECT 1 FROM @Scanned S LEFT JOIN @Expected E ON E.StockID=S.StockID WHERE E.StockID IS NULL)
            THROW 51913, 'The loading list contains a product that is not assigned to this shipment order.', 1;

        DECLARE @Now datetime2=SYSDATETIME(),@LoadingNumber varchar(24),@LoadedJson nvarchar(max),@OTDStatus varchar(10);
        SET @LoadingNumber=CONCAT('LDG-',FORMAT(@Now,'yyMMddHHmmssfff'),RIGHT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),5));
        SET @OTDStatus=CASE WHEN @ShipDate IS NULL THEN 'Unknown' WHEN CAST(@Now AS date)<=@ShipDate THEN 'OnTime' ELSE 'Late' END;
        SET @LoadedJson=(SELECT StockID AS stockId,ShipmentOrderLineID AS shipmentOrderLineId,
            ItemNo AS itemNo,LotNo AS lotNo,StockNumber AS stockNumber,Qty AS qty,Unit AS unit,Location AS location
            FROM @Expected ORDER BY PickSeq,StockID FOR JSON PATH);

        INSERT dbo.FG_LoadingConfirm(LoadingNumber,ShipmentOrderID,PickID,LicensePlate,CarrierCode,
            DockNo,ArrivalTS,DepartureTS,PalletsLoadedJSON,OTDStatus,OperatorID,ConfirmedAt,CreatedBy,CreatedTS)
        VALUES(@LoadingNumber,@ShipmentOrderID,@PickID,@LicensePlate,
            (SELECT CarrierCode FROM dbo.FG_ShipmentOrder WHERE ShipmentOrderID=@ShipmentOrderID),
            'PDA',@Now,@Now,@LoadedJson,@OTDStatus,@OperatorID,@Now,'pda',@Now);
        DECLARE @LoadingID int=CONVERT(int,SCOPE_IDENTITY());

        UPDATE L SET ReservationStatus='Loaded',ReleasedAt=@Now,ModifiedBy=@OperatorID,ModifiedTS=@Now
        FROM dbo.FG_ShipmentOrderLine L
        WHERE EXISTS(SELECT 1 FROM @Expected E WHERE E.ShipmentOrderLineID=L.ShipmentOrderLineID);
        UPDATE S SET Status='LOADED',ModifiedBy=@OperatorID,ModifiedTS=@Now
        FROM dbo.FG_Inventory S JOIN @Expected E ON E.StockID=S.StockID;
        UPDATE dbo.FG_ShipmentOrder SET Status='LOADED',ModifiedBy=@OperatorID,ModifiedTS=@Now
        WHERE ShipmentOrderID=@ShipmentOrderID;
        COMMIT TRANSACTION;
        SELECT @LoadingID AS LoadingID,@LoadingNumber AS LoadingNumber,(SELECT COUNT(*) FROM @Expected) AS LoadedCount,@OTDStatus AS OTDStatus;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- =====================================================================
--  FG Customer Return Note
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FG_CustomerReturn', N'U') IS NULL
    THROW 50001, 'dbo.FG_CustomerReturn does not exist.', 1;

IF COL_LENGTH(N'dbo.FG_CustomerReturn', N'Note') IS NULL
    ALTER TABLE dbo.FG_CustomerReturn ADD [Note] NVARCHAR(500) NULL;

IF COL_LENGTH(N'dbo.FG_CustomerReturn', N'StockID') IS NULL
    ALTER TABLE dbo.FG_CustomerReturn ADD StockID INT NULL;
IF COL_LENGTH(N'dbo.FG_CustomerReturn', N'LotID') IS NULL
    ALTER TABLE dbo.FG_CustomerReturn ADD LotID INT NULL;
IF COL_LENGTH(N'dbo.FG_CustomerReturn', N'ItemNo') IS NULL
    ALTER TABLE dbo.FG_CustomerReturn ADD ItemNo VARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.FG_CustomerReturn', N'ReturnQty') IS NULL
    ALTER TABLE dbo.FG_CustomerReturn ADD ReturnQty DECIMAL(12,3) NULL;
GO

UPDATE R
SET ItemNo=COALESCE(R.ItemNo,NULLIF(JSON_VALUE(J.SafeJson,'$[0].itemNo'),'')),
    ReturnQty=COALESCE(R.ReturnQty,TRY_CONVERT(decimal(12,3),JSON_VALUE(J.SafeJson,'$[0].qty')))
FROM dbo.FG_CustomerReturn R
CROSS APPLY(SELECT CASE WHEN ISJSON(R.ItemsJSON)=1 THEN R.ItemsJSON ELSE N'[]' END) J(SafeJson)
WHERE R.ItemNo IS NULL OR R.ReturnQty IS NULL;

UPDATE R
SET StockID=COALESCE(R.StockID,X.StockID),
    LotID=COALESCE(R.LotID,X.LotID),
    ItemNo=COALESCE(R.ItemNo,X.ItemNo),
    ReturnQty=COALESCE(R.ReturnQty,X.Qty)
FROM dbo.FG_CustomerReturn R
CROSS APPLY(SELECT CASE WHEN ISJSON(R.ItemsJSON)=1 THEN R.ItemsJSON ELSE N'[]' END) J(SafeJson)
OUTER APPLY
(
    SELECT TOP(1) S.StockID,S.LotID,S.ItemNo,S.Qty
    FROM dbo.FG_Inventory S
    LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=S.LotID
    LEFT JOIN dbo.FG_ShipmentOrderLine L ON L.StockID=S.StockID AND L.ShipmentOrderID=R.OriginalShipmentOrderID
    WHERE (NULLIF(JSON_VALUE(J.SafeJson,'$[0].stockNumber'),'') IS NOT NULL
           AND UPPER(S.StockNumber)=UPPER(JSON_VALUE(J.SafeJson,'$[0].stockNumber')))
       OR (NULLIF(JSON_VALUE(J.SafeJson,'$[0].lotNo'),'') IS NOT NULL
           AND UPPER(LOT.LotCode)=UPPER(JSON_VALUE(J.SafeJson,'$[0].lotNo')))
       OR (R.OriginalShipmentOrderID IS NOT NULL AND L.ShipmentOrderLineID IS NOT NULL
           AND (R.ItemNo IS NULL OR S.ItemNo=R.ItemNo))
    ORDER BY CASE WHEN UPPER(S.StockNumber)=UPPER(JSON_VALUE(J.SafeJson,'$[0].stockNumber')) THEN 0
                  WHEN UPPER(LOT.LotCode)=UPPER(JSON_VALUE(J.SafeJson,'$[0].lotNo')) THEN 1 ELSE 2 END,
             S.StockID DESC
) X
WHERE R.StockID IS NULL OR R.LotID IS NULL;

-- Remove only obsolete demo returns that never represented a shipped stock.
DELETE dbo.FG_CustomerReturn
WHERE StockID IS NULL
  AND ((CreatedBy='fg-seed' AND ReturnNumber IN ('RMA-SEED-001','RMA-SEED-002'))
    OR (CreatedBy='pda-fg-six-demo' AND ReturnNumber='FG-RMA-DEMO-002'));

IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WHERE ReturnNumber IS NULL OR CustomerCode IS NULL
          OR OriginalShipmentOrderID IS NULL OR StockID IS NULL OR ItemNo IS NULL OR ReturnQty IS NULL
          OR ReturnReason IS NULL OR ItemsJSON IS NULL OR Status IS NULL OR ReceivedAt IS NULL OR ReceivedBy IS NULL)
    THROW 52022, 'FG_CustomerReturn contains incomplete rows that require manual correction.', 1;
IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND is_nullable=1
      AND name IN ('ReturnNumber','CustomerCode','OriginalShipmentOrderID','StockID','ItemNo','ReturnQty',
                   'ReturnReason','ItemsJSON','Status','ReceivedAt','ReceivedBy')
)
BEGIN
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'UX_FG_CustomerReturn_ReturnNumber')
    DROP INDEX UX_FG_CustomerReturn_ReturnNumber ON dbo.FG_CustomerReturn;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'UX_FG_CustomerReturn_Stock')
    DROP INDEX UX_FG_CustomerReturn_Stock ON dbo.FG_CustomerReturn;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'IX_FG_CustomerReturn_Received')
    DROP INDEX IX_FG_CustomerReturn_Received ON dbo.FG_CustomerReturn;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Order')
    ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT FK_FG_CustomerReturn_Order;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Stock')
    ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT FK_FG_CustomerReturn_Stock;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Lot')
    ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT FK_FG_CustomerReturn_Lot;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Item')
    ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT FK_FG_CustomerReturn_Item;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_FG_CustomerReturn_Qty')
    ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT CK_FG_CustomerReturn_Qty;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_FG_CustomerReturn_ItemsJSON')
    ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT CK_FG_CustomerReturn_ItemsJSON;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN ReturnNumber varchar(24) NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN CustomerCode varchar(20) NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN OriginalShipmentOrderID int NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN StockID int NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN ItemNo varchar(20) NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN ReturnQty decimal(12,3) NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN ReturnReason varchar(60) NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN ItemsJSON nvarchar(max) NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN Status varchar(15) NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN ReceivedAt datetime2 NOT NULL;
ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN ReceivedBy nvarchar(450) NOT NULL;
END;

IF EXISTS(SELECT StockID FROM dbo.FG_CustomerReturn WHERE StockID IS NOT NULL GROUP BY StockID HAVING COUNT(*)>1)
    THROW 52020, 'FG_CustomerReturn contains duplicate StockID values.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'UX_FG_CustomerReturn_ReturnNumber')
    CREATE UNIQUE INDEX UX_FG_CustomerReturn_ReturnNumber ON dbo.FG_CustomerReturn(ReturnNumber) WHERE ReturnNumber IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'UX_FG_CustomerReturn_Stock')
    CREATE UNIQUE INDEX UX_FG_CustomerReturn_Stock ON dbo.FG_CustomerReturn(StockID) WHERE StockID IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'IX_FG_CustomerReturn_Received')
    CREATE INDEX IX_FG_CustomerReturn_Received ON dbo.FG_CustomerReturn(ReceivedAt DESC,ReturnID DESC);

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Order')
    ALTER TABLE dbo.FG_CustomerReturn WITH CHECK ADD CONSTRAINT FK_FG_CustomerReturn_Order
        FOREIGN KEY(OriginalShipmentOrderID) REFERENCES dbo.FG_ShipmentOrder(ShipmentOrderID);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Stock')
    ALTER TABLE dbo.FG_CustomerReturn WITH CHECK ADD CONSTRAINT FK_FG_CustomerReturn_Stock
        FOREIGN KEY(StockID) REFERENCES dbo.FG_Inventory(StockID);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Lot')
    ALTER TABLE dbo.FG_CustomerReturn WITH CHECK ADD CONSTRAINT FK_FG_CustomerReturn_Lot
        FOREIGN KEY(LotID) REFERENCES dbo.tbl_Lot(LotID);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Item')
    ALTER TABLE dbo.FG_CustomerReturn WITH CHECK ADD CONSTRAINT FK_FG_CustomerReturn_Item
        FOREIGN KEY(ItemNo) REFERENCES dbo.MD_Item(ItemNo);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_FG_CustomerReturn_Qty')
    ALTER TABLE dbo.FG_CustomerReturn WITH CHECK ADD CONSTRAINT CK_FG_CustomerReturn_Qty
        CHECK(ReturnQty IS NULL OR ReturnQty>0);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_FG_CustomerReturn_ItemsJSON')
    ALTER TABLE dbo.FG_CustomerReturn WITH CHECK ADD CONSTRAINT CK_FG_CustomerReturn_ItemsJSON
        CHECK(ItemsJSON IS NULL OR ISJSON(ItemsJSON)=1);
GO

IF OBJECT_ID(N'dbo.FG_ReturnDisposition',N'U') IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM dbo.FG_ReturnDisposition)
        THROW 52021, 'FG_ReturnDisposition contains data and cannot be removed automatically.', 1;
    DROP TABLE dbo.FG_ReturnDisposition;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_SCAN
    @Barcode varchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @B varchar(80)=LTRIM(RTRIM(ISNULL(@Barcode,'')));
    IF LEN(@B)<3 OR LEN(@B)>80 OR @B COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9_./-]%'
        THROW 52000, 'The finished-good return barcode format is invalid.', 1;

    DECLARE @Candidates TABLE
    (
        StockID int NOT NULL,StockNumber varchar(80) NULL,LotID int NULL,LotNo varchar(80) NULL,
        ShipmentOrderID int NOT NULL,ShipOrderNumber varchar(40) NULL,CustomerCode varchar(20) NULL,
        ItemNo varchar(20) NULL,ItemName nvarchar(120) NULL,ShippedAt datetime2 NULL,
        Qty decimal(12,3) NULL,StockStatus varchar(20) NULL,HoldFlag bit NOT NULL
    );

    INSERT @Candidates
    SELECT DISTINCT S.StockID,S.StockNumber,S.LotID,LOT.LotCode,O.ShipmentOrderID,O.ShipOrderNumber,
        O.CustomerCode,D.ItemNo,I.ItemName,C.DepartureTS,D.Qty,UPPER(ISNULL(S.Status,'')),ISNULL(S.HoldFlag,0)
    FROM dbo.FG_LoadingConfirm C
    JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=C.ShipmentOrderID
    JOIN dbo.FG_PickingDetail D ON D.PickID=C.PickID
    JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderLineID=D.ShipmentOrderLineID AND L.ShipmentOrderID=O.ShipmentOrderID
    JOIN dbo.FG_Inventory S ON S.StockID=D.StockID
    LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=S.LotID
    LEFT JOIN dbo.MD_Item I ON I.ItemNo=D.ItemNo
    WHERE C.DepartureTS IS NOT NULL
      AND (UPPER(ISNULL(S.StockNumber,''))=UPPER(@B) OR UPPER(ISNULL(LOT.LotCode,''))=UPPER(@B));

    INSERT @Candidates
    SELECT DISTINCT S.StockID,S.StockNumber,S.LotID,LOT.LotCode,O.ShipmentOrderID,O.ShipOrderNumber,
        O.CustomerCode,L.ItemNo,I.ItemName,C.DepartureTS,
        COALESCE(NULLIF(L.AllocatedQty,0),NULLIF(S.Qty,0),NULLIF(L.OrderedQty,0)),
        UPPER(ISNULL(S.Status,'')),ISNULL(S.HoldFlag,0)
    FROM dbo.FG_LoadingConfirm C
    JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=C.ShipmentOrderID
    JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderID=O.ShipmentOrderID
    JOIN dbo.FG_Inventory S ON S.StockID=L.StockID
    LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=S.LotID
    LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
    WHERE C.DepartureTS IS NOT NULL
      AND (C.PickID IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.FG_PickingDetail D WHERE D.PickID=C.PickID))
      AND (UPPER(ISNULL(S.StockNumber,''))=UPPER(@B) OR UPPER(ISNULL(LOT.LotCode,''))=UPPER(@B))
      AND NOT EXISTS(SELECT 1 FROM @Candidates X WHERE X.StockID=S.StockID AND X.ShipmentOrderID=O.ShipmentOrderID);

    IF NOT EXISTS(SELECT 1 FROM @Candidates)
    BEGIN
        IF EXISTS(SELECT 1 FROM dbo.FG_Inventory S LEFT JOIN dbo.tbl_Lot L ON L.LotID=S.LotID
                  WHERE UPPER(ISNULL(S.StockNumber,''))=UPPER(@B) OR UPPER(ISNULL(L.LotCode,''))=UPPER(@B))
            THROW 52001, 'The product exists, but no completed shipment history was found.', 1;
        THROW 52002, 'This barcode does not match a finished-good LOT or stock record.', 1;
    END;
    IF EXISTS(SELECT 1 FROM @Candidates WHERE ShippedAt>DATEADD(minute,5,SYSDATETIME()))
        THROW 52003, 'The shipment date is in the future. Verify the loading record.', 1;
    IF (SELECT COUNT(*) FROM (SELECT StockID,ShipmentOrderID FROM @Candidates GROUP BY StockID,ShipmentOrderID) X)>1
        THROW 52004, 'This barcode matches multiple shipped products. Scan the unique stock barcode.', 1;

    DECLARE @StockID int=(SELECT TOP(1) StockID FROM @Candidates ORDER BY ShippedAt DESC);
    IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WHERE StockID=@StockID)
        THROW 52005, 'This product was already received as a customer return.', 1;
    IF EXISTS(SELECT 1 FROM @Candidates WHERE StockID=@StockID AND StockStatus NOT IN ('LOADED','SHIPPED'))
        THROW 52006, 'Only loaded or shipped finished goods can be received as a customer return.', 1;
    IF EXISTS(SELECT 1 FROM @Candidates WHERE StockID=@StockID AND HoldFlag=1)
        THROW 52007, 'This finished good is already on hold.', 1;
    IF EXISTS(SELECT 1 FROM @Candidates WHERE StockID=@StockID AND
       (NULLIF(ShipOrderNumber,'') IS NULL OR NULLIF(CustomerCode,'') IS NULL OR
        NULLIF(ItemNo,'') IS NULL OR NULLIF(ItemName,'') IS NULL OR ISNULL(Qty,0)<=0))
        THROW 52008, 'The shipment record is incomplete. Verify shipment, customer, part, and quantity.', 1;

    SELECT TOP(1) @B AS Barcode,StockID,StockNumber,LotID,LotNo,ShipmentOrderID,ShipOrderNumber,
        CustomerCode,ItemNo,ItemName,ShippedAt,Qty
    FROM @Candidates WHERE StockID=@StockID ORDER BY ShippedAt DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_RECEIVE
    @Barcode varchar(80),
    @ReturnReason varchar(60),
    @Note nvarchar(500)=NULL,
    @OperatorID nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Reason varchar(60)=UPPER(LTRIM(RTRIM(ISNULL(@ReturnReason,''))));
    DECLARE @CleanNote nvarchar(500)=NULLIF(LTRIM(RTRIM(@Note)),N'');
    IF NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='FG_RETURN_REASON' AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
        THROW 52009, 'Select a valid return reason.', 1;
    IF LEN(ISNULL(@CleanNote,N''))>500 THROW 52010, 'Return note must be 500 characters or fewer.', 1;

    DECLARE @P TABLE
    (
        Barcode varchar(80),StockID int,StockNumber varchar(80),LotID int,LotNo varchar(80),
        ShipmentOrderID int,ShipOrderNumber varchar(40),CustomerCode varchar(20),ItemNo varchar(20),
        ItemName nvarchar(120),ShippedAt datetime2,Qty decimal(12,3)
    );
    INSERT @P EXEC dbo.FG_PDA_RETURN_SCAN @Barcode;

    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @StockID int=(SELECT StockID FROM @P),@Now datetime2=SYSDATETIME(),@ReturnID int,@ReturnNumber varchar(24);
        IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WITH(UPDLOCK,HOLDLOCK) WHERE StockID=@StockID)
            THROW 52005, 'This product was already received as a customer return.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.FG_Inventory WITH(UPDLOCK,HOLDLOCK)
                      WHERE StockID=@StockID AND UPPER(ISNULL(Status,'')) IN ('LOADED','SHIPPED') AND ISNULL(HoldFlag,0)=0)
            THROW 52011, 'The finished-good inventory status changed before return receipt.', 1;

        SET @ReturnNumber=CONCAT('RMA-',FORMAT(@Now,'yyMMddHHmmssfff'),LEFT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),5));
        INSERT dbo.FG_CustomerReturn
            (ReturnNumber,CustomerCode,OriginalShipmentOrderID,StockID,LotID,ItemNo,ReturnQty,
             ReturnReason,Note,ItemsJSON,Status,ReceivedAt,ReceivedBy,CapaTriggered,CreatedBy,CreatedTS)
        SELECT @ReturnNumber,CustomerCode,ShipmentOrderID,StockID,LotID,ItemNo,Qty,@Reason,@CleanNote,
            (SELECT StockID AS stockId,ItemNo AS itemNo,LotNo AS lotNo,StockNumber AS stockNumber,
                    Barcode AS barcode,Qty AS qty FOR JSON PATH),
            'Open',@Now,@OperatorID,0,'pda',@Now
        FROM @P;
        SET @ReturnID=CONVERT(int,SCOPE_IDENTITY());

        UPDATE dbo.FG_Inventory
        SET Status='RETURN_HOLD',HoldFlag=1,Location=NULL,ModifiedBy=@OperatorID,ModifiedTS=@Now
        WHERE StockID=@StockID;
        COMMIT TRANSACTION;

        SELECT @ReturnID AS ReturnID,Barcode,StockID,StockNumber,LotID,LotNo,ShipmentOrderID,
            ShipOrderNumber,CustomerCode,ItemNo,ItemName,ShippedAt,Qty
        FROM @P;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
GO


-- =====================================================================
--  WH Location Map Hierarchy
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_AreaLayout (
        AREACD NVARCHAR(80) NOT NULL CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY,
        X_PCT DECIMAL(8,2) NOT NULL,
        Y_PCT DECIMAL(8,2) NOT NULL,
        W_PCT DECIMAL(8,2) NOT NULL,
        H_PCT DECIMAL(8,2) NOT NULL,
        MODIFIED_BY NVARCHAR(80) NULL,
        MODIFIED_TS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_LAYOUT_MODIFIED_TS DEFAULT SYSDATETIME()
    );

    PRINT 'Created dbo.WH_AreaLayout';
END
ELSE
BEGIN
    PRINT 'dbo.WH_AreaLayout already exists';
END;
GO

IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.WH_AreaLayout', N'AREACD') < 160
BEGIN
    DECLARE @pkName sysname;
    SELECT @pkName = kc.name
    FROM sys.key_constraints kc
    WHERE kc.parent_object_id = OBJECT_ID(N'dbo.WH_AreaLayout')
      AND kc.[type] = 'PK';

    IF @pkName IS NOT NULL
    BEGIN
        DECLARE @dropSql nvarchar(max) = N'ALTER TABLE dbo.WH_AreaLayout DROP CONSTRAINT ' + QUOTENAME(@pkName);
        EXEC sys.sp_executesql @dropSql;
    END;

    ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN AREACD NVARCHAR(80) NOT NULL;
    ALTER TABLE dbo.WH_AreaLayout ADD CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY (AREACD);

    PRINT 'Expanded dbo.WH_AreaLayout.AREACD to NVARCHAR(80)';
END;
GO

IF OBJECT_ID(N'dbo.WH_WarehouseMaster', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_WarehouseMaster (
        WhCode VARCHAR(20) NOT NULL CONSTRAINT PK_WH_WAREHOUSE_MASTER PRIMARY KEY,
        WhName NVARCHAR(120) NULL,
        ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_WAREHOUSE_MASTER_ACTIVE DEFAULT 1,
        CreatedBy NVARCHAR(80) NULL,
        CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_WAREHOUSE_MASTER_CREATED_TS DEFAULT SYSDATETIME(),
        ModifiedBy NVARCHAR(80) NULL,
        ModifiedTS DATETIME2 NULL
    );

    PRINT 'Created dbo.WH_WarehouseMaster';
END;
GO

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    MERGE dbo.WH_WarehouseMaster AS tgt
    USING (
        SELECT DISTINCT CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20)) AS WhCode
        FROM dbo.MD_Location
        WHERE COALESCE(NULLIF(PlantCode, ''), 'WH') IS NOT NULL
    ) AS src ON tgt.WhCode = src.WhCode
    WHEN NOT MATCHED THEN INSERT
        (WhCode, WhName, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        (src.WhCode, src.WhCode, 1, 'system', SYSDATETIME());
END;
GO

IF OBJECT_ID(N'dbo.WH_AreaMaster', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_AreaMaster (
        WhCode VARCHAR(20) NULL,
        AreaCode VARCHAR(20) NOT NULL CONSTRAINT PK_WH_AREA_MASTER PRIMARY KEY,
        AreaName NVARCHAR(120) NULL,
        ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_AREA_MASTER_ACTIVE DEFAULT 1,
        CreatedBy NVARCHAR(80) NULL,
        CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_MASTER_CREATED_TS DEFAULT SYSDATETIME(),
        ModifiedBy NVARCHAR(80) NULL,
        ModifiedTS DATETIME2 NULL
    );

    PRINT 'Created dbo.WH_AreaMaster';
END;
GO

IF COL_LENGTH(N'dbo.WH_AreaMaster', N'WhCode') IS NULL
BEGIN
    ALTER TABLE dbo.WH_AreaMaster ADD WhCode VARCHAR(20) NULL;
    PRINT 'Added dbo.WH_AreaMaster.WhCode';
END;
GO

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    UPDATE A
       SET WhCode = COALESCE(NULLIF(A.WhCode, ''), X.WhCode, A.AreaCode)
    FROM dbo.WH_AreaMaster A
    OUTER APPLY (
        SELECT TOP (1) L.PlantCode AS WhCode
        FROM dbo.MD_Location L
        WHERE COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = A.AreaCode
          AND NULLIF(L.PlantCode, '') IS NOT NULL
        ORDER BY L.PlantCode
    ) X
    WHERE NULLIF(A.WhCode, '') IS NULL;

    MERGE dbo.WH_AreaMaster AS tgt
    USING (
        SELECT
            MIN(CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20))) AS WhCode,
            CAST(COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') AS varchar(20)) AS AreaCode
        FROM dbo.MD_Location
        WHERE COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') IS NOT NULL
        GROUP BY CAST(COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') AS varchar(20))
    ) AS src ON tgt.AreaCode = src.AreaCode
    WHEN NOT MATCHED THEN INSERT
        (WhCode, AreaCode, AreaName, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        (src.WhCode, src.AreaCode, src.AreaCode, 1, 'system', SYSDATETIME());
END;
GO

IF OBJECT_ID(N'dbo.WH_AreaSection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_AreaSection (
        WhCode VARCHAR(20) NULL,
        AreaCode VARCHAR(20) NOT NULL,
        SectionCode VARCHAR(20) NOT NULL,
        SectionName NVARCHAR(120) NULL,
        ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_AREA_SECTION_ACTIVE DEFAULT 1,
        CreatedBy NVARCHAR(80) NULL,
        CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_SECTION_CREATED_TS DEFAULT SYSDATETIME(),
        ModifiedBy NVARCHAR(80) NULL,
        ModifiedTS DATETIME2 NULL,
        CONSTRAINT PK_WH_AREA_SECTION PRIMARY KEY (AreaCode, SectionCode)
    );

    PRINT 'Created dbo.WH_AreaSection';
END;
GO

IF COL_LENGTH(N'dbo.WH_AreaSection', N'WhCode') IS NULL
BEGIN
    ALTER TABLE dbo.WH_AreaSection ADD WhCode VARCHAR(20) NULL;
    PRINT 'Added dbo.WH_AreaSection.WhCode';
END;
GO

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    UPDATE S
       SET WhCode = COALESCE(NULLIF(S.WhCode, ''), A.WhCode, X.WhCode)
    FROM dbo.WH_AreaSection S
    LEFT JOIN dbo.WH_AreaMaster A
           ON A.AreaCode = S.AreaCode
    OUTER APPLY (
        SELECT TOP (1) L.PlantCode AS WhCode
        FROM dbo.MD_Location L
        WHERE COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = S.AreaCode
          AND COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') = S.SectionCode
          AND NULLIF(L.PlantCode, '') IS NOT NULL
        ORDER BY L.PlantCode
    ) X
    WHERE NULLIF(S.WhCode, '') IS NULL;

    MERGE dbo.WH_AreaSection AS tgt
    USING (
        SELECT
            MIN(CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20))) AS WhCode,
            CAST(COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') AS varchar(20)) AS AreaCode,
            CAST(COALESCE(NULLIF(LocationType, ''), 'DEFAULT') AS varchar(20)) AS SectionCode
        FROM dbo.MD_Location
        WHERE COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') IS NOT NULL
        GROUP BY CAST(COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') AS varchar(20)),
                 CAST(COALESCE(NULLIF(LocationType, ''), 'DEFAULT') AS varchar(20))
    ) AS src
       ON tgt.AreaCode = src.AreaCode
      AND tgt.SectionCode = src.SectionCode
    WHEN NOT MATCHED THEN INSERT
        (WhCode, AreaCode, SectionCode, SectionName, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        (src.WhCode, src.AreaCode, src.SectionCode, src.SectionCode, 1, 'system', SYSDATETIME());
END;
GO

PRINT 'Warehouse hierarchy migration completed';
GO

IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NOT NULL
   AND EXISTS (
       SELECT 1
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.WH_AreaLayout')
         AND name IN (N'X_PCT', N'Y_PCT', N'W_PCT', N'H_PCT')
         AND precision < 8
   )
BEGIN
    ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN X_PCT DECIMAL(8,2) NOT NULL;
    ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN Y_PCT DECIMAL(8,2) NOT NULL;
    ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN W_PCT DECIMAL(8,2) NOT NULL;
    ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN H_PCT DECIMAL(8,2) NOT NULL;
END;
GO

-- =====================================================================
--  FG Location Assignment
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FG_LocationMaster', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_LocationMaster
    (
        LocationID varchar(20) NOT NULL,
        ActiveFlag bit NOT NULL CONSTRAINT DF_FG_LocationMaster_ActiveFlag DEFAULT (1),
        CreatedBy nvarchar(120) NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_FG_LocationMaster_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy nvarchar(120) NULL,
        ModifiedTS datetime2 NULL,
        CONSTRAINT PK_FG_LocationMaster PRIMARY KEY CLUSTERED (LocationID)
    );
END;

INSERT INTO dbo.FG_LocationMaster (LocationID, ActiveFlag, CreatedBy, CreatedTS)
SELECT DISTINCT L.LocationID, 1, N'migration', SYSDATETIME()
FROM dbo.MD_Location L
WHERE NOT EXISTS (SELECT 1 FROM dbo.FG_LocationMaster F WHERE F.LocationID = L.LocationID)
  AND
  (
      UPPER(ISNULL(L.LocationType, '')) IN ('FG', 'FINISHED_GOODS', 'FINISHED GOODS')
      OR UPPER(L.LocationID) LIKE 'FG%'
      OR EXISTS (SELECT 1 FROM dbo.FG_Inventory I WHERE I.Location = L.LocationID)
  );

PRINT 'dbo.FG_LocationMaster is ready.';
GO

-- FG history reads the existing operation records; inventory balances are not event quantities.
CREATE OR ALTER PROCEDURE dbo.FG_PDA_TRANSACTION_LIST
    @SearchText nvarchar(120)=NULL, @DateFrom date=NULL, @DateTo date=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @From date=COALESCE(@DateFrom,DATEADD(day,-30,CAST(GETDATE() AS date))), @To date=COALESCE(@DateTo,CAST(GETDATE() AS date));
    DECLARE @Search nvarchar(130)=N'%'+NULLIF(LTRIM(RTRIM(@SearchText)),N'')+N'%';
    ;WITH Events AS
    (
        SELECT P.CreatedTS AS EventTime,CONCAT('IN-',P.PutAwayID) AS EventID,L.LotCode AS LotNo,S.StockNumber,
            P.ItemNo,P.ActualLoc AS LocationID,P.Qty,'IN' AS Direction,'Put-Away' AS Status,
            COALESCE(P.OperatorID,P.CreatedBy) AS Worker,NULL AS ReasonCode,
            CONVERT(nvarchar(500),CONCAT(P.StorageMethod,CASE WHEN P.ContainerBarcode IS NULL THEN '' ELSE CONCAT(' / ',P.ContainerBarcode) END)) AS ReasonNote,
            CONVERT(nvarchar(450),NULL) AS Supervisor,CAST(NULL AS decimal(18,3)) AS BeforeQty,CAST(NULL AS decimal(18,3)) AS DeltaQty,CAST(NULL AS decimal(18,3)) AS AfterQty,
            'FG_PutAway' AS Source,CONVERT(nvarchar(500),COALESCE(P.ContainerBarcode,S.StockNumber)) AS Reference,NULL AS OutgoingSlip
        FROM dbo.FG_PutAway P LEFT JOIN dbo.FG_Inventory S ON S.StockID=P.StockID LEFT JOIN dbo.tbl_Lot L ON L.LotID=S.LotID
        WHERE UPPER(ISNULL(P.Status,'')) NOT IN ('CANCELLED','CANCELED')
        UNION ALL
        SELECT COALESCE(P.EndTS,P.CreatedTS),CONCAT('PICK-',P.PickID,'-',S.StockID),LOT.LotCode,S.StockNumber,
            COALESCE(S.ItemNo,L.ItemNo),COALESCE(L.Location,S.Location),COALESCE(J.Qty,J.LowerQty,NULLIF(L.AllocatedQty,0),L.OrderedQty),
            'PICK','Release',COALESCE(P.PickerID,P.CreatedBy),NULL,NULL,NULL,NULL,NULL,NULL,'FG_PickingFifo',O.ShipOrderNumber,O.OutgoingSlipNumber
        FROM dbo.FG_PickingFifo P JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=P.ShipmentOrderID
        OUTER APPLY OPENJSON(CASE WHEN ISJSON(P.PicksJSON)=1 THEN P.PicksJSON ELSE N'[]' END)
            WITH (StockID int '$.StockId',LowerStockID int '$.stockId',Qty decimal(18,3) '$.Qty',LowerQty decimal(18,3) '$.qty') J
        LEFT JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderID=P.ShipmentOrderID
            AND (L.StockID=COALESCE(J.StockID,J.LowerStockID) OR (J.StockID IS NULL AND J.LowerStockID IS NULL))
        LEFT JOIN dbo.FG_Inventory S ON S.StockID=COALESCE(J.StockID,J.LowerStockID,L.StockID)
        LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=COALESCE(S.LotID,L.LotID)
        WHERE UPPER(ISNULL(P.Status,'')) NOT IN ('CANCELLED','CANCELED') AND COALESCE(J.Qty,J.LowerQty,L.AllocatedQty,L.OrderedQty)>0
        UNION ALL
        SELECT COALESCE(C.ConfirmedAt,C.CreatedTS),CONCAT('LOAD-',C.LoadingID,'-',S.StockID),COALESCE(J.LotNo,LOT.LotCode),COALESCE(J.StockNumber,S.StockNumber),
            COALESCE(J.ItemNo,S.ItemNo,L.ItemNo),COALESCE(J.Location,L.Location,S.Location),COALESCE(J.Qty,NULLIF(L.AllocatedQty,0),L.OrderedQty),
            'LOAD','Loading',COALESCE(C.OperatorID,C.CreatedBy),NULL,CONCAT('Truck: ',C.LicensePlate),NULL,NULL,NULL,NULL,'FG_LoadingConfirm',O.ShipOrderNumber,O.OutgoingSlipNumber
        FROM dbo.FG_LoadingConfirm C JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=C.ShipmentOrderID
        OUTER APPLY OPENJSON(CASE WHEN ISJSON(C.PalletsLoadedJSON)=1 THEN C.PalletsLoadedJSON ELSE N'[]' END)
            WITH (StockID int '$.stockId',LotNo varchar(80) '$.lotNo',StockNumber varchar(80) '$.stockNumber',ItemNo varchar(20) '$.itemNo',Location varchar(20) '$.location',Qty decimal(18,3) '$.qty') J
        LEFT JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderID=C.ShipmentOrderID AND (L.StockID=J.StockID OR J.StockID IS NULL)
        LEFT JOIN dbo.FG_Inventory S ON S.StockID=COALESCE(J.StockID,L.StockID)
        LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=COALESCE(S.LotID,L.LotID)
        WHERE UPPER(ISNULL(C.OTDStatus,'')) NOT IN ('CANCELLED','CANCELED') AND COALESCE(J.Qty,L.AllocatedQty,L.OrderedQty)>0
        UNION ALL
        SELECT COALESCE(R.ReceivedAt,R.CreatedTS),CONCAT('RETURN-',R.ReturnID,'-',COALESCE(LOT.LotCode,J.LotNo),'-',COALESCE(S.StockNumber,J.StockNumber),'-',COALESCE(R.ItemNo,J.ItemNo)),
            COALESCE(LOT.LotCode,J.LotNo),COALESCE(S.StockNumber,J.StockNumber),COALESCE(R.ItemNo,J.ItemNo),NULL,COALESCE(R.ReturnQty,J.Qty),
            'RETURN','Return',COALESCE(R.ReceivedBy,R.CreatedBy),R.ReturnReason,R.Note,NULL,NULL,NULL,NULL,'FG_CustomerReturn',R.ReturnNumber,O.OutgoingSlipNumber
        FROM dbo.FG_CustomerReturn R LEFT JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=R.OriginalShipmentOrderID
        LEFT JOIN dbo.FG_Inventory S ON S.StockID=R.StockID
        LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=COALESCE(R.LotID,S.LotID)
        OUTER APPLY OPENJSON(CASE WHEN ISJSON(R.ItemsJSON)=1 THEN R.ItemsJSON ELSE N'[]' END)
            WITH (LotNo varchar(80) '$.lotNo',StockNumber varchar(80) '$.stockNumber',ItemNo varchar(20) '$.itemNo',Qty decimal(18,3) '$.qty') J
        WHERE UPPER(ISNULL(R.Status,'')) NOT IN ('CANCELLED','CANCELED','REJECTED')
        UNION ALL
        SELECT A.CreatedTS,CONCAT('ADJ-',A.AdjustID),L.LotCode,S.StockNumber,A.ItemNo,A.Location,A.Delta,
            'ADJ','Adjust',COALESCE(A.RequestedBy,A.CreatedBy),A.ReasonCode,A.ReasonNote,A.ApprovedBy,A.QtyBefore,A.Delta,A.QtyAfter,'FG_InventoryAdjust',A.AdjustNo,NULL
        FROM dbo.FG_InventoryAdjust A LEFT JOIN dbo.FG_Inventory S ON S.StockID=A.StockID LEFT JOIN dbo.tbl_Lot L ON L.LotID=COALESCE(A.LotID,S.LotID)
        WHERE UPPER(ISNULL(A.Status,''))='POSTED'
    )
    SELECT ROW_NUMBER() OVER(ORDER BY E.EventTime DESC,E.EventID DESC) AS ROW_NO,
        E.LotNo AS LOTNO,E.ItemNo AS PARTNO,CONVERT(nvarchar(10),E.EventTime,23) AS WDATE,CONVERT(nvarchar(8),E.EventTime,108) AS WTIME,
        E.LocationID AS LOCATION_NO,COALESCE(E.Qty,0) AS QTY,I.DefaultUOM AS UNIT,E.Status AS STATUS,E.Direction AS DIRECTION,
        COALESCE(NULLIF(U.UserName,''),E.Worker) AS WORKER_ID,E.ReasonCode AS REASON_CODE,E.ReasonNote AS REASON_NOTE,
        COALESCE(NULLIF(SU.UserName,''),E.Supervisor) AS SUPERVISOR,E.BeforeQty AS BEFORE_QTY,E.DeltaQty AS DELTA_QTY,E.AfterQty AS AFTER_QTY,
        NULL AS BEFORE_STATUS,NULL AS AFTER_STATUS,NULL AS BEFORE_LOCATION,NULL AS AFTER_LOCATION,E.Source AS SOURCE,E.Reference AS NOTE
    FROM Events E LEFT JOIN dbo.MD_Item I ON I.ItemNo=E.ItemNo
    LEFT JOIN dbo.AspNetUsers U ON U.Id=E.Worker LEFT JOIN dbo.AspNetUsers SU ON SU.Id=E.Supervisor
    WHERE E.EventTime>=@From AND E.EventTime<DATEADD(day,1,@To)
      AND (@Search IS NULL OR E.LotNo LIKE @Search OR E.StockNumber LIKE @Search OR E.ItemNo LIKE @Search
        OR I.ItemName LIKE @Search OR E.LocationID LIKE @Search OR E.Reference LIKE @Search OR E.OutgoingSlip LIKE @Search)
    ORDER BY E.EventTime DESC,E.EventID DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_HISTORY_TEST_RESET
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @By varchar(50)='pda-ppt-fg-history',@Lot int,@Wo int,@Stock int,@Order int,@Today datetime2=CONVERT(date,SYSDATETIME());
    BEGIN TRANSACTION;
    SELECT @Lot=LotID,@Wo=WoID FROM dbo.tbl_Lot WITH(UPDLOCK,HOLDLOCK) WHERE LotCode='5011FG260908970001' AND CreatedBy=@By;
    SELECT @Order=ShipmentOrderID FROM dbo.FG_ShipmentOrder WITH(UPDLOCK,HOLDLOCK) WHERE ShipOrderNumber='FG-PPT-SO-HIST' AND CreatedBy=@By;
    IF @Lot IS NULL OR @Order IS NULL THROW 51730,'FG History samples are missing. Run PDA_SEED.sql.',1;
    SELECT @Stock=StockID FROM dbo.FG_Inventory WHERE LotID=@Lot;
    IF EXISTS (SELECT 1 FROM dbo.FG_ShipmentOrderLine WHERE StockID=@Stock AND ShipmentOrderID<>@Order)
        THROW 51731,'History sample belongs to another order. Reset cancelled.',1;
    DELETE dbo.FG_CustomerReturn WHERE OriginalShipmentOrderID=@Order;
    DELETE dbo.FG_DeliveryNote WHERE ShipmentOrderID=@Order;
    DELETE dbo.FG_LoadingConfirm WHERE ShipmentOrderID=@Order;
    DELETE D FROM dbo.FG_PickingDetail D JOIN dbo.FG_PickingFifo P ON P.PickID=D.PickID WHERE P.ShipmentOrderID=@Order;
    DELETE dbo.FG_PickingFifo WHERE ShipmentOrderID=@Order;
    DELETE dbo.FG_ShipmentOrderLine WHERE ShipmentOrderID=@Order;
    DELETE dbo.FG_InventoryAdjust WHERE LotID=@Lot;
    DELETE dbo.FG_PutAway WHERE StockID=@Stock;
    IF @Stock IS NULL
    BEGIN
        INSERT dbo.FG_Inventory(StockNumber,WoID,ItemNo,LotID,CustomerCode,Qty,Location,Status,HoldFlag,StockTS,CreatedBy,CreatedTS)
        VALUES('FG-PPT-STK-970001',@Wo,'PPT-FG-HIST',@Lot,'PPT-CUSTOMER',22,'FG-PPT-G1','SHIPPED',0,DATEADD(second,1,@Today),@By,@Today);
        SET @Stock=SCOPE_IDENTITY();
    END;
    UPDATE dbo.FG_Inventory SET Qty=22,Status='SHIPPED',Location='FG-PPT-G1',StockTS=DATEADD(second,1,@Today) WHERE StockID=@Stock;
    UPDATE dbo.FG_ShipmentOrder SET Status='SHIPPED',ShipDate=CAST(@Today AS date) WHERE ShipmentOrderID=@Order;
    INSERT dbo.FG_PutAway(StockID,WoID,ItemNo,Qty,ActualLoc,StorageMethod,OperatorID,Status,CreatedBy,CreatedTS)
    VALUES(@Stock,@Wo,'PPT-FG-HIST',20,'FG-PPT-G1','LOCATION','TEST1','Confirmed',@By,DATEADD(second,1,@Today));
    INSERT dbo.FG_InventoryAdjust(AdjustNo,StockID,ItemNo,Location,LotID,QtyBefore,Delta,QtyAfter,ReasonCode,ReasonNote,Status,RequestedBy,ApprovedBy,CreatedBy,CreatedTS)
    VALUES('FG-PPT-HIST-ADJ',@Stock,'PPT-FG-HIST','FG-PPT-G1',@Lot,20,2,22,'COUNT_DIFF','PPT count correction','Posted','TEST1','TEST1',@By,DATEADD(second,2,@Today));
    INSERT dbo.FG_ShipmentOrderLine(ShipmentOrderID,LineSeq,ItemNo,OrderedQty,AllocatedQty,StockID,LotID,Location,ReservationStatus,CreatedBy,CreatedTS)
    VALUES(@Order,10,'PPT-FG-HIST',22,22,@Stock,@Lot,'FG-PPT-G1','Shipped',@By,DATEADD(second,3,@Today));
    DECLARE @PickJson nvarchar(max)=(SELECT @Stock AS StockId,22 AS Qty FOR JSON PATH);
    DECLARE @Json nvarchar(max)=(SELECT @Stock AS stockId,'5011FG260908970001' AS lotNo,'FG-PPT-STK-970001' AS stockNumber,'PPT-FG-HIST' AS itemNo,22 AS qty,'EA' AS unit,'FG-PPT-G1' AS location FOR JSON PATH);
    INSERT dbo.FG_PickingFifo(PickNumber,ShipmentOrderID,PickerID,EndTS,PicksJSON,PickedQty,OrderedQty,Status,CreatedBy,CreatedTS)
    VALUES('FG-PPT-HIST-PICK',@Order,'TEST1',DATEADD(second,3,@Today),@PickJson,22,22,'Picked',@By,DATEADD(second,3,@Today));
    DECLARE @Pick int=CONVERT(int,SCOPE_IDENTITY());
    INSERT dbo.FG_PickingDetail(PickID,ShipmentOrderLineID,StockID,LotID,ItemNo,Qty,Location,PickSeq,CreatedBy,CreatedTS)
    SELECT @Pick,ShipmentOrderLineID,@Stock,@Lot,'PPT-FG-HIST',22,'FG-PPT-G1',1,@By,DATEADD(second,3,@Today)
    FROM dbo.FG_ShipmentOrderLine WHERE ShipmentOrderID=@Order;
    INSERT dbo.FG_LoadingConfirm(LoadingNumber,ShipmentOrderID,PickID,LicensePlate,PalletsLoadedJSON,DepartureTS,OTDStatus,OperatorID,ConfirmedAt,CreatedBy,CreatedTS)
    VALUES('FG-PPT-HIST-LOAD',@Order,@Pick,'PPT-FG-HISTORY',@Json,DATEADD(second,4,@Today),'OnTime','TEST1',DATEADD(second,4,@Today),@By,DATEADD(second,4,@Today));
    INSERT dbo.FG_CustomerReturn(ReturnNumber,OriginalShipmentOrderID,CustomerCode,StockID,LotID,ItemNo,ReturnQty,
        ReturnReason,Note,ItemsJSON,Status,ReceivedAt,ReceivedBy,CreatedBy,CreatedTS)
    VALUES('FG-PPT-HIST-RETURN',@Order,'PPT-CUSTOMER',@Stock,@Lot,'PPT-FG-HIST',22,
        'DAMAGED_TRANSIT','PPT return note',@Json,'Open',DATEADD(second,5,@Today),'TEST1',@By,DATEADD(second,5,@Today));
    COMMIT TRANSACTION;
END;
GO

-- TEST1 PPT scenarios: restore only the selected screen's explicitly seeded rows.
CREATE OR ALTER PROCEDURE dbo.FG_PDA_PPT_TEST_RESET @Screen varchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Screen NOT IN ('qc','putaway','inventory','release','loading','return','adjust')
        THROW 51700, 'Unknown FG PPT test screen.', 1;
    DECLARE @SeedBy varchar(50) = CONCAT('pda-ppt-fg-', @Screen);
    DECLARE @Lots TABLE (LotID int PRIMARY KEY, LotCode varchar(40), WoID int, ItemNo varchar(20), Qty decimal(12,3), LocationID varchar(20));
    DECLARE @Orders TABLE (ID int PRIMARY KEY, Number varchar(24));
    BEGIN TRANSACTION;
    INSERT @Lots
    SELECT LotID, LotCode, WoID, ItemNo, BatchSize,
        CASE @Screen WHEN 'putaway' THEN 'FG-PPT-A1' WHEN 'inventory' THEN 'FG-PPT-B1'
            WHEN 'release' THEN 'FG-PPT-C1' WHEN 'loading' THEN 'FG-PPT-D1'
            WHEN 'return' THEN 'FG-PPT-E1' WHEN 'adjust' THEN 'FG-PPT-F1' END
    FROM dbo.tbl_Lot WITH (UPDLOCK,HOLDLOCK)
    WHERE CreatedBy=@SeedBy AND LotCode IN
      ('5011FG260908900001','5011FG260908900002','5011FG260908900003','5011FG260908900004',
       '5011FG260908910001','5011FG260908920001','5011FG260908920002','5011FG260908920003',
       '5011FG260908930001','5011FG260908930002','5011FG260908930003',
       '5011FG260908940001','5011FG260908940002','5011FG260908940003',
       '5011FG260908950001','5011FG260908950002','5011FG260908960001');
    IF (SELECT COUNT(*) FROM @Lots) <> CASE @Screen WHEN 'qc' THEN 4 WHEN 'putaway' THEN 1 WHEN 'inventory' THEN 3 WHEN 'release' THEN 3 WHEN 'loading' THEN 3 WHEN 'return' THEN 2 ELSE 1 END
        THROW 51701, 'FG PPT samples are missing. Run the FG PPT section of PDA_SEED.sql.', 1;
    INSERT @Orders SELECT ShipmentOrderID, ShipOrderNumber FROM dbo.FG_ShipmentOrder WITH (UPDLOCK,HOLDLOCK)
    WHERE CreatedBy=@SeedBy AND ShipOrderNumber IN ('FG-PPT-SO-REL','FG-PPT-SO-LOAD','FG-PPT-SO-RETURN','FG-PPT-SO-NOSHIP');
    IF (SELECT COUNT(*) FROM @Orders) <> CASE WHEN @Screen IN ('release','loading') THEN 1 WHEN @Screen='return' THEN 2 ELSE 0 END
        THROW 51702, 'FG PPT shipment samples are missing. Run PDA_SEED.sql.', 1;
    IF EXISTS (SELECT 1 FROM dbo.FG_ShipmentOrderLine L JOIN dbo.FG_Inventory S ON S.StockID=L.StockID
               WHERE S.LotID IN (SELECT LotID FROM @Lots) AND L.ShipmentOrderID NOT IN (SELECT ID FROM @Orders))
        THROW 51703, 'A PPT stock is assigned to another order. Reset was cancelled.', 1;

    DELETE FROM dbo.FG_CustomerReturn WHERE OriginalShipmentOrderID IN (SELECT ID FROM @Orders);
    DELETE FROM dbo.FG_DeliveryNote WHERE ShipmentOrderID IN (SELECT ID FROM @Orders);
    DELETE FROM dbo.FG_LoadingConfirm WHERE ShipmentOrderID IN (SELECT ID FROM @Orders);
    DELETE D FROM dbo.FG_PickingDetail D JOIN dbo.FG_PickingFifo P ON P.PickID=D.PickID WHERE P.ShipmentOrderID IN (SELECT ID FROM @Orders);
    DELETE FROM dbo.FG_PickingFifo WHERE ShipmentOrderID IN (SELECT ID FROM @Orders);
    DELETE FROM dbo.FG_ShipmentOrderLine WHERE ShipmentOrderID IN (SELECT ID FROM @Orders);
    DELETE FROM dbo.FG_InventoryAdjust WHERE LotID IN (SELECT LotID FROM @Lots);
    DELETE P FROM dbo.FG_PutAway P JOIN dbo.FG_Inventory S ON S.StockID=P.StockID
    WHERE S.LotID IN (SELECT LotID FROM @Lots);
    IF @Screen IN ('qc','putaway')
        DELETE FROM dbo.FG_Inventory WHERE LotID IN (SELECT LotID FROM @Lots);
    ELSE
    BEGIN
        INSERT dbo.FG_Inventory (StockNumber,WoID,ItemNo,LotID,CustomerCode,Qty,Location,Status,HoldFlag,StockTS,CreatedBy,CreatedTS)
        SELECT CONCAT('FG-PPT-STK-',RIGHT(LotCode,6)),WoID,ItemNo,LotID,'PPT-CUSTOMER',Qty,LocationID,'AVAILABLE',0,SYSDATETIME(),@SeedBy,SYSDATETIME()
        FROM @Lots L WHERE NOT EXISTS (SELECT 1 FROM dbo.FG_Inventory S WHERE S.LotID=L.LotID);
        UPDATE S SET Qty=L.Qty, Location=L.LocationID, HoldFlag=0,
            Status=CASE WHEN @Screen='loading' THEN 'RESERVED' WHEN @Screen='return' AND RIGHT(L.LotCode,6)='950001' THEN 'SHIPPED' ELSE 'AVAILABLE' END,
            StockTS=DATEADD(day,-5+CONVERT(int,RIGHT(L.LotCode,1)),SYSDATETIME()),ModifiedBy=@SeedBy,ModifiedTS=SYSDATETIME()
        FROM dbo.FG_Inventory S JOIN @Lots L ON L.LotID=S.LotID;
    END;
    UPDATE L SET RemainingQty=T.Qty, CurrentLocationID=CASE WHEN @Screen IN ('qc','putaway') THEN NULL ELSE T.LocationID END,
        Status='Completed', QualityFlag='PASS', ModifiedBy=@SeedBy, ModifiedTS=SYSDATETIME()
    FROM dbo.tbl_Lot L JOIN @Lots T ON T.LotID=L.LotID;
    IF @Screen IN ('qc','putaway')
        UPDATE Q SET InsEndTS=DATEADD(hour,-CASE RIGHT(L.LotCode,6) WHEN '900002' THEN 48 WHEN '900003' THEN 144 WHEN '900004' THEN 264 ELSE 2 END,SYSDATETIME())
        FROM dbo.QC_Inspection Q JOIN @Lots L ON L.LotID=Q.LotID WHERE Q.CreatedBy=@SeedBy;

    UPDATE O SET Status=CASE WHEN @Screen='release' THEN 'RELEASED' WHEN @Screen='loading' THEN 'PICKED' WHEN O.ShipOrderNumber='FG-PPT-SO-RETURN' THEN 'SHIPPED' ELSE 'OPEN' END,
        ShipDate=CAST(GETDATE() AS date),ModifiedBy=@SeedBy,ModifiedTS=SYSDATETIME()
    FROM dbo.FG_ShipmentOrder O JOIN @Orders T ON T.ID=O.ShipmentOrderID;
    IF @Screen='release'
        INSERT dbo.FG_ShipmentOrderLine (ShipmentOrderID,LineSeq,ItemNo,OrderedQty,AllocatedQty,ReservationStatus,CreatedBy,CreatedTS)
        SELECT O.ID,CASE L.ItemNo WHEN 'PPT-FG-REL-01' THEN 10 ELSE 20 END,L.ItemNo,SUM(L.Qty),0,'Open',@SeedBy,SYSDATETIME()
        FROM @Lots L CROSS JOIN @Orders O GROUP BY O.ID,L.ItemNo;
    IF @Screen IN ('loading','return')
        INSERT dbo.FG_ShipmentOrderLine (ShipmentOrderID,LineSeq,ItemNo,OrderedQty,AllocatedQty,StockID,LotID,Location,ReservationStatus,ReservedAt,CreatedBy,CreatedTS)
        SELECT O.ID,CONVERT(int,RIGHT(L.LotCode,1))*10,L.ItemNo,L.Qty,L.Qty,S.StockID,L.LotID,L.LocationID,
            CASE WHEN @Screen='loading' THEN 'Picked' WHEN O.Number='FG-PPT-SO-RETURN' THEN 'Shipped' ELSE 'Open' END,SYSDATETIME(),@SeedBy,SYSDATETIME()
        FROM @Lots L JOIN dbo.FG_Inventory S ON S.LotID=L.LotID
        JOIN @Orders O ON @Screen='loading' OR O.Number=CASE RIGHT(L.LotCode,6) WHEN '950001' THEN 'FG-PPT-SO-RETURN' ELSE 'FG-PPT-SO-NOSHIP' END;
    IF @Screen='loading'
    BEGIN
        DECLARE @LoadingOrderID int=(SELECT ID FROM @Orders),@LoadingPickID int,@LoadingJson nvarchar(max),@LoadingQty decimal(12,3);
        SELECT @LoadingQty=SUM(S.Qty),@LoadingJson=(SELECT S2.StockID AS stockId,S2.Qty AS qty
            FROM dbo.FG_ShipmentOrderLine L2 JOIN dbo.FG_Inventory S2 ON S2.StockID=L2.StockID
            WHERE L2.ShipmentOrderID=@LoadingOrderID ORDER BY L2.LineSeq FOR JSON PATH)
        FROM dbo.FG_ShipmentOrderLine L JOIN dbo.FG_Inventory S ON S.StockID=L.StockID
        WHERE L.ShipmentOrderID=@LoadingOrderID;
        INSERT dbo.FG_PickingFifo(PickNumber,ShipmentOrderID,PickerID,StartTS,EndTS,PicksJSON,PickedQty,OrderedQty,Status,CreatedBy,CreatedTS)
        VALUES(CONCAT('PICK-PPT-',RIGHT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),15)),@LoadingOrderID,'TEST1',
               SYSDATETIME(),SYSDATETIME(),@LoadingJson,@LoadingQty,@LoadingQty,'Picked',@SeedBy,SYSDATETIME());
        SET @LoadingPickID=CONVERT(int,SCOPE_IDENTITY());
        INSERT dbo.FG_PickingDetail(PickID,ShipmentOrderLineID,StockID,LotID,ItemNo,Qty,Location,PickSeq,CreatedBy,CreatedTS)
        SELECT @LoadingPickID,L.ShipmentOrderLineID,S.StockID,S.LotID,S.ItemNo,S.Qty,S.Location,
               ROW_NUMBER() OVER(ORDER BY L.LineSeq,L.ShipmentOrderLineID),@SeedBy,SYSDATETIME()
        FROM dbo.FG_ShipmentOrderLine L JOIN dbo.FG_Inventory S ON S.StockID=L.StockID
        WHERE L.ShipmentOrderID=@LoadingOrderID;
    END;
    IF @Screen='return'
    BEGIN
        DECLARE @ReturnOrderID int=(SELECT ID FROM @Orders WHERE Number='FG-PPT-SO-RETURN');
        DECLARE @ReturnPickID int,@ReturnJson nvarchar(max),@ReturnQty decimal(12,3);
        SELECT @ReturnQty=SUM(S.Qty),@ReturnJson=(SELECT S2.StockID AS stockId,S2.ItemNo AS itemNo,L2.LotCode AS lotNo,
            S2.StockNumber AS stockNumber,S2.Qty AS qty,S2.Location AS location
            FROM dbo.FG_ShipmentOrderLine SL2 JOIN dbo.FG_Inventory S2 ON S2.StockID=SL2.StockID
            LEFT JOIN dbo.tbl_Lot L2 ON L2.LotID=S2.LotID
            WHERE SL2.ShipmentOrderID=@ReturnOrderID ORDER BY SL2.LineSeq FOR JSON PATH)
        FROM dbo.FG_ShipmentOrderLine SL JOIN dbo.FG_Inventory S ON S.StockID=SL.StockID
        WHERE SL.ShipmentOrderID=@ReturnOrderID;
        INSERT dbo.FG_PickingFifo(PickNumber,ShipmentOrderID,PickerID,StartTS,EndTS,PicksJSON,PickedQty,OrderedQty,Status,CreatedBy,CreatedTS)
        VALUES(CONCAT('PICK-RETURN-',RIGHT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),11)),@ReturnOrderID,'TEST1',
               DATEADD(day,-1,SYSDATETIME()),DATEADD(day,-1,SYSDATETIME()),@ReturnJson,@ReturnQty,@ReturnQty,'Picked',@SeedBy,SYSDATETIME());
        SET @ReturnPickID=CONVERT(int,SCOPE_IDENTITY());
        INSERT dbo.FG_PickingDetail(PickID,ShipmentOrderLineID,StockID,LotID,ItemNo,Qty,Location,PickSeq,CreatedBy,CreatedTS)
        SELECT @ReturnPickID,SL.ShipmentOrderLineID,S.StockID,S.LotID,S.ItemNo,S.Qty,S.Location,
               ROW_NUMBER() OVER(ORDER BY SL.LineSeq,SL.ShipmentOrderLineID),@SeedBy,SYSDATETIME()
        FROM dbo.FG_ShipmentOrderLine SL JOIN dbo.FG_Inventory S ON S.StockID=SL.StockID
        WHERE SL.ShipmentOrderID=@ReturnOrderID;
        INSERT dbo.FG_LoadingConfirm(LoadingNumber,ShipmentOrderID,PickID,LicensePlate,PalletsLoadedJSON,DepartureTS,OTDStatus,ConfirmedAt,CreatedBy,CreatedTS)
        VALUES('FG-PPT-RETURN-LOAD',@ReturnOrderID,@ReturnPickID,'PPT-FG-RETURN',@ReturnJson,
               DATEADD(day,-1,SYSDATETIME()),'OnTime',DATEADD(day,-1,SYSDATETIME()),@SeedBy,SYSDATETIME());
    END;
    COMMIT TRANSACTION;
END;
GO
