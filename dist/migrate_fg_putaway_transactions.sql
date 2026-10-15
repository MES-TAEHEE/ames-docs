-- Retire the duplicate FG put-away table without changing current inventory.
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
CREATE OR ALTER PROCEDURE dbo.FG_PDA_PUTAWAY_COMPLETE
    @Barcode nvarchar(50),@Location varchar(20),@OperatorID varchar(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Barcode=NULLIF(LTRIM(RTRIM(@Barcode)),N'');
    SET @Location=NULLIF(LTRIM(RTRIM(@Location)),'');
    IF @Barcode IS NULL OR @Location IS NULL OR NULLIF(LTRIM(RTRIM(@OperatorID)),'') IS NULL
        THROW 52510,'Scan FG LOT and Location Barcode with a signed-in operator.',1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Lock int,@Resource nvarchar(255)=N'FG_PUTAWAY_LOT:'+UPPER(@Barcode);
        EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
        IF @Lock<0 THROW 52511,'This FG LOT is being processed. Scan it again.',1;
        SET @Resource=N'FG_PUTAWAY_LOCATION:'+UPPER(@Location);
        EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
        IF @Lock<0 THROW 52512,'This FG location is being updated. Scan it again.',1;
        DECLARE @LotID int,@PartNo varchar(20),@PartName nvarchar(200),@ProductionQty decimal(18,3),
            @StoredQty decimal(18,3),@StoredLocation varchar(20),@StoredPart varchar(20),
            @Qty decimal(18,3),@Capacity decimal(18,3),@Current decimal(18,3),@Now datetime2=SYSDATETIME();
        SELECT @LotID=L.LotID,@PartNo=L.ItemNo,@PartName=I.ItemName,@ProductionQty=R.Qty
        FROM dbo.tbl_Lot L WITH(UPDLOCK,HOLDLOCK)
        JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
        CROSS APPLY(SELECT SUM(P.GoodQty) Qty FROM dbo.PR_ProductionResult P
            WHERE P.LotID=L.LotID AND P.ProcessCode=L.ProcessCode AND ISNULL(P.DefectFlag,0)=0) R
        WHERE L.LotCode COLLATE DATABASE_DEFAULT=@Barcode COLLATE DATABASE_DEFAULT
          AND UPPER(ISNULL(L.Status,'')) IN('CONFIRMED','STOCKED')
          AND I.ItemType='ASSY' AND ISNULL(I.ActiveFlag,1)=1 AND R.Qty>0;
        IF @LotID IS NULL THROW 52513,'A confirmed finished-goods ASSY production LOT is required.',1;
        SELECT @StoredQty=Qty,@StoredLocation=LocationNo,@StoredPart=PartNo
        FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode;
        IF @StoredQty IS NOT NULL AND @StoredQty<=0
        BEGIN
            IF (SELECT TOP(1) SourceType FROM dbo.WH_InventoryTransaction
                WHERE LotNo=@Barcode AND QtyChange<>0 ORDER BY TransactionTime DESC,TransactionID DESC)='FG_OUTBOUND'
                THROW 52514,'This product has already been shipped. Put-away is not allowed.',1;
            THROW 52514,'This LOT has no available quantity for put-away.',1;
        END;
        IF @StoredQty IS NOT NULL AND NULLIF(LTRIM(RTRIM(@StoredLocation)),'') IS NOT NULL
            THROW 52515,'This FG LOT is already stocked. Use Relocation to change its location.',1;
        IF @StoredQty IS NOT NULL AND ISNULL(@StoredPart,'')<>@PartNo
            THROW 52516,'The inventory part does not match the production LOT.',1;
        SET @Qty=COALESCE(@StoredQty,@ProductionQty);
        SELECT @Capacity=Capacity FROM dbo.MD_Location WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationID=@Location AND LocationType='FG';
        IF @@ROWCOUNT=0 THROW 52517,'Scan a registered FG location barcode.',1;
        SELECT @Current=COALESCE(SUM(Qty),0) FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LocationNo=@Location;
        IF @Capacity>0 AND @Current+@Qty>@Capacity THROW 52518,'The FG location has insufficient load capacity.',1;
        IF @StoredQty IS NULL
            INSERT dbo.WH_Inventory(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
                VALUES(@Barcode,'PART',@PartNo,@PartName,@Location,@Qty,@Now,@Now,@Now);
        ELSE
            UPDATE dbo.WH_Inventory SET LocationNo=@Location,UpdatedAt=@Now WHERE LotNo=@Barcode;
        INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,
            QtyBefore,QtyChange,QtyAfter,ReasonCode,SourceType,OperatorID,Note,CreatedBy,CreatedTS)
        VALUES(@Now,CASE WHEN @StoredQty IS NULL THEN 'IN' ELSE 'MOVE' END,@PartNo,@Location,@Barcode,
            COALESCE(@StoredQty,0),CASE WHEN @StoredQty IS NULL THEN @Qty ELSE 0 END,@Qty,
            'PUTAWAY','FG_PUTAWAY',@OperatorID,
            CASE WHEN @StoredQty IS NULL THEN N'Finished goods put-away'
                 ELSE CONCAT(N'Moved from (unassigned) to ',@Location) END,@OperatorID,@Now);
        UPDATE dbo.tbl_Lot SET CurrentLocationID=@Location,Status='Stocked',InventoryStatus='STOCKED',
            ModifiedBy=@OperatorID,ModifiedTS=@Now WHERE LotID=@LotID;
        COMMIT TRANSACTION;
        SELECT @Barcode LotNo,@Qty Qty,CAST(CASE WHEN @StoredQty IS NULL THEN 0 ELSE 1 END AS bit) ExistingInventory;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.FG_PDA_TRANSACTION_LIST
    @SearchText nvarchar(120)=NULL,@DateFrom date=NULL,@DateTo date=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @From date=COALESCE(@DateFrom,DATEADD(day,-30,CAST(SYSDATETIME() AS date))),
        @To date=COALESCE(@DateTo,CAST(SYSDATETIME() AS date)),
        @Search nvarchar(130)=N'%'+NULLIF(LTRIM(RTRIM(@SearchText)),N'')+N'%';
    SELECT ROW_NUMBER() OVER(ORDER BY T.TransactionTime DESC,T.TransactionID DESC) ROW_NO,
        T.LotNo LOTNO,T.PartNo PARTNO,CONVERT(nvarchar(10),T.TransactionTime,23) WDATE,
        CONVERT(nvarchar(8),T.TransactionTime,108) WTIME,COALESCE(NULLIF(T.LocationNo,''),'N/A') LOCATION_NO,
        CASE WHEN T.TransactionType='MOVE' THEN T.QtyAfter ELSE ABS(T.QtyChange) END QTY,I.DefaultUOM UNIT,
        CASE WHEN T.SourceType='FG_RETURN' THEN N'Return'
             WHEN T.TransactionType='MOVE' THEN N'Location change'
             WHEN T.SourceType='FG_PUTAWAY' THEN N'Put-Away'
             WHEN T.TransactionType='OUT' THEN N'Outbound'
             WHEN T.TransactionType='IN' THEN N'Inbound' ELSE N'Adjust' END STATUS,
        CASE WHEN T.SourceType='FG_RETURN' THEN 'RETURN' ELSE T.TransactionType END DIRECTION,
        COALESCE(NULLIF(U.UserName,''),T.OperatorID) WORKER_ID,T.ReasonCode REASON_CODE,T.Note REASON_NOTE,
        T.ApproverID SUPERVISOR,T.QtyBefore BEFORE_QTY,T.QtyChange DELTA_QTY,T.QtyAfter AFTER_QTY,
        NULL BEFORE_STATUS,NULL AFTER_STATUS,
        CASE WHEN T.TransactionType='MOVE' AND T.Note LIKE N'Moved from % to %'
             THEN NULLIF(SUBSTRING(T.Note,12,CHARINDEX(N' to ',T.Note)-12),N'(unassigned)') END BEFORE_LOCATION,
        CASE WHEN T.TransactionType='MOVE' THEN T.LocationNo END AFTER_LOCATION,
        T.SourceType SOURCE,COALESCE(S.OutboundBarcode,T.Note) NOTE
    FROM dbo.WH_InventoryTransaction T
    LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=T.PartNo COLLATE DATABASE_DEFAULT
    LEFT JOIN dbo.AspNetUsers U ON U.Id COLLATE DATABASE_DEFAULT=T.OperatorID COLLATE DATABASE_DEFAULT
    LEFT JOIN dbo.FG_Shipment S ON T.SourceType='FG_OUTBOUND' AND S.ShipmentID=T.SourceID
    WHERE T.SourceType LIKE 'FG%'
      AND T.TransactionTime>=@From AND T.TransactionTime<DATEADD(day,1,@To)
      AND (@Search IS NULL OR T.LotNo LIKE @Search OR T.PartNo LIKE @Search
        OR I.ItemName COLLATE DATABASE_DEFAULT LIKE @Search OR T.LocationNo LIKE @Search
        OR T.Note LIKE @Search OR S.OutboundBarcode LIKE @Search)
    ORDER BY T.TransactionTime DESC,T.TransactionID DESC;
END;
GO
COMMIT TRANSACTION;
GO
