-- Actual barcode shipments only. No stock deletion, rebuilding, or shipment seeds.
-- Apply to an explicitly selected database. Deploy matching API/Web/PDA afterwards.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
GO
IF OBJECT_ID(N'dbo.FG_ShipmentOrder',N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.FG_Shipment',N'U') IS NOT NULL
        THROW 52100,'Both shipment tables exist. Resolve the migration state first.',1;
    IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn)
        THROW 52101,'Existing customer returns require an explicit shipment mapping before migration.',1;
    IF EXISTS(SELECT 1 FROM dbo.FG_ShipmentOrder WITH(UPDLOCK,HOLDLOCK)
        WHERE CreatedBy IS NULL OR CreatedBy NOT IN('fg-seed','pda-return-test','pda-ppt-fg-release',
            'pda-ppt-fg-loading','pda-ppt-fg-return','pda-ppt-fg-history','pda-fg-six-demo','fg-web-demo'))
        THROW 52102,'Non-demo shipment orders exist. Do not discard real orders.',1;
    IF EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction WITH(UPDLOCK,HOLDLOCK) WHERE SourceType='FG_OUTBOUND')
        THROW 52103,'Existing actual outbound transactions require migration before retiring shipment orders.',1;
    DECLARE @Sql nvarchar(max)=N'';
    SELECT @Sql=@Sql+N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(parent_object_id))
        +N' DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.foreign_keys WHERE referenced_object_id=OBJECT_ID(N'dbo.FG_ShipmentOrder');
    EXEC sys.sp_executesql @Sql;
    DELETE FROM dbo.FG_ShipmentOrder;
    EXEC sys.sp_rename N'dbo.FG_ShipmentOrder',N'FG_Shipment';
    EXEC sys.sp_rename N'dbo.FG_Shipment.ShipmentOrderID',N'ShipmentID',N'COLUMN';
    SET @Sql=N'';
    SELECT @Sql=@Sql+N'DROP INDEX '+QUOTENAME(name)+N' ON dbo.FG_Shipment;'
    FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_Shipment') AND is_primary_key=0 AND index_id>0;
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_Shipment DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FG_Shipment');
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_Shipment DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FG_Shipment');
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_Shipment DROP CONSTRAINT '+QUOTENAME(name)+N';'
    FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.FG_Shipment');
    EXEC sys.sp_executesql @Sql;
    SET @Sql=N'';
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_Shipment DROP COLUMN '+QUOTENAME(name)+N';'
    FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FG_Shipment') AND name<>N'ShipmentID';
    EXEC sys.sp_executesql @Sql;
    IF OBJECT_ID(N'dbo.PK_FG_ShipmentOrder',N'PK') IS NOT NULL
        EXEC sys.sp_rename N'dbo.PK_FG_ShipmentOrder',N'PK_FG_Shipment',N'OBJECT';
END;
GO
IF OBJECT_ID(N'dbo.FG_Shipment',N'U') IS NULL
    CREATE TABLE dbo.FG_Shipment(ShipmentID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FG_Shipment PRIMARY KEY);
IF COL_LENGTH(N'dbo.FG_Shipment',N'OutboundBarcode') IS NULL
    ALTER TABLE dbo.FG_Shipment ADD OutboundBarcode nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
        UnitType varchar(10) COLLATE DATABASE_DEFAULT NOT NULL,
        CustomerID varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
        ShippedAt datetime2 NOT NULL CONSTRAINT DF_FG_Shipment_ShippedAt DEFAULT SYSDATETIME(),
        ShippedBy varchar(20) COLLATE DATABASE_DEFAULT NOT NULL;
GO
IF COL_LENGTH(N'dbo.FG_CustomerReturn',N'ShipmentOrderID') IS NOT NULL
    EXEC sys.sp_rename N'dbo.FG_CustomerReturn.ShipmentOrderID',N'ShipmentID',N'COLUMN';
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_Shipment_Customer')
    ALTER TABLE dbo.FG_Shipment WITH CHECK ADD CONSTRAINT FK_FG_Shipment_Customer
        FOREIGN KEY(CustomerID) REFERENCES dbo.MD_Customer(CustomerID);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Shipment')
    ALTER TABLE dbo.FG_CustomerReturn WITH CHECK ADD CONSTRAINT FK_FG_CustomerReturn_Shipment
        FOREIGN KEY(ShipmentID) REFERENCES dbo.FG_Shipment(ShipmentID);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_Shipment') AND name=N'IX_FG_Shipment_ShippedAt')
    CREATE INDEX IX_FG_Shipment_ShippedAt ON dbo.FG_Shipment(ShippedAt DESC,ShipmentID DESC) INCLUDE(CustomerID,OutboundBarcode,UnitType,ShippedBy);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction') AND name=N'IX_WH_InventoryTransaction_Source')
    CREATE INDEX IX_WH_InventoryTransaction_Source ON dbo.WH_InventoryTransaction(SourceType,SourceID,TransactionType)
        INCLUDE(LotNo,PartNo,QtyChange,LocationNo,TransactionTime);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND name=N'UX_FG_CustomerReturn_ShipmentLot')
    CREATE UNIQUE INDEX UX_FG_CustomerReturn_ShipmentLot ON dbo.FG_CustomerReturn(ShipmentID,LotNo) WHERE LotNo IS NOT NULL;
GO
CREATE OR ALTER PROCEDURE dbo.FG_PDA_OUTBOUND_SCAN @Barcode nvarchar(50)
AS
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
        FROM dbo.tbl_Lot L JOIN dbo.PR_ImgLot P ON P.LotID=L.LotID
        JOIN dbo.MD_Customer M ON M.CustomerID COLLATE DATABASE_DEFAULT=P.CustomerCode COLLATE DATABASE_DEFAULT OR M.CustomerCode COLLATE DATABASE_DEFAULT=P.CustomerCode COLLATE DATABASE_DEFAULT
        WHERE L.LotCode COLLATE DATABASE_DEFAULT=T.LotNo COLLATE DATABASE_DEFAULT HAVING COUNT(DISTINCT M.CustomerID)=1
    ) C
    WHERE T.Qty>0 AND T.PartNo IS NOT NULL
      AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory X WHERE X.ParentLotNo=T.LotNo AND X.Qty>0)
    OPTION(MAXRECURSION 100);
    IF NOT EXISTS(SELECT 1 FROM @Items) THROW 52001,'No available part inventory remains in this unit.',1;
    IF EXISTS(SELECT 1 FROM @Items I LEFT JOIN dbo.MD_Location L ON L.LocationID=I.LocationNo
        WHERE NULLIF(LTRIM(RTRIM(I.LocationNo)),'') IS NULL OR L.LocationID IS NULL OR UPPER(ISNULL(L.LocationType,''))<>'FG')
        THROW 52006,'All outbound inventory must be put away in a registered FG location.',1;
    IF (SELECT COUNT(DISTINCT CustomerID) FROM @Items)>1
        THROW 52018,'This unit contains products for different customers. Separate them before outbound.',1;
    DECLARE @CustomerID varchar(20);
    IF NOT EXISTS(SELECT 1 FROM @Items WHERE CustomerID IS NULL) SELECT @CustomerID=MIN(CustomerID) FROM @Items;
    SELECT @Barcode OutboundBarcode,@UnitType UnitType,@LocationNo LocationNo,SUM(Qty) TotalQty,COUNT_BIG(*) ItemCount,
        @CustomerID CustomerID,(SELECT CustomerName FROM dbo.MD_Customer WHERE CustomerID=@CustomerID) CustomerName FROM @Items;
    SELECT LotNo,UnitType,PartNo,PartName,Qty,LocationNo FROM @Items ORDER BY LotNo;
END;
GO
CREATE OR ALTER PROCEDURE dbo.FG_PDA_OUTBOUND_COMPLETE
    @Barcode nvarchar(50),@OperatorID varchar(20),@CustomerID varchar(20)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @UnitType varchar(10),@Now datetime2=SYSDATETIME(),@ShipmentID int;
        IF NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode)
            THROW 52000,'No inventory record exists for this barcode.',1;
        SELECT @UnitType=UnitType FROM dbo.WH_Inventory WHERE LotNo=@Barcode;
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
        DECLARE @Items TABLE(LotNo nvarchar(50) PRIMARY KEY,PartNo varchar(20),PartName nvarchar(200),
            LocationNo varchar(20),Qty decimal(18,3),CustomerID varchar(20));
        INSERT @Items
        SELECT T.LotNo,T.PartNo,T.PartName,T.LocationNo,T.Qty,C.CustomerID
        FROM @Scope T OUTER APPLY
        (
            SELECT MIN(M.CustomerID) CustomerID
            FROM dbo.tbl_Lot L JOIN dbo.PR_ImgLot P ON P.LotID=L.LotID
            JOIN dbo.MD_Customer M ON M.CustomerID COLLATE DATABASE_DEFAULT=P.CustomerCode COLLATE DATABASE_DEFAULT OR M.CustomerCode COLLATE DATABASE_DEFAULT=P.CustomerCode COLLATE DATABASE_DEFAULT
            WHERE L.LotCode COLLATE DATABASE_DEFAULT=T.LotNo COLLATE DATABASE_DEFAULT HAVING COUNT(DISTINCT M.CustomerID)=1
        ) C
        WHERE T.Qty>0 AND NOT EXISTS(SELECT 1 FROM @Scope X WHERE X.ParentLotNo=T.LotNo AND X.Qty>0)
          AND (T.PartNo IS NOT NULL OR UPPER(ISNULL(T.UnitType,'')) NOT IN('PALLET','CASE'));
        IF NOT EXISTS(SELECT 1 FROM @Items) THROW 52001,'No available part inventory remains in this unit.',1;
        IF EXISTS(SELECT 1 FROM @Items WHERE NULLIF(LTRIM(RTRIM(PartNo)),'') IS NULL)
            THROW 52004,'Outbound inventory has no linked part number.',1;
        IF EXISTS(SELECT 1 FROM @Items I LEFT JOIN dbo.MD_Location L WITH(HOLDLOCK) ON L.LocationID=I.LocationNo
            WHERE NULLIF(LTRIM(RTRIM(I.LocationNo)),'') IS NULL OR L.LocationID IS NULL OR UPPER(ISNULL(L.LocationType,''))<>'FG')
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
        INSERT dbo.FG_Shipment(OutboundBarcode,UnitType,CustomerID,ShippedAt,ShippedBy)
            VALUES(@Barcode,@UnitType,@CustomerID,@Now,@OperatorID);
        SET @ShipmentID=CONVERT(int,SCOPE_IDENTITY());
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
GO
CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_SCAN @Barcode varchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @B varchar(80)=LTRIM(RTRIM(ISNULL(@Barcode,'')));
    IF LEN(@B)<3 OR LEN(@B)>50 OR @B COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9_./-]%'
        THROW 52000,'The finished-good return barcode format is invalid.',1;
    DECLARE @P TABLE(LotNo nvarchar(50),ShipmentID int,OutboundBarcode nvarchar(50),
        CustomerID varchar(20),CustomerName nvarchar(200),PartNo varchar(20),PartName nvarchar(200),
        ShippedAt datetime2,Qty decimal(18,3),LotID int);
    INSERT @P
    SELECT TOP(1) T.LotNo,S.ShipmentID,S.OutboundBarcode,S.CustomerID,C.CustomerName,
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
GO
CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_RECEIVE
    @Barcode varchar(80),@ReturnReason varchar(60),@Note nvarchar(500)=NULL,@OperatorID varchar(20)
AS
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
        INSERT @P EXEC dbo.FG_PDA_RETURN_SCAN @Barcode;
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
GO
-- Retired scenario reset must never manufacture actual shipments or returns.
CREATE OR ALTER PROCEDURE dbo.FG_PDA_PPT_TEST_RESET @Screen varchar(10)
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51700,'Legacy FG scenario seeding is retired. Use actual barcode operations.',1;
END;
GO
COMMIT TRANSACTION;
GO
