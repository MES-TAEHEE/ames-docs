-- Deploy Web/API/PDA together. Existing loaded rows stay Loaded. No stock updates.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.FG_DeliveryLoad','U') IS NULL
    THROW 52020,'FG_DeliveryLoad must exist before dispatch preparation migration.',1;
IF COL_LENGTH('dbo.FG_DeliveryLoad','LoadState') IS NULL
    ALTER TABLE dbo.FG_DeliveryLoad ADD LoadState varchar(10) NOT NULL
        CONSTRAINT DF_FG_DeliveryLoad_LoadState DEFAULT 'Loaded' WITH VALUES;
IF COL_LENGTH('dbo.FG_DeliveryLoad','PreparedBy') IS NULL
    ALTER TABLE dbo.FG_DeliveryLoad ADD PreparedBy varchar(20) NULL;
IF COL_LENGTH('dbo.FG_DeliveryLoad','PreparedAt') IS NULL
    ALTER TABLE dbo.FG_DeliveryLoad ADD PreparedAt datetime2 NULL;
ALTER TABLE dbo.FG_DeliveryLoad ALTER COLUMN LoadedBy varchar(20) NULL;
ALTER TABLE dbo.FG_DeliveryLoad ALTER COLUMN LoadedAt datetime2 NULL;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.FG_DeliveryLoad') AND name='CK_FG_DeliveryLoad_LoadState')
    EXEC('ALTER TABLE dbo.FG_DeliveryLoad WITH CHECK ADD CONSTRAINT CK_FG_DeliveryLoad_LoadState CHECK((LoadState=''Prepared'' AND PreparedBy IS NOT NULL AND PreparedAt IS NOT NULL AND LoadedBy IS NULL AND LoadedAt IS NULL) OR (LoadState=''Loaded'' AND LoadedBy IS NOT NULL AND LoadedAt IS NOT NULL))');
COMMIT TRANSACTION;
