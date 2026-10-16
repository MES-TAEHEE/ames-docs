-- =====================================================================
--  PDA_SCHEMA.sql
--  Consolidated Warehouse and Finished Goods schema for the PDA
--  Fresh databases: dist/AMES_Schema.sql already includes the deployed PDA
--  schema as of 2026-09-23. This file remains a legacy incremental upgrade;
--  do not apply it again in the fresh-database rebuild workflow.
--
--  Apply after dist/AMES_Schema.sql:
--    sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\pda\PDA_SCHEMA.sql
--
--  PDA business operations now use API-owned Pda*Sql.cs batches.
--  Legacy table upgrades and development reset procedures remain here.
-- =====================================================================
-- Target database must be selected explicitly by sqlcmd -d or in SSMS.
-- Existing databases: apply migrate_fg_customer_return_columns.sql first.

SET ANSI_NULLS ON;
IF OBJECT_ID(N'dbo.FG_Shipment',N'U') IS NOT NULL
BEGIN
    RAISERROR('Legacy PDA_SCHEMA.sql is retired after the actual FG shipment migration.',16,1);
    SET NOEXEC ON;
END;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO
IF OBJECT_ID(N'dbo.FG_CustomerReturn',N'U') IS NOT NULL
    AND COL_LENGTH(N'dbo.FG_CustomerReturn',N'CustomerID') IS NULL
    THROW 52016,'Apply migrate_fg_customer_return_columns.sql before PDA_SCHEMA.sql.',1;
GO

-- Location scope is referenced by WH/FG procedures below, so upgrade it first.
IF COL_LENGTH(N'dbo.MD_Location', N'WhCode') IS NULL
    ALTER TABLE dbo.MD_Location ADD WhCode varchar(20) NULL;
IF COL_LENGTH(N'dbo.MD_Location', N'AreaCode') IS NULL
    ALTER TABLE dbo.MD_Location ADD AreaCode varchar(20) NULL;
GO

-- Inbound records inventory first; Put-away assigns the physical location later.
IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.WH_Inventory')
      AND name = N'LocationNo'
      AND is_nullable = 0
)
    ALTER TABLE dbo.WH_Inventory ALTER COLUMN LocationNo varchar(50) NULL;

-- Procedures below reference this column before the inbound upgrade section.
IF COL_LENGTH(N'dbo.WH_Inventory', N'DeliveryNoteNo') IS NULL
    ALTER TABLE dbo.WH_Inventory ADD DeliveryNoteNo nvarchar(30) NULL;
GO

-- Portal Delivery Note receipts use the linked PO number as InvoiceNo.
IF OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SCM_DeliveryBox', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SCM_DeliveryLine', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_PurchaseOrder', N'U') IS NOT NULL
BEGIN
    UPDATE W
       SET InvoiceNo = PO.PoNumber
    FROM dbo.WH_Inventory W
    JOIN dbo.SCM_DeliveryBox B
      ON B.BoxNumber = W.LotNo
    JOIN dbo.SCM_DeliveryLine DL
      ON DL.DeliveryLineID = B.DeliveryLineID
    JOIN dbo.WH_PurchaseOrder PO
      ON PO.PoID = DL.PoID
    WHERE W.DeliveryNoteNo IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(W.InvoiceNo)), N'') IS NULL;
END;
GO

-- =====================================================================
--  FG outbound by inventory unit (PALLET / CASE / BOX / PART)
-- =====================================================================
IF OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_InventoryTransaction
    (
        TransactionID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_WH_InventoryTransaction PRIMARY KEY,
        TransactionTime datetime2 NOT NULL CONSTRAINT DF_WH_InventoryTransaction_Time DEFAULT SYSDATETIME(),
        TransactionType varchar(10) NOT NULL,
        PartNo varchar(50) NULL,
        LocationNo varchar(50) NULL,
        LotNo nvarchar(50) NOT NULL,
        QtyBefore decimal(18,3) NULL,
        QtyChange decimal(18,3) NOT NULL CONSTRAINT DF_WH_InventoryTransaction_QtyChange DEFAULT 0,
        QtyAfter decimal(18,3) NULL,
        ReasonCode varchar(30) NULL,
        SourceType varchar(30) NULL,
        SourceID int NULL,
        OperatorID nvarchar(450) NULL,
        ApproverID nvarchar(450) NULL,
        Note nvarchar(500) NULL,
        CreatedBy varchar(50) NOT NULL CONSTRAINT DF_WH_InventoryTransaction_CreatedBy DEFAULT 'system',
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_WH_InventoryTransaction_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedTS datetime2 NULL,
        ModifiedBy nvarchar(450) NULL,
        CONSTRAINT CK_WH_InventoryTransaction_Type CHECK (TransactionType IN ('IN','OUT','ADJ','MOVE'))
    );
END;
GO

IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'PartNo') IS NULL
    ALTER TABLE dbo.WH_InventoryTransaction ADD PartNo varchar(50) NULL;
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LocationNo') IS NULL
    ALTER TABLE dbo.WH_InventoryTransaction ADD LocationNo varchar(50) NULL;
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LotNo') IS NULL
    ALTER TABLE dbo.WH_InventoryTransaction ADD LotNo nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'RefDocType') IS NOT NULL
   AND COL_LENGTH(N'dbo.WH_InventoryTransaction', N'SourceType') IS NULL
    EXEC sys.sp_rename N'dbo.WH_InventoryTransaction.RefDocType', N'SourceType', N'COLUMN';
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'RefDocID') IS NOT NULL
   AND COL_LENGTH(N'dbo.WH_InventoryTransaction', N'SourceID') IS NULL
    EXEC sys.sp_rename N'dbo.WH_InventoryTransaction.RefDocID', N'SourceID', N'COLUMN';
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'RefDocNo') IS NOT NULL
    ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN RefDocNo;
GO

IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'ItemNo') IS NOT NULL
    EXEC(N'UPDATE dbo.WH_InventoryTransaction SET PartNo=COALESCE(PartNo,CONVERT(varchar(50),ItemNo));');
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LocationID') IS NOT NULL
    EXEC(N'UPDATE dbo.WH_InventoryTransaction SET LocationNo=COALESCE(LocationNo,CONVERT(varchar(50),LocationID));');
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LotID') IS NOT NULL
   AND OBJECT_ID(N'dbo.tbl_Lot', N'U') IS NOT NULL
    EXEC(N'UPDATE T SET LotNo=COALESCE(NULLIF(T.LotNo,N''''),L.LotCode) FROM dbo.WH_InventoryTransaction T LEFT JOIN dbo.tbl_Lot L ON L.LotID=T.LotID;');
UPDATE dbo.WH_InventoryTransaction
   SET LotNo=CONCAT(N'LEGACY-TX-',TransactionID)
 WHERE NULLIF(LTRIM(RTRIM(LotNo)),N'') IS NULL;
UPDATE dbo.WH_InventoryTransaction
   SET TransactionType=CASE UPPER(TransactionType)
       WHEN 'IN' THEN 'IN' WHEN 'RECEIVE' THEN 'IN'
       WHEN 'OUT' THEN 'OUT' WHEN 'ISSUE' THEN 'OUT' WHEN 'CANCEL' THEN 'OUT'
       ELSE 'ADJ' END
 WHERE TransactionType NOT IN ('IN','OUT','ADJ');
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_WH_InventoryTransaction_Search' AND object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    DROP INDEX IX_WH_InventoryTransaction_Search ON dbo.WH_InventoryTransaction;
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'ItemNo') IS NOT NULL
    ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN ItemNo;
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LocationID') IS NOT NULL
    ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN LocationID;
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LotID') IS NOT NULL
    ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN LotID;
ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN LotNo nvarchar(50) NOT NULL;
ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyBefore decimal(18,3) NULL;
ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyChange decimal(18,3) NOT NULL;
ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyAfter decimal(18,3) NULL;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction') AND name=N'CK_WH_InventoryTransaction_Type')
    ALTER TABLE dbo.WH_InventoryTransaction DROP CONSTRAINT CK_WH_InventoryTransaction_Type;
ALTER TABLE dbo.WH_InventoryTransaction WITH CHECK ADD CONSTRAINT CK_WH_InventoryTransaction_Type CHECK(TransactionType IN('IN','OUT','ADJ','MOVE'));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_WH_InventoryTransaction_Search' AND object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    CREATE INDEX IX_WH_InventoryTransaction_Search ON dbo.WH_InventoryTransaction(TransactionType,PartNo,LocationNo,LotNo);
GO


IF OBJECT_ID(N'dbo.Seq_FG_OutboundEvent', N'SO') IS NOT NULL
    DROP SEQUENCE dbo.Seq_FG_OutboundEvent;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_OUTBOUND_TEST_RESET
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DELETE dbo.WH_InventoryTransaction
     WHERE (SourceType = 'FG_OUTBOUND' AND (LotNo LIKE N'FGLOT-DEMO-%' OR LotNo = N'FGPART-DEMO-001'))
        OR (SourceType = 'FG_PALLET_OUTBOUND' AND LotNo IN (N'FGLOT-DEMO-001-A', N'FGLOT-DEMO-001-B'));

    IF EXISTS (SELECT 1 FROM dbo.WH_Inventory WHERE LotNo = N'FGPAL-DEMO-001')
        UPDATE dbo.WH_Inventory
           SET UnitType = 'PALLET', ParentLotNo = NULL, PartNo = NULL, PartName = NULL,
               PalletNo = NULL,
               LocationNo = N'FG-A01-01', Qty = 60,
               InvoiceNo = N'INV-FG-DEMO-001', DeliveryNoteNo = N'DN-FG-DEMO-001',
               ReceivedAt = DATEADD(day, -1, SYSDATETIME()), UpdatedAt = SYSDATETIME()
         WHERE LotNo = N'FGPAL-DEMO-001';
    ELSE
        INSERT dbo.WH_Inventory
            (LotNo, UnitType, ParentLotNo, PartNo, PartName, PalletNo,
             LocationNo, Qty, InvoiceNo, DeliveryNoteNo, ReceivedAt, CreatedAt, UpdatedAt)
        VALUES
            (N'FGPAL-DEMO-001', 'PALLET', NULL, NULL, NULL, NULL,
             N'FG-A01-01', 60, N'INV-FG-DEMO-001', N'DN-FG-DEMO-001',
             DATEADD(day, -1, SYSDATETIME()), SYSDATETIME(), SYSDATETIME());

    IF EXISTS (SELECT 1 FROM dbo.WH_Inventory WHERE LotNo = N'FGLOT-DEMO-001-A')
        UPDATE dbo.WH_Inventory
           SET UnitType = 'PART', ParentLotNo = N'FGPAL-DEMO-001',
               PartNo = N'81710-PI010NNB', PartName = N'TRIM ASSY-TAIL GATE SIDE,LH',
               PalletNo = NULL,
               LocationNo = N'FG-A01-01', Qty = 40,
               InvoiceNo = N'INV-FG-DEMO-001', DeliveryNoteNo = N'DN-FG-DEMO-001',
               ReceivedAt = DATEADD(day, -1, SYSDATETIME()), UpdatedAt = SYSDATETIME()
         WHERE LotNo = N'FGLOT-DEMO-001-A';
    ELSE
        INSERT dbo.WH_Inventory
            (LotNo, UnitType, ParentLotNo, PartNo, PartName, PalletNo,
             LocationNo, Qty, InvoiceNo, DeliveryNoteNo, ReceivedAt, CreatedAt, UpdatedAt)
        VALUES
            (N'FGLOT-DEMO-001-A', 'PART', N'FGPAL-DEMO-001', N'81710-PI010NNB',
             N'TRIM ASSY-TAIL GATE SIDE,LH', NULL,
             N'FG-A01-01', 40, N'INV-FG-DEMO-001', N'DN-FG-DEMO-001',
             DATEADD(day, -1, SYSDATETIME()), SYSDATETIME(), SYSDATETIME());

    IF EXISTS (SELECT 1 FROM dbo.WH_Inventory WHERE LotNo = N'FGLOT-DEMO-001-B')
        UPDATE dbo.WH_Inventory
           SET UnitType = 'PART', ParentLotNo = N'FGPAL-DEMO-001',
               PartNo = N'81710-PI000YGN', PartName = N'TRIM ASSY-TAIL GATE SIDE,RH',
               PalletNo = NULL,
               LocationNo = N'FG-A01-01', Qty = 20,
               InvoiceNo = N'INV-FG-DEMO-001', DeliveryNoteNo = N'DN-FG-DEMO-001',
               ReceivedAt = DATEADD(day, -1, SYSDATETIME()), UpdatedAt = SYSDATETIME()
         WHERE LotNo = N'FGLOT-DEMO-001-B';
    ELSE
        INSERT dbo.WH_Inventory
            (LotNo, UnitType, ParentLotNo, PartNo, PartName, PalletNo,
             LocationNo, Qty, InvoiceNo, DeliveryNoteNo, ReceivedAt, CreatedAt, UpdatedAt)
        VALUES
            (N'FGLOT-DEMO-001-B', 'PART', N'FGPAL-DEMO-001', N'81710-PI000YGN',
             N'TRIM ASSY-TAIL GATE SIDE,RH', NULL,
             N'FG-A01-01', 20, N'INV-FG-DEMO-001', N'DN-FG-DEMO-001',
             DATEADD(day, -1, SYSDATETIME()), SYSDATETIME(), SYSDATETIME());

    IF EXISTS (SELECT 1 FROM dbo.WH_Inventory WHERE LotNo = N'FGPART-DEMO-001')
        UPDATE dbo.WH_Inventory
           SET UnitType = 'PART', ParentLotNo = NULL,
               PartNo = N'85710-NQ500NNB', PartName = N'FRT U/TRIM LARGE PART',
               PalletNo = NULL, LocationNo = N'FG-A02-01', Qty = 1,
               InvoiceNo = N'INV-FG-DEMO-002', DeliveryNoteNo = N'DN-FG-DEMO-002',
               ReceivedAt = DATEADD(day, -1, SYSDATETIME()), UpdatedAt = SYSDATETIME()
         WHERE LotNo = N'FGPART-DEMO-001';
    ELSE
        INSERT dbo.WH_Inventory
            (LotNo, UnitType, ParentLotNo, PartNo, PartName, PalletNo,
             LocationNo, Qty, InvoiceNo, DeliveryNoteNo, ReceivedAt, CreatedAt, UpdatedAt)
        VALUES
            (N'FGPART-DEMO-001', 'PART', NULL, N'85710-NQ500NNB', N'FRT U/TRIM LARGE PART', NULL,
             N'FG-A02-01', 1, N'INV-FG-DEMO-002', N'DN-FG-DEMO-002',
             DATEADD(day, -1, SYSDATETIME()), SYSDATETIME(), SYSDATETIME());

    COMMIT TRANSACTION;
END;
GO

UPDATE dbo.MD_Location
SET WhCode = COALESCE(WhCode, 'EOS'),
    AreaCode = COALESCE(AreaCode, CASE WHEN LocationID LIKE 'FG-%' THEN 'FG_AREA' ELSE 'MAT_AREA' END)
WHERE WhCode IS NULL OR AreaCode IS NULL;
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
        PartNo varchar(50) NULL,
        LocationNo varchar(50) NULL,
        LotNo nvarchar(50) NOT NULL,
        QtyBefore decimal(18,3) NULL,
        QtyChange decimal(18,3) NOT NULL CONSTRAINT DF_WH_InventoryTransaction_QtyChange DEFAULT 0,
        QtyAfter decimal(18,3) NULL,
        ReasonCode varchar(30) NULL,
        SourceType varchar(30) NULL,
        SourceID int NULL,
        OperatorID nvarchar(450) NULL,
        ApproverID nvarchar(450) NULL,
        Note nvarchar(500) NULL,
        CreatedBy varchar(50) NOT NULL CONSTRAINT DF_WH_InventoryTransaction_CreatedBy DEFAULT 'system',
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_WH_InventoryTransaction_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedTS datetime2 NULL,
        ModifiedBy nvarchar(450) NULL,
        CONSTRAINT PK_WH_InventoryTransaction PRIMARY KEY CLUSTERED (TransactionID),
        CONSTRAINT CK_WH_InventoryTransaction_Type CHECK (TransactionType IN ('IN', 'OUT', 'ADJ', 'MOVE'))
    );
END;
IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LotNo') IS NULL
    ALTER TABLE dbo.WH_InventoryTransaction ADD LotNo nvarchar(50) NULL;
GO

IF OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_TransactionHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.WH_InventoryTransaction)
BEGIN
    INSERT INTO dbo.WH_InventoryTransaction
    (
        TransactionTime, TransactionType, PartNo, LocationNo, LotNo,
        QtyBefore, QtyChange, QtyAfter, ReasonCode, SourceType, SourceID,
        OperatorID, ApproverID, Note, CreatedBy, CreatedTS, ModifiedTS, ModifiedBy
    )
    SELECT
        COALESCE(TxnTime, CreatedTS, SYSDATETIME()),
        CASE WHEN TxnType IN ('IN', 'OUT', 'ADJ') THEN TxnType ELSE 'ADJ' END,
        ItemNo,
        LocationID,
        COALESCE(L.LotCode,CONCAT(N'LEGACY-HISTORY-',H.TxnID)),
        QtyBefore,
        COALESCE(Delta, 0),
        QtyAfter,
        ReasonCode,
        H.RefDocType,
        H.RefDocID,
        OperatorID,
        ApproverID,
        Note,
        COALESCE(CreatedBy, 'system'),
        COALESCE(CreatedTS, SYSDATETIME()),
        ModifiedTS,
        ModifiedBy
    FROM dbo.WH_TransactionHistory H
    LEFT JOIN dbo.tbl_Lot L ON L.LotID=H.LotID;
END;
GO

-- WH adjustment history consolidation. Preserve legacy reference IDs for audit;
-- new adjustments are recorded directly as ADJ transactions without approval.
IF OBJECT_ID(N'dbo.WH_InventoryAdjust', N'U') IS NOT NULL
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime, TransactionType, PartNo, LocationNo, LotNo,
         QtyBefore, QtyChange, QtyAfter, ReasonCode, SourceType, SourceID,
         OperatorID, ApproverID, Note, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT COALESCE(A.CreatedTS, SYSDATETIME()), 'ADJ', A.ItemNo, A.LocationID,
           COALESCE(L.LotCode,CONCAT(N'LEGACY-ADJUST-',A.AdjustID)),
           A.QtyBefore, COALESCE(A.Delta, 0), A.QtyAfter, A.ReasonCode,
           'WH_InventoryAdjust', A.AdjustID, COALESCE(A.RequestedBy, A.CreatedBy),
           A.ApprovedBy, A.ReasonNote, A.CreatedBy, COALESCE(A.CreatedTS, SYSDATETIME()),
           A.ModifiedBy, A.ModifiedTS
    FROM dbo.WH_InventoryAdjust A
    LEFT JOIN dbo.tbl_Lot L ON L.LotID=A.LotID
    WHERE NOT EXISTS
        (SELECT 1 FROM dbo.WH_InventoryTransaction T
         WHERE T.SourceType='WH_InventoryAdjust' AND T.SourceID=A.AdjustID);
    DROP TABLE dbo.WH_InventoryAdjust;
    COMMIT TRANSACTION;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InventoryTransaction_Time' AND object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    CREATE INDEX IX_WH_InventoryTransaction_Time ON dbo.WH_InventoryTransaction (TransactionTime DESC, TransactionID DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InventoryTransaction_Search' AND object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    CREATE INDEX IX_WH_InventoryTransaction_Search ON dbo.WH_InventoryTransaction (TransactionType, PartNo, LocationNo, LotNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InventoryTransaction_LotNo' AND object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    CREATE INDEX IX_WH_InventoryTransaction_LotNo ON dbo.WH_InventoryTransaction (LotNo);
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
IF COL_LENGTH(N'dbo.MD_Item', N'SparePartNo') IS NULL
    ALTER TABLE dbo.MD_Item ADD SparePartNo nvarchar(80) NULL;
IF COL_LENGTH(N'dbo.MD_Item', N'ApplicableEquipment') IS NULL
    ALTER TABLE dbo.MD_Item ADD ApplicableEquipment nvarchar(80) NULL;
IF COL_LENGTH(N'dbo.MD_Item', N'MakerName') IS NULL
    ALTER TABLE dbo.MD_Item ADD MakerName nvarchar(80) NULL;
GO

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

IF COL_LENGTH(N'dbo.WH_Inventory', N'DeliveryNoteNo') IS NULL
    ALTER TABLE dbo.WH_Inventory ADD DeliveryNoteNo nvarchar(30) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WH_Inventory') AND name = N'IX_WH_Inventory_DeliveryNoteNo')
    CREATE INDEX IX_WH_Inventory_DeliveryNoteNo ON dbo.WH_Inventory(DeliveryNoteNo);
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WH_Inventory') AND name = N'IX_WH_Inventory_DeliveryLineID')
    DROP INDEX IX_WH_Inventory_DeliveryLineID ON dbo.WH_Inventory;
IF COL_LENGTH(N'dbo.WH_Inventory', N'DeliveryLineID') IS NOT NULL
    ALTER TABLE dbo.WH_Inventory DROP COLUMN DeliveryLineID;
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

-- SCM delivery notes are available in either receive mode; legacy package documents keep their configured mode.

-- =====================================================================
--  Inbound / box barcode scan
--  Source: dbo.WH_InboundPackage, dbo.tbl_Lot, dbo.WH_PurchaseOrder
-- =====================================================================

-- =====================================================================
--  Inbound / Receive LOT
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_PPT_TEST_RESET @Screen varchar(10)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @S varchar(10)=LOWER(LTRIM(RTRIM(ISNULL(@Screen,''))));
    IF @S NOT IN('release','putaway','inventory','adjust','history') THROW 51700,'Unknown WH PPT test screen.',1;
    DECLARE @Rows table
    (
      LotNo nvarchar(100),
      ItemNo varchar(50),
      Qty decimal(18,3),
      LocationNo varchar(50),
      ReceivedAt datetime2
    );
    INSERT @Rows
    SELECT L.LotCode,L.ItemNo,COALESCE(NULLIF(L.BatchSize,0),NULLIF(L.RemainingQty,0),1),
      CASE
        WHEN L.LotCode='5011LL260908810001' THEN 'B0-10-A1'
        WHEN L.LotCode='5011LL260908810002' THEN 'B0-10-B1'
        WHEN L.LotCode='5011LL260908810003' THEN 'B0-09-D2'
        WHEN L.LotCode='5011LL260908810004' THEN 'B0-08-B1'
        WHEN L.LotCode IN('5011LL260908850001','5011LL260908850002','5011LL260908850003') THEN NULL
        WHEN L.LotCode='5011LL260908850004' THEN 'B0-10-A1'
        WHEN L.LotCode='5011LL260908820001' THEN 'B0-10-A1'
        WHEN L.LotCode='5011LL260908820002' THEN 'B0-10-B1'
        WHEN L.LotCode='5011LL260908820003' THEN 'B0-09-D2'
        WHEN L.LotCode='5011LL260908830001' THEN 'B0-12-B1'
        ELSE 'B0-08-C1' END,
      COALESCE(L.ProducedAt,SYSDATETIME())
    FROM dbo.tbl_Lot L
    WHERE (@S='release' AND L.LotCode LIKE '5011LL26090881%')
       OR (@S='putaway' AND L.LotCode LIKE '5011LL26090885%')
       OR (@S='inventory' AND L.LotCode LIKE '5011LL26090882%')
       OR (@S='adjust' AND L.LotCode LIKE '5011LL26090883%')
       OR (@S='history' AND L.LotCode LIKE '5011LL26090884%');
    IF (SELECT COUNT(*) FROM @Rows) <> CASE @S WHEN 'release' THEN 4 WHEN 'putaway' THEN 4 WHEN 'inventory' THEN 3 ELSE 1 END
        THROW 51702,'SCTEST1 WH data is missing. Apply PDA_SEED.sql first.',1;
    BEGIN TRANSACTION;
    DELETE T FROM dbo.WH_InventoryTransaction T JOIN @Rows R ON R.LotNo=T.LotNo COLLATE DATABASE_DEFAULT;
    MERGE dbo.WH_Inventory T USING(SELECT R.*,I.ItemName FROM @Rows R LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=R.ItemNo) S ON T.LotNo=S.LotNo
    WHEN MATCHED THEN UPDATE SET PartNo=S.ItemNo,PartName=S.ItemName,LocationNo=S.LocationNo,Qty=S.Qty,ReceivedAt=S.ReceivedAt,UpdatedAt=SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
      VALUES(S.LotNo,'PART',S.ItemNo,S.ItemName,S.LocationNo,S.Qty,S.ReceivedAt,SYSDATETIME(),SYSDATETIME());
    IF @S='putaway' UPDATE dbo.WH_Inventory
      SET DeliveryNoteNo=CASE WHEN LotNo='5011LL260908850004' THEN 'PPT-WH-NOTE-PUTAWAY-RELOCATE' ELSE 'PPT-WH-NOTE-PUTAWAY' END,
          LocationNo=CASE WHEN LotNo='5011LL260908850004' THEN 'B0-10-A1' ELSE NULL END,UpdatedAt=SYSDATETIME()
      WHERE LotNo LIKE '5011LL26090885%';
    UPDATE L SET RemainingQty=R.Qty,CurrentLocationID=R.LocationNo,
      InventoryStatus=CASE WHEN @S='putaway' AND R.LocationNo IS NULL THEN 'RECEIVED' ELSE 'STORED' END,
      ModifiedBy='pda-ppt',ModifiedTS=SYSDATETIME()
    FROM dbo.tbl_Lot L JOIN @Rows R ON R.LotNo=L.LotCode COLLATE DATABASE_DEFAULT;
    IF @S='release' UPDATE dbo.WH_PickSlip SET PickedQty=0,Status='Open',CloseDate=NULL,CloseUserId=NULL,ModifiedBy='pda-ppt',ModifiedTS=SYSDATETIME() WHERE PickSlipNo='PS-PPT-WH-01';
    IF @S='history' INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,ReasonCode,SourceType,OperatorID,Note,CreatedBy,CreatedTS)
      SELECT DATEADD(second,V.OffsetSeconds,CONVERT(datetime2,CONVERT(date,SYSDATETIME()))),V.TransactionType,
        LEFT(R.ItemNo,20),LEFT(R.LocationNo,20),R.LotNo,V.QtyBefore,V.QtyChange,V.QtyAfter,V.ReasonCode,'PPT',N'SCTEST1',V.Note,N'pda-ppt',SYSDATETIME()
      FROM @Rows R
      CROSS JOIN (VALUES
        (1,'IN',CAST(0 AS decimal(18,3)),CAST(20 AS decimal(18,3)),CAST(20 AS decimal(18,3)),'INBOUND',N'PPT inbound 20 EA'),
        (2,'OUT',CAST(20 AS decimal(18,3)),CAST(-4 AS decimal(18,3)),CAST(16 AS decimal(18,3)),'PRODUCTION',N'PPT outbound 4 EA'),
        (3,'ADJ',CAST(16 AS decimal(18,3)),CAST(2 AS decimal(18,3)),CAST(18 AS decimal(18,3)),'COUNT_DIFF',N'PPT count correction +2 EA')
      ) V(OffsetSeconds,TransactionType,QtyBefore,QtyChange,QtyAfter,ReasonCode,Note);
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_SIMPLE_TEST_RESET
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Lots table(LotNo nvarchar(100));
    INSERT @Lots VALUES
      (N'5011LL260908800001'),(N'5011LL260908800002'),(N'5011LL260908800003'),
      (N'CKD260908800000001'),(N'CKD260908800000002'),(N'CKD260908800000003');
    IF (SELECT COUNT(*) FROM dbo.SCM_DeliveryBox B JOIN @Lots L ON L.LotNo=B.BoxNumber COLLATE DATABASE_DEFAULT WHERE B.ActiveFlag=1) <> 6
       OR (SELECT COUNT(*) FROM dbo.SCM_DeliveryNote WHERE NoteNumber IN('PPT-WH-NOTE-LOCAL','PPT-WH-NOTE-CKD')) <> 2
        THROW 51701,'SCTEST1 inbound data is missing. Apply PDA_SEED.sql first.',1;
    BEGIN TRANSACTION;
    DELETE T FROM dbo.WH_InventoryTransaction T JOIN @Lots L ON L.LotNo=T.LotNo COLLATE DATABASE_DEFAULT;
    DELETE W FROM dbo.WH_Inventory W JOIN @Lots L ON L.LotNo=W.LotNo;
    UPDATE L SET CurrentLocationID=NULL,InventoryStatus='CREATED',RemainingQty=COALESCE(NULLIF(BatchSize,0),RemainingQty),
      ModifiedBy='pda-test',ModifiedTS=SYSDATETIME()
    FROM dbo.tbl_Lot L JOIN @Lots X ON X.LotNo=L.LotCode COLLATE DATABASE_DEFAULT;
    UPDATE dbo.WH_PurchaseOrder SET ReceivedQty=0,Status='Open',ModifiedBy='pda-test',ModifiedTS=SYSDATETIME()
    WHERE PoNumber='PPT-INBOUND';
    UPDATE DL SET ReceivedQty=0
    FROM dbo.SCM_DeliveryLine DL
    JOIN dbo.SCM_Delivery D ON D.DeliveryID=DL.DeliveryID
    WHERE D.DeliveryNumber IN('PPT-WH-DL-LOCAL','PPT-WH-DL-CKD');
    UPDATE dbo.SCM_Delivery SET Status='Shipped',ShipDate=CONVERT(date,SYSDATETIME()),
      ShippedAt=SYSDATETIME(),ShippedBy='SCTEST1',ModifiedBy='pda-test',ModifiedTS=SYSDATETIME()
    WHERE DeliveryNumber IN('PPT-WH-DL-LOCAL','PPT-WH-DL-CKD');
    COMMIT TRANSACTION;
END;
GO


-- =====================================================================
--  Inbound / Move received LOT location
-- =====================================================================

-- =====================================================================
--  Inbound / Cancel receipt
-- =====================================================================
GO

-- =====================================================================
--  WH Inventory
-- =====================================================================


GO

-- =====================================================================
--  WH Adjust
-- =====================================================================
SET NOCOUNT ON;
GO

-- =====================================================================
--  Adjust / scan current stock
--  Source: dbo.WH_Inventory, dbo.MD_Item
--  ScanText accepts the canonical LOT No or Box No.
-- =====================================================================

-- =====================================================================
--  Adjust / save quantity change
--  Target: dbo.WH_Inventory
--  Audit:  dbo.WH_InventoryTransaction only (no separate approval workflow)
-- =====================================================================
GO

-- =====================================================================
--  WH Release
-- =====================================================================
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.WH_PickSlip', N'PickSlipNo') IS NULL
    ALTER TABLE dbo.WH_PickSlip ADD PickSlipNo nvarchar(40) NULL;
GO

IF COL_LENGTH(N'dbo.WH_PickSlip', N'ReqLocation') IS NULL
    ALTER TABLE dbo.WH_PickSlip ADD ReqLocation nvarchar(40) NULL;
GO

IF COL_LENGTH(N'dbo.WH_PickSlip', N'ReqSeqNo') IS NULL
    ALTER TABLE dbo.WH_PickSlip ADD ReqSeqNo int NULL;
GO

IF COL_LENGTH(N'dbo.WH_PickSlip', N'ReqUserId') IS NULL
    ALTER TABLE dbo.WH_PickSlip ADD ReqUserId nvarchar(80) NULL;
GO

IF COL_LENGTH(N'dbo.WH_PickSlip', N'PrintDate') IS NULL
    ALTER TABLE dbo.WH_PickSlip ADD PrintDate datetime2 NULL;
GO

IF COL_LENGTH(N'dbo.WH_PickSlip', N'CloseDate') IS NULL
    ALTER TABLE dbo.WH_PickSlip ADD CloseDate datetime2 NULL;
GO

IF COL_LENGTH(N'dbo.WH_PickSlip', N'CloseUserId') IS NULL
    ALTER TABLE dbo.WH_PickSlip ADD CloseUserId nvarchar(80) NULL;
GO

UPDATE dbo.WH_PickSlip
   SET PickSlipNo = CONCAT(N'RS-', PickSlipID)
 WHERE NULLIF(PickSlipNo, N'') IS NULL;
GO

UPDATE dbo.WH_PickSlip
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
    FROM dbo.WH_OLD_Inventory I
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

-- FIFO uses the unified inventory receipt time and LOT No as deterministic ties.

-- =====================================================================
--  Release / Pick Slip status
--  Source: dbo.WH_PickSlip
-- =====================================================================

-- =====================================================================
--  Release / Pick Slip lines
--  Source: dbo.WH_PickSlip, dbo.WH_Inventory
-- =====================================================================

-- =====================================================================
--  Release / LOT scan validation
--  Source: dbo.WH_PickSlip, dbo.WH_Inventory
-- =====================================================================

-- =====================================================================
--  Release / Pick LOT
-- =====================================================================
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
        CreatedBy varchar(50) NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_FG_InventoryAdjust_CreatedTS DEFAULT SYSDATETIME(),
        CONSTRAINT PK_FG_InventoryAdjust PRIMARY KEY CLUSTERED (AdjustID)
    );

    CREATE INDEX IX_FG_InventoryAdjust_Stock
        ON dbo.FG_InventoryAdjust (StockID, CreatedTS DESC);
END;
GO

IF COL_LENGTH(N'dbo.FG_InventoryAdjust', N'ApprovedBy') IS NOT NULL
    ALTER TABLE dbo.FG_InventoryAdjust DROP COLUMN ApprovedBy;
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


-- =====================================================================
--  FG Customer Return Note
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FG_DeliveryNote', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.FG_DeliveryNote', N'DnNumber') < 60
    ALTER TABLE dbo.FG_DeliveryNote ALTER COLUMN DnNumber VARCHAR(60) NULL;
GO


GO


GO

IF OBJECT_ID(N'dbo.FG_ReturnDisposition',N'U') IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM dbo.FG_ReturnDisposition)
        THROW 52021, 'FG_ReturnDisposition contains data and cannot be removed automatically.', 1;
    DROP TABLE dbo.FG_ReturnDisposition;
END;
GO


GO


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

IF OBJECT_ID(N'dbo.MD_SparePart', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MD_SparePart', N'Maker') IS NULL
BEGIN
    ALTER TABLE dbo.MD_SparePart ADD Maker NVARCHAR(100) NULL;
    PRINT 'Added dbo.MD_SparePart.Maker';
END;
GO

IF OBJECT_ID(N'dbo.MD_SparePart', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MD_SparePart', N'ExtraLocation') IS NULL
BEGIN
    ALTER TABLE dbo.MD_SparePart ADD ExtraLocation NVARCHAR(60) NULL;
    PRINT 'Added dbo.MD_SparePart.ExtraLocation';
END;
GO

IF OBJECT_ID(N'dbo.Seq_MNT_SparePartSerial', N'SO') IS NOT NULL
    DROP SEQUENCE dbo.Seq_MNT_SparePartSerial;
GO

IF OBJECT_ID(N'dbo.MNT_SparePartItem', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MNT_SparePartItem
    (
        SparePartItemID bigint IDENTITY NOT NULL CONSTRAINT PK_MNT_SparePartItem PRIMARY KEY,
        SerialNo varchar(24) NOT NULL CONSTRAINT UX_MNT_SparePartItem_SerialNo UNIQUE,
        SparePartNo varchar(16) NOT NULL,
        StatusCode varchar(15) NOT NULL CONSTRAINT DF_MNT_SparePartItem_Status DEFAULT 'CREATED',
        LocationID varchar(20) NULL,
        CreatedBy varchar(50) NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_MNT_SparePartItem_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy nvarchar(450) NULL,
        ModifiedTS datetime2 NULL,
        CONSTRAINT CK_MNT_SparePartItem_Status CHECK (StatusCode IN ('CREATED','IN_STOCK','RELEASED'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MNT_SparePartItem') AND name=N'IX_MNT_SparePartItem_PartStatus')
    CREATE INDEX IX_MNT_SparePartItem_PartStatus ON dbo.MNT_SparePartItem(SparePartNo,StatusCode,LocationID);
GO

IF COL_LENGTH(N'dbo.MNT_SparePartsTxn', N'SparePartItemID') IS NULL
    ALTER TABLE dbo.MNT_SparePartsTxn ADD SparePartItemID bigint NULL;
IF COL_LENGTH(N'dbo.MNT_SparePartsTxn', N'ReversalOfTxnID') IS NULL
    ALTER TABLE dbo.MNT_SparePartsTxn ADD ReversalOfTxnID int NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MNT_SparePartsTxn') AND name=N'IX_MNT_SparePartsTxn_Item')
    CREATE INDEX IX_MNT_SparePartsTxn_Item ON dbo.MNT_SparePartsTxn(SparePartItemID,TxnAt DESC,SparePartsTxnID DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MNT_SparePartsTxn') AND name=N'UX_MNT_SparePartsTxn_Reversal')
    CREATE UNIQUE INDEX UX_MNT_SparePartsTxn_Reversal ON dbo.MNT_SparePartsTxn(ReversalOfTxnID) WHERE ReversalOfTxnID IS NOT NULL;
GO


CREATE OR ALTER PROCEDURE dbo.SP_PDA_SIMPLE_TEST_RESET
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_SparePart WHERE SparePartNo = 'EOS-SP-K9-269999')
        INSERT INTO dbo.MD_SparePart
            (SparePartNo, Category, ApplicableEquip, PartNo, PartName, Maker, UOM, OnHandQty,
             SafetyStock, SupplierID, ZoneCode, Slot, ActiveFlag, CreatedBy, CreatedTS)
        VALUES
            ('EOS-SP-K9-269999', 'K', '9', 'PDA-SP-TEST-001', N'PDA Spare Parts Test', N'DEMO INDUSTRIAL', 'EA', 0,
             1, 'SP-DEMO-V01', 'SP_EXTRA', 'EX', 1, 'pda-test-reset', SYSDATETIME());

    DELETE FROM dbo.MNT_SparePartsTxn WHERE SparePartNo = 'EOS-SP-K9-269999';
    DELETE FROM dbo.MNT_SparePartItem WHERE SparePartNo = 'EOS-SP-K9-269999';

    UPDATE dbo.MD_SparePart
       SET Maker = N'DEMO INDUSTRIAL', SupplierID = 'SP-DEMO-V01',
           OnHandQty = 0, ZoneCode = 'SP_EXTRA', Slot = 'EX', ActiveFlag = 1,
           ModifiedBy = N'pda-test-reset', ModifiedTS = SYSDATETIME()
     WHERE SparePartNo = 'EOS-SP-K9-269999';

    INSERT dbo.MNT_SparePartItem(SerialNo,SparePartNo,StatusCode,CreatedBy,CreatedTS)
    VALUES('SPI-TEST-000001','EOS-SP-K9-269999','CREATED','pda-test-reset',SYSDATETIME());

    COMMIT TRANSACTION;
END;
GO

-- MD_Location owns the Warehouse/FG physical hierarchy. Spare-parts storage
-- is managed only by MD_SparePart.ZoneCode/Slot/ExtraLocation.
IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.MD_Location', N'WhCode') IS NULL
        ALTER TABLE dbo.MD_Location ADD WhCode VARCHAR(20) NULL;

    IF COL_LENGTH(N'dbo.MD_Location', N'AreaCode') IS NULL
        ALTER TABLE dbo.MD_Location ADD AreaCode VARCHAR(20) NULL;

    IF COL_LENGTH(N'dbo.MD_Location', N'ZoneCode') < 20
        ALTER TABLE dbo.MD_Location ALTER COLUMN ZoneCode VARCHAR(20) NULL;
END;
GO

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.MD_Location
       SET WhCode = 'EOS',
           AreaCode = CASE
               WHEN UPPER(LocationID) LIKE 'FG%'
                 OR UPPER(COALESCE(LocationType, '')) IN ('FG', 'FINISHED_GOODS', 'FINISHED GOODS')
                   THEN 'FG_AREA'
               ELSE 'MAT_AREA'
           END
     WHERE UPPER(LocationID) NOT LIKE 'SP-%';

    DELETE FROM dbo.MD_Location
     WHERE AreaCode = 'SPARE_PARTS_AREA' OR UPPER(LocationID) LIKE 'SP-%';

    IF NOT EXISTS (
        SELECT 1 FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.MD_Location') AND c.name = N'WhCode')
        ALTER TABLE dbo.MD_Location ADD CONSTRAINT DF_MD_Location_WhCode DEFAULT ('EOS') FOR WhCode;

    IF NOT EXISTS (
        SELECT 1 FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.MD_Location') AND c.name = N'AreaCode')
        ALTER TABLE dbo.MD_Location ADD CONSTRAINT DF_MD_Location_AreaCode DEFAULT ('MAT_AREA') FOR AreaCode;

    ALTER TABLE dbo.MD_Location ALTER COLUMN WhCode VARCHAR(20) NOT NULL;
    ALTER TABLE dbo.MD_Location ALTER COLUMN AreaCode VARCHAR(20) NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    MERGE dbo.WH_WarehouseMaster AS tgt
    USING (
        SELECT DISTINCT CAST(WhCode AS varchar(20)) AS WhCode
        FROM dbo.MD_Location
        WHERE NULLIF(WhCode, '') IS NOT NULL
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
        SELECT TOP (1) L.WhCode
        FROM dbo.MD_Location L
        WHERE L.AreaCode = A.AreaCode
          AND NULLIF(L.WhCode, '') IS NOT NULL
        ORDER BY L.WhCode
    ) X
    WHERE NULLIF(A.WhCode, '') IS NULL;

    MERGE dbo.WH_AreaMaster AS tgt
    USING (
        SELECT
            MIN(CAST(WhCode AS varchar(20))) AS WhCode,
            CAST(AreaCode AS varchar(20)) AS AreaCode
        FROM dbo.MD_Location
        WHERE NULLIF(AreaCode, '') IS NOT NULL
        GROUP BY CAST(AreaCode AS varchar(20))
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
        SELECT TOP (1) L.WhCode
        FROM dbo.MD_Location L
        WHERE L.AreaCode = S.AreaCode
          AND COALESCE(NULLIF(L.ZoneCode, ''), 'DEFAULT') = S.SectionCode
          AND NULLIF(L.WhCode, '') IS NOT NULL
        ORDER BY L.WhCode
    ) X
    WHERE NULLIF(S.WhCode, '') IS NULL;

    MERGE dbo.WH_AreaSection AS tgt
    USING (
        SELECT
            MIN(CAST(WhCode AS varchar(20))) AS WhCode,
            CAST(AreaCode AS varchar(20)) AS AreaCode,
            CAST(COALESCE(NULLIF(ZoneCode, ''), 'DEFAULT') AS varchar(20)) AS SectionCode
        FROM dbo.MD_Location
        WHERE NULLIF(AreaCode, '') IS NOT NULL
        GROUP BY CAST(AreaCode AS varchar(20)),
                 CAST(COALESCE(NULLIF(ZoneCode, ''), 'DEFAULT') AS varchar(20))
    ) AS src
       ON tgt.AreaCode = src.AreaCode
      AND tgt.SectionCode = src.SectionCode
    WHEN NOT MATCHED THEN INSERT
        (WhCode, AreaCode, SectionCode, SectionName, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        (src.WhCode, src.AreaCode, src.SectionCode, src.SectionCode, 1, 'system', SYSDATETIME());
END;
GO

-- Spare-parts locations are not Warehouse Location Master records.
IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
    DELETE FROM dbo.MD_Location WHERE AreaCode = 'SPARE_PARTS_AREA';
GO

IF OBJECT_ID(N'dbo.WH_AreaSection', N'U') IS NOT NULL
    DELETE FROM dbo.WH_AreaSection WHERE AreaCode = 'SPARE_PARTS_AREA';
GO

IF OBJECT_ID(N'dbo.WH_AreaMaster', N'U') IS NOT NULL
    DELETE FROM dbo.WH_AreaMaster WHERE AreaCode = 'SPARE_PARTS_AREA';
GO

IF OBJECT_ID(N'dbo.MD_CodeItem', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.MD_CodeItem SET ParentCodeID = NULL
    WHERE GroupCode = 'MNT_ZONE' AND ParentCodeID = 'WH_AREA_SPARE_PARTS_AREA';
    DELETE FROM dbo.MD_CodeItem WHERE CodeID = 'WH_AREA_SPARE_PARTS_AREA';
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
    DELETE dbo.FG_CustomerReturn WHERE ShipmentOrderID=@Order;
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
    INSERT dbo.FG_InventoryAdjust(AdjustNo,StockID,ItemNo,Location,LotID,QtyBefore,Delta,QtyAfter,ReasonCode,ReasonNote,Status,RequestedBy,CreatedBy,CreatedTS)
    VALUES('FG-PPT-HIST-ADJ',@Stock,'PPT-FG-HIST','FG-PPT-G1',@Lot,20,2,22,'COUNT_DIFF','PPT count correction','Posted','SCTEST1',@By,DATEADD(second,2,@Today));
    INSERT dbo.FG_ShipmentOrderLine(ShipmentOrderID,LineSeq,ItemNo,OrderedQty,AllocatedQty,StockID,LotID,Location,ReservationStatus,CreatedBy,CreatedTS)
    VALUES(@Order,10,'PPT-FG-HIST',22,22,@Stock,@Lot,'FG-PPT-G1','Shipped',@By,DATEADD(second,3,@Today));
    DECLARE @PickJson nvarchar(max)=(SELECT @Stock AS StockId,22 AS Qty FOR JSON PATH);
    DECLARE @Json nvarchar(max)=(SELECT @Stock AS stockId,'5011FG260908970001' AS lotNo,'FG-PPT-STK-970001' AS stockNumber,'PPT-FG-HIST' AS itemNo,22 AS qty,'EA' AS unit,'FG-PPT-G1' AS location FOR JSON PATH);
    INSERT dbo.FG_PickingFifo(PickNumber,ShipmentOrderID,PickerID,EndTS,PicksJSON,PickedQty,OrderedQty,Status,CreatedBy,CreatedTS)
    VALUES('FG-PPT-HIST-PICK',@Order,'SCTEST1',DATEADD(second,3,@Today),@PickJson,22,22,'Picked',@By,DATEADD(second,3,@Today));
    DECLARE @Pick int=CONVERT(int,SCOPE_IDENTITY());
    INSERT dbo.FG_PickingDetail(PickID,ShipmentOrderLineID,StockID,LotID,ItemNo,Qty,Location,PickSeq,CreatedBy,CreatedTS)
    SELECT @Pick,ShipmentOrderLineID,@Stock,@Lot,'PPT-FG-HIST',22,'FG-PPT-G1',1,@By,DATEADD(second,3,@Today)
    FROM dbo.FG_ShipmentOrderLine WHERE ShipmentOrderID=@Order;
    INSERT dbo.FG_LoadingConfirm(LoadingNumber,ShipmentOrderID,PickID,LicensePlate,PalletsLoadedJSON,DepartureTS,OTDStatus,OperatorID,ConfirmedAt,CreatedBy,CreatedTS)
    VALUES('FG-PPT-HIST-LOAD',@Order,@Pick,'PPT-FG-HISTORY',@Json,DATEADD(second,4,@Today),'OnTime','SCTEST1',DATEADD(second,4,@Today),@By,DATEADD(second,4,@Today));
    INSERT dbo.FG_CustomerReturn(ShipmentOrderID,CustomerID,LotID,PartNo,PartName,LotNo,ReturnQty,
        ReturnReason,Note,Status,ReceivedAt,ReceivedBy,CreatedBy,CreatedTS)
    SELECT @Order,C.CustomerID,@Lot,I.ItemNo,I.ItemName,'5011FG260908970001',22,
        'DAMAGED_TRANSIT','PPT return note','Open',DATEADD(second,5,@Today),'SCTEST1',@By,DATEADD(second,5,@Today)
    FROM dbo.FG_ShipmentOrder O JOIN dbo.MD_Customer C ON C.CustomerID=O.CustomerCode OR C.CustomerCode=O.CustomerCode
    JOIN dbo.MD_Item I ON I.ItemNo='PPT-FG-HIST' WHERE O.ShipmentOrderID=@Order;
    IF @@ROWCOUNT<>1 THROW 51732,'Configure a real shipment customer and part before resetting return samples.',1;
    COMMIT TRANSACTION;
END;
GO


GO

CREATE OR ALTER TRIGGER dbo.TR_WH_OLD_Inventory_SyncUnifiedInventory
ON dbo.WH_OLD_Inventory
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DELETE Target
    FROM dbo.WH_Inventory Target
    JOIN deleted D
      ON Target.LotNo = COALESCE(
          NULLIF((SELECT L.LotCode FROM dbo.tbl_Lot L WHERE L.LotID = D.LotID), N''),
          CONCAT(N'LEGACY-WH-', RIGHT(REPLICATE('0',10) + CONVERT(varchar(10),D.InventoryID),10))) COLLATE DATABASE_DEFAULT
    WHERE NOT EXISTS (SELECT 1 FROM inserted I WHERE I.InventoryID = D.InventoryID);

    ;WITH SourceRows AS
    (
        SELECT
            COALESCE(NULLIF(L.LotCode,N''),CONCAT(N'LEGACY-WH-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),I.InventoryID),10))) COLLATE DATABASE_DEFAULT AS LotNo,
            COALESCE(I.ItemNo,L.ItemNo) AS PartNo,
            M.ItemName AS PartName,
            P.CaseNo,
            I.LocationID AS LocationNo,
            COALESCE(I.OnHandQty,0) AS Qty,
            CASE WHEN UPPER(COALESCE(I.Status,'RECEIVED')) IN ('RECEIVED','OK','STORED') THEN 'AVAILABLE' ELSE UPPER(I.Status) END AS InventoryStatus,
            P.InvoiceNo,
            COALESCE(I.LastReceivedAt,P.ReceivedAt,I.CreatedTS,sysdatetime()) AS ReceivedAt,
            COALESCE(I.CreatedTS,I.LastReceivedAt,P.CreatedTS,sysdatetime()) AS CreatedAt,
            COALESCE(I.ModifiedTS,I.CreatedTS,I.LastReceivedAt,P.ModifiedTS,P.CreatedTS,sysdatetime()) AS UpdatedAt
        FROM inserted I
        LEFT JOIN dbo.tbl_Lot L ON L.LotID = I.LotID
        LEFT JOIN dbo.MD_Item M ON M.ItemNo = COALESCE(I.ItemNo,L.ItemNo)
        OUTER APPLY
        (
            SELECT TOP (1) IP.CaseNo,IP.InvoiceNo,IP.ReceivedAt,IP.CreatedTS,IP.ModifiedTS
            FROM dbo.WH_InboundPackage IP
            WHERE IP.LotID = I.LotID
            ORDER BY COALESCE(IP.ReceivedAt,IP.ModifiedTS,IP.CreatedTS) DESC,IP.InboundPackageID DESC
        ) P
    )
    MERGE dbo.WH_Inventory AS Target
    USING SourceRows AS Source ON Target.LotNo = Source.LotNo
    WHEN MATCHED AND Source.Qty > 0 AND Source.InventoryStatus NOT IN ('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN
        UPDATE SET PartNo=Source.PartNo,PartName=Source.PartName,PalletNo=Source.CaseNo,
                   LocationNo=Source.LocationNo,Qty=Source.Qty,
                   InvoiceNo=Source.InvoiceNo,ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
    WHEN NOT MATCHED AND Source.Qty > 0 AND Source.InventoryStatus NOT IN ('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN
        INSERT (LotNo,UnitType,PartNo,PartName,PalletNo,LocationNo,Qty,InvoiceNo,ReceivedAt,CreatedAt,UpdatedAt)
        VALUES (Source.LotNo,'PART',Source.PartNo,Source.PartName,Source.CaseNo,Source.LocationNo,Source.Qty,Source.InvoiceNo,Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt)
    WHEN MATCHED AND (Source.Qty <= 0 OR Source.InventoryStatus IN ('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED')) THEN DELETE;
END;
GO

-- Box identifiers are canonical LOT numbers. WH_Inventory.PalletNo is the MD_Pallet number (renamed from CaseNo by migrate_md_pallet.sql).
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_BoxNo')
    DROP INDEX IX_WH_Inventory_BoxNo ON dbo.WH_Inventory;
IF COL_LENGTH(N'dbo.WH_Inventory',N'BoxNo') IS NOT NULL
    ALTER TABLE dbo.WH_Inventory DROP COLUMN BoxNo;
GO

-- BEGIN FG SHIPMENT CONSOLIDATION
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
        (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
         ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
    SELECT COALESCE(P.EndTS,P.StartTS,D.CreatedTS,SYSDATETIME()),'OUT',D.ItemNo,D.Location,
           COALESCE(L.LotCode,CONCAT(N'LEGACY-FG-PICK-',D.PickDetailID)),
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
          WHERE T.SourceType='FG_LEGACY_PICK' AND T.SourceID=D.PickDetailID
      );
END;

IF OBJECT_ID(N'dbo.FG_LoadingConfirm', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
BEGIN
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
         ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
    SELECT COALESCE(C.DepartureTS,C.ConfirmedAt,C.CreatedTS,SYSDATETIME()),'OUT',J.ItemNo,J.Location,
           COALESCE(J.LotNo,CONCAT(N'LEGACY-FG-LOAD-',C.LoadingID,N'-',COALESCE(J.ItemNo,N'ITEM'))),
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
          WHERE T.SourceType='FG_LEGACY_LOAD' AND T.SourceID=C.LoadingID
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
-- END FG SHIPMENT CONSOLIDATION
-- BEGIN FG INVENTORY CONSOLIDATION
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.WH_Inventory',N'U') IS NULL
    THROW 52088,'WH_Inventory must exist before consolidating finished-goods inventory.',1;
IF OBJECT_ID(N'dbo.WH_InventoryTransaction',N'U') IS NULL
    THROW 52089,'WH_InventoryTransaction must exist before consolidating finished-goods adjustments.',1;
GO

-- Add the LOT references in their own batch so subsequent statements compile
-- on databases upgraded from the legacy StockID schema.
IF OBJECT_ID(N'dbo.FG_PutAway',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.FG_PutAway',N'LotNo') IS NULL
    ALTER TABLE dbo.FG_PutAway ADD LotNo nvarchar(50) NULL;
IF OBJECT_ID(N'dbo.FG_CustomerReturn',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.FG_CustomerReturn',N'LotNo') IS NULL
    ALTER TABLE dbo.FG_CustomerReturn ADD LotNo nvarchar(50) NULL;
GO

-- Preserve every legacy finished-goods LOT before retiring the duplicate tables.
IF OBJECT_ID(N'dbo.FG_Inventory', N'U') IS NOT NULL
BEGIN
    ;WITH SourceRows AS
    (
        SELECT
            COALESCE(NULLIF(L.LotCode,N''),NULLIF(F.StockNumber,N''),
                CONCAT(N'LEGACY-FG-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),F.StockID),10))) COLLATE DATABASE_DEFAULT AS LotNo,
            F.ItemNo AS PartNo,
            I.ItemName AS PartName,
            F.Location AS LocationNo,
            COALESCE(F.Qty,0) AS Qty,
            COALESCE(F.StockTS,F.CreatedTS,SYSDATETIME()) AS ReceivedAt,
            COALESCE(F.CreatedTS,F.StockTS,SYSDATETIME()) AS CreatedAt,
            COALESCE(F.ModifiedTS,F.CreatedTS,F.StockTS,SYSDATETIME()) AS UpdatedAt,
            UPPER(COALESCE(F.Status,'AVAILABLE')) AS LegacyStatus
        FROM dbo.FG_Inventory F
        LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID
        LEFT JOIN dbo.MD_Item I ON I.ItemNo=F.ItemNo
    )
    MERGE dbo.WH_Inventory AS Target
    USING SourceRows AS Source ON Source.LotNo=Target.LotNo
    WHEN MATCHED THEN UPDATE SET
        PartNo=Source.PartNo,PartName=Source.PartName,LocationNo=Source.LocationNo,
        Qty=CASE WHEN Source.LegacyStatus IN ('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN 0 ELSE Source.Qty END,
        ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
    WHEN NOT MATCHED THEN INSERT
        (LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
    VALUES
        (Source.LotNo,'PART',Source.PartNo,Source.PartName,Source.LocationNo,
         CASE WHEN Source.LegacyStatus IN ('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN 0 ELSE Source.Qty END,
         Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt);

    IF COL_LENGTH(N'dbo.FG_PutAway',N'LotNo') IS NULL
        ALTER TABLE dbo.FG_PutAway ADD LotNo nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.FG_CustomerReturn',N'LotNo') IS NULL
        ALTER TABLE dbo.FG_CustomerReturn ADD LotNo nvarchar(50) NULL;

    UPDATE P SET LotNo=X.LotNo
    FROM dbo.FG_PutAway P
    JOIN
    (
        SELECT F.StockID,COALESCE(NULLIF(L.LotCode,N''),NULLIF(F.StockNumber,N''),
            CONCAT(N'LEGACY-FG-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),F.StockID),10))) AS LotNo
        FROM dbo.FG_Inventory F LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID
    ) X ON X.StockID=P.StockID
    WHERE P.LotNo IS NULL;

    IF COL_LENGTH(N'dbo.FG_CustomerReturn',N'ItemsJSON') IS NOT NULL
        EXEC(N'UPDATE R SET LotNo=COALESCE(X.LotNo,JSON_VALUE(R.ItemsJSON,''$[0].lotNo''))
    FROM dbo.FG_CustomerReturn R
    LEFT JOIN
    (
        SELECT F.StockID,COALESCE(NULLIF(L.LotCode,N''''),NULLIF(F.StockNumber,N''''),
            CONCAT(N''LEGACY-FG-'',RIGHT(REPLICATE(''0'',10)+CONVERT(varchar(10),F.StockID),10))) AS LotNo
        FROM dbo.FG_Inventory F LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID
    ) X ON X.StockID=R.StockID
    WHERE R.LotNo IS NULL;');

    UPDATE O SET ItemsJSON=J.ItemsJSON
    FROM dbo.FG_ShipmentOrder O
    CROSS APPLY
    (
        SELECT N'['+STRING_AGG(
            JSON_MODIFY(E.[value],'$.lotNo',COALESCE(NULLIF(L.LotCode,N''),NULLIF(F.StockNumber,N''),
                CASE WHEN F.StockID IS NOT NULL THEN CONCAT(N'LEGACY-FG-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),F.StockID),10)) END,
                JSON_VALUE(E.[value],'$.lotNo'))),N',')
            WITHIN GROUP (ORDER BY TRY_CONVERT(int,E.[key]))+N']'
        FROM OPENJSON(CASE WHEN ISJSON(O.ItemsJSON)=1 THEN O.ItemsJSON ELSE N'[]' END) E
        LEFT JOIN dbo.FG_Inventory F ON F.StockID=TRY_CONVERT(int,JSON_VALUE(E.[value],'$.stockId'))
        LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID
        WHERE JSON_VALUE(E.[value],'$.stockId') IS NULL OR F.StockID IS NOT NULL
    ) J(ItemsJSON)
    WHERE J.ItemsJSON IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.FG_PutAway',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.FG_PutAway',N'LotNo') IS NULL
    ALTER TABLE dbo.FG_PutAway ADD LotNo nvarchar(50) NULL;
IF OBJECT_ID(N'dbo.FG_CustomerReturn',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.FG_CustomerReturn',N'LotNo') IS NULL
    ALTER TABLE dbo.FG_CustomerReturn ADD LotNo nvarchar(50) NULL;
GO

-- Adjustment rows are ordinary inventory transactions; do not store them twice.
IF OBJECT_ID(N'dbo.FG_InventoryAdjust',N'U') IS NOT NULL
BEGIN
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,
         QtyBefore,QtyChange,QtyAfter,ReasonCode,SourceType,SourceID,
         OperatorID,Note,CreatedBy,CreatedTS)
    SELECT COALESCE(A.CreatedTS,SYSDATETIME()),'ADJ',A.ItemNo,A.Location,
           COALESCE(NULLIF(L.LotCode,N''),NULLIF(F.StockNumber,N''),CONCAT(N'LEGACY-FG-ADJUST-',A.AdjustID)),
           A.QtyBefore,A.Delta,A.QtyAfter,A.ReasonCode,'FG_ADJUST',A.AdjustID,
           COALESCE(A.RequestedBy,A.CreatedBy),A.ReasonNote,
           LEFT(COALESCE(NULLIF(A.CreatedBy,''),'system'),20),COALESCE(A.CreatedTS,SYSDATETIME())
    FROM dbo.FG_InventoryAdjust A
    LEFT JOIN dbo.FG_Inventory F ON F.StockID=A.StockID
    LEFT JOIN dbo.tbl_Lot L ON L.LotID=COALESCE(A.LotID,F.LotID)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.WH_InventoryTransaction T
        WHERE T.SourceType='FG_ADJUST' AND T.SourceID=A.AdjustID
    );
END;
GO


-- Keep the simple PDA scenarios on the canonical inventory only.
CREATE OR ALTER PROCEDURE dbo.FG_PDA_PPT_TEST_RESET @Screen varchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Screen NOT IN('qc','putaway','inventory','return','adjust') THROW 51700,'Unknown FG PPT test screen.',1;
    DECLARE @SeedBy varchar(50)=CONCAT('pda-ppt-fg-',@Screen);
    DECLARE @Lots TABLE(LotID int PRIMARY KEY,LotCode varchar(40),ItemNo varchar(20),Qty decimal(12,3),LocationID varchar(20));
    INSERT @Lots SELECT LotID,LotCode,ItemNo,BatchSize,
        CASE @Screen WHEN 'putaway' THEN 'FG-PPT-A1' WHEN 'inventory' THEN 'FG-PPT-B1'
             WHEN 'return' THEN 'FG-PPT-E1' WHEN 'adjust' THEN 'FG-PPT-F1' END
        FROM dbo.tbl_Lot WHERE CreatedBy=@SeedBy;
    IF NOT EXISTS(SELECT 1 FROM @Lots) THROW 51701,'FG PPT samples are missing. Run PDA_SEED.sql.',1;
    BEGIN TRANSACTION;
    DELETE T FROM dbo.WH_InventoryTransaction T JOIN @Lots L ON L.LotCode=T.LotNo COLLATE DATABASE_DEFAULT WHERE T.SourceType IN('FG_ADJUST','FG_PPT_HISTORY');
    DELETE P FROM dbo.FG_PutAway P JOIN @Lots L ON L.LotCode=P.LotNo;
    IF @Screen IN('qc','putaway') DELETE W FROM dbo.WH_Inventory W JOIN @Lots L ON L.LotCode=W.LotNo;
    ELSE
        MERGE dbo.WH_Inventory AS T USING
        (SELECT L.LotCode,L.ItemNo,I.ItemName,L.LocationID,L.Qty FROM @Lots L LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=L.ItemNo) S
        ON S.LotCode=T.LotNo
        WHEN MATCHED THEN UPDATE SET PartNo=S.ItemNo,PartName=S.ItemName,LocationNo=S.LocationID,Qty=S.Qty,UpdatedAt=SYSDATETIME()
        WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
            VALUES(S.LotCode,'PART',S.ItemNo,S.ItemName,S.LocationID,S.Qty,SYSDATETIME(),SYSDATETIME(),SYSDATETIME());
    UPDATE L SET RemainingQty=T.Qty,CurrentLocationID=CASE WHEN @Screen IN('qc','putaway') THEN NULL ELSE T.LocationID END,
        ProcessCode='IMG',Status='CONFIRMED',QualityFlag='OK',ModifiedBy=LEFT(@SeedBy,20),ModifiedTS=SYSDATETIME()
    FROM dbo.tbl_Lot L JOIN @Lots T ON T.LotID=L.LotID;
    IF @Screen='return'
    BEGIN
        DELETE R FROM dbo.FG_CustomerReturn R JOIN @Lots L ON L.LotCode=R.LotNo;
        UPDATE O SET Status=CASE WHEN O.ShipOrderNumber='FG-PPT-SO-RETURN' THEN 'SHIPPED' ELSE 'OPEN' END,
            ShippedAt=CASE WHEN O.ShipOrderNumber='FG-PPT-SO-RETURN' THEN DATEADD(day,-1,SYSDATETIME()) ELSE NULL END,
            ItemsJSON=J.ItemsJSON,ModifiedBy=LEFT(@SeedBy,20),ModifiedTS=SYSDATETIME()
        FROM dbo.FG_ShipmentOrder O
        CROSS APPLY
        (
            SELECT 10 lineSeq,L.ItemNo itemNo,L.Qty orderedQty,L.Qty allocatedQty,L.LotCode lotNo,L.LotID lotId,L.LocationID location
            FROM @Lots L
            WHERE (O.ShipOrderNumber='FG-PPT-SO-RETURN' AND RIGHT(L.LotCode,6)='950001')
               OR (O.ShipOrderNumber='FG-PPT-SO-NOSHIP' AND RIGHT(L.LotCode,6)='950002')
            FOR JSON PATH
        ) J(ItemsJSON)
        WHERE O.CreatedBy=@SeedBy AND O.ShipOrderNumber IN('FG-PPT-SO-RETURN','FG-PPT-SO-NOSHIP');
    END;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_HISTORY_TEST_RESET
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @LotID int,@Today datetime2=CONVERT(date,SYSDATETIME()),@LotNo nvarchar(50)=N'5011FG260908970001';
    SELECT @LotID=LotID FROM dbo.tbl_Lot WHERE LotCode=@LotNo AND CreatedBy='pda-ppt-fg-history';
    IF @LotID IS NULL THROW 51730,'FG History samples are missing. Run PDA_SEED.sql.',1;
    MERGE dbo.WH_Inventory AS T USING(SELECT @LotNo LotNo) S ON S.LotNo=T.LotNo
    WHEN MATCHED THEN UPDATE SET PartNo='PPT-FG-HIST',PartName=N'PPT FG HISTORY',LocationNo='FG-PPT-G1',Qty=0,UpdatedAt=@Today
    WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
        VALUES(@LotNo,'PART','PPT-FG-HIST',N'PPT FG HISTORY','FG-PPT-G1',0,@Today,@Today,@Today);
    DELETE dbo.WH_InventoryTransaction WHERE SourceType='FG_PPT_HISTORY' AND LotNo=@LotNo;
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,ReasonCode,SourceType,OperatorID,Note,CreatedBy,CreatedTS)
    VALUES
        (DATEADD(second,1,@Today),'IN','PPT-FG-HIST','FG-PPT-G1',@LotNo,0,20,20,'PUTAWAY','FG_PPT_HISTORY','SCTEST1',N'PPT Put-Away','pda-ppt-fg-history',SYSDATETIME()),
        (DATEADD(second,2,@Today),'ADJ','PPT-FG-HIST','FG-PPT-G1',@LotNo,20,2,22,'COUNT_DIFF','FG_PPT_HISTORY','SCTEST1',N'PPT count correction','pda-ppt-fg-history',SYSDATETIME()),
        (DATEADD(second,3,@Today),'OUT','PPT-FG-HIST','FG-PPT-G1',@LotNo,22,-22,0,'OUTBOUND','FG_PPT_HISTORY','SCTEST1',N'PPT outbound','pda-ppt-fg-history',SYSDATETIME()),
        (DATEADD(second,4,@Today),'IN','PPT-FG-HIST','FG-PPT-G1',@LotNo,0,22,22,'RETURN','FG_PPT_HISTORY','SCTEST1',N'PPT customer return','pda-ppt-fg-history',SYSDATETIME());
END;
GO

DROP TRIGGER IF EXISTS dbo.TR_FG_Inventory_SyncUnifiedInventory;
GO

-- Remove legacy foreign keys and identity references after all data is preserved.
DECLARE @DropSql nvarchar(max)=N'';
SELECT @DropSql+=N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(parent_object_id))+
    N' DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.foreign_keys
WHERE referenced_object_id IN(OBJECT_ID(N'dbo.FG_Inventory'),OBJECT_ID(N'dbo.FG_InventoryAdjust'));
IF @DropSql<>N'' EXEC sys.sp_executesql @DropSql;
GO

IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'UX_FG_CustomerReturn_Stock')
    DROP INDEX UX_FG_CustomerReturn_Stock ON dbo.FG_CustomerReturn;
IF COL_LENGTH(N'dbo.FG_CustomerReturn',N'StockID') IS NOT NULL
BEGIN
    ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN StockID int NULL;
    ALTER TABLE dbo.FG_CustomerReturn DROP COLUMN StockID;
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'IX_FG_CustomerReturn_LotNo')
    CREATE INDEX IX_FG_CustomerReturn_LotNo ON dbo.FG_CustomerReturn(LotNo) WHERE LotNo IS NOT NULL;

IF COL_LENGTH(N'dbo.FG_PutAway',N'StockID') IS NOT NULL
    ALTER TABLE dbo.FG_PutAway DROP COLUMN StockID;
GO

DROP TABLE IF EXISTS dbo.FG_InventoryAdjust;
DROP TABLE IF EXISTS dbo.FG_Inventory;
GO

-- The legacy WH compatibility trigger must no longer reference the retired FG table.
CREATE OR ALTER TRIGGER dbo.TR_WH_OLD_Inventory_SyncUnifiedInventory
ON dbo.WH_OLD_Inventory
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DELETE Target
    FROM dbo.WH_Inventory Target
    JOIN deleted D ON Target.LotNo=COALESCE(NULLIF((SELECT L.LotCode FROM dbo.tbl_Lot L WHERE L.LotID=D.LotID),N''),CONCAT(N'LEGACY-WH-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),D.InventoryID),10))) COLLATE DATABASE_DEFAULT
    WHERE NOT EXISTS(SELECT 1 FROM inserted I WHERE I.InventoryID=D.InventoryID);

    ;WITH SourceRows AS
    (
        SELECT
            COALESCE(NULLIF(L.LotCode,N''),CONCAT(N'LEGACY-WH-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),I.InventoryID),10))) COLLATE DATABASE_DEFAULT LotNo,
            COALESCE(I.ItemNo,L.ItemNo) PartNo,M.ItemName PartName,P.CaseNo,I.LocationID LocationNo,
            COALESCE(I.OnHandQty,0) Qty,
            CASE WHEN UPPER(COALESCE(I.Status,'RECEIVED')) IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN 'UNAVAILABLE' ELSE 'AVAILABLE' END InventoryStatus,
            P.InvoiceNo,
            COALESCE(I.LastReceivedAt,P.ReceivedAt,I.CreatedTS,sysdatetime()) ReceivedAt,
            COALESCE(I.CreatedTS,I.LastReceivedAt,P.CreatedTS,sysdatetime()) CreatedAt,
            COALESCE(I.ModifiedTS,I.CreatedTS,I.LastReceivedAt,P.ModifiedTS,P.CreatedTS,sysdatetime()) UpdatedAt
        FROM inserted I
        LEFT JOIN dbo.tbl_Lot L ON L.LotID=I.LotID
        LEFT JOIN dbo.MD_Item M ON M.ItemNo=COALESCE(I.ItemNo,L.ItemNo)
        OUTER APPLY(SELECT TOP(1) IP.CaseNo,IP.InvoiceNo,IP.ReceivedAt,IP.CreatedTS,IP.ModifiedTS FROM dbo.WH_InboundPackage IP WHERE IP.LotID=I.LotID ORDER BY COALESCE(IP.ReceivedAt,IP.ModifiedTS,IP.CreatedTS) DESC,IP.InboundPackageID DESC) P
    )
    MERGE dbo.WH_Inventory AS Target
    USING SourceRows AS Source ON Target.LotNo=Source.LotNo
    WHEN MATCHED AND Source.Qty>0 AND Source.InventoryStatus='AVAILABLE' THEN
        UPDATE SET PartNo=Source.PartNo,PartName=Source.PartName,PalletNo=Source.CaseNo,LocationNo=Source.LocationNo,
                   Qty=Source.Qty,InvoiceNo=Source.InvoiceNo,ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
    WHEN NOT MATCHED AND Source.Qty>0 AND Source.InventoryStatus='AVAILABLE' THEN
        INSERT(LotNo,UnitType,PartNo,PartName,PalletNo,LocationNo,Qty,InvoiceNo,ReceivedAt,CreatedAt,UpdatedAt)
        VALUES(Source.LotNo,'PART',Source.PartNo,Source.PartName,Source.CaseNo,Source.LocationNo,Source.Qty,Source.InvoiceNo,Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt)
    WHEN MATCHED AND (Source.Qty<=0 OR Source.InventoryStatus<>'AVAILABLE') THEN DELETE;
END;
GO

IF OBJECT_ID(N'dbo.FG_Inventory',N'U') IS NOT NULL OR OBJECT_ID(N'dbo.FG_InventoryAdjust',N'U') IS NOT NULL
    THROW 52090,'Legacy finished-goods inventory tables were not removed.',1;
GO

-- END FG INVENTORY CONSOLIDATION
