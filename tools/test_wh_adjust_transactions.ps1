# Integration regression: all schema, fixture and stock changes are rolled back.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$line = Get-Content (Join-Path $root 'src/04_Api/AMES.Api/appsettings.json') | Where-Object { $_ -match '^\s*"AMES"\s*:' } | Select-Object -First 1
$cs = [regex]::Match($line, '"AMES"\s*:\s*"([^"]+)"').Groups[1].Value
$conn = [System.Data.SqlClient.SqlConnection]::new($cs)
$conn.Open()
$tx = $conn.BeginTransaction()
function Run([string]$sql) {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandTimeout = 60
    $cmd.CommandText = $sql
    try { [void]$cmd.ExecuteNonQuery() } finally { $cmd.Dispose() }
}
try {
    Run @'
IF OBJECT_ID('dbo.WH_InventoryAdjust','U') IS NULL
    CREATE TABLE dbo.WH_InventoryAdjust (
        AdjustID int IDENTITY PRIMARY KEY, ItemNo varchar(20), LocationID varchar(20), LotID int,
        QtyBefore decimal(14,3), Delta decimal(14,3), QtyAfter decimal(14,3), ReasonCode varchar(30),
        ReasonNote nvarchar(500), RequestedBy nvarchar(450), ApprovedBy nvarchar(450),
        CreatedBy varchar(50) NOT NULL, CreatedTS datetime2, ModifiedBy nvarchar(450), ModifiedTS datetime2);
SET IDENTITY_INSERT dbo.WH_InventoryAdjust ON;
INSERT dbo.WH_InventoryAdjust
    (AdjustID, QtyBefore, Delta, QtyAfter, ReasonCode, ReasonNote, RequestedBy, ApprovedBy, CreatedBy, CreatedTS)
VALUES
    (-2147483600,10,1,11,'COUNT_DIFF',N'adjust migration missing',N'audit-worker',N'legacy-approver','adjust-regression',SYSDATETIME()),
    (-2147483599,11,1,12,'COUNT_DIFF',N'adjust migration linked',N'audit-worker',NULL,'adjust-regression',SYSDATETIME());
SET IDENTITY_INSERT dbo.WH_InventoryAdjust OFF;
INSERT dbo.WH_InventoryTransaction
    (TransactionType,QtyBefore,QtyChange,QtyAfter,RefDocType,RefDocID,OperatorID,Note,CreatedBy)
VALUES ('ADJ',11,1,12,'WH_InventoryAdjust',-2147483599,N'audit-worker',N'adjust migration linked','adjust-regression');
'@
    $batches = [regex]::Split((Get-Content -Raw -Encoding UTF8 (Join-Path $root 'dist/pda/PDA_SCHEMA.sql')), '(?im)^\s*GO\s*\r?$')
    $migration = @($batches | Where-Object { $_ -match '-- WH adjustment history consolidation\.' })
    if ($migration.Count -ne 1) { throw 'Expected one consolidation migration.' }
    Run $migration[0]
    Run $migration[0]
    Run @'
IF OBJECT_ID('dbo.WH_InventoryAdjust','U') IS NOT NULL THROW 51000,'Legacy table remains.',1;
IF (SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE RefDocType='WH_InventoryAdjust' AND RefDocID IN (-2147483600,-2147483599)) <> 2
    THROW 51000,'Migration lost or duplicated an adjustment.',1;
IF NOT EXISTS (SELECT 1 FROM dbo.WH_InventoryTransaction WHERE RefDocType='WH_InventoryAdjust' AND RefDocID=-2147483600
    AND QtyBefore=10 AND QtyChange=1 AND QtyAfter=11 AND ReasonCode='COUNT_DIFF'
    AND OperatorID=N'audit-worker' AND ApproverID=N'legacy-approver' AND Note=N'adjust migration missing')
    THROW 51000,'Migration did not preserve audit details.',1;
'@
    $procedures = @($batches | Where-Object { $_ -match '(?im)^CREATE OR ALTER PROCEDURE dbo\.WH_PDA_(ADJUST_SAVE_QTY|PPT_TEST_RESET|INBOUND_SIMPLE_TEST_RESET)\b' })
    if ($procedures.Count -ne 3) { throw 'Expected three updated WH procedures.' }
    foreach ($batch in $procedures) { Run $batch }
    Run @'
IF EXISTS (SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID('dbo.WH_PDA_ADJUST_SAVE_QTY') AND name LIKE '%Supervisor%')
    THROW 51000,'Supervisor parameter remains.',1;
DECLARE @LotID int, @InventoryID int, @Scan nvarchar(80), @Before decimal(14,3), @Reason nvarchar(30);
SELECT TOP(1) @LotID=W.LotID,@InventoryID=W.InventoryID,@Scan=L.LotCode,@Before=W.OnHandQty
FROM dbo.WH_Inventory W JOIN dbo.tbl_Lot L ON L.LotID=W.LotID
WHERE W.OnHandQty BETWEEN 1 AND 999999998 AND W.OnHandQty=FLOOR(W.OnHandQty)
  AND UPPER(COALESCE(W.Status,'Received')) NOT IN ('CANCELED','RELEASED','PICKED')
  AND NOT EXISTS (SELECT 1 FROM dbo.FG_Inventory F WHERE F.LotID=W.LotID)
  AND (SELECT COUNT(*) FROM dbo.WH_Inventory W2 WHERE W2.LotID=W.LotID)=1
ORDER BY W.InventoryID;
IF @LotID IS NULL THROW 51000,'No eligible WH stock fixture.',1;
SELECT TOP(1) @Reason=CodeValue FROM dbo.MD_CodeItem WHERE GroupCode='INV_ADJUST_REASON' AND ISNULL(UseFlag,1)=1 ORDER BY CodeValue;
IF @Reason IS NULL THROW 51000,'No adjustment reason configured.',1;
DECLARE @LastTxn bigint=(SELECT ISNULL(MAX(TransactionID),0) FROM dbo.WH_InventoryTransaction);
EXEC dbo.WH_PDA_ADJUST_SAVE_QTY @ScanText=@Scan,@DeltaQty=1,@ReasonCode=@Reason,@ReasonNote=N'adjust transaction regression',@UserId=N'adjust-regression';
IF (SELECT OnHandQty FROM dbo.WH_Inventory WHERE InventoryID=@InventoryID)<>@Before+1
    THROW 51000,'Inventory quantity incorrect.',1;
IF (SELECT RemainingQty FROM dbo.tbl_Lot WHERE LotID=@LotID)<>@Before+1
    THROW 51000,'Lot quantity incorrect.',1;
IF (SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE TransactionID>@LastTxn AND LotID=@LotID AND TransactionType='ADJ')<>1
    THROW 51000,'Expected exactly one adjustment transaction.',1;
IF NOT EXISTS (SELECT 1 FROM dbo.WH_InventoryTransaction WHERE TransactionID>@LastTxn AND LotID=@LotID
    AND QtyBefore=@Before AND QtyChange=1 AND QtyAfter=@Before+1 AND ReasonCode=@Reason
    AND RefDocType='LOT' AND RefDocID=@LotID AND OperatorID=N'adjust-regression' AND ApproverID IS NULL
    AND Note=N'adjust transaction regression')
    THROW 51000,'Adjustment audit fields incorrect.',1;
'@
    Write-Output 'PASS: history backfill, duplicate prevention, rerun, no supervisor params, stock update and single ADJ transaction.'
}
finally {
    try { $tx.Rollback(); Write-Output 'ROLLED BACK: no fixture or stock changes retained.' } finally { $conn.Dispose() }
}
