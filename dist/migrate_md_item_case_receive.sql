-- Add only the case-receipt policy; select the target database explicitly.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NULL
    THROW 51443, 'MD_Item must exist before applying the case receipt policy.', 1;
-- Preserve settings in local databases that used the earlier column name.
IF COL_LENGTH(N'dbo.MD_Item', N'ScanRequired') IS NULL
   AND COL_LENGTH(N'dbo.MD_Item', N'RequireBoxScanOnCaseReceive') IS NOT NULL
    EXEC sys.sp_rename N'dbo.MD_Item.RequireBoxScanOnCaseReceive', N'ScanRequired', N'COLUMN';
IF COL_LENGTH(N'dbo.MD_Item', N'ScanRequired') IS NULL
    ALTER TABLE dbo.MD_Item ADD ScanRequired bit NOT NULL
        CONSTRAINT DF_MD_Item_ScanRequired DEFAULT (0) WITH VALUES;
