SET NOCOUNT ON;
SET XACT_ABORT ON;
-- PDA business commands are maintained in PdaFinishedGoodsSql.cs; only development resets remain below.
SET ANSI_NULLS ON;
IF OBJECT_ID(N'dbo.FG_Shipment',N'U') IS NOT NULL
BEGIN
    RAISERROR('Legacy FG consolidation cannot overwrite actual shipment procedures.',16,1);
    SET NOEXEC ON;
END;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
GO
IF OBJECT_ID(N'dbo.FG_CustomerReturn',N'U') IS NOT NULL
    AND COL_LENGTH(N'dbo.FG_CustomerReturn',N'CustomerID') IS NULL
    THROW 52016,'Apply migrate_fg_customer_return_columns.sql before FG inventory consolidation.',1;
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

IF OBJECT_ID(N'dbo.FG_Inventory',N'U') IS NOT NULL OR OBJECT_ID(N'dbo.FG_InventoryAdjust',N'U') IS NOT NULL
    THROW 52090,'Legacy finished-goods inventory tables were not removed.',1;
GO
