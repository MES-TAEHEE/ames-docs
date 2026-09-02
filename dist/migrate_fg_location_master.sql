/*
  Finished-goods location assignment
  ----------------------------------
  Keeps FG ownership separate from the shared dbo.MD_Location hierarchy.
  The web repository also creates this table defensively for local environments.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FG_LocationMaster', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_LocationMaster
    (
        LocationID varchar(20) NOT NULL,
        ActiveFlag bit NOT NULL CONSTRAINT DF_FG_LocationMaster_ActiveFlag DEFAULT (1),
        CreatedBy nvarchar(120) NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_FG_LocationMaster_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy nvarchar(120) NULL,
        ModifiedTS datetime2 NULL,
        CONSTRAINT PK_FG_LocationMaster PRIMARY KEY CLUSTERED (LocationID)
    );
END;

INSERT INTO dbo.FG_LocationMaster (LocationID, ActiveFlag, CreatedBy, CreatedTS)
SELECT DISTINCT L.LocationID, 1, N'migration', SYSDATETIME()
FROM dbo.MD_Location L
WHERE NOT EXISTS (SELECT 1 FROM dbo.FG_LocationMaster F WHERE F.LocationID = L.LocationID)
  AND
  (
      UPPER(ISNULL(L.LocationType, '')) IN ('FG', 'FINISHED_GOODS', 'FINISHED GOODS')
      OR UPPER(L.LocationID) LIKE 'FG%'
      OR EXISTS (SELECT 1 FROM dbo.FG_Inventory I WHERE I.Location = L.LocationID)
  );

PRINT 'dbo.FG_LocationMaster is ready.';
