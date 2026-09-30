SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
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

    UPDATE R SET LotNo=COALESCE(X.LotNo,JSON_VALUE(R.ItemsJSON,'$[0].lotNo'))
    FROM dbo.FG_CustomerReturn R
    LEFT JOIN
    (
        SELECT F.StockID,COALESCE(NULLIF(L.LotCode,N''),NULLIF(F.StockNumber,N''),
            CONCAT(N'LEGACY-FG-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),F.StockID),10))) AS LotNo
        FROM dbo.FG_Inventory F LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID
    ) X ON X.StockID=R.StockID
    WHERE R.LotNo IS NULL;

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

CREATE OR ALTER PROCEDURE dbo.FG_PDA_INVENTORY_LIST
    @SearchText nvarchar(120)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Search nvarchar(130)=N'%'+NULLIF(LTRIM(RTRIM(@SearchText)),N'')+N'%';
    SELECT W.LotNo,W.PartNo AS ItemNo,
        COALESCE(NULLIF(W.PartName,N''),I.ItemName COLLATE DATABASE_DEFAULT) AS ItemName,L.LotID,
        IMG.CustomerCode,COALESCE(W.Qty,0) AS Qty,COALESCE(NULLIF(I.DefaultUOM,''),'EA') AS Unit,
        W.LocationNo AS Location,'AVAILABLE' AS Status,W.ReceivedAt AS StockTS
    FROM dbo.WH_Inventory W
    LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=W.PartNo
    LEFT JOIN dbo.tbl_Lot L ON L.LotCode COLLATE DATABASE_DEFAULT=W.LotNo
    LEFT JOIN dbo.PR_ImgLot IMG ON IMG.LotID=L.LotID
    LEFT JOIN dbo.MD_Location ML ON ML.LocationID COLLATE DATABASE_DEFAULT=W.LocationNo
    WHERE W.Qty>0
      AND (UPPER(COALESCE(ML.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')
      AND (@Search IS NULL OR W.LotNo LIKE @Search OR W.PartNo LIKE @Search
        OR W.PartName LIKE @Search OR I.ItemName LIKE @Search OR W.LocationNo LIKE @Search)
    ORDER BY W.ReceivedAt DESC,W.LotNo DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_ADJUST_SCAN_STOCK
    @ScanText nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Scan nvarchar(80)=LTRIM(RTRIM(ISNULL(@ScanText,N'')));
    IF @Scan=N'' THROW 51600,'Finished goods Lot No is required.',1;
    IF LEN(@Scan)<3 OR @Scan COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9-]%'
        THROW 51604,'The barcode format is invalid.',1;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.WH_Inventory W
        LEFT JOIN dbo.MD_Location ML ON ML.LocationID COLLATE DATABASE_DEFAULT=W.LocationNo
        WHERE UPPER(W.LotNo)=UPPER(@Scan)
          AND (UPPER(COALESCE(ML.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')
    ) THROW 51601,'The specified finished goods Lot No could not be found.',1;

    SELECT N'FG' AS RECEIVE_TYPE,N'N' AS YN,W.LotNo AS LOTNO,W.LotNo AS BARCODE,
        N'dbo.WH_Inventory' AS SOURCE_TABLE,CAST(NULL AS nvarchar(50)) AS NOTENO,
        CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,W.CaseNo AS CASE_NO,W.InvoiceNo AS INVOICE_NO,
        W.ParentLotNo AS CONTAINER_NO,W.PartNo AS PARTNO,COALESCE(NULLIF(W.PartName,N''),I.ItemName COLLATE DATABASE_DEFAULT) AS PARTNM,
        W.Qty AS QTY,COALESCE(NULLIF(I.DefaultUOM,''),'EA') AS UNIT,CAST(NULL AS nvarchar(30)) AS PONO,
        CAST(NULL AS int) AS PONO_SEQ,CAST(NULL AS nvarchar(30)) AS VENDCD,
        CAST(NULL AS nvarchar(100)) AS VENDNM,CONVERT(date,L.ProducedAt) AS PROD_DATE,
        CAST(NULL AS date) AS DELI_DATE,CONVERT(date,W.ReceivedAt) AS ARRIV_DATE,
        CAST(NULL AS date) AS SHIP_DATE,CAST(NULL AS date) AS PACK_DATE,
        W.LocationNo AS RECEIVED_LOCATION,N'AVAILABLE' AS RECEIVED_STATUS
    FROM dbo.WH_Inventory W
    LEFT JOIN dbo.tbl_Lot L ON L.LotCode COLLATE DATABASE_DEFAULT=W.LotNo
    LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=W.PartNo
    WHERE UPPER(W.LotNo)=UPPER(@Scan);
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_ADJUST_SAVE_QTY
    @ScanText nvarchar(80),@DeltaQty decimal(18,3),@ReasonCode nvarchar(30),
    @ReasonNote nvarchar(500)=NULL,@UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Scan nvarchar(80)=LTRIM(RTRIM(ISNULL(@ScanText,N''))),
            @Reason nvarchar(30)=UPPER(LTRIM(RTRIM(ISNULL(@ReasonCode,N'')))),
            @Note nvarchar(500)=NULLIF(LTRIM(RTRIM(@ReasonNote)),N''),
            @User nvarchar(40)=COALESCE(NULLIF(LTRIM(RTRIM(@UserId)),N''),N'PDA'),
            @ItemNo varchar(50),@Location varchar(50),@Before decimal(18,3),@After decimal(18,3),@LotID int;
    IF @Scan=N'' THROW 51610,'Finished goods Lot No is required.',1;
    IF COALESCE(@DeltaQty,0)=0 THROW 51611,'Adjustment quantity must be different from zero.',1;
    IF @Reason=N'' THROW 51612,'Reason code is required.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='INV_ADJUST_REASON' AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
        THROW 51619,'Unsupported inventory adjustment reason.',1;

    BEGIN TRANSACTION;
    SELECT @ItemNo=W.PartNo,@Location=W.LocationNo,@Before=W.Qty,@LotID=L.LotID
    FROM dbo.WH_Inventory W WITH(UPDLOCK,ROWLOCK)
    LEFT JOIN dbo.tbl_Lot L ON L.LotCode COLLATE DATABASE_DEFAULT=W.LotNo
    LEFT JOIN dbo.MD_Location ML ON ML.LocationID COLLATE DATABASE_DEFAULT=W.LocationNo
    WHERE UPPER(W.LotNo)=UPPER(@Scan)
      AND (UPPER(COALESCE(ML.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%');
    IF @ItemNo IS NULL THROW 51614,'The specified finished goods Lot No could not be found.',1;
    SET @After=@Before+@DeltaQty;
    IF @After<0 THROW 51617,'After Qty cannot be below zero.',1;
    IF @After<>FLOOR(@After) OR @After>999999999 THROW 51618,'New quantity must be a whole number from 0 to 999999999.',1;

    UPDATE dbo.WH_Inventory SET Qty=@After,UpdatedAt=SYSDATETIME() WHERE LotNo=@Scan;
    IF @LotID IS NOT NULL
        UPDATE dbo.tbl_Lot SET RemainingQty=@After,ModifiedTS=SYSDATETIME(),ModifiedBy=LEFT(@User,20) WHERE LotID=@LotID;
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
         ReasonCode,SourceType,OperatorID,Note,CreatedBy,CreatedTS)
    VALUES
        (SYSDATETIME(),'ADJ',@ItemNo,@Location,@Scan,@Before,@DeltaQty,@After,
         @Reason,'FG_ADJUST',@User,@Note,LEFT(@User,20),SYSDATETIME());
    COMMIT TRANSACTION;
    EXEC dbo.FG_PDA_ADJUST_SCAN_STOCK @ScanText=@Scan;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_SCAN
    @Barcode varchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @B varchar(80)=LTRIM(RTRIM(ISNULL(@Barcode,'')));
    IF LEN(@B)<3 OR LEN(@B)>80 OR @B COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9_./-]%'
        THROW 52000,'The finished-good return barcode format is invalid.',1;
    DECLARE @P TABLE
    (
        LotNo nvarchar(50),ShipmentOrderID int,ShipOrderNumber varchar(40),CustomerCode varchar(20),
        ItemNo varchar(20),ItemName nvarchar(200),ShippedAt datetime2,Qty decimal(18,3),LotID int
    );
    INSERT @P
    SELECT DISTINCT J.LotNo,O.ShipmentOrderID,O.ShipOrderNumber,O.CustomerCode,J.ItemNo,I.ItemName,
        COALESCE(O.ShippedAt,O.DepartureAt,O.LoadingConfirmedAt),
        COALESCE(NULLIF(J.AllocatedQty,0),NULLIF(J.OrderedQty,0),W.Qty),L.LotID
    FROM dbo.FG_ShipmentOrder O
    CROSS APPLY OPENJSON(CASE WHEN ISJSON(O.ItemsJSON)=1 THEN O.ItemsJSON ELSE N'[]' END)
        WITH (LotNo nvarchar(50) '$.lotNo',ItemNo varchar(20) '$.itemNo',
              OrderedQty decimal(18,3) '$.orderedQty',AllocatedQty decimal(18,3) '$.allocatedQty') J
    LEFT JOIN dbo.WH_Inventory W ON W.LotNo=J.LotNo
    LEFT JOIN dbo.tbl_Lot L ON L.LotCode=J.LotNo
    LEFT JOIN dbo.MD_Item I ON I.ItemNo=J.ItemNo
    WHERE UPPER(J.LotNo)=UPPER(@B) AND UPPER(COALESCE(O.Status,''))='SHIPPED'
      AND COALESCE(O.ShippedAt,O.DepartureAt,O.LoadingConfirmedAt) IS NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM @P)
    BEGIN
        IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WHERE UPPER(LotNo)=UPPER(@B))
            THROW 52001,'The product exists, but no completed shipment history was found.',1;
        THROW 52002,'This barcode does not match a shipped finished-good LOT.',1;
    END;
    IF (SELECT COUNT(*) FROM @P)>1 THROW 52004,'This barcode matches multiple shipped products.',1;
    IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WHERE UPPER(LotNo)=UPPER(@B))
        THROW 52005,'This product was already received as a customer return.',1;
    IF EXISTS(SELECT 1 FROM @P WHERE NULLIF(ShipOrderNumber,'') IS NULL OR NULLIF(CustomerCode,'') IS NULL
        OR NULLIF(ItemNo,'') IS NULL OR NULLIF(ItemName,'') IS NULL OR COALESCE(Qty,0)<=0)
        THROW 52008,'The shipment record is incomplete. Verify shipment, customer, part, and quantity.',1;
    SELECT @B AS Barcode,LotID,LotNo,
        ShipmentOrderID,ShipOrderNumber,CustomerCode,ItemNo,ItemName,ShippedAt,Qty FROM @P;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_RECEIVE
    @Barcode varchar(80),@ReturnReason varchar(60),@Note nvarchar(500)=NULL,@OperatorID nvarchar(450)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Reason varchar(60)=UPPER(LTRIM(RTRIM(ISNULL(@ReturnReason,'')))),
            @CleanNote nvarchar(500)=NULLIF(LTRIM(RTRIM(@Note)),N'');
    IF NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='FG_RETURN_REASON' AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
        THROW 52009,'Select a valid return reason.',1;
    DECLARE @P TABLE
    (
        Barcode varchar(80),LotID int,LotNo varchar(80),
        ShipmentOrderID int,ShipOrderNumber varchar(40),CustomerCode varchar(20),ItemNo varchar(20),
        ItemName nvarchar(200),ShippedAt datetime2,Qty decimal(18,3)
    );
    INSERT @P EXEC dbo.FG_PDA_RETURN_SCAN @Barcode;
    BEGIN TRANSACTION;
    DECLARE @Now datetime2=SYSDATETIME(),@ReturnID int,@ReturnNumber varchar(24),@LotNo nvarchar(50)=(SELECT LotNo FROM @P);
    IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@LotNo)
        THROW 52005,'This product was already received as a customer return.',1;
    SET @ReturnNumber=CONCAT('RMA-',FORMAT(@Now,'yyMMddHHmmssfff'),LEFT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),5));
    INSERT dbo.FG_CustomerReturn
        (ReturnNumber,CustomerCode,OriginalShipmentOrderID,LotNo,LotID,ItemNo,ReturnQty,
         ReturnReason,Note,ItemsJSON,Status,ReceivedAt,ReceivedBy,CapaTriggered,CreatedBy,CreatedTS)
    SELECT @ReturnNumber,CustomerCode,ShipmentOrderID,LotNo,LotID,ItemNo,Qty,@Reason,@CleanNote,
        (SELECT ItemNo AS itemNo,LotNo AS lotNo,LotNo AS stockNumber,Barcode AS barcode,
                Qty AS qty,CAST(NULL AS varchar(50)) AS location FOR JSON PATH),
        'Open',@Now,@OperatorID,0,'pda',@Now FROM @P;
    SET @ReturnID=CONVERT(int,SCOPE_IDENTITY());
    MERGE dbo.WH_Inventory AS T
    USING (SELECT LotNo,ItemNo,ItemName,Qty FROM @P) AS S ON S.LotNo=T.LotNo
    WHEN MATCHED THEN UPDATE SET Qty=S.Qty,LocationNo=NULL,UpdatedAt=@Now
    WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
        VALUES(S.LotNo,'PART',S.ItemNo,S.ItemName,NULL,S.Qty,@Now,@Now,@Now);
    INSERT dbo.WH_InventoryTransaction
        (TransactionTime,TransactionType,PartNo,LotNo,QtyBefore,QtyChange,QtyAfter,
         ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
    SELECT @Now,'IN',ItemNo,LotNo,0,Qty,Qty,'RETURN','FG_RETURN',@ReturnID,
        @OperatorID,@CleanNote,LEFT(COALESCE(NULLIF(@OperatorID,N''),N'pda'),20),@Now FROM @P;
    COMMIT TRANSACTION;
    SELECT @ReturnID AS ReturnID,* FROM @P;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_TRANSACTION_LIST
    @SearchText nvarchar(120)=NULL,@DateFrom date=NULL,@DateTo date=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @From date=COALESCE(@DateFrom,DATEADD(day,-30,CAST(GETDATE() AS date))),
            @To date=COALESCE(@DateTo,CAST(GETDATE() AS date)),
            @Search nvarchar(130)=N'%'+NULLIF(LTRIM(RTRIM(@SearchText)),N'')+N'%';
    ;WITH Events AS
    (
        SELECT T.TransactionTime EventTime,CONCAT('TX-',T.TransactionID) EventID,T.LotNo,T.PartNo,T.LocationNo,
            ABS(T.QtyChange) Qty,CASE T.TransactionType WHEN 'IN' THEN N'Inbound' WHEN 'OUT' THEN N'Outbound' ELSE N'Adjust' END Status,
            T.TransactionType Direction,T.OperatorID Worker,T.ReasonCode,T.Note ReasonNote,T.ApproverID Supervisor,
            T.QtyBefore BeforeQty,T.QtyChange DeltaQty,T.QtyAfter AfterQty,T.SourceType Source,T.Note Reference
        FROM dbo.WH_InventoryTransaction T
        WHERE UPPER(COALESCE(T.SourceType,'')) LIKE 'FG%'
        UNION ALL
        SELECT P.CreatedTS,CONCAT('IN-',P.PutAwayID),P.LotNo,P.ItemNo COLLATE DATABASE_DEFAULT,
            P.ActualLoc COLLATE DATABASE_DEFAULT,P.Qty,N'Put-Away','IN',
            COALESCE(P.OperatorID,P.CreatedBy) COLLATE DATABASE_DEFAULT,NULL,
            P.ContainerBarcode COLLATE DATABASE_DEFAULT,NULL,NULL,NULL,NULL,N'FG_PutAway',
            P.ContainerBarcode COLLATE DATABASE_DEFAULT
        FROM dbo.FG_PutAway P WHERE UPPER(COALESCE(P.Status,'')) NOT IN('CANCELLED','CANCELED')
        UNION ALL
        SELECT COALESCE(R.ReceivedAt,R.CreatedTS),CONCAT('RETURN-',R.ReturnID),R.LotNo,
            R.ItemNo COLLATE DATABASE_DEFAULT,NULL,R.ReturnQty,N'Return','IN',
            COALESCE(R.ReceivedBy,R.CreatedBy) COLLATE DATABASE_DEFAULT,
            R.ReturnReason COLLATE DATABASE_DEFAULT,R.Note COLLATE DATABASE_DEFAULT,
            NULL,NULL,NULL,NULL,N'FG_CustomerReturn',R.ReturnNumber COLLATE DATABASE_DEFAULT
        FROM dbo.FG_CustomerReturn R WHERE UPPER(COALESCE(R.Status,'')) NOT IN('CANCELLED','CANCELED','REJECTED')
    )
    SELECT ROW_NUMBER() OVER(ORDER BY E.EventTime DESC,E.EventID DESC) AS ROW_NO,
        COALESCE(NULLIF(E.LotNo COLLATE DATABASE_DEFAULT,''),'N/A') LOTNO,E.ItemNo PARTNO,CONVERT(nvarchar(10),E.EventTime,23) WDATE,
        CONVERT(nvarchar(8),E.EventTime,108) WTIME,COALESCE(NULLIF(E.LocationID COLLATE DATABASE_DEFAULT,''),'N/A') LOCATION_NO,
        COALESCE(E.Qty,0) QTY,I.DefaultUOM UNIT,E.Status STATUS,E.Direction DIRECTION,
        COALESCE(NULLIF(U.UserName COLLATE DATABASE_DEFAULT,''),E.Worker COLLATE DATABASE_DEFAULT) WORKER_ID,E.ReasonCode REASON_CODE,E.ReasonNote REASON_NOTE,
        E.Supervisor SUPERVISOR,E.BeforeQty BEFORE_QTY,E.DeltaQty DELTA_QTY,E.AfterQty AFTER_QTY,
        NULL BEFORE_STATUS,NULL AFTER_STATUS,NULL BEFORE_LOCATION,NULL AFTER_LOCATION,E.Source SOURCE,E.Reference NOTE
    FROM Events E LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=E.ItemNo
    LEFT JOIN dbo.AspNetUsers U ON U.Id COLLATE DATABASE_DEFAULT=E.Worker
    WHERE E.EventTime>=@From AND E.EventTime<DATEADD(day,1,@To)
      AND (@Search IS NULL OR E.LotNo COLLATE DATABASE_DEFAULT LIKE @Search
        OR E.ItemNo COLLATE DATABASE_DEFAULT LIKE @Search
        OR I.ItemName COLLATE DATABASE_DEFAULT LIKE @Search
        OR E.LocationID COLLATE DATABASE_DEFAULT LIKE @Search
        OR E.Reference COLLATE DATABASE_DEFAULT LIKE @Search)
    ORDER BY E.EventTime DESC,E.EventID DESC;
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
