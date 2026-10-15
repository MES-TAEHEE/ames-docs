-- FG Customer Return: one shipped LOT / part per ReturnID.
-- Run only with coordinated API/Web/PDA deployment and a verified DB backup.
-- No database recreation, seed reset or return-row deletion.
SET ANSI_NULLS ON;
IF OBJECT_ID(N'dbo.FG_Shipment',N'U') IS NOT NULL
BEGIN
    RAISERROR('Legacy return migration is retired. Use migrate_fg_shipment_actual.sql.',16,1);
    SET NOEXEC ON;
END;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.FG_CustomerReturn',N'U') IS NULL
        THROW 52010,'FG_CustomerReturn is missing.',1;
    -- Do not silently discard multi-part legacy JSON when normalizing the row.
    IF COL_LENGTH('dbo.FG_CustomerReturn','ItemsJSON') IS NOT NULL
        EXEC(N'IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R
            WHERE ISJSON(R.ItemsJSON)=1 AND (SELECT COUNT(*) FROM OPENJSON(R.ItemsJSON))>1)
            THROW 52011,''Split multi-part legacy returns before applying this migration.'',1;');
    IF COL_LENGTH('dbo.FG_CustomerReturn','CustomerCode') IS NOT NULL
        EXEC sys.sp_rename N'dbo.FG_CustomerReturn.CustomerCode',N'CustomerID',N'COLUMN';
    IF COL_LENGTH('dbo.FG_CustomerReturn','OriginalShipmentOrderID') IS NOT NULL
        EXEC sys.sp_rename N'dbo.FG_CustomerReturn.OriginalShipmentOrderID',N'ShipmentOrderID',N'COLUMN';
    IF COL_LENGTH('dbo.FG_CustomerReturn','ItemNo') IS NOT NULL
        EXEC sys.sp_rename N'dbo.FG_CustomerReturn.ItemNo',N'PartNo',N'COLUMN';
    IF COL_LENGTH('dbo.FG_CustomerReturn','PartName') IS NULL
        EXEC(N'ALTER TABLE dbo.FG_CustomerReturn ADD PartName nvarchar(200) NULL;');
    EXEC(N'
        IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R
            LEFT JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=R.ShipmentOrderID
            OUTER APPLY(SELECT COUNT(*) Matches FROM dbo.MD_Customer C
                WHERE C.CustomerID COLLATE DATABASE_DEFAULT=O.CustomerCode COLLATE DATABASE_DEFAULT
                   OR C.CustomerCode COLLATE DATABASE_DEFAULT=O.CustomerCode COLLATE DATABASE_DEFAULT) C
            WHERE C.Matches<>1)
            THROW 52012,''Return shipment customer has no unique Customer Master match.'',1;
        UPDATE R SET CustomerID=C.CustomerID,
            PartName=COALESCE(NULLIF(R.PartName,N''''),I.ItemName)
        FROM dbo.FG_CustomerReturn R
        JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=R.ShipmentOrderID
        JOIN dbo.MD_Customer C ON C.CustomerID COLLATE DATABASE_DEFAULT=O.CustomerCode COLLATE DATABASE_DEFAULT
            OR C.CustomerCode COLLATE DATABASE_DEFAULT=O.CustomerCode COLLATE DATABASE_DEFAULT
        LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=R.PartNo COLLATE DATABASE_DEFAULT;
        IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WHERE NULLIF(PartNo,'''') IS NULL OR NULLIF(PartName,N'''') IS NULL)
            THROW 52013,''Resolve missing return part names before applying this migration.'',1;
        ALTER TABLE dbo.FG_CustomerReturn ALTER COLUMN PartName nvarchar(200) NOT NULL;');
    DECLARE @Drop TABLE(Name sysname PRIMARY KEY);
    INSERT @Drop VALUES('ReturnNumber'),('RMANo'),('CustomerClaimID'),('OriginalDeliveryNoteID'),('ItemsJSON'),('NcrID'),('CapaTriggered');
    DECLARE @Sql nvarchar(max)=N'';
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT '+QUOTENAME(o.name)+N';'
    FROM sys.objects o
    WHERE o.parent_object_id=OBJECT_ID(N'dbo.FG_CustomerReturn')
      AND (o.object_id IN(SELECT dc.object_id FROM sys.default_constraints dc JOIN sys.columns c
            ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id JOIN @Drop d ON d.Name=c.name)
        OR o.object_id IN(SELECT ck.object_id FROM sys.check_constraints ck
            JOIN sys.sql_expression_dependencies dep ON dep.referencing_id=ck.object_id
            JOIN sys.columns c ON c.object_id=dep.referenced_id AND c.column_id=dep.referenced_minor_id JOIN @Drop d ON d.Name=c.name)
        OR o.object_id IN(SELECT fc.constraint_object_id FROM sys.foreign_key_columns fc JOIN sys.columns c
            ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN @Drop d ON d.Name=c.name));
    EXEC sys.sp_executesql @Sql;
    SET @Sql=N'';
    SELECT @Sql=@Sql+N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.FG_CustomerReturn;'
    FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.FG_CustomerReturn') AND i.is_primary_key=0
      AND EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        JOIN @Drop d ON d.Name=c.name WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id);
    EXEC sys.sp_executesql @Sql;
    SET @Sql=N'';
    SELECT @Sql=@Sql+N'ALTER TABLE dbo.FG_CustomerReturn DROP COLUMN '+QUOTENAME(c.name)+N';'
    FROM sys.columns c JOIN @Drop d ON d.Name=c.name WHERE c.object_id=OBJECT_ID(N'dbo.FG_CustomerReturn');
    EXEC sys.sp_executesql @Sql;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_SCAN @Barcode varchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @B varchar(80)=LTRIM(RTRIM(ISNULL(@Barcode,'')));
    IF LEN(@B)<3 OR LEN(@B)>80 OR @B COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9_./-]%'
        THROW 52000,'The finished-good return barcode format is invalid.',1;
    DECLARE @P TABLE(LotNo nvarchar(50),ShipmentOrderID int,ShipOrderNumber varchar(40),
        CustomerID varchar(20),CustomerName nvarchar(200),PartNo varchar(20),PartName nvarchar(200),
        ShippedAt datetime2,Qty decimal(18,3),LotID int);
    INSERT @P
    SELECT DISTINCT J.LotNo,O.ShipmentOrderID,O.ShipOrderNumber,C.CustomerID,C.CustomerName,
        J.PartNo,I.ItemName,COALESCE(O.ShippedAt,O.DepartureAt,O.LoadingConfirmedAt),
        COALESCE(NULLIF(J.AllocatedQty,0),NULLIF(J.OrderedQty,0)),L.LotID
    FROM dbo.FG_ShipmentOrder O
    CROSS APPLY OPENJSON(CASE WHEN ISJSON(O.ItemsJSON)=1 THEN O.ItemsJSON ELSE N'[]' END)
        WITH(LotNo nvarchar(50) '$.lotNo',PartNo varchar(20) '$.itemNo',
            OrderedQty decimal(18,3) '$.orderedQty',AllocatedQty decimal(18,3) '$.allocatedQty') J
    LEFT JOIN dbo.tbl_Lot L ON L.LotCode COLLATE DATABASE_DEFAULT=J.LotNo COLLATE DATABASE_DEFAULT
    LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=J.PartNo COLLATE DATABASE_DEFAULT
    LEFT JOIN dbo.MD_Customer C ON C.CustomerID COLLATE DATABASE_DEFAULT=O.CustomerCode COLLATE DATABASE_DEFAULT
        OR C.CustomerCode COLLATE DATABASE_DEFAULT=O.CustomerCode COLLATE DATABASE_DEFAULT
    WHERE J.LotNo COLLATE DATABASE_DEFAULT=@B COLLATE DATABASE_DEFAULT
        AND UPPER(COALESCE(O.Status,''))='SHIPPED'
        AND COALESCE(O.ShippedAt,O.DepartureAt,O.LoadingConfirmedAt) IS NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM @P)
        THROW 52002,'This barcode does not match a shipped finished-good LOT.',1;
    IF (SELECT COUNT(*) FROM @P)<>1
        THROW 52004,'The barcode has multiple shipments or customer matches. Verify the source data.',1;
    IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WHERE LotNo COLLATE DATABASE_DEFAULT=@B COLLATE DATABASE_DEFAULT)
        THROW 52005,'This product was already received as a customer return.',1;
    IF EXISTS(SELECT 1 FROM @P WHERE NULLIF(ShipOrderNumber,'') IS NULL OR NULLIF(CustomerID,'') IS NULL
        OR NULLIF(PartNo,'') IS NULL OR NULLIF(PartName,N'') IS NULL OR COALESCE(Qty,0)<=0)
        THROW 52008,'Verify the shipped customer in Customer Master and the part, name and quantity.',1;
    SELECT @B Barcode,LotID,LotNo,ShipmentOrderID,ShipOrderNumber,CustomerID,CustomerName,
        PartNo,PartName,ShippedAt,Qty FROM @P;
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
    IF NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='FG_RETURN_REASON'
        AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
        THROW 52009,'Select a valid return reason.',1;
    IF NULLIF(LTRIM(RTRIM(@OperatorID)),N'') IS NULL THROW 52014,'A signed-in operator is required.',1;
    DECLARE @P TABLE(Barcode varchar(80),LotID int,LotNo nvarchar(50),ShipmentOrderID int,
        ShipOrderNumber varchar(40),CustomerID varchar(20),CustomerName nvarchar(200),
        PartNo varchar(20),PartName nvarchar(200),ShippedAt datetime2,Qty decimal(18,3));
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT @P EXEC dbo.FG_PDA_RETURN_SCAN @Barcode;
        DECLARE @Now datetime2=SYSDATETIME(),@ReturnID int,@LotNo nvarchar(50)=(SELECT LotNo FROM @P);
        IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@LotNo)
            THROW 52005,'This product was already received as a customer return.',1;
        IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@LotNo AND Qty<>0)
            THROW 52015,'This LOT already has inventory. Resolve the existing stock before receiving a return.',1;
        INSERT dbo.FG_CustomerReturn
            (CustomerID,ShipmentOrderID,LotNo,LotID,PartNo,PartName,ReturnQty,
             ReturnReason,Note,Status,ReceivedAt,ReceivedBy,CreatedBy,CreatedTS)
        SELECT CustomerID,ShipmentOrderID,LotNo,LotID,PartNo,PartName,Qty,@Reason,@CleanNote,
            'Open',@Now,@OperatorID,LEFT(@OperatorID,20),@Now FROM @P;
        SET @ReturnID=CONVERT(int,SCOPE_IDENTITY());
        MERGE dbo.WH_Inventory WITH(HOLDLOCK) AS T
        USING(SELECT LotNo,PartNo,PartName,Qty FROM @P) S ON S.LotNo=T.LotNo
        WHEN MATCHED THEN UPDATE SET PartNo=S.PartNo,PartName=S.PartName,Qty=S.Qty,LocationNo=NULL,UpdatedAt=@Now
        WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
            VALUES(S.LotNo,'PART',S.PartNo,S.PartName,NULL,S.Qty,@Now,@Now,@Now);
        INSERT dbo.WH_InventoryTransaction
            (TransactionTime,TransactionType,PartNo,LotNo,QtyBefore,QtyChange,QtyAfter,
             ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
        SELECT @Now,'IN',PartNo,LotNo,0,Qty,Qty,'RETURN','FG_RETURN',@ReturnID,
            @OperatorID,@CleanNote,LEFT(@OperatorID,20),@Now FROM @P;
        COMMIT TRANSACTION;
        SELECT @ReturnID ReturnID,* FROM @P;
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
        WHERE UPPER(COALESCE(T.SourceType,'')) LIKE 'FG%' AND T.SourceType<>'FG_RETURN'
        UNION ALL
        SELECT P.CreatedTS,CONCAT('IN-',P.PutAwayID),P.LotNo,P.ItemNo COLLATE DATABASE_DEFAULT,
            P.ActualLoc COLLATE DATABASE_DEFAULT,P.Qty,N'Put-Away','IN',
            COALESCE(P.OperatorID,P.CreatedBy) COLLATE DATABASE_DEFAULT,NULL,
            P.ContainerBarcode COLLATE DATABASE_DEFAULT,NULL,NULL,NULL,NULL,N'FG_PutAway',
            P.ContainerBarcode COLLATE DATABASE_DEFAULT
        FROM dbo.FG_PutAway P WHERE UPPER(COALESCE(P.Status,'')) NOT IN('CANCELLED','CANCELED')
        UNION ALL
        SELECT COALESCE(R.ReceivedAt,R.CreatedTS),CONCAT('RETURN-',R.ReturnID),R.LotNo,
            R.PartNo COLLATE DATABASE_DEFAULT,NULL,R.ReturnQty,N'Return','IN',
            COALESCE(R.ReceivedBy,R.CreatedBy) COLLATE DATABASE_DEFAULT,
            R.ReturnReason COLLATE DATABASE_DEFAULT,R.Note COLLATE DATABASE_DEFAULT,
            NULL,NULL,NULL,NULL,N'FG_CustomerReturn',CONVERT(nvarchar(80),R.ReturnID) COLLATE DATABASE_DEFAULT
        FROM dbo.FG_CustomerReturn R WHERE UPPER(COALESCE(R.Status,'')) NOT IN('CANCELLED','CANCELED','REJECTED')
    )
    SELECT ROW_NUMBER() OVER(ORDER BY E.EventTime DESC,E.EventID DESC) AS ROW_NO,
        COALESCE(NULLIF(E.LotNo COLLATE DATABASE_DEFAULT,''),'N/A') LOTNO,E.PartNo PARTNO,CONVERT(nvarchar(10),E.EventTime,23) WDATE,
        CONVERT(nvarchar(8),E.EventTime,108) WTIME,COALESCE(NULLIF(E.LocationNo COLLATE DATABASE_DEFAULT,''),'N/A') LOCATION_NO,
        COALESCE(E.Qty,0) QTY,I.DefaultUOM UNIT,E.Status STATUS,E.Direction DIRECTION,
        COALESCE(NULLIF(U.UserName COLLATE DATABASE_DEFAULT,''),E.Worker COLLATE DATABASE_DEFAULT) WORKER_ID,E.ReasonCode REASON_CODE,E.ReasonNote REASON_NOTE,
        E.Supervisor SUPERVISOR,E.BeforeQty BEFORE_QTY,E.DeltaQty DELTA_QTY,E.AfterQty AFTER_QTY,
        NULL BEFORE_STATUS,NULL AFTER_STATUS,NULL BEFORE_LOCATION,NULL AFTER_LOCATION,E.Source SOURCE,E.Reference NOTE
    FROM Events E LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=E.PartNo
    LEFT JOIN dbo.AspNetUsers U ON U.Id COLLATE DATABASE_DEFAULT=E.Worker
    WHERE E.EventTime>=@From AND E.EventTime<DATEADD(day,1,@To)
      AND (@Search IS NULL OR E.LotNo COLLATE DATABASE_DEFAULT LIKE @Search
        OR E.PartNo COLLATE DATABASE_DEFAULT LIKE @Search
        OR I.ItemName COLLATE DATABASE_DEFAULT LIKE @Search
        OR E.LocationNo COLLATE DATABASE_DEFAULT LIKE @Search
        OR E.Reference COLLATE DATABASE_DEFAULT LIKE @Search)
    ORDER BY E.EventTime DESC,E.EventID DESC;
END;
GO
