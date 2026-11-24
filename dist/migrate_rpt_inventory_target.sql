SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.RPT_InventoryTarget',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RPT_InventoryTarget(
        InventoryScope varchar(3) COLLATE DATABASE_DEFAULT NOT NULL,
        ItemCategory nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
        Uom varchar(10) COLLATE DATABASE_DEFAULT NOT NULL,
        TargetQty decimal(18,3) NOT NULL,
        CreatedBy varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_RPT_InventoryTarget_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy varchar(20) COLLATE DATABASE_DEFAULT NULL,
        ModifiedTS datetime2 NULL,
        CONSTRAINT PK_RPT_InventoryTarget PRIMARY KEY(InventoryScope,ItemCategory,Uom),
        CONSTRAINT CK_RPT_InventoryTarget_Scope CHECK(InventoryScope IN('ALL','WH','FG')),
        CONSTRAINT CK_RPT_InventoryTarget_Qty CHECK(TargetQty>0)
    );
END;

-- Expose Edit in the permission matrix; preserve every existing grant.
UPDATE dbo.SYS_Screen SET PermissionCriteria='__X'
WHERE ScreenCode='RPT-005' AND PermissionCriteria='_XX';
