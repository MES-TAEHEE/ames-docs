-- Run with sqlcmd -v Apply=0 for a rollback-only preview, then Apply=1 to commit.
-- Location Map coordinates are Bay=X (01...), Aisle=Y (A1...), Slot=Floor (F1...).
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @Apply bit = $(Apply);
IF @Apply IS NULL THROW 51900, 'Specify Apply=0 or Apply=1.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM dbo.MD_Location l
        JOIN dbo.MD_CodeItem a ON a.GroupCode='WH_AREA' AND a.CodeValue=l.AreaCode
        WHERE l.ActiveFlag=1 AND a.Attribute1 IS NOT NULL
          AND (LEN(a.Attribute1)<>2 OR l.Aisle IS NULL OR l.Bay IS NULL OR l.Slot IS NULL
               OR TRY_CONVERT(int,l.Bay)<1 OR l.Aisle NOT LIKE '[A-Z][1-9]%'
               OR l.Slot NOT LIKE 'F[1-9]%')
    ) THROW 51901, 'An active map location has invalid coordinates.', 1;

    SELECT l.LocationID AS OldID,
           CONVERT(varchar(20),CONCAT(a.Attribute1,l.Bay,l.Aisle,l.Slot)) AS NewID
    INTO #LocationRename
    FROM dbo.MD_Location l WITH (UPDLOCK,HOLDLOCK)
    JOIN dbo.MD_CodeItem a ON a.GroupCode='WH_AREA' AND a.CodeValue=l.AreaCode
    WHERE l.ActiveFlag=1 AND a.Attribute1 IS NOT NULL
      AND l.LocationID<>CONCAT(a.Attribute1,l.Bay,l.Aisle,l.Slot);

    IF EXISTS (SELECT NewID FROM #LocationRename GROUP BY NewID HAVING COUNT(*)>1)
        THROW 51902, 'More than one grid cell maps to the same LocationID.', 1;
    IF EXISTS (SELECT 1 FROM #LocationRename m JOIN dbo.MD_Location l ON l.LocationID=m.NewID)
        THROW 51903, 'A target LocationID already exists.', 1;

    SELECT OldID,NewID FROM #LocationRename ORDER BY OldID;

    UPDATE t SET LocationNo=m.NewID FROM dbo.WH_Inventory t JOIN #LocationRename m ON t.LocationNo COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET LocationNo=m.NewID FROM dbo.WH_InventoryTransaction t JOIN #LocationRename m ON t.LocationNo COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET CurrentLocationID=m.NewID FROM dbo.tbl_Lot t JOIN #LocationRename m ON t.CurrentLocationID COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET LocationID=m.NewID FROM dbo.MNT_SparePartItem t JOIN #LocationRename m ON t.LocationID COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET ActualLoc=m.NewID FROM dbo.FG_PutAway t JOIN #LocationRename m ON t.ActualLoc COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET SuggestedLoc=m.NewID FROM dbo.FG_PutAway t JOIN #LocationRename m ON t.SuggestedLoc COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET PhysicalLocation=m.NewID FROM dbo.QC_Hold t JOIN #LocationRename m ON t.PhysicalLocation COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET StorageLoc=m.NewID FROM dbo.MD_Mold t JOIN #LocationRename m ON t.StorageLoc COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET StorageLoc=m.NewID FROM dbo.MNT_MoldShotCount t JOIN #LocationRename m ON t.StorageLoc COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET ReqLocation=m.NewID FROM dbo.WH_PickSlip t JOIN #LocationRename m ON t.ReqLocation COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET ReqLocation=m.NewID FROM dbo.WH_ReleaseSchedule t JOIN #LocationRename m ON t.ReqLocation COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE t SET DeliveryDestination=m.NewID FROM dbo.WH_PurchaseOrder t JOIN #LocationRename m ON t.DeliveryDestination COLLATE DATABASE_DEFAULT=m.OldID COLLATE DATABASE_DEFAULT;
    UPDATE l SET LocationID=m.NewID, LocationName=m.NewID, ModifiedBy='loc-map', ModifiedTS=SYSDATETIME()
    FROM dbo.MD_Location l JOIN #LocationRename m ON l.LocationID=m.OldID;

    IF EXISTS (SELECT 1 FROM #LocationRename m JOIN dbo.WH_Inventory i ON i.LocationNo=m.OldID)
       OR EXISTS (SELECT 1 FROM #LocationRename m JOIN dbo.WH_InventoryTransaction t ON t.LocationNo=m.OldID)
       OR EXISTS (SELECT 1 FROM #LocationRename m JOIN dbo.tbl_Lot l ON l.CurrentLocationID=m.OldID)
       OR EXISTS (SELECT 1 FROM #LocationRename m LEFT JOIN dbo.MD_Location l ON l.LocationID=m.NewID WHERE l.LocationID IS NULL)
        THROW 51904, 'Location migration verification failed.', 1;

    SELECT COUNT(*) AS RenamedLocations FROM #LocationRename;
    IF @Apply=1 COMMIT TRANSACTION;
    ELSE ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
