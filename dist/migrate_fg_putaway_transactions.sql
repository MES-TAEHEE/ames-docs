-- Retire the duplicate FG put-away table without changing current inventory.
-- Put-away/history commands are maintained in PdaFinishedGoodsSql.cs, not database procedures.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
IF OBJECT_ID(N'dbo.FG_PutAway',N'U') IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE referenced_object_id=OBJECT_ID(N'dbo.FG_PutAway'))
        THROW 52500,'FG_PutAway has dependent foreign keys. Review them before retirement.',1;
    DECLARE @Legacy TABLE(PutAwayID int PRIMARY KEY,LotNo nvarchar(50),PartNo varchar(20),Qty decimal(18,3),
        LocationNo varchar(20),EventAt datetime2,Actor varchar(20),Metadata nvarchar(max),TransactionID bigint);
    INSERT @Legacy(PutAwayID,LotNo,PartNo,Qty,LocationNo,EventAt,Actor,Metadata)
    SELECT P.PutAwayID,P.LotNo,P.ItemNo,P.Qty,P.ActualLoc,COALESCE(P.CreatedTS,P.ModifiedTS),
        COALESCE(NULLIF(P.OperatorID,''),P.CreatedBy),
        (SELECT P.WoID,P.SuggestedLoc,P.LocOverrideReason,P.PalletCount,P.PalletQty,P.LabelPrintedTS,
            P.StorageMethod,P.ContainerType,P.ContainerBarcode,P.Status FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
    FROM dbo.FG_PutAway P WITH(TABLOCKX,HOLDLOCK);
    IF EXISTS(SELECT 1 FROM @Legacy WHERE NULLIF(LotNo,N'') IS NULL OR NULLIF(PartNo,'') IS NULL
        OR Qty<=0 OR EventAt IS NULL OR NULLIF(Actor,'') IS NULL)
        THROW 52501,'An old put-away row cannot be represented safely as a transaction.',1;
    UPDATE P SET TransactionID=M.TransactionID
    FROM @Legacy P OUTER APPLY
    (
        SELECT TOP(1) T.TransactionID FROM dbo.WH_InventoryTransaction T WITH(UPDLOCK,HOLDLOCK)
        WHERE T.SourceType='FG_PUTAWAY' AND T.TransactionType='IN' AND T.LotNo=P.LotNo
          AND T.PartNo=P.PartNo AND T.QtyChange=P.Qty
          AND ISNULL(T.LocationNo,'')=ISNULL(P.LocationNo,'')
          AND ABS(DATEDIFF_BIG(millisecond,T.TransactionTime,P.EventAt))<=1000
        ORDER BY ABS(DATEDIFF_BIG(millisecond,T.TransactionTime,P.EventAt)),T.TransactionID
    ) M;
    IF EXISTS(SELECT TransactionID FROM @Legacy WHERE TransactionID IS NOT NULL GROUP BY TransactionID HAVING COUNT(*)>1)
        THROW 52502,'Multiple old put-away rows matched the same transaction.',1;
    IF EXISTS(SELECT 1 FROM @Legacy P LEFT JOIN dbo.WH_InventoryTransaction T ON T.TransactionID=P.TransactionID
        WHERE LEN(CONCAT(NULLIF(T.Note,N''),N' | Legacy put-away: ',P.Metadata))>500)
        THROW 52503,'Put-away metadata exceeds the transaction note limit. Nothing was removed.',1;
    UPDATE T SET Note=CONCAT(NULLIF(T.Note,N''),N' | Legacy put-away: ',P.Metadata)
    FROM dbo.WH_InventoryTransaction T JOIN @Legacy P ON P.TransactionID=T.TransactionID;
    INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,
        QtyBefore,QtyChange,QtyAfter,ReasonCode,SourceType,OperatorID,Note,CreatedBy,CreatedTS)
    SELECT EventAt,'IN',PartNo,LocationNo,LotNo,0,Qty,Qty,'PUTAWAY','FG_PUTAWAY',Actor,
        CONCAT(N'Legacy put-away: ',Metadata),Actor,EventAt FROM @Legacy WHERE TransactionID IS NULL;
    IF EXISTS(SELECT 1 FROM @Legacy P WHERE NOT EXISTS(
        SELECT 1 FROM dbo.WH_InventoryTransaction T WHERE T.SourceType='FG_PUTAWAY'
          AND T.TransactionType='IN' AND T.LotNo=P.LotNo AND T.PartNo=P.PartNo AND T.QtyChange=P.Qty
          AND ISNULL(T.LocationNo,'')=ISNULL(P.LocationNo,'')
          AND ABS(DATEDIFF_BIG(millisecond,T.TransactionTime,P.EventAt))<=1000))
        THROW 52504,'Not all old put-away events were preserved.',1;
    DROP TABLE dbo.FG_PutAway;
END;
GO
COMMIT TRANSACTION;
GO
