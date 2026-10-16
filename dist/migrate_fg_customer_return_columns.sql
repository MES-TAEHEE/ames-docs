-- FG Customer Return: one shipped LOT / part per ReturnID.
-- Run only with coordinated API/Web/PDA deployment and a verified DB backup.
-- No database recreation, seed reset or return-row deletion.
-- Return/history commands are maintained in PdaFinishedGoodsSql.cs, not database procedures.
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
