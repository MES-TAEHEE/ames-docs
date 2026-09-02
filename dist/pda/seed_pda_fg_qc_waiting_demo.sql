-- Optional development samples for QC Waiting. Run against the local test DB.
-- Adds 8 passed LOTs with different waiting ages; existing rows are never reset.
-- Only tbl_Lot and QC_Inspection are written. MD, PP and FG inventory are untouched.
-- Re-running skips these LOTs, including any that have since been put away.
-- Renames the two older, unstocked waiting samples without replacing their QC history.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @SeedBy varchar(50) = 'pda-fg-qc-waiting-demo';
DECLARE @Now datetime2 = SYSDATETIME();
DECLARE @Samples TABLE
(
    Seq int PRIMARY KEY,
    LotCode varchar(40),
    ItemNo varchar(20),
    Qty decimal(12,3),
    AgeHours int
);

-- Age margins keep all four color bands visible across local/UTC clock differences.
INSERT INTO @Samples VALUES
    (1, '5011FG260831000101', '81710-PI000NNB', 32, 12),
    (2, '5011FG260831000102', '81710-PI000YGN', 24, 54),
    (3, '5011FG260831000103', '81710-PI010NNB', 48, 102),
    (4, '5011FG260831000104', '81710-PI010YGN', 36, 150),
    (5, '5011FG260831000105', '82301-PI000NNB', 40, 198),
    (6, '5011FG260831000106', '82301-PI000YGU', 28, 270),
    (7, '5011FG260831000107', '81711-PI000NNB', 60, 318),
    (8, '5011FG260831000108', '81711-PI000YGN', 56, 366);

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE l SET LotCode = CONCAT('5011FG', CONVERT(char(6), l.ProducedAt, 12),
        CASE l.LotCode WHEN 'FG-DEMO-WAIT-001' THEN '000901' ELSE '000902' END)
    FROM dbo.tbl_Lot l
    WHERE l.CreatedBy = 'pda-fg-six-demo'
      AND l.LotCode IN ('FG-DEMO-WAIT-001', 'FG-DEMO-WAIT-002')
      AND l.ProducedAt IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.FG_Inventory f WHERE f.LotID = l.LotID)
      AND NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot other WHERE other.LotCode =
          CONCAT('5011FG', CONVERT(char(6), l.ProducedAt, 12),
              CASE l.LotCode WHEN 'FG-DEMO-WAIT-001' THEN '000901' ELSE '000902' END));

    IF EXISTS
    (
        SELECT 1 FROM @Samples s
        WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Item i WHERE i.ItemNo = s.ItemNo)
    ) THROW 51000, 'Required existing part masters are missing. No masters will be created.', 1;

    IF EXISTS
    (
        SELECT 1 FROM @Samples s
        JOIN dbo.tbl_Lot l ON l.LotCode = s.LotCode
        WHERE l.CreatedBy <> @SeedBy
    ) THROW 51000, 'A sample LOT number is already owned by other data.', 1;

    IF EXISTS
    (
        SELECT 1 FROM @Samples s
        JOIN dbo.QC_Inspection q ON q.InspectionNo = CONCAT('FGWAIT-QC-', s.Seq)
        WHERE q.CreatedBy <> @SeedBy
    ) THROW 51000, 'A sample inspection number is already owned by other data.', 1;

    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt,
         Status, QualityFlag, InventoryStatus, ExpiryDate, CreatedBy, CreatedTS)
    SELECT s.LotCode, s.ItemNo, 'FINAL', s.Qty, s.Qty,
           DATEADD(hour, -s.AgeHours - 2, @Now), 'Completed', 'PASS', 'QC_PASS',
           DATEADD(year, 1, CAST(DATEADD(hour, -s.AgeHours - 2, @Now) AS date)), @SeedBy, @Now
    FROM @Samples s
    WHERE NOT EXISTS
        (SELECT 1 FROM dbo.tbl_Lot l WITH (UPDLOCK, HOLDLOCK) WHERE l.LotCode = s.LotCode);

    INSERT INTO dbo.QC_Inspection
        (InspectionNo, InspectionType, LotID, ItemNo, Mode, SampleSize,
         BatchQty, CumulativeGood, DefectQtyTotal, Verdict, CriticalFlag,
         InspectorID, InsStartTS, InsEndTS, CreatedBy, CreatedTS)
    SELECT CONCAT('FGWAIT-QC-', s.Seq), 'FQC', l.LotID, s.ItemNo, 'Normal', 5,
           s.Qty, CONVERT(int, s.Qty), 0, 'PASS', 0, 'admin',
           DATEADD(hour, 1, l.ProducedAt), DATEADD(hour, 2, l.ProducedAt), @SeedBy, @Now
    FROM @Samples s
    JOIN dbo.tbl_Lot l ON l.LotCode = s.LotCode AND l.CreatedBy = @SeedBy
    WHERE NOT EXISTS
        (SELECT 1 FROM dbo.QC_Inspection q WITH (UPDLOCK, HOLDLOCK)
         WHERE q.InspectionNo = CONCAT('FGWAIT-QC-', s.Seq))
      AND NOT EXISTS (SELECT 1 FROM dbo.QC_Inspection q WHERE q.LotID = l.LotID)
      AND NOT EXISTS (SELECT 1 FROM dbo.FG_Inventory f WHERE f.LotID = l.LotID);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT l.LotCode, l.ItemNo, i.ItemName, l.BatchSize AS Qty, i.DefaultUOM AS Unit,
       q.InsEndTS AS QcPassedAt
FROM dbo.tbl_Lot l
JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
JOIN dbo.QC_Inspection q ON q.LotID = l.LotID AND q.CreatedBy = @SeedBy
WHERE l.CreatedBy = @SeedBy
ORDER BY q.InsEndTS, l.LotID;
