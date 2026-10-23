namespace AMES.Data.Repositories;

// Parameterized batches owned by the application; no database procedure installation is required.
public static class PdaFinishedGoodsSql
{
    public const string Inventory = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @Search nvarchar(130)=N'%'+NULLIF(LTRIM(RTRIM(@SearchText)),N'')+N'%';
            SELECT W.LotNo,W.PartNo AS ItemNo,
                COALESCE(NULLIF(W.PartName,N''),I.ItemName COLLATE DATABASE_DEFAULT) AS ItemName,L.LotID,
                COALESCE(IMG.CustomerCode,PNT.CustomerCode) AS CustomerCode,COALESCE(W.Qty,0) AS Qty,COALESCE(NULLIF(I.DefaultUOM,''),'EA') AS Unit,
                W.LocationNo AS Location,'AVAILABLE' AS Status,W.ReceivedAt AS StockTS
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=W.PartNo
            LEFT JOIN dbo.tbl_Lot L ON L.LotCode COLLATE DATABASE_DEFAULT=W.LotNo
            LEFT JOIN dbo.PR_ImgLot IMG ON IMG.LotID=L.LotID
            LEFT JOIN dbo.PR_PntLot PNT ON PNT.LotID=L.LotID
            LEFT JOIN dbo.MD_Location ML ON ML.LocationID COLLATE DATABASE_DEFAULT=W.LocationNo
            WHERE W.Qty>0
              AND I.ItemType='ASSY'
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)
              AND (@Search IS NULL OR W.LotNo LIKE @Search OR W.PartNo LIKE @Search
                OR W.PartName LIKE @Search OR I.ItemName LIKE @Search OR W.LocationNo LIKE @Search)
            ORDER BY W.ReceivedAt DESC,W.LotNo DESC;
        END;
        """;

    public const string Transactions = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @From date=COALESCE(@DateFrom,DATEADD(day,-30,CAST(SYSDATETIME() AS date))),
                @To date=COALESCE(@DateTo,CAST(SYSDATETIME() AS date)),
                @Search nvarchar(130)=N'%'+NULLIF(LTRIM(RTRIM(@SearchText)),N'')+N'%';
            SELECT ROW_NUMBER() OVER(ORDER BY T.TransactionTime DESC,T.TransactionID DESC) ROW_NO,
                T.LotNo LOTNO,T.PartNo PARTNO,CONVERT(nvarchar(10),T.TransactionTime,23) WDATE,
                CONVERT(nvarchar(8),T.TransactionTime,108) WTIME,COALESCE(NULLIF(T.LocationNo,''),'N/A') LOCATION_NO,
                CASE WHEN T.TransactionType='MOVE' THEN T.QtyAfter ELSE ABS(T.QtyChange) END QTY,I.DefaultUOM UNIT,
                CASE WHEN T.SourceType IN('FG_DEFECT','INV_DEFECT') THEN N'Defect'
                     WHEN T.SourceType='FG_RETURN' THEN N'Return'
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
                T.SourceType SOURCE,COALESCE(S.LotNo,T.Note) NOTE
            FROM dbo.WH_InventoryTransaction T
            LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=T.PartNo COLLATE DATABASE_DEFAULT
            LEFT JOIN dbo.AspNetUsers U ON U.Id COLLATE DATABASE_DEFAULT=T.OperatorID COLLATE DATABASE_DEFAULT
            LEFT JOIN dbo.FG_Shipment S ON T.SourceType='FG_OUTBOUND' AND S.ShipmentID=T.SourceID
            WHERE T.SourceType LIKE 'FG%'
              AND T.TransactionTime>=@From AND T.TransactionTime<DATEADD(day,1,@To)
              AND (@Search IS NULL OR T.LotNo LIKE @Search OR T.PartNo LIKE @Search
                OR I.ItemName COLLATE DATABASE_DEFAULT LIKE @Search OR T.LocationNo LIKE @Search
                OR T.Note LIKE @Search OR S.LotNo LIKE @Search)
            ORDER BY T.TransactionTime DESC,T.TransactionID DESC;
        END;
        """;

    public const string AdjustScan = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
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
                  AND EXISTS(SELECT 1 FROM dbo.MD_Item SI WHERE SI.ItemNo=W.PartNo AND SI.ItemType='ASSY')
            ) THROW 51601,'The specified finished goods Lot No could not be found.',1;

            SELECT N'FG' AS RECEIVE_TYPE,N'N' AS YN,W.LotNo AS LOTNO,W.LotNo AS BARCODE,
                N'dbo.WH_Inventory' AS SOURCE_TABLE,CAST(NULL AS nvarchar(50)) AS NOTENO,
                CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,W.PalletNo AS CASE_NO,W.InvoiceNo AS INVOICE_NO,
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
        """;

    public const string AdjustSave = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            DECLARE @Scan nvarchar(80)=LTRIM(RTRIM(ISNULL(@ScanText,N''))),
                    @Reason nvarchar(30)=UPPER(LTRIM(RTRIM(ISNULL(@ReasonCode,N'')))),
                    @Note nvarchar(500)=NULLIF(LTRIM(RTRIM(@ReasonNote)),N''),
                    @User nvarchar(40)=COALESCE(NULLIF(LTRIM(RTRIM(@UserId)),N''),N'PDA'),
                    @ItemNo varchar(50),@Location varchar(50),@Before decimal(18,3),@After decimal(18,3),@LotID int;
            IF @Scan=N'' THROW 51610,'Finished goods Lot No is required.',1;
            IF EXISTS(SELECT 1 FROM dbo.WH_Inventory W JOIN dbo.MD_Location L ON L.LocationID=W.LocationNo
                WHERE W.LotNo=@Scan AND L.LocationLevel=1)
                THROW 51614,'Primary locations cannot hold stock. Move this LOT to a detail location first.',1;
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
              AND EXISTS(SELECT 1 FROM dbo.MD_Item SI WHERE SI.ItemNo=W.PartNo AND SI.ItemType='ASSY');
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
        """ + AdjustScanResult + """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        END;
        """;

    public const string PutAway = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
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
                    WHERE P.LotID=L.LotID AND (P.ProcessCode=L.ProcessCode OR P.ProcessCode='RWK') AND ISNULL(P.DefectFlag,0)=0) R
                WHERE L.LotCode COLLATE DATABASE_DEFAULT=@Barcode COLLATE DATABASE_DEFAULT
                  AND UPPER(ISNULL(L.Status,'')) IN('CONFIRMED','STOCKED')
                  AND I.ItemType='ASSY' AND ISNULL(I.ActiveFlag,1)=1 AND R.Qty>0;
                IF @LotID IS NULL THROW 52513,'A confirmed finished-goods ASSY production LOT is required.',1;
                IF EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction WHERE LotNo=@Barcode AND SourceType IN('FG_DEFECT','INV_DEFECT') AND QtyAfter=0)
                    THROW 52514,'This LOT was rejected as defective. Put-away is not allowed.',1;
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
                    WHERE LocationID=@Location AND LocationType='FG' AND LocationLevel=2 AND COALESCE(ActiveFlag,1)=1;
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
        """;

    public const string OutboundScan = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @UnitType varchar(10),@LocationNo varchar(20);
            IF NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory WHERE LotNo=@Barcode)
                THROW 52000,'No inventory record exists for this barcode.',1;
            SELECT @UnitType=UnitType,@LocationNo=LocationNo FROM dbo.WH_Inventory WHERE LotNo=@Barcode;
            IF NULLIF(LTRIM(RTRIM(@UnitType)),'') IS NULL THROW 52003,'The inventory unit type is missing.',1;
            DECLARE @Items TABLE(LotNo nvarchar(50) PRIMARY KEY,UnitType varchar(10),PartNo varchar(20),
                PartName nvarchar(200),LocationNo varchar(20),Qty decimal(18,3),CustomerID varchar(20));
            ;WITH UnitTree AS
            (
                SELECT LotNo,UnitType,ParentLotNo,PartNo,PartName,LocationNo,Qty FROM dbo.WH_Inventory WHERE LotNo=@Barcode
                UNION ALL
                SELECT C.LotNo,C.UnitType,C.ParentLotNo,C.PartNo,C.PartName,C.LocationNo,C.Qty
                FROM dbo.WH_Inventory C JOIN UnitTree P ON C.ParentLotNo=P.LotNo
            )
            INSERT @Items
            SELECT T.LotNo,T.UnitType,T.PartNo,T.PartName,T.LocationNo,T.Qty,C.CustomerID
            FROM UnitTree T OUTER APPLY
            (
                SELECT MIN(M.CustomerID) CustomerID
                FROM dbo.tbl_Lot L
                LEFT JOIN dbo.PR_ImgLot I ON I.LotID=L.LotID
                LEFT JOIN dbo.PR_PntLot N ON N.LotID=L.LotID
                CROSS APPLY (SELECT COALESCE(I.CustomerCode,N.CustomerCode) AS CustomerCode) P
                JOIN dbo.MD_Customer M ON M.CustomerID COLLATE DATABASE_DEFAULT=P.CustomerCode COLLATE DATABASE_DEFAULT OR M.CustomerCode COLLATE DATABASE_DEFAULT=P.CustomerCode COLLATE DATABASE_DEFAULT
                WHERE L.LotCode COLLATE DATABASE_DEFAULT=T.LotNo COLLATE DATABASE_DEFAULT AND P.CustomerCode IS NOT NULL HAVING COUNT(DISTINCT M.CustomerID)=1
            ) C
            WHERE T.Qty>0 AND T.PartNo IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory X WHERE X.ParentLotNo=T.LotNo AND X.Qty>0)
            OPTION(MAXRECURSION 100);
            IF NOT EXISTS(SELECT 1 FROM @Items) THROW 52001,'No available part inventory remains in this unit.',1;
            IF EXISTS(SELECT 1 FROM @Items I LEFT JOIN dbo.MD_Location L ON L.LocationID=I.LocationNo
                WHERE NULLIF(LTRIM(RTRIM(I.LocationNo)),'') IS NULL OR L.LocationID IS NULL OR L.LocationLevel<>2
                    OR COALESCE(L.ActiveFlag,1)=0 OR UPPER(ISNULL(L.LocationType,''))<>'FG')
                THROW 52006,'All outbound inventory must be put away in a registered FG location.',1;
            IF (SELECT COUNT(DISTINCT CustomerID) FROM @Items)>1
                THROW 52018,'This unit contains products for different customers. Separate them before outbound.',1;
            DECLARE @CustomerID varchar(20);
            IF NOT EXISTS(SELECT 1 FROM @Items WHERE CustomerID IS NULL) SELECT @CustomerID=MIN(CustomerID) FROM @Items;
            SELECT @Barcode OutboundBarcode,@UnitType UnitType,@LocationNo LocationNo,SUM(Qty) TotalQty,COUNT_BIG(*) ItemCount,
                @CustomerID CustomerID,(SELECT CustomerName FROM dbo.MD_Customer WHERE CustomerID=@CustomerID) CustomerName,MIN(CustomerID) KnownCustomerID FROM @Items;
            SELECT LotNo,UnitType,PartNo,PartName,Qty,LocationNo FROM @Items ORDER BY LotNo;
        END;
        """;

    public const string OutboundComplete = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        SET ANSI_PADDING ON;
        SET ANSI_WARNINGS ON;
        SET ARITHABORT ON;
        SET CONCAT_NULL_YIELDS_NULL ON;
        SET NUMERIC_ROUNDABORT OFF;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            BEGIN TRY
                BEGIN TRANSACTION;
                DECLARE @UnitType varchar(10),@PalletNo varchar(50),@Now datetime2=SYSDATETIME(),@ShipmentID int;
                DECLARE @ShippingGate int;
                EXEC @ShippingGate=sys.sp_getapplock @Resource='FG_SHIPPING_ORDERS',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
                IF @ShippingGate<0 THROW 52020,'Shipping is busy. Retry shortly.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode)
                    THROW 52000,'No inventory record exists for this barcode.',1;
                SELECT @UnitType=UnitType,@PalletNo=COALESCE(NULLIF(LTRIM(RTRIM(PalletNo)),''),CASE WHEN UnitType='PALLET' THEN LotNo END) FROM dbo.WH_Inventory WHERE LotNo=@Barcode;
                IF NULLIF(LTRIM(RTRIM(@UnitType)),'') IS NULL THROW 52003,'The inventory unit type is missing.',1;
                IF NULLIF(LTRIM(RTRIM(@OperatorID)),'') IS NULL THROW 52014,'A signed-in operator is required.',1;
                DECLARE @Scope TABLE(LotNo nvarchar(50) PRIMARY KEY,UnitType varchar(10),ParentLotNo nvarchar(50),
                    PartNo varchar(20),PartName nvarchar(200),LocationNo varchar(20),Qty decimal(18,3));
                ;WITH UnitTree AS
                (
                    SELECT LotNo,UnitType,ParentLotNo,PartNo,PartName,LocationNo,Qty FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode
                    UNION ALL
                    SELECT C.LotNo,C.UnitType,C.ParentLotNo,C.PartNo,C.PartName,C.LocationNo,C.Qty
                    FROM dbo.WH_Inventory C WITH(UPDLOCK,HOLDLOCK) JOIN UnitTree P ON C.ParentLotNo=P.LotNo
                ) INSERT @Scope SELECT * FROM UnitTree OPTION(MAXRECURSION 100);
                IF EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad D WITH(UPDLOCK,HOLDLOCK) JOIN @Scope S ON S.LotNo=D.LotNo
                    WHERE D.ShipmentID IS NULL AND D.DeliveryID<>COALESCE(@ShippingDeliveryID,-1))
                    THROW 52020,'This inventory is loaded on a DCR delivery. Complete that delivery instead of individual outbound.',1;
                DECLARE @Items TABLE(LotNo nvarchar(50) PRIMARY KEY,PartNo varchar(20),PartName nvarchar(200),
                    LocationNo varchar(20),Qty decimal(18,3),CustomerID varchar(20));
                INSERT @Items
                SELECT T.LotNo,T.PartNo,T.PartName,T.LocationNo,T.Qty,C.CustomerID
                FROM @Scope T OUTER APPLY
                (
                    SELECT MIN(M.CustomerID) CustomerID
                    FROM dbo.tbl_Lot L
                    LEFT JOIN dbo.PR_ImgLot I ON I.LotID=L.LotID
                    LEFT JOIN dbo.PR_PntLot N ON N.LotID=L.LotID
                    CROSS APPLY (SELECT COALESCE(I.CustomerCode,N.CustomerCode) AS CustomerCode) P
                    JOIN dbo.MD_Customer M ON M.CustomerID COLLATE DATABASE_DEFAULT=P.CustomerCode COLLATE DATABASE_DEFAULT OR M.CustomerCode COLLATE DATABASE_DEFAULT=P.CustomerCode COLLATE DATABASE_DEFAULT
                    WHERE L.LotCode COLLATE DATABASE_DEFAULT=T.LotNo COLLATE DATABASE_DEFAULT AND P.CustomerCode IS NOT NULL HAVING COUNT(DISTINCT M.CustomerID)=1
                ) C
                WHERE T.Qty>0 AND NOT EXISTS(SELECT 1 FROM @Scope X WHERE X.ParentLotNo=T.LotNo AND X.Qty>0)
                  AND (T.PartNo IS NOT NULL OR UPPER(ISNULL(T.UnitType,'')) NOT IN('PALLET','CASE'));
                IF NOT EXISTS(SELECT 1 FROM @Items) THROW 52001,'No available part inventory remains in this unit.',1;
                IF EXISTS(SELECT 1 FROM @Items WHERE NULLIF(LTRIM(RTRIM(PartNo)),'') IS NULL)
                    THROW 52004,'Outbound inventory has no linked part number.',1;
                IF EXISTS(SELECT 1 FROM @Items I LEFT JOIN dbo.MD_Location L WITH(HOLDLOCK) ON L.LocationID=I.LocationNo
                    WHERE NULLIF(LTRIM(RTRIM(I.LocationNo)),'') IS NULL OR L.LocationID IS NULL OR L.LocationLevel<>2
                        OR COALESCE(L.ActiveFlag,1)=0 OR UPPER(ISNULL(L.LocationType,''))<>'FG')
                    THROW 52006,'All outbound inventory must be put away in a registered FG location.',1;
                IF (SELECT COUNT(DISTINCT CustomerID) FROM @Items)>1
                    THROW 52018,'This unit contains products for different customers. Separate them before outbound.',1;
                SET @CustomerID=NULLIF(LTRIM(RTRIM(@CustomerID)),'');
                IF @CustomerID IS NULL AND NOT EXISTS(SELECT 1 FROM @Items WHERE CustomerID IS NULL)
                    SELECT @CustomerID=MIN(CustomerID) FROM @Items;
                IF @CustomerID IS NULL THROW 52017,'Select the outbound customer before confirming shipment.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.MD_Customer WITH(HOLDLOCK) WHERE CustomerID=@CustomerID AND UPPER(ISNULL(Status,'ACTIVE')) NOT IN('INACTIVE','DISABLED'))
                    THROW 52017,'Select a valid active customer from Customer Master.',1;
                IF EXISTS(SELECT 1 FROM @Items WHERE CustomerID<>@CustomerID)
                    THROW 52019,'The selected customer does not match the production LOT customer.',1;
                INSERT dbo.FG_Shipment(LotNo,UnitType,PalletNo,CustomerID,ShippedAt,ShippedBy)
                    VALUES(@Barcode,@UnitType,@PalletNo,@CustomerID,@Now,@OperatorID);
                SET @ShipmentID=CONVERT(int,SCOPE_IDENTITY());
                IF @PalletNo IS NOT NULL
                    UPDATE dbo.MD_Pallet SET ShippedFlag=1,ShippedAt=@Now,ShippedCustomerID=@CustomerID,ReturnedAt=NULL,
                        ModifiedBy=LEFT(@OperatorID,20),ModifiedTS=@Now
                    WHERE PalletNo=@PalletNo;
                INSERT dbo.WH_InventoryTransaction
                    (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
                     ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
                SELECT @Now,'OUT',PartNo,LocationNo,LotNo,Qty,-Qty,0,@UnitType+'_OUTBOUND','FG_OUTBOUND',@ShipmentID,
                    @OperatorID,@UnitType+N' LOT: '+@Barcode,@OperatorID,@Now FROM @Items;
                UPDATE I SET Qty=0,UpdatedAt=@Now FROM dbo.WH_Inventory I JOIN @Scope S ON S.LotNo=I.LotNo WHERE I.Qty>0;
                COMMIT TRANSACTION;
                SELECT @ShipmentID ShipmentID,COUNT(*) ProcessedCount,SUM(Qty) TotalQty,@UnitType UnitType FROM @Items;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
        END;
        """;

    public const string ReturnScan = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @B varchar(80)=LTRIM(RTRIM(ISNULL(@Barcode,'')));
            IF LEN(@B)<3 OR LEN(@B)>50 OR @B COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9_./-]%'
                THROW 52000,'The finished-good return barcode format is invalid.',1;
            DECLARE @P TABLE(LotNo nvarchar(50),ShipmentID int,OutboundBarcode nvarchar(50),
                CustomerID varchar(20),CustomerName nvarchar(200),PartNo varchar(20),PartName nvarchar(200),
                ShippedAt datetime2,Qty decimal(18,3),LotID int);
            INSERT @P
            SELECT TOP(1) T.LotNo,S.ShipmentID,S.LotNo,S.CustomerID,C.CustomerName,
                T.PartNo,COALESCE(NULLIF(I.PartName COLLATE DATABASE_DEFAULT,N''),M.ItemName COLLATE DATABASE_DEFAULT),S.ShippedAt,-T.QtyChange,L.LotID
            FROM dbo.WH_InventoryTransaction T JOIN dbo.FG_Shipment S ON S.ShipmentID=T.SourceID
            JOIN dbo.MD_Customer C ON C.CustomerID=S.CustomerID
            LEFT JOIN dbo.WH_Inventory I ON I.LotNo=T.LotNo
            LEFT JOIN dbo.MD_Item M ON M.ItemNo COLLATE DATABASE_DEFAULT=T.PartNo COLLATE DATABASE_DEFAULT
            LEFT JOIN dbo.tbl_Lot L ON L.LotCode COLLATE DATABASE_DEFAULT=T.LotNo COLLATE DATABASE_DEFAULT
            WHERE T.LotNo=@B AND T.TransactionType='OUT' AND T.SourceType='FG_OUTBOUND' AND T.QtyChange<0
            ORDER BY S.ShippedAt DESC,T.TransactionID DESC;
            IF NOT EXISTS(SELECT 1 FROM @P) THROW 52002,'This LOT has no actual finished-goods shipment history.',1;
            IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R JOIN @P P ON P.ShipmentID=R.ShipmentID AND P.LotNo=R.LotNo)
                THROW 52005,'This LOT was already received as a return for its latest shipment.',1;
            IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WHERE LotNo=@B AND Qty<>0)
                THROW 52015,'This LOT already has inventory. Resolve the existing stock before receiving a return.',1;
            IF EXISTS(SELECT 1 FROM @P WHERE NULLIF(PartName,N'') IS NULL OR Qty<=0)
                THROW 52008,'Verify the shipped part name and quantity.',1;
            SELECT @B Barcode,LotID,LotNo,ShipmentID,OutboundBarcode,CustomerID,CustomerName,PartNo,PartName,ShippedAt,Qty FROM @P;
        END;
        """;

    public const string ReturnReceive = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            DECLARE @Reason varchar(60)=UPPER(LTRIM(RTRIM(ISNULL(@ReturnReason,'')))),@CleanNote nvarchar(500)=NULLIF(LTRIM(RTRIM(@Note)),N'');
            IF NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='FG_RETURN_REASON' AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
                THROW 52009,'Select a valid return reason.',1;
            IF NULLIF(LTRIM(RTRIM(@OperatorID)),'') IS NULL THROW 52014,'A signed-in operator is required.',1;
            DECLARE @P TABLE(Barcode varchar(80),LotID int,LotNo nvarchar(50),ShipmentID int,OutboundBarcode nvarchar(50),
                CustomerID varchar(20),CustomerName nvarchar(200),PartNo varchar(20),PartName nvarchar(200),ShippedAt datetime2,Qty decimal(18,3));
            BEGIN TRY
                BEGIN TRANSACTION;
                BEGIN
            SET NOCOUNT ON;
            DECLARE @ReturnScanB varchar(80)=LTRIM(RTRIM(ISNULL(@Barcode,'')));
            IF LEN(@ReturnScanB)<3 OR LEN(@ReturnScanB)>50 OR @ReturnScanB COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9_./-]%'
                THROW 52000,'The finished-good return barcode format is invalid.',1;
            DECLARE @ReturnScanP TABLE(LotNo nvarchar(50),ShipmentID int,OutboundBarcode nvarchar(50),
                CustomerID varchar(20),CustomerName nvarchar(200),PartNo varchar(20),PartName nvarchar(200),
                ShippedAt datetime2,Qty decimal(18,3),LotID int);
            INSERT @ReturnScanP
            SELECT TOP(1) T.LotNo,S.ShipmentID,S.LotNo,S.CustomerID,C.CustomerName,
                T.PartNo,COALESCE(NULLIF(I.PartName COLLATE DATABASE_DEFAULT,N''),M.ItemName COLLATE DATABASE_DEFAULT),S.ShippedAt,-T.QtyChange,L.LotID
            FROM dbo.WH_InventoryTransaction T JOIN dbo.FG_Shipment S ON S.ShipmentID=T.SourceID
            JOIN dbo.MD_Customer C ON C.CustomerID=S.CustomerID
            LEFT JOIN dbo.WH_Inventory I ON I.LotNo=T.LotNo
            LEFT JOIN dbo.MD_Item M ON M.ItemNo COLLATE DATABASE_DEFAULT=T.PartNo COLLATE DATABASE_DEFAULT
            LEFT JOIN dbo.tbl_Lot L ON L.LotCode COLLATE DATABASE_DEFAULT=T.LotNo COLLATE DATABASE_DEFAULT
            WHERE T.LotNo=@ReturnScanB AND T.TransactionType='OUT' AND T.SourceType='FG_OUTBOUND' AND T.QtyChange<0
            ORDER BY S.ShippedAt DESC,T.TransactionID DESC;
            IF NOT EXISTS(SELECT 1 FROM @ReturnScanP) THROW 52002,'This LOT has no actual finished-goods shipment history.',1;
            IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R JOIN @ReturnScanP P ON P.ShipmentID=R.ShipmentID AND P.LotNo=R.LotNo)
                THROW 52005,'This LOT was already received as a return for its latest shipment.',1;
            IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WHERE LotNo=@ReturnScanB AND Qty<>0)
                THROW 52015,'This LOT already has inventory. Resolve the existing stock before receiving a return.',1;
            IF EXISTS(SELECT 1 FROM @ReturnScanP WHERE NULLIF(PartName,N'') IS NULL OR Qty<=0)
                THROW 52008,'Verify the shipped part name and quantity.',1;
            INSERT @P SELECT @ReturnScanB Barcode,LotID,LotNo,ShipmentID,OutboundBarcode,CustomerID,CustomerName,PartNo,PartName,ShippedAt,Qty FROM @ReturnScanP;
        END;
                DECLARE @Now datetime2=SYSDATETIME(),@ReturnID int,@LotNo nvarchar(50)=(SELECT LotNo FROM @P),@ShipmentID int=(SELECT ShipmentID FROM @P);
                IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WITH(UPDLOCK,HOLDLOCK) WHERE ShipmentID=@ShipmentID AND LotNo=@LotNo)
                    THROW 52005,'This LOT was already returned for this shipment.',1;
                IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@LotNo AND Qty<>0)
                    THROW 52015,'This LOT already has inventory. Resolve the existing stock before receiving a return.',1;
                IF (SELECT TOP(1) SourceID FROM dbo.WH_InventoryTransaction
                    WHERE LotNo=@LotNo AND SourceType='FG_OUTBOUND' AND TransactionType='OUT'
                    ORDER BY TransactionTime DESC,TransactionID DESC)<>@ShipmentID
                    THROW 52004,'The latest shipment changed. Scan the returned LOT again.',1;
                INSERT dbo.FG_CustomerReturn(CustomerID,ShipmentID,LotNo,LotID,PartNo,PartName,ReturnQty,ReturnReason,Note,Status,ReceivedAt,ReceivedBy,CreatedBy,CreatedTS)
                SELECT CustomerID,ShipmentID,LotNo,LotID,PartNo,PartName,Qty,@Reason,@CleanNote,'Open',@Now,@OperatorID,@OperatorID,@Now FROM @P;
                SET @ReturnID=CONVERT(int,SCOPE_IDENTITY());
                MERGE dbo.WH_Inventory WITH(HOLDLOCK) AS T USING(SELECT LotNo,PartNo,PartName,Qty FROM @P) S ON S.LotNo=T.LotNo
                WHEN MATCHED THEN UPDATE SET PartNo=S.PartNo,PartName=S.PartName,Qty=S.Qty,LocationNo=NULL,ParentLotNo=NULL,UpdatedAt=@Now
                WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
                    VALUES(S.LotNo,'PART',S.PartNo,S.PartName,NULL,S.Qty,@Now,@Now,@Now);
                INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LotNo,QtyBefore,QtyChange,QtyAfter,
                    ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
                SELECT @Now,'IN',PartNo,LotNo,0,Qty,Qty,'RETURN','FG_RETURN',@ReturnID,@OperatorID,@CleanNote,@OperatorID,@Now FROM @P;
                COMMIT TRANSACTION;
                SELECT @ReturnID ReturnID,* FROM @P;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
        END;
        """;

    private const string AdjustScanResult = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @ResultScan nvarchar(80)=LTRIM(RTRIM(ISNULL(@ScanText,N'')));
            IF @ResultScan=N'' THROW 51600,'Finished goods Lot No is required.',1;
            IF LEN(@ResultScan)<3 OR @ResultScan COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9-]%'
                THROW 51604,'The barcode format is invalid.',1;
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.WH_Inventory W
                LEFT JOIN dbo.MD_Location ML ON ML.LocationID COLLATE DATABASE_DEFAULT=W.LocationNo
                WHERE UPPER(W.LotNo)=UPPER(@ResultScan)
                  AND (UPPER(COALESCE(ML.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')
            ) THROW 51601,'The specified finished goods Lot No could not be found.',1;

            SELECT N'FG' AS RECEIVE_TYPE,N'N' AS YN,W.LotNo AS LOTNO,W.LotNo AS BARCODE,
                N'dbo.WH_Inventory' AS SOURCE_TABLE,CAST(NULL AS nvarchar(50)) AS NOTENO,
                CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,W.PalletNo AS CASE_NO,W.InvoiceNo AS INVOICE_NO,
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
            WHERE UPPER(W.LotNo)=UPPER(@ResultScan);
        END;
        """;

}
