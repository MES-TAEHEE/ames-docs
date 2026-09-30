/*
  Warehouse core-table consolidation
  Final dbo.WH_* tables:
    WH_Inventory
    WH_InventoryTransaction
    WH_PurchaseOrder
    WH_PickSlip

  Run with sqlcmd/SSMS against the intended database only.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.WH_PickSlip', N'U') IS NULL
       AND OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NOT NULL
        EXEC sys.sp_rename N'dbo.WH_ReleaseSchedule', N'WH_PickSlip';

    /* A partially upgraded database can contain both names. Never discard rows. */
    IF OBJECT_ID(N'dbo.WH_PickSlip', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.WH_ReleaseSchedule)
            THROW 52002, 'WH_ReleaseSchedule still contains rows; merge them before consolidation.', 1;
        DROP TABLE dbo.WH_ReleaseSchedule;
    END;

    IF COL_LENGTH(N'dbo.WH_PickSlip', N'PickSlipID') IS NULL
       AND COL_LENGTH(N'dbo.WH_PickSlip', N'ReleaseScheduleID') IS NOT NULL
        EXEC sys.sp_rename N'dbo.WH_PickSlip.ReleaseScheduleID', N'PickSlipID', N'COLUMN';

    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.WH_PickSlip') AND name=N'PK_WH_ReleaseSchedule')
       AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.WH_PickSlip') AND name=N'PK_WH_PickSlip')
        EXEC sys.sp_rename N'dbo.PK_WH_ReleaseSchedule', N'PK_WH_PickSlip', N'OBJECT';

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_PickSlip') AND name=N'IX_WH_ReleaseSchedule_PickSlipNo')
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_PickSlip') AND name=N'IX_WH_PickSlip_PickSlipNo')
        EXEC sys.sp_rename N'dbo.WH_PickSlip.IX_WH_ReleaseSchedule_PickSlipNo', N'IX_WH_PickSlip_PickSlipNo', N'INDEX';

    IF OBJECT_ID(N'dbo.WH_PickSlip', N'U') IS NULL
        THROW 52000, 'WH_PickSlip is required.', 1;

    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LotNo') IS NULL
        ALTER TABLE dbo.WH_InventoryTransaction ADD LotNo nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'PartNo') IS NULL
        ALTER TABLE dbo.WH_InventoryTransaction ADD PartNo varchar(50) NULL;
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LocationNo') IS NULL
        ALTER TABLE dbo.WH_InventoryTransaction ADD LocationNo varchar(50) NULL;

    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'ItemNo') IS NOT NULL
        EXEC(N'UPDATE dbo.WH_InventoryTransaction SET PartNo=COALESCE(PartNo,CONVERT(varchar(50),ItemNo));');
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LocationID') IS NOT NULL
        EXEC(N'UPDATE dbo.WH_InventoryTransaction SET LocationNo=COALESCE(LocationNo,CONVERT(varchar(50),LocationID));');
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LotID') IS NOT NULL
        EXEC(N'UPDATE T SET LotNo=COALESCE(NULLIF(T.LotNo,N''''),L.LotCode) FROM dbo.WH_InventoryTransaction T LEFT JOIN dbo.tbl_Lot L ON L.LotID=T.LotID;');
    UPDATE dbo.WH_InventoryTransaction SET LotNo=CONCAT(N'LEGACY-TX-',TransactionID) WHERE NULLIF(LTRIM(RTRIM(LotNo)),N'') IS NULL;
    UPDATE dbo.WH_InventoryTransaction SET TransactionType=CASE UPPER(TransactionType)
        WHEN 'IN' THEN 'IN' WHEN 'RECEIVE' THEN 'IN'
        WHEN 'OUT' THEN 'OUT' WHEN 'ISSUE' THEN 'OUT' WHEN 'CANCEL' THEN 'OUT'
        ELSE 'ADJ' END WHERE TransactionType NOT IN('IN','OUT','ADJ');
    IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction') AND name=N'IX_WH_InventoryTransaction_Search')
        DROP INDEX IX_WH_InventoryTransaction_Search ON dbo.WH_InventoryTransaction;
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'ItemNo') IS NOT NULL ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN ItemNo;
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LocationID') IS NOT NULL ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN LocationID;
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LotID') IS NOT NULL ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN LotID;
    ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN LotNo nvarchar(50) NOT NULL;
    ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyBefore decimal(18,3) NULL;
    ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyChange decimal(18,3) NOT NULL;
    ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyAfter decimal(18,3) NULL;
    CREATE INDEX IX_WH_InventoryTransaction_Search ON dbo.WH_InventoryTransaction(TransactionType,PartNo,LocationNo,LotNo);

    IF EXISTS
       (SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction')
          AND name=N'CK_WH_InventoryTransaction_Type')
        ALTER TABLE dbo.WH_InventoryTransaction DROP CONSTRAINT CK_WH_InventoryTransaction_Type;

    ALTER TABLE dbo.WH_InventoryTransaction WITH CHECK
        ADD CONSTRAINT CK_WH_InventoryTransaction_Type
        CHECK (TransactionType IN ('IN','OUT','ADJ'));

    /* Preserve warehouse and area names in common codes. */
    IF OBJECT_ID(N'dbo.WH_WarehouseMaster', N'U') IS NOT NULL
    BEGIN
        MERGE dbo.MD_CodeItem AS T
        USING
        (
            SELECT
                LEFT(CONCAT('WH_CODE_', WhCode), 41) AS CodeID,
                LEFT(WhCode, 20) AS CodeValue,
                COALESCE(NULLIF(WhName, N''), WhCode) AS CodeName,
                ROW_NUMBER() OVER (ORDER BY WhCode) * 10 AS SortOrder,
                CAST(COALESCE(ActiveFlag, 1) AS bit) AS UseFlag
            FROM dbo.WH_WarehouseMaster
        ) AS S
        ON T.CodeID = S.CodeID
        WHEN MATCHED THEN UPDATE SET
            T.GroupCode = 'WH_CODE', T.CodeValue = S.CodeValue,
            T.CodeName = S.CodeName, T.SortOrder = S.SortOrder,
            T.UseFlag = S.UseFlag, T.ModifiedBy = 'wh-core', T.ModifiedTS = SYSDATETIME()
        WHEN NOT MATCHED THEN INSERT
            (CodeID, GroupCode, CodeValue, CodeName, SortOrder, UseFlag, CreatedBy, CreatedTS)
        VALUES
            (S.CodeID, 'WH_CODE', S.CodeValue, S.CodeName, S.SortOrder, S.UseFlag, 'wh-core', SYSDATETIME());
    END;

    IF OBJECT_ID(N'dbo.WH_AreaMaster', N'U') IS NOT NULL
    BEGIN
        MERGE dbo.MD_CodeItem AS T
        USING
        (
            SELECT
                LEFT(CONCAT('WH_AREA_', A.AreaCode), 41) AS CodeID,
                LEFT(A.AreaCode, 20) AS CodeValue,
                COALESCE(NULLIF(A.AreaName, N''), A.AreaCode) AS CodeName,
                LEFT(CONCAT('WH_CODE_', A.WhCode), 41) AS ParentCodeID,
                ROW_NUMBER() OVER (ORDER BY A.WhCode, A.AreaCode) * 10 AS SortOrder,
                CAST(COALESCE(A.ActiveFlag, 1) AS bit) AS UseFlag
            FROM dbo.WH_AreaMaster A
        ) AS S
        ON T.CodeID = S.CodeID
        WHEN MATCHED THEN UPDATE SET
            T.GroupCode = 'WH_AREA', T.CodeValue = S.CodeValue,
            T.CodeName = S.CodeName, T.ParentCodeID = S.ParentCodeID,
            T.SortOrder = S.SortOrder, T.UseFlag = S.UseFlag,
            T.ModifiedBy = 'wh-core', T.ModifiedTS = SYSDATETIME()
        WHEN NOT MATCHED THEN INSERT
            (CodeID, GroupCode, CodeValue, CodeName, ParentCodeID, SortOrder, UseFlag, CreatedBy, CreatedTS)
        VALUES
            (S.CodeID, 'WH_AREA', S.CodeValue, S.CodeName, S.ParentCodeID,
             S.SortOrder, S.UseFlag, 'wh-core', SYSDATETIME());
    END;

    IF OBJECT_ID(N'dbo.WH_AreaSection', N'U') IS NOT NULL
    BEGIN
        MERGE dbo.MD_CodeItem AS T
        USING
        (
            SELECT
                LEFT(CONCAT('WH_ZONE_', SectionCode), 41) AS CodeID,
                LEFT(SectionCode, 20) AS CodeValue,
                MAX(COALESCE(NULLIF(SectionName, N''), SectionCode)) AS CodeName,
                MIN(CAST(COALESCE(ActiveFlag, 1) AS int)) AS UseFlag,
                ROW_NUMBER() OVER (ORDER BY SectionCode) * 10 AS SortOrder
            FROM dbo.WH_AreaSection
            GROUP BY SectionCode
        ) AS S
        ON T.CodeID = S.CodeID
        WHEN MATCHED THEN UPDATE SET
            T.GroupCode = 'WH_ZONE', T.CodeValue = S.CodeValue,
            T.CodeName = S.CodeName, T.SortOrder = S.SortOrder,
            T.UseFlag = CONVERT(bit, S.UseFlag),
            T.ModifiedBy = 'wh-core', T.ModifiedTS = SYSDATETIME()
        WHEN NOT MATCHED THEN INSERT
            (CodeID, GroupCode, CodeValue, CodeName, SortOrder, UseFlag, CreatedBy, CreatedTS)
        VALUES
            (S.CodeID, 'WH_ZONE', S.CodeValue, S.CodeName, S.SortOrder,
             CONVERT(bit, S.UseFlag), 'wh-core', SYSDATETIME());
    END;

    /* Preserve any active legacy inventory row not already in the unified table. */
    IF OBJECT_ID(N'dbo.WH_OLD_Inventory', N'U') IS NOT NULL
    BEGIN
        ;WITH Legacy AS
        (
            SELECT
                COALESCE(NULLIF(L.LotCode COLLATE DATABASE_DEFAULT, N''),
                    CONCAT(N'LEGACY-WH-', RIGHT(REPLICATE('0', 10) + CONVERT(varchar(10), O.InventoryID), 10))) AS LotNo,
                COALESCE(O.ItemNo COLLATE DATABASE_DEFAULT, L.ItemNo COLLATE DATABASE_DEFAULT) AS PartNo,
                I.ItemName COLLATE DATABASE_DEFAULT AS PartName,
                O.LocationID COLLATE DATABASE_DEFAULT AS LocationNo,
                CONVERT(decimal(18,3), COALESCE(O.OnHandQty, 0)) AS Qty,
                COALESCE(O.LastReceivedAt, O.CreatedTS, SYSDATETIME()) AS ReceivedAt,
                COALESCE(O.CreatedTS, O.LastReceivedAt, SYSDATETIME()) AS CreatedAt,
                COALESCE(O.ModifiedTS, O.CreatedTS, O.LastReceivedAt, SYSDATETIME()) AS UpdatedAt,
                ROW_NUMBER() OVER
                (
                    PARTITION BY COALESCE(NULLIF(L.LotCode COLLATE DATABASE_DEFAULT, N''),
                        CONCAT(N'LEGACY-WH-', RIGHT(REPLICATE('0', 10) + CONVERT(varchar(10), O.InventoryID), 10)))
                    ORDER BY O.InventoryID DESC
                ) AS RN
            FROM dbo.WH_OLD_Inventory O
            LEFT JOIN dbo.tbl_Lot L ON L.LotID = O.LotID
            LEFT JOIN dbo.MD_Item I
              ON I.ItemNo COLLATE DATABASE_DEFAULT = COALESCE(O.ItemNo COLLATE DATABASE_DEFAULT, L.ItemNo COLLATE DATABASE_DEFAULT)
            WHERE COALESCE(O.OnHandQty, 0) > 0
              AND UPPER(COALESCE(O.Status, 'RECEIVED')) NOT IN
                  ('CANCELED', 'CANCELLED', 'RELEASED', 'PICKED', 'SHIPPED', 'DELIVERED', 'CLOSED')
        )
        MERGE dbo.WH_Inventory AS T
        USING (SELECT * FROM Legacy WHERE RN = 1) AS S
        ON T.LotNo COLLATE DATABASE_DEFAULT = S.LotNo COLLATE DATABASE_DEFAULT
        WHEN MATCHED THEN UPDATE SET
            T.PartNo = COALESCE(T.PartNo, S.PartNo),
            T.PartName = COALESCE(T.PartName, S.PartName),
            T.LocationNo = COALESCE(T.LocationNo, S.LocationNo),
            T.Qty = S.Qty,
            T.ReceivedAt = COALESCE(T.ReceivedAt, S.ReceivedAt),
            T.UpdatedAt = CASE WHEN T.UpdatedAt > S.UpdatedAt THEN T.UpdatedAt ELSE S.UpdatedAt END
        WHEN NOT MATCHED THEN INSERT
            (LotNo, UnitType, PartNo, PartName, LocationNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
        VALUES
            (S.LotNo, 'PART', S.PartNo, S.PartName, S.LocationNo, S.Qty,
             S.ReceivedAt, S.CreatedAt, S.UpdatedAt);
    END;

    /* Preserve legacy audit records exactly once. */
    IF OBJECT_ID(N'dbo.WH_TransactionHistory', N'U') IS NOT NULL
    BEGIN
        INSERT dbo.WH_InventoryTransaction
            (TransactionTime, TransactionType, PartNo, LocationNo, LotNo,
             QtyBefore, QtyChange, QtyAfter, ReasonCode, RefDocType, RefDocID,
             OperatorID, ApproverID, Note, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
        SELECT
            COALESCE(H.TxnTime, H.CreatedTS, SYSDATETIME()),
            CASE UPPER(COALESCE(NULLIF(H.TxnType, ''), 'ADJ'))
                WHEN 'RECEIVE' THEN 'IN'
                WHEN 'ISSUE' THEN 'OUT'
                WHEN 'ADJUST' THEN 'ADJ'
                ELSE LEFT(UPPER(COALESCE(NULLIF(H.TxnType, ''), 'ADJ')), 10)
            END,
            H.ItemNo, H.LocationID, COALESCE(L.LotCode,CONCAT(N'LEGACY-HISTORY-',H.TxnID)),
            H.QtyBefore, COALESCE(H.Delta, 0), H.QtyAfter, H.ReasonCode,
            'LEGACY_WH_TXN', CONVERT(int, H.TxnID), H.OperatorID, H.ApproverID,
            H.Note, LEFT(COALESCE(NULLIF(H.CreatedBy, ''), 'wh-core'), 20),
            COALESCE(H.CreatedTS, H.TxnTime, SYSDATETIME()), H.ModifiedBy, H.ModifiedTS
        FROM dbo.WH_TransactionHistory H
        LEFT JOIN dbo.tbl_Lot L ON L.LotID = H.LotID
        WHERE NOT EXISTS
        (
            SELECT 1 FROM dbo.WH_InventoryTransaction T
            WHERE T.RefDocType = 'LEGACY_WH_TXN' AND T.RefDocID = CONVERT(int, H.TxnID)
        );
    END;

    IF OBJECT_ID(N'dbo.WH_Receiving', N'U') IS NOT NULL
    BEGIN
        INSERT dbo.WH_InventoryTransaction
            (TransactionTime, TransactionType, PartNo, LocationNo, LotNo,
             QtyBefore, QtyChange, QtyAfter, ReasonCode, RefDocType, RefDocID,
             OperatorID, Note, CreatedBy, CreatedTS)
        SELECT
            COALESCE(R.ReceivedAt, R.CreatedTS, SYSDATETIME()), 'IN', R.ItemNo,
            R.LocationID, COALESCE(NULLIF(R.LotCode,N''),CONCAT(N'LEGACY-RECEIVE-',R.ReceivingID)), 0, COALESCE(R.ReceivedQty, 0), COALESCE(R.ReceivedQty, 0),
            'LEGACY_RECEIVE', 'LEGACY_RECEIVING', R.ReceivingID, R.ReceivedBy,
            R.ReceivingNo, LEFT(COALESCE(NULLIF(R.CreatedBy, ''), 'wh-core'), 20),
            COALESCE(R.CreatedTS, R.ReceivedAt, SYSDATETIME())
        FROM dbo.WH_Receiving R
        WHERE NOT EXISTS
        (
            SELECT 1 FROM dbo.WH_InventoryTransaction T
            WHERE T.RefDocType = 'LEGACY_RECEIVING' AND T.RefDocID = R.ReceivingID
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

/* Inbound source is SCM delivery data, with tbl_Lot as the local fallback. */
CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_DOCUMENT_INFO
    @ReceiveMode nvarchar(10),
    @DocumentBarcode nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
    DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@DocumentBarcode, N'')));
    DECLARE @NoteNumber nvarchar(50);

    IF @Mode NOT IN (N'LOCAL', N'CKD') THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;

    SELECT TOP (1) @NoteNumber = N.NoteNumber
    FROM dbo.SCM_DeliveryNote N
    LEFT JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
    LEFT JOIN dbo.SCM_Delivery D ON D.DeliveryID = ND.DeliveryID
    WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @Barcode
       OR D.DeliveryNumber COLLATE DATABASE_DEFAULT = @Barcode
    ORDER BY CASE WHEN N.NoteNumber COLLATE DATABASE_DEFAULT = @Barcode THEN 0 ELSE 1 END;

    IF @NoteNumber IS NULL
        THROW 51402, 'Barcode was not found in inbound source tables.', 1;
    IF EXISTS
    (
        SELECT 1 FROM dbo.SCM_DeliveryNote N
        JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
        JOIN dbo.SCM_Delivery D ON D.DeliveryID = ND.DeliveryID
        WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
          AND D.Status NOT IN ('Shipped', 'Received')
    )
        THROW 51404, 'Only shipped delivery notes can be received.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.SCM_DeliveryNote N
        JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
        JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID = ND.DeliveryID
        JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID = DL.DeliveryLineID AND B.ActiveFlag = 1
        WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
    )
        THROW 51405, 'The delivery note has no active box labels.', 1;

    SELECT
        @Mode AS RECEIVE_TYPE, N.NoteID AS INBOUND_DOCUMENT_ID,
        N.NoteNumber AS DOCUMENT_BARCODE, N.NoteNumber AS DOCUMENT_NO,
        N.VendorID AS VENDCD, COALESCE(V.VendorName, N.VendorID) AS VENDNM,
        CAST(NULL AS nvarchar(50)) AS CASE_NO,
        CAST(NULL AS nvarchar(50)) AS INVOICE_NO,
        CAST(NULL AS nvarchar(50)) AS CONTAINER_NO,
        MIN(D.ShipDate) AS SHIP_DATE, CAST(NULL AS date) AS PACK_DATE,
        MAX(D.DeliveryDate) AS DELI_DATE, MAX(D.DeliveryDate) AS ARRIV_DATE,
        COUNT(B.BoxID) AS TOTAL_BOXES,
        SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE 1 END) AS SCANNED_BOXES,
        CASE WHEN COUNT(B.BoxID) = SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE 1 END)
             THEN N'Y' ELSE N'N' END AS YN
    FROM dbo.SCM_DeliveryNote N
    JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
    JOIN dbo.SCM_Delivery D ON D.DeliveryID = ND.DeliveryID
    JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID = D.DeliveryID
    JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID = DL.DeliveryLineID AND B.ActiveFlag = 1
    LEFT JOIN dbo.MD_Vendor V ON V.VendorID = N.VendorID
    LEFT JOIN dbo.WH_Inventory W
      ON W.DeliveryNoteNo COLLATE DATABASE_DEFAULT = N.NoteNumber COLLATE DATABASE_DEFAULT
                                  AND W.LotNo = B.BoxNumber AND W.Qty > 0
    WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
    GROUP BY N.NoteID, N.NoteNumber, N.VendorID, V.VendorName;

    SELECT
        B.ItemNo AS PARTNO, MAX(B.ItemName) AS PARTNM,
        COUNT(B.BoxID) AS BOX_COUNT,
        SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE 1 END) AS SCAN_COUNT,
        SUM(B.Quantity) AS DELIVERED_QTY,
        SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE B.Quantity END) AS RECEIVED_QTY,
        SUM(CASE WHEN W.LotNo IS NULL THEN B.Quantity ELSE 0 END) AS REMAINING_QTY,
        MAX(B.UnitCode) AS UNIT,
        CASE WHEN COUNT(B.BoxID) = SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE 1 END)
             THEN N'Y' ELSE N'N' END AS YN
    FROM dbo.SCM_DeliveryNote N
    JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
    JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID = ND.DeliveryID
    JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID = DL.DeliveryLineID AND B.ActiveFlag = 1
    LEFT JOIN dbo.WH_Inventory W
      ON W.DeliveryNoteNo COLLATE DATABASE_DEFAULT = N.NoteNumber COLLATE DATABASE_DEFAULT
                                  AND W.LotNo = B.BoxNumber AND W.Qty > 0
    WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
    GROUP BY B.ItemNo
    ORDER BY B.ItemNo;

    SELECT
        B.ItemNo AS PARTNO, B.BoxNumber AS BOX_BARCODE,
        COALESCE(NULLIF(DL.VendorLotNo, N''), B.BoxNumber) AS LOTNO,
        B.Quantity AS QTY, B.UnitCode AS UNIT,
        CASE WHEN W.LotNo IS NULL THEN N'N' ELSE N'Y' END AS YN
    FROM dbo.SCM_DeliveryNote N
    JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
    JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID = ND.DeliveryID
    JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID = DL.DeliveryLineID AND B.ActiveFlag = 1
    LEFT JOIN dbo.WH_Inventory W
      ON W.DeliveryNoteNo COLLATE DATABASE_DEFAULT = N.NoteNumber COLLATE DATABASE_DEFAULT
                                  AND W.LotNo = B.BoxNumber AND W.Qty > 0
    WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
    ORDER BY B.ItemNo, B.BoxSeq;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_SCAN_LOT
    @ReceiveMode nvarchar(10),
    @LotBarcode nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
    DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));

    IF @Mode NOT IN (N'LOCAL', N'CKD') THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N'' THROW 51401, 'Barcode is required.', 1;

    IF EXISTS (SELECT 1 FROM dbo.SCM_DeliveryBox WHERE BoxNumber = @Barcode AND ActiveFlag = 1)
    BEGIN
        IF NOT EXISTS
        (
            SELECT 1 FROM dbo.SCM_DeliveryBox B
            JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID = B.DeliveryLineID
            JOIN dbo.SCM_Delivery D ON D.DeliveryID = DL.DeliveryID
            WHERE B.BoxNumber = @Barcode AND B.ActiveFlag = 1
              AND D.Status IN ('Shipped', 'Received')
        )
            THROW 51404, 'Only boxes from a shipped delivery note can be received.', 1;

        SELECT TOP (1)
            @Mode AS RECEIVE_TYPE, CASE WHEN W.LotNo IS NULL THEN N'Y' ELSE N'N' END AS YN,
            COALESCE(NULLIF(DL.VendorLotNo, N''), B.BoxNumber) AS LOTNO,
            B.BoxNumber AS BARCODE, N'dbo.SCM_DeliveryBox' AS SOURCE_TABLE,
            N.NoteNumber AS NOTENO, CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
            CAST(NULL AS nvarchar(50)) AS CASE_NO, PO.PoNumber AS INVOICE_NO,
            CAST(NULL AS nvarchar(50)) AS CONTAINER_NO,
            B.ItemNo AS PARTNO, B.ItemName AS PARTNM,
            COALESCE(W.Qty, B.Quantity) AS QTY, B.UnitCode AS UNIT,
            PO.PoNumber AS PONO, PO.PoLineNo AS PONO_SEQ,
            D.VendorID AS VENDCD, COALESCE(V.VendorName, D.VendorID) AS VENDNM,
            DL.ProductionDate AS PROD_DATE, D.DeliveryDate AS DELI_DATE,
            D.DeliveryDate AS ARRIV_DATE, D.ShipDate AS SHIP_DATE,
            CAST(NULL AS date) AS PACK_DATE, W.LocationNo AS RECEIVED_LOCATION,
            CASE WHEN W.LotNo IS NULL THEN N'Open' ELSE N'Received' END AS RECEIVED_STATUS
        FROM dbo.SCM_DeliveryBox B
        JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID = B.DeliveryLineID
        JOIN dbo.SCM_Delivery D ON D.DeliveryID = DL.DeliveryID
        JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID = D.DeliveryID
        JOIN dbo.SCM_DeliveryNote N ON N.NoteID = ND.NoteID
        LEFT JOIN dbo.WH_PurchaseOrder PO ON PO.PoID = DL.PoID
        LEFT JOIN dbo.MD_Vendor V ON V.VendorID = D.VendorID
        LEFT JOIN dbo.WH_Inventory W ON W.LotNo = B.BoxNumber AND W.Qty > 0
        WHERE B.BoxNumber = @Barcode AND B.ActiveFlag = 1
        ORDER BY N.NoteID DESC;
        RETURN;
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode COLLATE DATABASE_DEFAULT = @Barcode)
        THROW 51402, 'Barcode was not found in inbound source tables.', 1;

    SELECT TOP (1)
        @Mode AS RECEIVE_TYPE, CASE WHEN W.LotNo IS NULL THEN N'Y' ELSE N'N' END AS YN,
        L.LotCode AS LOTNO, L.LotCode AS BARCODE, N'dbo.tbl_Lot' AS SOURCE_TABLE,
        PO.PoNumber AS NOTENO, CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
        CAST(NULL AS nvarchar(50)) AS CASE_NO, PO.PoNumber AS INVOICE_NO,
        CAST(NULL AS nvarchar(50)) AS CONTAINER_NO,
        L.ItemNo AS PARTNO, I.ItemName AS PARTNM,
        COALESCE(W.Qty, NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), PO.OrderQty, 0) AS QTY,
        COALESCE(PO.UnitCode, I.DefaultUOM, 'EA') AS UNIT,
        PO.PoNumber AS PONO, PO.PoLineNo AS PONO_SEQ,
        PO.VendorID AS VENDCD, COALESCE(V.VendorName, PO.VendorID) AS VENDNM,
        CONVERT(date, L.ProducedAt) AS PROD_DATE, PO.DueDate AS DELI_DATE,
        PO.DueDate AS ARRIV_DATE, CAST(NULL AS date) AS SHIP_DATE,
        CAST(NULL AS date) AS PACK_DATE, W.LocationNo AS RECEIVED_LOCATION,
        CASE WHEN W.LotNo IS NULL THEN N'Open' ELSE N'Received' END AS RECEIVED_STATUS
    FROM dbo.tbl_Lot L
    LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT = L.ItemNo COLLATE DATABASE_DEFAULT
    OUTER APPLY
    (
        SELECT TOP (1) P.* FROM dbo.WH_PurchaseOrder P
        WHERE P.ItemNo COLLATE DATABASE_DEFAULT = L.ItemNo COLLATE DATABASE_DEFAULT
        ORDER BY CASE WHEN COALESCE(P.OrderQty,0) > COALESCE(P.ReceivedQty,0) THEN 0 ELSE 1 END,
                 P.DueDate, P.PoID
    ) PO
    LEFT JOIN dbo.MD_Vendor V ON V.VendorID = PO.VendorID
    LEFT JOIN dbo.WH_Inventory W ON W.LotNo COLLATE DATABASE_DEFAULT = L.LotCode COLLATE DATABASE_DEFAULT AND W.Qty > 0
    WHERE L.LotCode COLLATE DATABASE_DEFAULT = @Barcode
    ORDER BY L.LotID DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_RECEIVE_LOT
    @ReceiveMode nvarchar(10),
    @LotBarcode nvarchar(50),
    @LocationId nvarchar(30) = NULL,
    @UserId nvarchar(40),
    @SimulateFailure bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
    DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));
    DECLARE @Location varchar(50) = NULLIF(LTRIM(RTRIM(@LocationId)), N'');
    DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @PartNo varchar(50), @PartName nvarchar(400), @Qty decimal(18,3),
            @Unit varchar(20), @InvoiceNo nvarchar(100), @DeliveryNoteNo nvarchar(60),
            @PoID int, @LotID int, @UnitType varchar(10) = 'PART';

    IF @Mode NOT IN (N'LOCAL', N'CKD') THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;
    IF @Barcode = N'' THROW 51401, 'Barcode is required.', 1;
    IF @Location IS NOT NULL AND NOT EXISTS
       (SELECT 1 FROM dbo.MD_Location WHERE LocationID = @Location AND COALESCE(ActiveFlag,1)=1)
        THROW 51406, 'Location barcode was not found.', 1;

    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.WH_Inventory WITH (UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode AND Qty>0)
        THROW 51407, 'This barcode has already been received.', 1;

    IF EXISTS (SELECT 1 FROM dbo.SCM_DeliveryBox WHERE BoxNumber=@Barcode AND ActiveFlag=1)
    BEGIN
        SELECT TOP (1)
            @PartNo=B.ItemNo, @PartName=B.ItemName, @Qty=B.Quantity, @Unit=B.UnitCode,
            @PoID=DL.PoID, @InvoiceNo=PO.PoNumber, @DeliveryNoteNo=N.NoteNumber,
            @UnitType='BOX'
        FROM dbo.SCM_DeliveryBox B
        JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID=B.DeliveryLineID
        JOIN dbo.SCM_Delivery D ON D.DeliveryID=DL.DeliveryID
        JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID=D.DeliveryID
        JOIN dbo.SCM_DeliveryNote N ON N.NoteID=ND.NoteID
        LEFT JOIN dbo.WH_PurchaseOrder PO ON PO.PoID=DL.PoID
        WHERE B.BoxNumber=@Barcode AND B.ActiveFlag=1 AND D.Status IN ('Shipped','Received')
        ORDER BY N.NoteID DESC;

        IF @PartNo IS NULL THROW 51404, 'Only boxes from a shipped delivery note can be received.', 1;
    END
    ELSE
    BEGIN
        SELECT TOP (1)
            @LotID=L.LotID, @PartNo=L.ItemNo, @PartName=I.ItemName,
            @Qty=COALESCE(NULLIF(L.RemainingQty,0),NULLIF(L.BatchSize,0),0),
            @Unit=I.DefaultUOM
        FROM dbo.tbl_Lot L
        LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
        WHERE L.LotCode=@Barcode ORDER BY L.LotID DESC;
        IF @LotID IS NULL THROW 51402, 'Barcode was not found in inbound source tables.', 1;

        SELECT TOP (1) @PoID=P.PoID,@InvoiceNo=P.PoNumber,@Unit=COALESCE(@Unit,P.UnitCode)
        FROM dbo.WH_PurchaseOrder P
        WHERE P.ItemNo=@PartNo
        ORDER BY CASE WHEN COALESCE(P.OrderQty,0)>COALESCE(P.ReceivedQty,0) THEN 0 ELSE 1 END,
                 P.DueDate,P.PoID;
    END;

    INSERT dbo.WH_Inventory
        (LotNo,UnitType,PartNo,PartName,LocationNo,Qty,InvoiceNo,ReceivedAt,CreatedAt,UpdatedAt,DeliveryNoteNo)
    VALUES
        (@Barcode,@UnitType,@PartNo,@PartName,@Location,@Qty,@InvoiceNo,
         SYSDATETIME(),SYSDATETIME(),SYSDATETIME(),@DeliveryNoteNo);

    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,
         QtyBefore,QtyChange,QtyAfter,ReasonCode,RefDocType,RefDocID,
         OperatorID,Note,CreatedBy,CreatedTS)
    VALUES
        (SYSDATETIME(),'IN',@PartNo,@Location,@Barcode,
         0,@Qty,@Qty,'INBOUND',CASE WHEN @DeliveryNoteNo IS NULL THEN 'LOT' ELSE 'DELIVERY_NOTE' END,
         @PoID,@User,COALESCE(@DeliveryNoteNo,@InvoiceNo),LEFT(@User,20),SYSDATETIME());

    IF @PoID IS NOT NULL
        UPDATE dbo.WH_PurchaseOrder
           SET ReceivedQty=COALESCE(ReceivedQty,0)+@Qty,
               Status=CASE WHEN COALESCE(ReceivedQty,0)+@Qty>=COALESCE(OrderQty,0) THEN 'Received' ELSE 'Partial' END,
               ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME()
         WHERE PoID=@PoID;

    IF @LotID IS NOT NULL
        UPDATE dbo.tbl_Lot
           SET RemainingQty=@Qty,CurrentLocationID=COALESCE(@Location,CurrentLocationID),
               InventoryStatus=CASE WHEN @Location IS NULL THEN 'RECEIVED' ELSE 'STORED' END,
               ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME()
         WHERE LotID=@LotID;

    IF @DeliveryNoteNo IS NOT NULL
    BEGIN
        UPDATE DL
           SET ReceivedQty=X.ReceivedQty
        FROM dbo.SCM_DeliveryLine DL
        CROSS APPLY
        (
            SELECT COALESCE(SUM(B.Quantity),0) AS ReceivedQty
            FROM dbo.SCM_DeliveryBox B
            JOIN dbo.SCM_Delivery D ON D.DeliveryID=DL.DeliveryID
            JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID=D.DeliveryID
            JOIN dbo.SCM_DeliveryNote N ON N.NoteID=ND.NoteID
            JOIN dbo.WH_Inventory W
              ON W.LotNo COLLATE DATABASE_DEFAULT = B.BoxNumber COLLATE DATABASE_DEFAULT
             AND W.DeliveryNoteNo COLLATE DATABASE_DEFAULT = N.NoteNumber COLLATE DATABASE_DEFAULT
             AND W.Qty>0
            WHERE B.DeliveryLineID=DL.DeliveryLineID AND B.ActiveFlag=1 AND N.NoteNumber=@DeliveryNoteNo
        ) X
        WHERE EXISTS
        (
            SELECT 1 FROM dbo.SCM_Delivery D
            JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID=D.DeliveryID
            JOIN dbo.SCM_DeliveryNote N ON N.NoteID=ND.NoteID
            WHERE D.DeliveryID=DL.DeliveryID AND N.NoteNumber=@DeliveryNoteNo
        );

        UPDATE D SET Status=CASE WHEN EXISTS
        (
            SELECT 1 FROM dbo.SCM_DeliveryLine DL
            JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID=DL.DeliveryLineID AND B.ActiveFlag=1
            LEFT JOIN dbo.WH_Inventory W
              ON W.LotNo COLLATE DATABASE_DEFAULT = B.BoxNumber COLLATE DATABASE_DEFAULT
             AND W.Qty>0
            WHERE DL.DeliveryID=D.DeliveryID AND W.LotNo IS NULL
        ) THEN 'Shipped' ELSE 'Received' END,
        ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME()
        FROM dbo.SCM_Delivery D
        JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID=D.DeliveryID
        JOIN dbo.SCM_DeliveryNote N ON N.NoteID=ND.NoteID
        WHERE N.NoteNumber=@DeliveryNoteNo;
    END;

    IF @SimulateFailure=1 THROW 51420, 'Simulated Inbound API failure. Database transaction was rolled back.', 1;
    COMMIT TRANSACTION;
    EXEC dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode=@Mode,@LotBarcode=@Barcode;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_MOVE_LOCATION
    @ReceiveMode nvarchar(10), @LotBarcode nvarchar(50),
    @LocationId nvarchar(30), @UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Barcode nvarchar(50)=LTRIM(RTRIM(ISNULL(@LotBarcode,N'')));
    DECLARE @Location varchar(50)=NULLIF(LTRIM(RTRIM(@LocationId)),N'');
    DECLARE @User nvarchar(40)=COALESCE(NULLIF(LTRIM(RTRIM(@UserId)),N''),N'PDA');
    DECLARE @BeforeLocation varchar(50),@PartNo varchar(50),@Qty decimal(18,3),@LotID int;
    IF @Location IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.MD_Location WHERE LocationID=@Location AND COALESCE(ActiveFlag,1)=1)
        THROW 51406, 'Location barcode was not found.', 1;
    BEGIN TRANSACTION;
    SELECT @BeforeLocation=LocationNo,@PartNo=PartNo,@Qty=Qty FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode AND Qty>0;
    IF @PartNo IS NULL THROW 51408, 'This barcode has not been received yet.', 1;
    SELECT TOP(1) @LotID=LotID FROM dbo.tbl_Lot WHERE LotCode COLLATE DATABASE_DEFAULT=@Barcode ORDER BY LotID DESC;
    UPDATE dbo.WH_Inventory SET LocationNo=@Location,UpdatedAt=SYSDATETIME() WHERE LotNo=@Barcode;
    IF @LotID IS NOT NULL UPDATE dbo.tbl_Lot SET CurrentLocationID=@Location,InventoryStatus='STORED',ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME() WHERE LotID=@LotID;
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,ReasonCode,RefDocType,OperatorID,Note,CreatedBy,CreatedTS)
    VALUES(SYSDATETIME(),'ADJ',@PartNo,@Location,@Barcode,@Qty,0,@Qty,'PUT_AWAY','LOT',@User,
           CONCAT('Moved from ',COALESCE(@BeforeLocation,'(unassigned)'),' to ',@Location),LEFT(@User,20),SYSDATETIME());
    COMMIT TRANSACTION;
    EXEC dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode=@ReceiveMode,@LotBarcode=@Barcode;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INBOUND_CANCEL_RECEIPT
    @ReceiveMode nvarchar(10), @LotBarcode nvarchar(50), @UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Barcode nvarchar(50)=LTRIM(RTRIM(ISNULL(@LotBarcode,N'')));
    DECLARE @User nvarchar(40)=COALESCE(NULLIF(LTRIM(RTRIM(@UserId)),N''),N'PDA');
    DECLARE @PartNo varchar(50),@Location varchar(50),@Qty decimal(18,3),@Invoice nvarchar(100),@DeliveryNote nvarchar(60),@LotID int,@PoID int;
    BEGIN TRANSACTION;
    SELECT @PartNo=PartNo,@Location=LocationNo,@Qty=Qty,@Invoice=InvoiceNo,@DeliveryNote=DeliveryNoteNo
    FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode AND Qty>0;
    IF @PartNo IS NULL THROW 51409, 'This barcode has not been received.', 1;
    SELECT TOP(1) @LotID=LotID FROM dbo.tbl_Lot WHERE LotCode=@Barcode ORDER BY LotID DESC;
    IF @DeliveryNote IS NOT NULL
        SELECT TOP(1) @PoID=DL.PoID
        FROM dbo.SCM_DeliveryBox B
        JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID=B.DeliveryLineID
        WHERE B.BoxNumber=@Barcode AND B.ActiveFlag=1;
    ELSE
        SELECT TOP(1) @PoID=PoID
        FROM dbo.WH_PurchaseOrder
        WHERE PoNumber=@Invoice AND ItemNo=@PartNo
        ORDER BY PoID DESC;
    DELETE dbo.WH_Inventory WHERE LotNo=@Barcode;
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,ReasonCode,RefDocType,RefDocID,OperatorID,Note,CreatedBy,CreatedTS)
    VALUES(SYSDATETIME(),'OUT',@PartNo,@Location,@Barcode,@Qty,-@Qty,0,'INBOUND_CANCEL',
           CASE WHEN @DeliveryNote IS NULL THEN 'LOT' ELSE 'DELIVERY_NOTE' END,@PoID,@User,'Inbound receipt canceled',LEFT(@User,20),SYSDATETIME());
    IF @PoID IS NOT NULL
        UPDATE PO
           SET ReceivedQty=X.NewReceivedQty,
               Status=CASE
                   WHEN X.NewReceivedQty<=0 THEN 'Open'
                   WHEN X.NewReceivedQty>=COALESCE(PO.OrderQty,0) THEN 'Received'
                   ELSE 'Partial'
               END,
               ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME()
        FROM dbo.WH_PurchaseOrder PO
        CROSS APPLY(VALUES(CASE WHEN COALESCE(PO.ReceivedQty,0)<@Qty THEN 0 ELSE PO.ReceivedQty-@Qty END)) X(NewReceivedQty)
        WHERE PO.PoID=@PoID;
    IF @LotID IS NOT NULL UPDATE dbo.tbl_Lot SET CurrentLocationID=NULL,InventoryStatus='CREATED',ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME() WHERE LotID=@LotID;
    IF @DeliveryNote IS NOT NULL
    BEGIN
        UPDATE DL SET ReceivedQty=X.ReceivedQty
        FROM dbo.SCM_DeliveryLine DL
        CROSS APPLY(SELECT COALESCE(SUM(B.Quantity),0) ReceivedQty FROM dbo.SCM_DeliveryBox B
          JOIN dbo.SCM_Delivery D ON D.DeliveryID=DL.DeliveryID
          JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID=D.DeliveryID
          JOIN dbo.SCM_DeliveryNote N ON N.NoteID=ND.NoteID
          JOIN dbo.WH_Inventory W ON W.LotNo=B.BoxNumber
            AND W.DeliveryNoteNo COLLATE DATABASE_DEFAULT=N.NoteNumber COLLATE DATABASE_DEFAULT AND W.Qty>0
          WHERE B.DeliveryLineID=DL.DeliveryLineID AND B.ActiveFlag=1
            AND N.NoteNumber COLLATE DATABASE_DEFAULT=@DeliveryNote) X
        WHERE EXISTS(SELECT 1 FROM dbo.SCM_Delivery D JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID=D.DeliveryID
          JOIN dbo.SCM_DeliveryNote N ON N.NoteID=ND.NoteID WHERE D.DeliveryID=DL.DeliveryID
            AND N.NoteNumber COLLATE DATABASE_DEFAULT=@DeliveryNote);
        UPDATE D SET Status='Shipped',ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME()
        FROM dbo.SCM_Delivery D JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID=D.DeliveryID
        JOIN dbo.SCM_DeliveryNote N ON N.NoteID=ND.NoteID
        WHERE N.NoteNumber COLLATE DATABASE_DEFAULT=@DeliveryNote;
    END;
    COMMIT TRANSACTION;
    EXEC dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode=@ReceiveMode,@LotBarcode=@Barcode;
END;
GO

/* Warehouse inventory queries now read the unified LOT table only. */
CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_STATUS_LIST
    @SearchText nvarchar(80)=NULL,@StockDateFrom date=NULL,@StockDateTo date=NULL,@AreaCode nvarchar(20)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Q nvarchar(80)=NULLIF(LTRIM(RTRIM(@SearchText)),N'');
    ;WITH S AS
    (
        SELECT W.PartNo,MAX(COALESCE(NULLIF(W.PartName,N''),I.ItemName COLLATE DATABASE_DEFAULT)) PartName,
            SUM(W.Qty) SumQty,MAX(W.ReceivedAt) LastReceivedDate,
            COUNT(DISTINCT W.LotNo) LotCount,COUNT(DISTINCT W.LocationNo) LocationCount,
            MAX(I.CarType) CarType,MAX(COALESCE(I.DefaultUOM,'EA')) Unit,
            MAX(COALESCE(I.MinStock,0)) MinQty,MAX(COALESCE(I.MaxStock,0)) MaxQty
        FROM dbo.WH_Inventory W
        LEFT JOIN dbo.MD_Item I
          ON I.ItemNo COLLATE DATABASE_DEFAULT = W.PartNo COLLATE DATABASE_DEFAULT
        LEFT JOIN dbo.MD_Location L
          ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
        WHERE W.Qty>0 AND W.PartNo IS NOT NULL AND COALESCE(L.AreaCode,'')<>'FG_AREA'
          AND (@AreaCode IS NULL OR L.AreaCode=@AreaCode)
          AND (@StockDateFrom IS NULL OR CONVERT(date,W.ReceivedAt)>=@StockDateFrom)
          AND (@StockDateTo IS NULL OR CONVERT(date,W.ReceivedAt)<=@StockDateTo)
          AND (@Q IS NULL OR W.PartNo LIKE N'%'+@Q+N'%' OR W.PartName LIKE N'%'+@Q+N'%'
               OR W.LotNo LIKE N'%'+@Q+N'%' OR W.LocationNo LIKE N'%'+@Q+N'%'
               OR I.ItemName LIKE N'%'+@Q+N'%')
        GROUP BY W.PartNo
    )
    SELECT ROW_NUMBER() OVER(ORDER BY S.PartNo) INVENTORY_ID,S.PartNo PARTNO,S.PartName PARTNM,
        P.LotNo LOTNO,COALESCE(P.LocationNo,N'-') PRIMARY_LOCATION,S.SumQty SUM_QTY,
        CAST(0 AS decimal(18,3)) RESERVED_QTY,S.LastReceivedDate LAST_RECEIVED_DATE,
        S.CarType VINCD,S.Unit UNIT,CAST(NULL AS decimal(18,3)) MIN_INV_DAY,S.MinQty MIN_INV_QTY,
        CAST(NULL AS decimal(18,3)) MAX_INV_DAY,S.MaxQty MAX_INV_QTY,S.LotCount LOT_COUNT,
        S.LocationCount LOCATION_COUNT,
        CASE WHEN S.MinQty>0 AND S.SumQty<S.MinQty THEN N'BELOW_MIN'
             WHEN S.MaxQty>0 AND S.SumQty>S.MaxQty THEN N'OVER_MAX' ELSE N'NORMAL' END STATUS,
        CASE WHEN S.MinQty>0 AND S.SumQty<S.MinQty THEN N'Below Min'
             WHEN S.MaxQty>0 AND S.SumQty>S.MaxQty THEN N'Over Max' ELSE N'Normal' END STATUSNM
    FROM S
    OUTER APPLY(SELECT TOP(1) W.LotNo,W.LocationNo FROM dbo.WH_Inventory W
      WHERE W.PartNo COLLATE DATABASE_DEFAULT = S.PartNo COLLATE DATABASE_DEFAULT
        AND W.Qty>0 ORDER BY W.ReceivedAt,W.LotNo) P
    ORDER BY S.PartNo;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_LOCATION_LIST
    @ItemNo nvarchar(40),@StockDateFrom date=NULL,@StockDateTo date=NULL,@AreaCode nvarchar(20)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ROW_NUMBER() OVER(ORDER BY COALESCE(L.LocationID COLLATE DATABASE_DEFAULT,W.LocationNo,N'-')) ROW_NO,
        W.PartNo PARTNO,COALESCE(L.LocationID COLLATE DATABASE_DEFAULT,W.LocationNo,N'-') LOCATION_NO,L.LocationName LOCATION_NM,
        L.WhCode WHCD,COALESCE(WC.CodeName,L.WhCode) WHNM,L.AreaCode AREACD,
        COALESCE(AC.CodeName,L.AreaCode) AREANM,L.ZoneCode ZONECD,L.ZoneCode ZONENM,
        L.Aisle RACK_X,L.Bay RACK_Y,L.Slot RACK_Z,SUM(W.Qty) SUM_QTY
    FROM dbo.WH_Inventory W
    LEFT JOIN dbo.MD_Location L
      ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
    LEFT JOIN dbo.MD_CodeItem WC ON WC.GroupCode='WH_CODE' AND WC.CodeValue=L.WhCode
    LEFT JOIN dbo.MD_CodeItem AC ON AC.GroupCode='WH_AREA' AND AC.CodeValue=L.AreaCode
    WHERE W.PartNo=@ItemNo AND W.Qty>0 AND COALESCE(L.AreaCode,'')<>'FG_AREA'
      AND (@AreaCode IS NULL OR L.AreaCode=@AreaCode)
      AND (@StockDateFrom IS NULL OR CONVERT(date,W.ReceivedAt)>=@StockDateFrom)
      AND (@StockDateTo IS NULL OR CONVERT(date,W.ReceivedAt)<=@StockDateTo)
    GROUP BY W.PartNo,W.LocationNo,L.LocationID,L.LocationName,L.WhCode,WC.CodeName,
      L.AreaCode,AC.CodeName,L.ZoneCode,L.Aisle,L.Bay,L.Slot
    ORDER BY COALESCE(L.LocationID COLLATE DATABASE_DEFAULT,W.LocationNo,N'-');
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_LOCATION_CONTENTS
    @LocationId nvarchar(40),@StockDateFrom date=NULL,@StockDateTo date=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT W.LotNo LOTNO,W.PartNo PARTNO,
        COALESCE(NULLIF(W.PartName,N''),I.ItemName COLLATE DATABASE_DEFAULT) PARTNM,
        W.Qty QTY,COALESCE(I.DefaultUOM,'EA') UNIT,N'RECEIVED' INV_STATUS,
        CONVERT(nvarchar(10),W.ReceivedAt,23) WORK_DATE,CONVERT(nvarchar(8),W.ReceivedAt,108) WORK_TIME
    FROM dbo.WH_Inventory W
    LEFT JOIN dbo.MD_Item I
      ON I.ItemNo COLLATE DATABASE_DEFAULT = W.PartNo COLLATE DATABASE_DEFAULT
    WHERE UPPER(COALESCE(W.LocationNo,N''))=UPPER(LTRIM(RTRIM(@LocationId))) AND W.Qty>0
      AND (@StockDateFrom IS NULL OR CONVERT(date,W.ReceivedAt)>=@StockDateFrom)
      AND (@StockDateTo IS NULL OR CONVERT(date,W.ReceivedAt)<=@StockDateTo)
    ORDER BY W.PartNo,W.ReceivedAt,W.LotNo;
END;
GO

/* Pick Slip replaces Release Schedule; pick history is WH_InventoryTransaction. */
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_SLIP_STATUS @PickSlipNo nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Slip nvarchar(40)=UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo,N''))));
    SELECT @Slip PICK_SLIPNO,CASE WHEN COUNT(*)>0 THEN 1 ELSE 0 END EXISTS_FLAG,
      CASE WHEN COUNT(*)>0 AND SUM(CASE WHEN UPPER(COALESCE(Status,'OPEN')) IN('CLOSED','RELEASED','CANCELED','CANCELLED') THEN 1 ELSE 0 END)=COUNT(*) THEN 1 ELSE 0 END IS_CLOSED,
      COUNT(*) LINE_COUNT,MIN(ReqLocation) REQ_LOCATION,CONVERT(date,MAX(RequiredAt)) REQ_DATE,
      MAX(CloseDate) CLOSE_DATE,
      CASE WHEN COUNT(*)=0 THEN N'Pick Slip was not found.'
           WHEN SUM(CASE WHEN UPPER(COALESCE(Status,'OPEN')) IN('CLOSED','RELEASED','CANCELED','CANCELLED') THEN 1 ELSE 0 END)=COUNT(*) THEN N'Pick Slip is already closed.'
           ELSE N'Pick Slip is ready.' END MESSAGE
    FROM dbo.WH_PickSlip
    WHERE UPPER(COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID)))=@Slip;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_PICK_LINES @PickSlipNo nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Slip nvarchar(40)=UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo,N''))));
    ;WITH R AS
    (
      SELECT COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID)) PickSlipNo,ItemNo,
        SUM(COALESCE(DemandQty,0)) DemandQty,MAX(ReqUserId) ReqUserId,MAX(Status) Status
      FROM dbo.WH_PickSlip WHERE UPPER(COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID)))=@Slip
      GROUP BY COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID)),ItemNo
    ),P AS
    (
      SELECT T.PartNo,COUNT(DISTINCT T.LotNo) BoxQty,SUM(ABS(T.QtyChange)) PickedQty
      FROM dbo.WH_InventoryTransaction T JOIN dbo.WH_PickSlip S ON S.PickSlipID=T.RefDocID
      WHERE T.TransactionType='OUT' AND T.RefDocType='PICK_SLIP'
        AND UPPER(COALESCE(NULLIF(S.PickSlipNo,N''),CONCAT(N'RS-',S.PickSlipID)))=@Slip
      GROUP BY T.PartNo
    )
    SELECT R.PickSlipNo PICK_SLIPNO,R.ItemNo PARTNO,I.ItemName PARTNM,
      R.DemandQty REQ_BOX_QTY,COALESCE(P.BoxQty,0) PICKED_BOX_QTY,COALESCE(P.PickedQty,0) PICKED_QTY,
      R.ReqUserId REQ_USERID,L.LOC_01,L.LOC_02,L.LOC_03,R.Status STATUS
    FROM R LEFT JOIN dbo.MD_Item I ON I.ItemNo=R.ItemNo LEFT JOIN P ON P.ItemNo=R.ItemNo
    OUTER APPLY
    (
      SELECT MAX(CASE WHEN RN=1 THEN LocationNo END) LOC_01,
             MAX(CASE WHEN RN=2 THEN LocationNo END) LOC_02,
             MAX(CASE WHEN RN=3 THEN LocationNo END) LOC_03
      FROM(SELECT W.LocationNo,ROW_NUMBER() OVER(ORDER BY W.ReceivedAt,W.LotNo) RN
           FROM dbo.WH_Inventory W
           WHERE W.PartNo COLLATE DATABASE_DEFAULT = R.ItemNo COLLATE DATABASE_DEFAULT
             AND W.Qty>0) X WHERE RN<=3
    ) L
    ORDER BY R.ItemNo;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo nvarchar(40),@LotNo nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Slip nvarchar(40)=UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo,N''))));
    DECLARE @Lot nvarchar(50)=LTRIM(RTRIM(ISNULL(@LotNo,N'')));
    DECLARE @Item varchar(50),@Name nvarchar(400),@Location varchar(50),@Qty decimal(18,3),@Received datetime2,@Oldest nvarchar(100);
    SELECT @Item=W.PartNo,@Name=COALESCE(NULLIF(W.PartName,N''),I.ItemName COLLATE DATABASE_DEFAULT),@Location=W.LocationNo,@Qty=W.Qty,@Received=W.ReceivedAt
    FROM dbo.WH_Inventory W
    LEFT JOIN dbo.MD_Item I
      ON I.ItemNo COLLATE DATABASE_DEFAULT = W.PartNo COLLATE DATABASE_DEFAULT
    WHERE UPPER(W.LotNo)=UPPER(@Lot);
    IF @Item IS NULL
    BEGIN SELECT @Slip PICK_SLIPNO,@Lot LOTNO,NULL PARTNO,NULL PARTNM,CAST(0 AS decimal(18,3)) QTY,NULL UNIT,NULL LOCATION_NO,NULL LOCATION_NM,NULL ZONECD,NULL INV_STATUS,NULL PROD_DATE,NULL RCV_DATE,CAST(0 AS bit) IS_FIFO_SUGGESTED,CAST(0 AS bit) IS_VALID,N'LOT was not found.' MESSAGE; RETURN; END;
    IF COALESCE(@Qty,0)<=0
    BEGIN SELECT @Slip PICK_SLIPNO,@Lot LOTNO,@Item PARTNO,@Name PARTNM,@Qty QTY,I.DefaultUOM UNIT,@Location LOCATION_NO,L.LocationName LOCATION_NM,L.ZoneCode ZONECD,N'RELEASED' INV_STATUS,NULL PROD_DATE,CONVERT(nvarchar(20),@Received,23) RCV_DATE,CAST(0 AS bit) IS_FIFO_SUGGESTED,CAST(0 AS bit) IS_VALID,N'LOT is not available for release.' MESSAGE FROM dbo.MD_Item I LEFT JOIN dbo.MD_Location L ON L.LocationID=@Location WHERE I.ItemNo=@Item; RETURN; END;
    IF NOT EXISTS(SELECT 1 FROM dbo.WH_PickSlip WHERE UPPER(COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID)))=@Slip AND ItemNo=@Item AND UPPER(COALESCE(Status,'OPEN')) NOT IN('CLOSED','RELEASED','CANCELED','CANCELLED'))
    BEGIN SELECT @Slip PICK_SLIPNO,@Lot LOTNO,@Item PARTNO,@Name PARTNM,@Qty QTY,I.DefaultUOM UNIT,@Location LOCATION_NO,L.LocationName LOCATION_NM,L.ZoneCode ZONECD,N'STORED' INV_STATUS,NULL PROD_DATE,CONVERT(nvarchar(20),@Received,23) RCV_DATE,CAST(0 AS bit) IS_FIFO_SUGGESTED,CAST(0 AS bit) IS_VALID,N'Wrong item. This LOT is not requested by the selected Pick Slip.' MESSAGE FROM dbo.MD_Item I LEFT JOIN dbo.MD_Location L ON L.LocationID=@Location WHERE I.ItemNo=@Item; RETURN; END;
    SELECT TOP(1) @Oldest=LotNo FROM dbo.WH_Inventory WHERE PartNo=@Item AND Qty>0 ORDER BY ReceivedAt,LotNo;
    SELECT @Slip PICK_SLIPNO,@Lot LOTNO,@Item PARTNO,@Name PARTNM,@Qty QTY,I.DefaultUOM UNIT,@Location LOCATION_NO,L.LocationName LOCATION_NM,L.ZoneCode ZONECD,N'STORED' INV_STATUS,NULL PROD_DATE,CONVERT(nvarchar(20),@Received,23) RCV_DATE,
      CONVERT(bit,CASE WHEN UPPER(@Oldest)=UPPER(@Lot) THEN 1 ELSE 0 END) IS_FIFO_SUGGESTED,
      CONVERT(bit,CASE WHEN UPPER(@Oldest)=UPPER(@Lot) THEN 1 ELSE 0 END) IS_VALID,
      CASE WHEN UPPER(@Oldest)=UPPER(@Lot) THEN N'LOT is ready to pick.' ELSE CONCAT(N'FIFO violation. Pick LOT ',@Oldest,N' first.') END MESSAGE
    FROM dbo.MD_Item I LEFT JOIN dbo.MD_Location L ON L.LocationID=@Location WHERE I.ItemNo=@Item;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_PICK_LOT
    @PickSlipNo nvarchar(40),@LotNo nvarchar(50),@UserId nvarchar(80),@TerminalId nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @V table(PICK_SLIPNO nvarchar(40),LOTNO nvarchar(50),PARTNO varchar(50),PARTNM nvarchar(400),QTY decimal(18,3),UNIT varchar(20),LOCATION_NO varchar(50),LOCATION_NM nvarchar(120),ZONECD varchar(20),INV_STATUS varchar(20),PROD_DATE nvarchar(20),RCV_DATE nvarchar(20),IS_FIFO_SUGGESTED bit,IS_VALID bit,MESSAGE nvarchar(300));
    INSERT @V EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo=@PickSlipNo,@LotNo=@LotNo;
    IF NOT EXISTS(SELECT 1 FROM @V WHERE IS_VALID=1) BEGIN SELECT * FROM @V; RETURN; END;
    DECLARE @Part varchar(50),@Location varchar(50),@Qty decimal(18,3),@SlipID int,@User nvarchar(80)=COALESCE(NULLIF(@UserId,N''),N'PDA');
    SELECT @Part=PARTNO,@Location=LOCATION_NO,@Qty=QTY FROM @V WHERE IS_VALID=1;
    SELECT TOP(1) @SlipID=PickSlipID FROM dbo.WH_PickSlip WHERE UPPER(COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID)))=UPPER(@PickSlipNo) AND ItemNo=@Part ORDER BY PickSlipID;
    BEGIN TRANSACTION;
    UPDATE dbo.WH_Inventory SET Qty=0,UpdatedAt=SYSDATETIME() WHERE UPPER(LotNo)=UPPER(@LotNo) AND Qty=@Qty;
    IF @@ROWCOUNT<>1 THROW 51620,'LOT inventory changed before Release.',1;
    INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,ReasonCode,RefDocType,RefDocID,OperatorID,Note,CreatedBy,CreatedTS)
    VALUES(SYSDATETIME(),'OUT',@Part,@Location,@LotNo,@Qty,-@Qty,0,'RELEASE_PICK','PICK_SLIP',@SlipID,@User,CONCAT('PDA release pick ',@PickSlipNo),LEFT(@User,20),SYSDATETIME());
    UPDATE dbo.WH_PickSlip SET PickedQty=COALESCE(PickedQty,0)+1,
      Status=CASE WHEN COALESCE(PickedQty,0)+1>=COALESCE(DemandQty,0) THEN 'Closed' ELSE 'Partial' END,
      CloseDate=CASE WHEN COALESCE(PickedQty,0)+1>=COALESCE(DemandQty,0) THEN SYSDATETIME() ELSE CloseDate END,
      CloseUserId=CASE WHEN COALESCE(PickedQty,0)+1>=COALESCE(DemandQty,0) THEN @User ELSE CloseUserId END,
      ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME() WHERE PickSlipID=@SlipID;
    COMMIT TRANSACTION;
    SELECT PICK_SLIPNO,LOTNO,PARTNO,PARTNM,QTY,UNIT,LOCATION_NO,LOCATION_NM,ZONECD,N'RELEASED' INV_STATUS,PROD_DATE,RCV_DATE,IS_FIFO_SUGGESTED,IS_VALID,N'Release pick completed.' MESSAGE FROM @V;
END;
GO

/* FG Adjust must not depend on the retired WH_OLD_Inventory table. */
CREATE OR ALTER PROCEDURE dbo.FG_PDA_ADJUST_SCAN_STOCK @ScanText nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Scan nvarchar(80)=LTRIM(RTRIM(ISNULL(@ScanText,N''))),@StockID int;
    IF @Scan=N'' THROW 51600,'Finished goods Lot No is required.',1;
    SELECT TOP(1) @StockID=F.StockID FROM dbo.FG_Inventory F LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID
    WHERE (UPPER(COALESCE(L.LotCode,N''))=UPPER(@Scan) OR UPPER(COALESCE(F.StockNumber,N''))=UPPER(@Scan))
      AND UPPER(COALESCE(F.Status,N'Available')) NOT IN(N'CANCELED',N'CANCELLED',N'SHIPPED',N'DELIVERED',N'CLOSED')
    ORDER BY CASE WHEN COALESCE(F.Qty,0)>0 THEN 0 ELSE 1 END,F.StockID DESC;
    IF @StockID IS NULL AND EXISTS(SELECT 1 FROM dbo.WH_Inventory W LEFT JOIN dbo.MD_Location M
       ON M.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
       WHERE UPPER(W.LotNo)=UPPER(@Scan) AND W.Qty>0 AND COALESCE(M.AreaCode,'')<>'FG_AREA')
       THROW 51605,'Warehouse material cannot be adjusted in Finished Goods Adjust.',1;
    IF @StockID IS NULL THROW 51601,'The specified finished goods Lot No could not be found.',1;
    SELECT N'FG' RECEIVE_TYPE,N'N' YN,COALESCE(NULLIF(L.LotCode,N''),F.StockNumber) LOTNO,
      COALESCE(NULLIF(L.LotCode,N''),F.StockNumber) BARCODE,N'dbo.FG_Inventory' SOURCE_TABLE,
      NULL NOTENO,NULL CASE_BARCODE,NULL CASE_NO,NULL INVOICE_NO,NULL CONTAINER_NO,F.ItemNo PARTNO,I.ItemName PARTNM,
      COALESCE(F.Qty,0) QTY,I.DefaultUOM UNIT,NULL PONO,NULL PONO_SEQ,NULL VENDCD,NULL VENDNM,
      CONVERT(date,L.ProducedAt) PROD_DATE,NULL DELI_DATE,CONVERT(date,F.StockTS) ARRIV_DATE,NULL SHIP_DATE,NULL PACK_DATE,
      F.Location RECEIVED_LOCATION,COALESCE(F.Status,N'Available') RECEIVED_STATUS
    FROM dbo.FG_Inventory F LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID LEFT JOIN dbo.MD_Item I ON I.ItemNo=F.ItemNo
    WHERE F.StockID=@StockID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_ADJUST_SAVE_QTY
    @ScanText nvarchar(80),@DeltaQty decimal(18,3),@ReasonCode nvarchar(30),
    @ReasonNote nvarchar(500)=NULL,@UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Scan nvarchar(80)=LTRIM(RTRIM(ISNULL(@ScanText,N'')));
    DECLARE @Reason nvarchar(30)=UPPER(LTRIM(RTRIM(ISNULL(@ReasonCode,N''))));
    DECLARE @Note nvarchar(500)=NULLIF(LTRIM(RTRIM(@ReasonNote)),N'');
    DECLARE @User nvarchar(40)=COALESCE(NULLIF(LTRIM(RTRIM(@UserId)),N''),N'PDA');
    DECLARE @StockID int,@ItemNo varchar(20),@Location varchar(20),@LotID int,
            @BeforeQty decimal(18,3),@AfterQty decimal(18,3),@LotCode varchar(80);
    IF @Scan=N'' THROW 51610,'Finished goods Lot No is required.',1;
    IF COALESCE(@DeltaQty,0)=0 THROW 51611,'Adjustment quantity must be different from zero.',1;
    IF @Reason=N'' THROW 51612,'Reason code is required.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='INV_ADJUST_REASON' AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
       THROW 51619,'Unsupported inventory adjustment reason.',1;
    BEGIN TRANSACTION;
    SELECT TOP(1) @StockID=F.StockID,@ItemNo=F.ItemNo,@Location=F.Location,@LotID=F.LotID,
      @BeforeQty=COALESCE(F.Qty,0),@LotCode=COALESCE(NULLIF(L.LotCode,N''),F.StockNumber)
    FROM dbo.FG_Inventory F WITH(UPDLOCK,ROWLOCK) LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID
    WHERE (UPPER(COALESCE(L.LotCode,N''))=UPPER(@Scan) OR UPPER(COALESCE(F.StockNumber,N''))=UPPER(@Scan))
      AND UPPER(COALESCE(F.Status,N'Available')) NOT IN(N'CANCELED',N'CANCELLED',N'SHIPPED',N'DELIVERED',N'CLOSED')
    ORDER BY CASE WHEN COALESCE(F.Qty,0)>0 THEN 0 ELSE 1 END,F.StockID DESC;
    IF @StockID IS NULL AND EXISTS(SELECT 1 FROM dbo.WH_Inventory W LEFT JOIN dbo.MD_Location M
       ON M.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
       WHERE UPPER(W.LotNo)=UPPER(@Scan) AND W.Qty>0 AND COALESCE(M.AreaCode,'')<>'FG_AREA')
       THROW 51615,'Warehouse material cannot be adjusted in Finished Goods Adjust.',1;
    IF @StockID IS NULL THROW 51614,'The specified finished goods Lot No could not be found.',1;
    SET @AfterQty=@BeforeQty+@DeltaQty;
    IF @AfterQty<0 THROW 51617,'After Qty cannot be below zero.',1;
    IF @AfterQty<>FLOOR(@AfterQty) OR @AfterQty>999999999 THROW 51618,'New quantity must be a whole number from 0 to 999999999.',1;
    UPDATE dbo.FG_Inventory SET Qty=@AfterQty,Status=N'AVAILABLE',ModifiedTS=SYSDATETIME(),ModifiedBy=@User WHERE StockID=@StockID;
    IF @LotID IS NOT NULL UPDATE dbo.tbl_Lot SET RemainingQty=(SELECT COALESCE(SUM(COALESCE(F.Qty,0)),0) FROM dbo.FG_Inventory F
      WHERE F.LotID=@LotID AND UPPER(COALESCE(F.Status,N'Available')) NOT IN(N'CANCELED',N'CANCELLED',N'SHIPPED',N'DELIVERED',N'CLOSED')),
      ModifiedTS=SYSDATETIME(),ModifiedBy=LEFT(@User,20) WHERE LotID=@LotID;
    INSERT dbo.FG_InventoryAdjust(AdjustNo,StockID,ItemNo,Location,LotID,QtyBefore,Delta,QtyAfter,ReasonCode,ReasonNote,Status,RequestedBy,CreatedBy,CreatedTS)
    VALUES(CONCAT('FGADJ-',FORMAT(SYSDATETIME(),'yyMMddHHmmss')),@StockID,@ItemNo,@Location,@LotID,@BeforeQty,@DeltaQty,@AfterQty,
      CONVERT(varchar(30),@Reason),@Note,N'Posted',@User,LEFT(@User,50),SYSDATETIME());
    COMMIT TRANSACTION;
    EXEC dbo.FG_PDA_ADJUST_SCAN_STOCK @ScanText=@LotCode;
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
    IF (SELECT COUNT(*) FROM dbo.SCM_DeliveryBox B JOIN @Lots L ON L.LotNo=B.BoxNumber WHERE B.ActiveFlag=1) <> 6
       OR (SELECT COUNT(*) FROM dbo.SCM_DeliveryNote WHERE NoteNumber IN('PPT-WH-NOTE-LOCAL','PPT-WH-NOTE-CKD')) <> 2
        THROW 51701,'SCTEST1 inbound data is missing. Apply PDA_SEED.sql first.',1;
    BEGIN TRANSACTION;
    DELETE T FROM dbo.WH_InventoryTransaction T JOIN @Lots L ON L.LotNo=T.LotNo COLLATE DATABASE_DEFAULT;
    DELETE W FROM dbo.WH_Inventory W JOIN @Lots L ON L.LotNo=W.LotNo;
    UPDATE L SET CurrentLocationID=NULL,InventoryStatus='CREATED',RemainingQty=COALESCE(NULLIF(BatchSize,0),RemainingQty),
      ModifiedBy='pda-test',ModifiedTS=SYSDATETIME()
    FROM dbo.tbl_Lot L
    JOIN @Lots X ON X.LotNo COLLATE DATABASE_DEFAULT = L.LotCode COLLATE DATABASE_DEFAULT;
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

CREATE OR ALTER PROCEDURE dbo.WH_PDA_PPT_TEST_RESET @Screen varchar(10)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @S varchar(10)=LOWER(LTRIM(RTRIM(ISNULL(@Screen,''))));
    IF @S NOT IN('release','putaway','inventory','adjust','history') THROW 51700,'Unknown WH PPT test screen.',1;
    DECLARE @Rows table
    (
      LotNo nvarchar(100) COLLATE DATABASE_DEFAULT,
      ItemNo varchar(50) COLLATE DATABASE_DEFAULT,
      Qty decimal(18,3),
      LocationNo varchar(50) COLLATE DATABASE_DEFAULT,
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
    MERGE dbo.WH_Inventory T
    USING
    (
      SELECT R.*,I.ItemName COLLATE DATABASE_DEFAULT AS ItemName
      FROM @Rows R
      LEFT JOIN dbo.MD_Item I
        ON I.ItemNo COLLATE DATABASE_DEFAULT = R.ItemNo COLLATE DATABASE_DEFAULT
    ) S ON T.LotNo=S.LotNo
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
    FROM dbo.tbl_Lot L
    JOIN @Rows R ON R.LotNo COLLATE DATABASE_DEFAULT = L.LotCode COLLATE DATABASE_DEFAULT;
    IF @S='release' UPDATE dbo.WH_PickSlip SET PickedQty=0,Status='Open',CloseDate=NULL,CloseUserId=NULL,ModifiedBy='pda-ppt',ModifiedTS=SYSDATETIME() WHERE PickSlipNo='PS-PPT-WH-01';
    IF @S='history' INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,ReasonCode,RefDocType,OperatorID,Note,CreatedBy,CreatedTS)
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

/* Remove retired procedures/triggers first, then the retired tables. */
DROP TRIGGER IF EXISTS dbo.TR_WH_OLD_Inventory_SyncUnifiedInventory;
DROP TRIGGER IF EXISTS dbo.TR_FG_Inventory_SyncUnifiedInventory;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DROP TABLE IF EXISTS dbo.WH_ReleasePicking;
    DROP TABLE IF EXISTS dbo.WH_Receiving;
    DROP TABLE IF EXISTS dbo.WH_InboundPackage;
    DROP TABLE IF EXISTS dbo.WH_TransactionHistory;
    DROP TABLE IF EXISTS dbo.WH_OLD_Inventory;
    DROP TABLE IF EXISTS dbo.WH_AreaLayout;
    DROP TABLE IF EXISTS dbo.WH_AreaSection;
    DROP TABLE IF EXISTS dbo.WH_AreaMaster;
    DROP TABLE IF EXISTS dbo.WH_WarehouseMaster;

    IF EXISTS
    (
        SELECT 1 FROM sys.tables
        WHERE schema_id=SCHEMA_ID('dbo') AND name LIKE 'WH[_]%'
          AND name NOT IN ('WH_Inventory','WH_InventoryTransaction','WH_PurchaseOrder','WH_PickSlip')
    )
        THROW 52001, 'Unexpected WH_* table remains after consolidation.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT name AS RemainingWarehouseTable
FROM sys.tables
WHERE schema_id=SCHEMA_ID('dbo') AND name LIKE 'WH[_]%'
ORDER BY name;
GO
