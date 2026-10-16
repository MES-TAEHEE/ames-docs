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
-- PDA business commands are maintained in PdaWarehouseSql.cs / PdaFinishedGoodsSql.cs.
-- Only development reset procedures are installed by this migration.
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
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'RefDocType') IS NOT NULL
       AND COL_LENGTH(N'dbo.WH_InventoryTransaction', N'SourceType') IS NULL
        EXEC sys.sp_rename N'dbo.WH_InventoryTransaction.RefDocType', N'SourceType', N'COLUMN';
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'RefDocID') IS NOT NULL
       AND COL_LENGTH(N'dbo.WH_InventoryTransaction', N'SourceID') IS NULL
        EXEC sys.sp_rename N'dbo.WH_InventoryTransaction.RefDocID', N'SourceID', N'COLUMN';
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'RefDocNo') IS NOT NULL
        ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN RefDocNo;

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
    IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction') AND name=N'IX_WH_InventoryTransaction_LotNo')
        DROP INDEX IX_WH_InventoryTransaction_LotNo ON dbo.WH_InventoryTransaction;
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'ItemNo') IS NOT NULL ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN ItemNo;
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LocationID') IS NOT NULL ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN LocationID;
    IF COL_LENGTH(N'dbo.WH_InventoryTransaction', N'LotID') IS NOT NULL ALTER TABLE dbo.WH_InventoryTransaction DROP COLUMN LotID;
    ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN LotNo nvarchar(50) NOT NULL;
    ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyBefore decimal(18,3) NULL;
    ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyChange decimal(18,3) NOT NULL;
    ALTER TABLE dbo.WH_InventoryTransaction ALTER COLUMN QtyAfter decimal(18,3) NULL;
    CREATE INDEX IX_WH_InventoryTransaction_Search ON dbo.WH_InventoryTransaction(TransactionType,PartNo,LocationNo,LotNo);
    CREATE INDEX IX_WH_InventoryTransaction_LotNo ON dbo.WH_InventoryTransaction(LotNo);

    IF EXISTS
       (SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction')
          AND name=N'CK_WH_InventoryTransaction_Type')
        ALTER TABLE dbo.WH_InventoryTransaction DROP CONSTRAINT CK_WH_InventoryTransaction_Type;

    ALTER TABLE dbo.WH_InventoryTransaction WITH CHECK
        ADD CONSTRAINT CK_WH_InventoryTransaction_Type
        CHECK (TransactionType IN ('IN','OUT','ADJ','MOVE'));

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
             QtyBefore, QtyChange, QtyAfter, ReasonCode, SourceType, SourceID,
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
            WHERE T.SourceType = 'LEGACY_WH_TXN' AND T.SourceID = CONVERT(int, H.TxnID)
        );
    END;

    IF OBJECT_ID(N'dbo.WH_Receiving', N'U') IS NOT NULL
    BEGIN
        INSERT dbo.WH_InventoryTransaction
            (TransactionTime, TransactionType, PartNo, LocationNo, LotNo,
             QtyBefore, QtyChange, QtyAfter, ReasonCode, SourceType, SourceID,
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
            WHERE T.SourceType = 'LEGACY_RECEIVING' AND T.SourceID = R.ReceivingID
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





/* Warehouse inventory queries now read the unified LOT table only. */



/* Pick Slip replaces Release Schedule; pick history is WH_InventoryTransaction. */




/* FG Adjust must not depend on the retired WH_OLD_Inventory table. */


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
