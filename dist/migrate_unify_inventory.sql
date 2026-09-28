USE [AMES_DEV];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DROP TRIGGER IF EXISTS dbo.TR_WH_Inventory_SyncUnifiedColumns;
DROP TRIGGER IF EXISTS dbo.TR_WH_OLD_Inventory_SyncUnifiedInventory;
DROP TRIGGER IF EXISTS dbo.TR_FG_Inventory_SyncUnifiedInventory;
GO

-- Rename the legacy table once. A rerun leaves the new canonical table untouched.
IF OBJECT_ID(N'dbo.WH_OLD_Inventory',N'U') IS NULL
   AND OBJECT_ID(N'dbo.WH_Inventory',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.WH_Inventory',N'InventoryID') IS NOT NULL
    EXEC sys.sp_rename N'dbo.WH_Inventory',N'WH_OLD_Inventory';
GO

IF OBJECT_ID(N'dbo.WH_OLD_Inventory',N'U') IS NULL
    THROW 51600,'dbo.WH_OLD_Inventory does not exist.',1;
GO

-- A prior transitional build added canonical columns to the legacy table.
-- Make those columns optional so original legacy INSERT statements continue to work.
IF COL_LENGTH(N'dbo.WH_OLD_Inventory',N'LotNo') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory') AND name=N'FK_WH_Inventory_ParentLot')
        ALTER TABLE dbo.WH_OLD_Inventory DROP CONSTRAINT FK_WH_Inventory_ParentLot;

    DECLARE @PkName sysname,@PkColumn sysname,@Sql nvarchar(500);
    SELECT @PkName=kc.name,@PkColumn=c.name
    FROM sys.key_constraints kc
    JOIN sys.index_columns ic ON ic.object_id=kc.parent_object_id AND ic.index_id=kc.unique_index_id AND ic.key_ordinal=1
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE kc.parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory') AND kc.type='PK';

    IF @PkName IS NOT NULL AND @PkColumn<>N'InventoryID'
    BEGIN
        SET @Sql=N'ALTER TABLE dbo.WH_OLD_Inventory DROP CONSTRAINT '+QUOTENAME(@PkName);
        EXEC sys.sp_executesql @Sql;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory') AND type='PK')
        ALTER TABLE dbo.WH_OLD_Inventory ADD CONSTRAINT PK_WH_OLD_Inventory PRIMARY KEY CLUSTERED(InventoryID);

    ALTER TABLE dbo.WH_OLD_Inventory ALTER COLUMN LotNo nvarchar(50) NULL;
    ALTER TABLE dbo.WH_OLD_Inventory ALTER COLUMN UnitType varchar(10) NULL;
    ALTER TABLE dbo.WH_OLD_Inventory ALTER COLUMN LocationNo varchar(50) NULL;
    ALTER TABLE dbo.WH_OLD_Inventory ALTER COLUMN Qty decimal(18,3) NULL;
    ALTER TABLE dbo.WH_OLD_Inventory ALTER COLUMN InventoryStatus varchar(20) NULL;
    ALTER TABLE dbo.WH_OLD_Inventory ALTER COLUMN ReceivedAt datetime2(7) NULL;
    ALTER TABLE dbo.WH_OLD_Inventory ALTER COLUMN CreatedAt datetime2(7) NULL;
    ALTER TABLE dbo.WH_OLD_Inventory ALTER COLUMN UpdatedAt datetime2(7) NULL;
END;
GO

DECLARE @OldPk sysname=(SELECT name FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory') AND type='PK');
IF @OldPk IS NOT NULL AND @OldPk<>N'PK_WH_OLD_Inventory'
BEGIN
    DECLARE @OldPkQualified nvarchar(300)=N'dbo.'+QUOTENAME(@OldPk);
    EXEC sys.sp_rename @OldPkQualified,N'PK_WH_OLD_Inventory',N'OBJECT';
END;

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name=N'DF_WH_Inventory_UnitType' AND parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory'))
    EXEC sys.sp_rename N'dbo.DF_WH_Inventory_UnitType',N'DF_WH_OLD_Inventory_UnitType',N'OBJECT';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name=N'DF_WH_Inventory_Qty' AND parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory'))
    EXEC sys.sp_rename N'dbo.DF_WH_Inventory_Qty',N'DF_WH_OLD_Inventory_Qty',N'OBJECT';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name=N'DF_WH_Inventory_InventoryStatus' AND parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory'))
    EXEC sys.sp_rename N'dbo.DF_WH_Inventory_InventoryStatus',N'DF_WH_OLD_Inventory_InventoryStatus',N'OBJECT';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name=N'DF_WH_Inventory_CreatedAt' AND parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory'))
    EXEC sys.sp_rename N'dbo.DF_WH_Inventory_CreatedAt',N'DF_WH_OLD_Inventory_CreatedAt',N'OBJECT';
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name=N'DF_WH_Inventory_UpdatedAt' AND parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory'))
    EXEC sys.sp_rename N'dbo.DF_WH_Inventory_UpdatedAt',N'DF_WH_OLD_Inventory_UpdatedAt',N'OBJECT';
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_WH_Inventory_UnitType' AND parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory'))
    EXEC sys.sp_rename N'dbo.CK_WH_Inventory_UnitType',N'CK_WH_OLD_Inventory_UnitType',N'OBJECT';
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_WH_Inventory_Qty' AND parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory'))
    EXEC sys.sp_rename N'dbo.CK_WH_Inventory_Qty',N'CK_WH_OLD_Inventory_Qty',N'OBJECT';
GO

IF OBJECT_ID(N'dbo.WH_Inventory',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_Inventory
    (
        LotNo nvarchar(50) NOT NULL,
        UnitType varchar(10) NOT NULL CONSTRAINT DF_WH_Inventory_UnitType DEFAULT('PART'),
        ParentLotNo nvarchar(50) NULL,
        PartNo varchar(50) NULL,
        PartName nvarchar(200) NULL,
        PalletNo nvarchar(50) NULL,
        CaseNo nvarchar(50) NULL,
        BoxNo nvarchar(50) NULL,
        LocationNo varchar(50) NULL,
        Qty decimal(18,3) NOT NULL CONSTRAINT DF_WH_Inventory_Qty DEFAULT(0),
        InvoiceNo nvarchar(50) NULL,
        DeliveryNoteNo nvarchar(30) NULL,
        ReceivedAt datetime2(7) NOT NULL,
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_WH_Inventory_CreatedAt DEFAULT(sysdatetime()),
        UpdatedAt datetime2(7) NOT NULL CONSTRAINT DF_WH_Inventory_UpdatedAt DEFAULT(sysdatetime()),
        CONSTRAINT PK_WH_Inventory PRIMARY KEY CLUSTERED(LotNo),
        CONSTRAINT FK_WH_Inventory_ParentLot FOREIGN KEY(ParentLotNo) REFERENCES dbo.WH_Inventory(LotNo),
        CONSTRAINT CK_WH_Inventory_UnitType CHECK(UnitType IN('PALLET','CASE','BOX','PART')),
        CONSTRAINT CK_WH_Inventory_Qty CHECK(Qty>=0)
    );
END;
GO

IF COL_LENGTH(N'dbo.WH_Inventory',N'PartName') IS NULL
    ALTER TABLE dbo.WH_Inventory ADD PartName nvarchar(200) NULL;
IF COL_LENGTH(N'dbo.WH_Inventory',N'DeliveryNoteNo') IS NULL
    ALTER TABLE dbo.WH_Inventory ADD DeliveryNoteNo nvarchar(30) NULL;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_DeliveryLineID')
    DROP INDEX IX_WH_Inventory_DeliveryLineID ON dbo.WH_Inventory;
IF COL_LENGTH(N'dbo.WH_Inventory',N'DeliveryLineID') IS NOT NULL
    ALTER TABLE dbo.WH_Inventory DROP COLUMN DeliveryLineID;
IF COL_LENGTH(N'dbo.WH_InventoryTransaction',N'LotNo') IS NULL
    ALTER TABLE dbo.WH_InventoryTransaction ADD LotNo nvarchar(50) NULL;
GO

IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory')
      AND name=N'LocationNo'
      AND is_nullable=0
)
    ALTER TABLE dbo.WH_Inventory ALTER COLUMN LocationNo varchar(50) NULL;
GO

IF OBJECT_ID(N'dbo.SCM_DeliveryBox',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SCM_DeliveryLine',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_PurchaseOrder',N'U') IS NOT NULL
BEGIN
    UPDATE W
       SET InvoiceNo=PO.PoNumber
    FROM dbo.WH_Inventory W
    JOIN dbo.SCM_DeliveryBox B ON B.BoxNumber=COALESCE(W.BoxNo,W.LotNo)
    JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID=B.DeliveryLineID
    JOIN dbo.WH_PurchaseOrder PO ON PO.PoID=DL.PoID
    WHERE W.DeliveryNoteNo IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(W.InvoiceNo)),N'') IS NULL;
END;
GO

-- Preserve data from the short-lived hybrid layout, then return the legacy
-- table to its original column set.
IF COL_LENGTH(N'dbo.WH_OLD_Inventory',N'LotNo') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        MERGE dbo.WH_Inventory AS Target
        USING
        (
            SELECT W.LotNo,W.UnitType,W.ParentLotNo,W.PartNo,M.ItemName PartName,W.PalletNo,W.CaseNo,W.BoxNo,
                   W.LocationNo,W.Qty,W.InventoryStatus,W.InvoiceNo,W.ReceivedAt,W.CreatedAt,W.UpdatedAt
            FROM dbo.WH_OLD_Inventory W
            LEFT JOIN dbo.MD_Item M ON M.ItemNo=W.PartNo
            WHERE W.LotNo IS NOT NULL AND W.Qty>0
              AND UPPER(COALESCE(W.InventoryStatus,''AVAILABLE'')) NOT IN(''CANCELED'',''CANCELLED'',''RELEASED'',''PICKED'',''SHIPPED'',''DELIVERED'',''CLOSED'')
        ) AS Source ON Target.LotNo=Source.LotNo
        WHEN MATCHED THEN UPDATE SET UnitType=Source.UnitType,PartNo=Source.PartNo,PartName=Source.PartName,
            PalletNo=Source.PalletNo,CaseNo=Source.CaseNo,BoxNo=Source.BoxNo,LocationNo=Source.LocationNo,
            Qty=Source.Qty,InvoiceNo=Source.InvoiceNo,
            ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
        WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,PalletNo,CaseNo,BoxNo,LocationNo,Qty,InvoiceNo,ReceivedAt,CreatedAt,UpdatedAt)
        VALUES(Source.LotNo,Source.UnitType,Source.PartNo,Source.PartName,Source.PalletNo,Source.CaseNo,Source.BoxNo,Source.LocationNo,Source.Qty,Source.InvoiceNo,Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt);';

    DECLARE @CleanupSql nvarchar(max)=N'';
    SELECT @CleanupSql+=N'DROP INDEX '+QUOTENAME(I.name)+N' ON dbo.WH_OLD_Inventory;'
    FROM sys.indexes I
    WHERE I.object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory')
      AND I.is_primary_key=0 AND I.is_unique_constraint=0
      AND EXISTS
      (
          SELECT 1 FROM sys.index_columns IC JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
          WHERE IC.object_id=I.object_id AND IC.index_id=I.index_id
            AND C.name IN(N'LotNo',N'UnitType',N'ParentLotNo',N'PartNo',N'PalletNo',N'CaseNo',N'BoxNo',N'LocationNo',N'Qty',N'InventoryStatus',N'InvoiceNo',N'ReceivedAt',N'CreatedAt',N'UpdatedAt',N'RowVersion')
      );
    SELECT @CleanupSql+=N'ALTER TABLE dbo.WH_OLD_Inventory DROP CONSTRAINT '+QUOTENAME(DC.name)+N';'
    FROM sys.default_constraints DC JOIN sys.columns C ON C.object_id=DC.parent_object_id AND C.column_id=DC.parent_column_id
    WHERE DC.parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory')
      AND C.name IN(N'LotNo',N'UnitType',N'ParentLotNo',N'PartNo',N'PalletNo',N'CaseNo',N'BoxNo',N'LocationNo',N'Qty',N'InventoryStatus',N'InvoiceNo',N'ReceivedAt',N'CreatedAt',N'UpdatedAt',N'RowVersion');
    SELECT @CleanupSql+=N'ALTER TABLE dbo.WH_OLD_Inventory DROP CONSTRAINT '+QUOTENAME(CC.name)+N';'
    FROM sys.check_constraints CC
    WHERE CC.parent_object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory');
    IF @CleanupSql<>N'' EXEC sys.sp_executesql @CleanupSql;

    DECLARE @DropColumns nvarchar(max)=N'';
    SELECT @DropColumns=STRING_AGG(QUOTENAME(C.name),N',') WITHIN GROUP(ORDER BY C.column_id)
    FROM sys.columns C
    WHERE C.object_id=OBJECT_ID(N'dbo.WH_OLD_Inventory')
      AND C.name IN(N'LotNo',N'UnitType',N'ParentLotNo',N'PartNo',N'PalletNo',N'CaseNo',N'BoxNo',N'LocationNo',N'Qty',N'InventoryStatus',N'InvoiceNo',N'ReceivedAt',N'CreatedAt',N'UpdatedAt',N'RowVersion');
    IF @DropColumns<>N''
    BEGIN
        DECLARE @DropColumnsSql nvarchar(max)=N'ALTER TABLE dbo.WH_OLD_Inventory DROP COLUMN '+@DropColumns+N';';
        EXEC sys.sp_executesql @DropColumnsSql;
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_ParentLotNo')
    CREATE INDEX IX_WH_Inventory_ParentLotNo ON dbo.WH_Inventory(ParentLotNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_PartNo')
    CREATE INDEX IX_WH_Inventory_PartNo ON dbo.WH_Inventory(PartNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_PalletNo')
    CREATE INDEX IX_WH_Inventory_PalletNo ON dbo.WH_Inventory(PalletNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_CaseNo')
    CREATE INDEX IX_WH_Inventory_CaseNo ON dbo.WH_Inventory(CaseNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_BoxNo')
    CREATE INDEX IX_WH_Inventory_BoxNo ON dbo.WH_Inventory(BoxNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_LocationNo')
    CREATE INDEX IX_WH_Inventory_LocationNo ON dbo.WH_Inventory(LocationNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_FIFO')
    CREATE INDEX IX_WH_Inventory_FIFO ON dbo.WH_Inventory(PartNo,ReceivedAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_Inventory') AND name=N'IX_WH_Inventory_DeliveryNoteNo')
    CREATE INDEX IX_WH_Inventory_DeliveryNoteNo ON dbo.WH_Inventory(DeliveryNoteNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.WH_InventoryTransaction') AND name=N'IX_WH_InventoryTransaction_LotNo')
    CREATE INDEX IX_WH_InventoryTransaction_LotNo ON dbo.WH_InventoryTransaction(LotNo);
GO

;WITH Package AS
(
    SELECT P.*,ROW_NUMBER() OVER(PARTITION BY P.LotID ORDER BY COALESCE(P.ReceivedAt,P.ModifiedTS,P.CreatedTS) DESC,P.InboundPackageID DESC) RowNo
    FROM dbo.WH_InboundPackage P
), LegacySource AS
(
    SELECT
        COALESCE(NULLIF(L.LotCode,N''),CONCAT(N'LEGACY-WH-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),W.InventoryID),10))) LotNo,
        COALESCE(W.ItemNo,L.ItemNo) PartNo,M.ItemName PartName,P.CaseNo,P.BoxBarcode BoxNo,W.LocationID LocationNo,
        COALESCE(W.OnHandQty,0) Qty,
        CASE WHEN UPPER(COALESCE(W.Status,'RECEIVED')) IN('RECEIVED','OK','STORED') THEN 'AVAILABLE' ELSE UPPER(W.Status) END InventoryStatus,
        P.InvoiceNo,
        COALESCE(W.LastReceivedAt,P.ReceivedAt,W.CreatedTS,sysdatetime()) ReceivedAt,
        COALESCE(W.CreatedTS,W.LastReceivedAt,P.CreatedTS,sysdatetime()) CreatedAt,
        COALESCE(W.ModifiedTS,W.CreatedTS,W.LastReceivedAt,P.ModifiedTS,P.CreatedTS,sysdatetime()) UpdatedAt,
        ROW_NUMBER() OVER(PARTITION BY COALESCE(NULLIF(L.LotCode,N''),CONCAT(N'LEGACY-WH-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),W.InventoryID),10))) ORDER BY W.InventoryID DESC) RowNo
    FROM dbo.WH_OLD_Inventory W
    LEFT JOIN dbo.tbl_Lot L ON L.LotID=W.LotID
    LEFT JOIN dbo.MD_Item M ON M.ItemNo=COALESCE(W.ItemNo,L.ItemNo)
    LEFT JOIN Package P ON P.LotID=W.LotID AND P.RowNo=1
    WHERE COALESCE(W.OnHandQty,0)>0
      AND UPPER(COALESCE(W.Status,'RECEIVED')) NOT IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED')
)
MERGE dbo.WH_Inventory AS Target
USING (SELECT * FROM LegacySource WHERE RowNo=1) AS Source ON Target.LotNo=Source.LotNo
WHEN MATCHED THEN UPDATE SET PartNo=Source.PartNo,PartName=Source.PartName,CaseNo=Source.CaseNo,BoxNo=Source.BoxNo,
    LocationNo=Source.LocationNo,Qty=Source.Qty,InvoiceNo=Source.InvoiceNo,
    ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,CaseNo,BoxNo,LocationNo,Qty,InvoiceNo,ReceivedAt,CreatedAt,UpdatedAt)
VALUES(Source.LotNo,'PART',Source.PartNo,Source.PartName,Source.CaseNo,Source.BoxNo,Source.LocationNo,Source.Qty,Source.InvoiceNo,Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt);
GO

IF OBJECT_ID(N'dbo.INV_Inventory',N'U') IS NOT NULL
BEGIN
    MERGE dbo.WH_Inventory AS Target
    USING dbo.INV_Inventory AS Source ON Target.LotNo=Source.LotNo
    WHEN MATCHED THEN UPDATE SET UnitType=Source.UnitType,ParentLotNo=Source.ParentLotNo,PartNo=Source.PartNo,
        PalletNo=Source.PalletNo,CaseNo=Source.CaseNo,BoxNo=Source.BoxNo,LocationNo=Source.LocationNo,Qty=Source.Qty,
        InvoiceNo=Source.InvoiceNo,ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
    WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,ParentLotNo,PartNo,PalletNo,CaseNo,BoxNo,LocationNo,Qty,InvoiceNo,ReceivedAt,CreatedAt,UpdatedAt)
    VALUES(Source.LotNo,Source.UnitType,Source.ParentLotNo,Source.PartNo,Source.PalletNo,Source.CaseNo,Source.BoxNo,Source.LocationNo,Source.Qty,Source.InvoiceNo,Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt);

    DROP TABLE dbo.INV_Inventory;
END;
GO

;WITH FgSource AS
(
    SELECT
        COALESCE(NULLIF(L.LotCode,N''),CONCAT(N'LEGACY-FG-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),F.StockID),10))) LotNo,
        F.ItemNo PartNo,M.ItemName PartName,F.Location LocationNo,F.Qty,UPPER(COALESCE(F.Status,'AVAILABLE')) InventoryStatus,
        COALESCE(F.StockTS,F.CreatedTS,sysdatetime()) ReceivedAt,
        COALESCE(F.CreatedTS,F.StockTS,sysdatetime()) CreatedAt,
        COALESCE(F.ModifiedTS,F.CreatedTS,F.StockTS,sysdatetime()) UpdatedAt
    FROM dbo.FG_Inventory F
    LEFT JOIN dbo.tbl_Lot L ON L.LotID=F.LotID
    LEFT JOIN dbo.MD_Item M ON M.ItemNo=F.ItemNo
    WHERE COALESCE(F.Qty,0)>0
      AND UPPER(COALESCE(F.Status,'AVAILABLE')) NOT IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED')
)
MERGE dbo.WH_Inventory AS Target
USING FgSource AS Source ON Target.LotNo=Source.LotNo
WHEN MATCHED THEN UPDATE SET PartNo=Source.PartNo,PartName=Source.PartName,LocationNo=Source.LocationNo,Qty=Source.Qty,
    ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
VALUES(Source.LotNo,'PART',Source.PartNo,Source.PartName,Source.LocationNo,Source.Qty,Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt);
GO

DELETE FROM dbo.WH_Inventory
WHERE Qty<=0;
GO

CREATE OR ALTER TRIGGER dbo.TR_WH_OLD_Inventory_SyncUnifiedInventory
ON dbo.WH_OLD_Inventory
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DELETE Target
    FROM dbo.WH_Inventory Target
    JOIN deleted D ON Target.LotNo=COALESCE(NULLIF((SELECT L.LotCode FROM dbo.tbl_Lot L WHERE L.LotID=D.LotID),N''),CONCAT(N'LEGACY-WH-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),D.InventoryID),10)))
    WHERE NOT EXISTS(SELECT 1 FROM inserted I WHERE I.InventoryID=D.InventoryID)
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.FG_Inventory F LEFT JOIN dbo.tbl_Lot FL ON FL.LotID=F.LotID
          WHERE COALESCE(NULLIF(FL.LotCode,N''),CONCAT(N'LEGACY-FG-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),F.StockID),10)))=Target.LotNo
            AND COALESCE(F.Qty,0)>0 AND UPPER(COALESCE(F.Status,'AVAILABLE')) NOT IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED')
      );

    ;WITH SourceRows AS
    (
        SELECT
            COALESCE(NULLIF(L.LotCode,N''),CONCAT(N'LEGACY-WH-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),I.InventoryID),10))) LotNo,
            COALESCE(I.ItemNo,L.ItemNo) PartNo,M.ItemName PartName,P.CaseNo,P.BoxBarcode BoxNo,I.LocationID LocationNo,
            COALESCE(I.OnHandQty,0) Qty,
            CASE WHEN UPPER(COALESCE(I.Status,'RECEIVED')) IN('RECEIVED','OK','STORED') THEN 'AVAILABLE' ELSE UPPER(I.Status) END InventoryStatus,
            P.InvoiceNo,
            COALESCE(I.LastReceivedAt,P.ReceivedAt,I.CreatedTS,sysdatetime()) ReceivedAt,
            COALESCE(I.CreatedTS,I.LastReceivedAt,P.CreatedTS,sysdatetime()) CreatedAt,
            COALESCE(I.ModifiedTS,I.CreatedTS,I.LastReceivedAt,P.ModifiedTS,P.CreatedTS,sysdatetime()) UpdatedAt
        FROM inserted I
        LEFT JOIN dbo.tbl_Lot L ON L.LotID=I.LotID
        LEFT JOIN dbo.MD_Item M ON M.ItemNo=COALESCE(I.ItemNo,L.ItemNo)
        OUTER APPLY(SELECT TOP(1) IP.CaseNo,IP.BoxBarcode,IP.InvoiceNo,IP.ReceivedAt,IP.CreatedTS,IP.ModifiedTS FROM dbo.WH_InboundPackage IP WHERE IP.LotID=I.LotID ORDER BY COALESCE(IP.ReceivedAt,IP.ModifiedTS,IP.CreatedTS) DESC,IP.InboundPackageID DESC) P
    )
    MERGE dbo.WH_Inventory AS Target
    USING SourceRows AS Source ON Target.LotNo=Source.LotNo
    WHEN MATCHED AND Source.Qty>0 AND Source.InventoryStatus NOT IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN
        UPDATE SET PartNo=Source.PartNo,PartName=Source.PartName,CaseNo=Source.CaseNo,BoxNo=Source.BoxNo,LocationNo=Source.LocationNo,
                   Qty=Source.Qty,InvoiceNo=Source.InvoiceNo,ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
    WHEN NOT MATCHED AND Source.Qty>0 AND Source.InventoryStatus NOT IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN
        INSERT(LotNo,UnitType,PartNo,PartName,CaseNo,BoxNo,LocationNo,Qty,InvoiceNo,ReceivedAt,CreatedAt,UpdatedAt)
        VALUES(Source.LotNo,'PART',Source.PartNo,Source.PartName,Source.CaseNo,Source.BoxNo,Source.LocationNo,Source.Qty,Source.InvoiceNo,Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt)
    WHEN MATCHED AND (Source.Qty<=0 OR Source.InventoryStatus IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED')) THEN DELETE;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_FG_Inventory_SyncUnifiedInventory
ON dbo.FG_Inventory
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DELETE Target
    FROM dbo.WH_Inventory Target
    JOIN deleted D ON Target.LotNo=COALESCE(NULLIF((SELECT L.LotCode FROM dbo.tbl_Lot L WHERE L.LotID=D.LotID),N''),CONCAT(N'LEGACY-FG-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),D.StockID),10)))
    WHERE NOT EXISTS(SELECT 1 FROM inserted I WHERE I.StockID=D.StockID)
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.WH_OLD_Inventory W LEFT JOIN dbo.tbl_Lot WL ON WL.LotID=W.LotID
          WHERE COALESCE(NULLIF(WL.LotCode,N''),CONCAT(N'LEGACY-WH-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),W.InventoryID),10)))=Target.LotNo
            AND COALESCE(W.OnHandQty,0)>0 AND UPPER(COALESCE(W.Status,'RECEIVED')) NOT IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED')
      );

    ;WITH SourceRows AS
    (
        SELECT COALESCE(NULLIF(L.LotCode,N''),CONCAT(N'LEGACY-FG-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),I.StockID),10))) LotNo,
            I.ItemNo PartNo,M.ItemName PartName,I.Location LocationNo,COALESCE(I.Qty,0) Qty,UPPER(COALESCE(I.Status,'AVAILABLE')) InventoryStatus,
            COALESCE(I.StockTS,I.CreatedTS,sysdatetime()) ReceivedAt,COALESCE(I.CreatedTS,I.StockTS,sysdatetime()) CreatedAt,
            COALESCE(I.ModifiedTS,I.CreatedTS,I.StockTS,sysdatetime()) UpdatedAt
        FROM inserted I LEFT JOIN dbo.tbl_Lot L ON L.LotID=I.LotID LEFT JOIN dbo.MD_Item M ON M.ItemNo=I.ItemNo
    )
    MERGE dbo.WH_Inventory AS Target
    USING SourceRows AS Source ON Target.LotNo=Source.LotNo
    WHEN MATCHED AND Source.Qty>0 AND Source.InventoryStatus NOT IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN
        UPDATE SET PartNo=Source.PartNo,PartName=Source.PartName,LocationNo=Source.LocationNo,Qty=Source.Qty,
                   ReceivedAt=Source.ReceivedAt,UpdatedAt=Source.UpdatedAt
    WHEN NOT MATCHED AND Source.Qty>0 AND Source.InventoryStatus NOT IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED') THEN
        INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
        VALUES(Source.LotNo,'PART',Source.PartNo,Source.PartName,Source.LocationNo,Source.Qty,Source.ReceivedAt,Source.CreatedAt,Source.UpdatedAt)
    WHEN MATCHED AND (Source.Qty<=0 OR Source.InventoryStatus IN('CANCELED','CANCELLED','RELEASED','PICKED','SHIPPED','DELIVERED','CLOSED')) THEN DELETE;
END;
GO

SELECT
    (SELECT COUNT(*) FROM dbo.WH_OLD_Inventory) LegacyRows,
    (SELECT COUNT(*) FROM dbo.WH_Inventory) UnifiedRows,
    (SELECT COUNT(*) FROM dbo.WH_Inventory WHERE Qty>0) ActiveUnifiedRows;
GO
