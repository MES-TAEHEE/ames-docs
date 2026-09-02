-- =====================================================================
--  migrate_wh_picking_slip.sql
--  Warehouse web picking slip support
--
--  Adds WM20233/WM20231-style pick-slip grouping fields to the AMES
--  warehouse release schedule table.
--
--  Apply:
--    sqlcmd -S <server>,<port> -U <user> -P <password> -C -d AMES_DEV -i dist\migrate_wh_picking_slip.sql
-- =====================================================================
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_ReleaseSchedule
    (
        ReleaseScheduleID int IDENTITY(1,1) NOT NULL,
        WoID int NULL,
        ItemNo varchar(20) NULL,
        DemandQty decimal(14,3) NULL,
        PickedQty decimal(14,3) NULL,
        RequiredAt datetime2 NULL,
        Priority tinyint NULL,
        Status varchar(20) NULL,
        CreatedBy varchar(50) NOT NULL,
        CreatedTS datetime2 NULL CONSTRAINT DF_WH_ReleaseSchedule_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy nvarchar(450) NULL,
        ModifiedTS datetime2 NULL,
        CONSTRAINT PK_WH_ReleaseSchedule PRIMARY KEY CLUSTERED (ReleaseScheduleID)
    );
END;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PickSlipNo') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD PickSlipNo nvarchar(40) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqLocation') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqLocation nvarchar(40) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqSeqNo') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqSeqNo int NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqUserId') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqUserId nvarchar(80) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PrintDate') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD PrintDate datetime2 NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'CloseDate') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD CloseDate datetime2 NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'CloseUserId') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD CloseUserId nvarchar(80) NULL;
GO

UPDATE dbo.WH_ReleaseSchedule
   SET PickSlipNo = CONCAT(N'RS-', ReleaseScheduleID)
 WHERE NULLIF(PickSlipNo, N'') IS NULL;
GO

UPDATE dbo.WH_ReleaseSchedule
   SET ReqSeqNo = 1
 WHERE ReqSeqNo IS NULL;
GO

UPDATE dbo.WH_ReleaseSchedule
   SET ReqUserId = CreatedBy
 WHERE NULLIF(ReqUserId, N'') IS NULL;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.WH_ReleaseSchedule')
      AND name = N'IX_WH_ReleaseSchedule_PickSlipNo'
)
    CREATE INDEX IX_WH_ReleaseSchedule_PickSlipNo
        ON dbo.WH_ReleaseSchedule (PickSlipNo, ReqSeqNo, ReleaseScheduleID);
GO

