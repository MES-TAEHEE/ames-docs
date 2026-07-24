-- =====================================================================
--  seed_pda_fg_putaway_demo_data.sql
--  PDA Finished Goods put-away demo data
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\seed_pda_fg_putaway_demo_data.sql
-- =====================================================================
SET NOCOUNT ON;

DECLARE @CreatedBy varchar(50) = 'pda-fg-seed';
DECLARE @CustomerCode varchar(20) = 'GEO';
DECLARE @Qty decimal(14,3) = 32;
DECLARE @ItemNo varchar(20) = NULL;
DECLARE @LotID int = NULL;
DECLARE @LotCode varchar(40) = NULL;
DECLARE @WoID int = NULL;
DECLARE @WoNumber varchar(20) = NULL;
DECLARE @SuggestedLocation varchar(20) = NULL;
DECLARE @PackType varchar(20) = NULL;

-- Do not seed MD master data here. FG Put-Away reads the production
-- master tables, so this demo script only uses already configured masters.
IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NULL
    THROW 51000, 'dbo.MD_Item is required before FG Put-Away demo seed.', 1;

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NULL
    THROW 51000, 'dbo.MD_Location is required before FG Put-Away demo seed.', 1;

IF OBJECT_ID(N'dbo.tbl_Lot', N'U') IS NULL
    THROW 51000, 'dbo.tbl_Lot is required before FG Put-Away demo seed.', 1;

IF OBJECT_ID(N'dbo.PP_WorkOrder', N'U') IS NULL
    THROW 51000, 'dbo.PP_WorkOrder is required before FG Put-Away demo seed.', 1;

IF OBJECT_ID(N'dbo.QC_Inspection', N'U') IS NULL
    THROW 51000, 'dbo.QC_Inspection is required before FG Put-Away demo seed.', 1;

SELECT TOP (1)
       @ItemNo = I.ItemNo,
       @PackType = UPPER(NULLIF(P.PackType, '')),
       @CustomerCode = COALESCE(NULLIF(I.CarType, ''), @CustomerCode)
FROM dbo.MD_Item I
OUTER APPLY
(
    SELECT TOP (1) PS.PackType
    FROM dbo.MD_PackagingSpec PS
    WHERE PS.ItemID = I.ItemNo
      AND UPPER(ISNULL(PS.Status, 'ACTIVE')) IN ('ACTIVE', 'USE', 'Y')
    ORDER BY
        CASE UPPER(ISNULL(PS.PackType, ''))
            WHEN 'PALLET' THEN 0
            WHEN 'BOX' THEN 1
            WHEN 'RACK' THEN 2
            ELSE 3
        END,
        PS.PackSpecID
) P
WHERE ISNULL(I.ActiveFlag, 1) = 1
  AND UPPER(ISNULL(I.ItemType, '')) IN ('FG', 'FINISHED', 'FINISHED_GOODS')
ORDER BY
    CASE WHEN P.PackType IS NULL THEN 1 ELSE 0 END,
    I.ItemNo;

IF @ItemNo IS NULL
    THROW 51000, 'No active finished-goods item exists in dbo.MD_Item. Register the item in master data first.', 1;

SELECT TOP (1)
       @SuggestedLocation = L.LocationID
FROM dbo.MD_Location L
WHERE ISNULL(L.ActiveFlag, 1) = 1
  AND (
        UPPER(ISNULL(L.LocationType, '')) IN ('FG', 'FINISHED_GOODS', 'FINISHED GOODS')
        OR UPPER(L.LocationID) LIKE 'FG%'
      )
ORDER BY L.LocationID;

IF @SuggestedLocation IS NULL
    THROW 51000, 'No active FG location exists in dbo.MD_Location. Register the FG location master first.', 1;

IF NULLIF(@PackType, '') IS NULL
    SET @PackType = 'LOCATION';

IF @PackType NOT IN ('PALLET', 'BOX', 'RACK')
    SET @PackType = 'LOCATION';

IF OBJECT_ID(N'dbo.MD_PackagingSpec', N'U') IS NULL
BEGIN
    SET @PackType = 'LOCATION';
END;

SELECT TOP (1)
       @LotID = L.LotID,
       @LotCode = L.LotCode,
       @WoID = W.WoID,
       @WoNumber = W.WoNumber,
       @Qty = CAST(COALESCE(NULLIF(Q.BatchQty, 0), NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), NULLIF(W.CompletedQty, 0), NULLIF(W.OrderQty, 0), @Qty) AS decimal(14,3))
FROM dbo.tbl_Lot L
LEFT JOIN dbo.PP_WorkOrder W
    ON W.WoID = L.WoID
OUTER APPLY
(
    SELECT TOP (1) QI.BatchQty
    FROM dbo.QC_Inspection QI
    WHERE (QI.LotID = L.LotID OR (L.WoID IS NOT NULL AND QI.WoID = L.WoID))
      AND UPPER(ISNULL(QI.Verdict, '')) IN ('PASS', 'PASSED', 'OK')
    ORDER BY QI.InsEndTS DESC, QI.InspectionID DESC
) Q
WHERE COALESCE(NULLIF(L.ItemNo, ''), W.ItemNo) = @ItemNo
  AND (
        Q.BatchQty IS NOT NULL
        OR UPPER(ISNULL(L.QualityFlag, '')) IN ('PASS', 'PASSED', 'OK')
      )
  AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.FG_Inventory S
          WHERE (S.LotID = L.LotID OR (L.WoID IS NOT NULL AND S.WoID = L.WoID))
            AND UPPER(ISNULL(S.Status, '')) NOT IN ('CANCELED', 'CANCELLED')
      )
ORDER BY L.ProducedAt DESC, L.LotID DESC;

IF @LotID IS NULL
    THROW 51000, 'No existing QC-passed unstocked FG lot exists. Create the lot through production/QC flow first.', 1;

IF OBJECT_ID(N'dbo.FG_PutAway', N'U') IS NOT NULL
BEGIN
    DELETE PA
      FROM dbo.FG_PutAway PA
      JOIN dbo.FG_Inventory S ON S.StockID = PA.StockID
     WHERE S.StockNumber = 'FG-DEMO-OCCUPIED'
       AND S.CreatedBy = @CreatedBy;
END;

IF OBJECT_ID(N'dbo.FG_Inventory', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.FG_Inventory
     WHERE StockNumber = 'FG-DEMO-OCCUPIED'
       AND CreatedBy = @CreatedBy;

    IF NOT EXISTS (SELECT 1 FROM dbo.FG_Inventory WHERE StockNumber = 'FG-DEMO-OCCUPIED')
        INSERT INTO dbo.FG_Inventory
            (StockNumber, ItemNo, CustomerCode, Qty, Location, Status, HoldFlag, StockTS, CreatedBy)
        VALUES
            ('FG-DEMO-OCCUPIED', @ItemNo, @CustomerCode, 32, @SuggestedLocation, 'Available', 0,
             DATEADD(day, -1, SYSDATETIME()), @CreatedBy);
    ELSE
        UPDATE dbo.FG_Inventory
           SET ItemNo = @ItemNo,
               CustomerCode = @CustomerCode,
               Qty = 32,
               Location = @SuggestedLocation,
               Status = 'Available',
               HoldFlag = 0,
               ModifiedBy = @CreatedBy,
               ModifiedTS = SYSDATETIME()
         WHERE StockNumber = 'FG-DEMO-OCCUPIED';
END;

SELECT 'FG Put-Away LOT' AS Demo, @LotCode AS ScanValue
UNION ALL
SELECT 'FG Put-Away typed LOT', CONCAT('FGLOT:', @LotCode)
UNION ALL
SELECT 'FG Put-Away WO', @WoNumber
UNION ALL
SELECT 'FG Put-Away typed WO', CONCAT('FGWO:', @WoNumber)
UNION ALL
SELECT 'FG Put-Away suggested location', @SuggestedLocation
UNION ALL
SELECT 'FG Put-Away expected scan type', @PackType
UNION ALL
SELECT 'FG Put-Away next scan barcode',
       CASE @PackType
           WHEN 'PALLET' THEN CONCAT('FGPAL:', @SuggestedLocation)
           WHEN 'BOX' THEN CONCAT('FGBOX:', @SuggestedLocation)
           WHEN 'RACK' THEN CONCAT('FGRACK:', @SuggestedLocation)
           ELSE CONCAT('FGLOC:', @SuggestedLocation)
       END
UNION ALL
SELECT 'FG Put-Away wrong pallet/location test',
       CASE @PackType
           WHEN 'PALLET' THEN CONCAT('FGLOC:', @SuggestedLocation)
           ELSE CONCAT('FGPAL:', @SuggestedLocation)
       END;
