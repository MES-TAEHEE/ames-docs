# Set AMES_TEST_CONNECTION_STRING, then run this against a local test database.
# Fixtures are rolled back; no inventory quantity is changed.
$ErrorActionPreference = 'Stop'
if (-not $env:AMES_TEST_CONNECTION_STRING) { throw 'Set AMES_TEST_CONNECTION_STRING first.' }
$conn = [System.Data.SqlClient.SqlConnection]::new($env:AMES_TEST_CONNECTION_STRING)
$conn.Open()
$tx = $conn.BeginTransaction()
function Query([string]$sql, [hashtable]$parameters = @{}) {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText = $sql
    foreach ($name in $parameters.Keys) { [void]$cmd.Parameters.AddWithValue($name, $parameters[$name]) }
    try {
        $reader = $cmd.ExecuteReader()
        $table = [System.Data.DataTable]::new()
        $table.Load($reader)
        return ,$table
    } finally { $cmd.Dispose() }
}
function Search([string]$barcode) {
    Query "EXEC dbo.WH_PDA_TRANSACTION_LIST @SearchText=@scan, @DateFrom='2099-01-01', @DateTo='2099-01-01'" @{ '@scan'=$barcode }
}
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
try {
    $fixture = Query @'
SELECT TOP (1) P.InboundPackageID, P.LotID, P.ItemNo, L.LotCode,
       D.CaseNo, D.DocumentBarcode, P2.LotID AS SiblingLotID
FROM dbo.WH_InboundPackage P
JOIN dbo.WH_InboundDocument D ON D.InboundDocumentID=P.InboundDocumentID
JOIN dbo.tbl_Lot L ON L.LotID=P.LotID
JOIN dbo.WH_InboundPackage P2 ON P2.InboundDocumentID=P.InboundDocumentID AND P2.LotID<>P.LotID
WHERE D.ReceiveType=N'CKD' AND D.CaseNo IS NOT NULL
ORDER BY P.InboundPackageID, P2.InboundPackageID;
'@
    Check ($fixture.Rows.Count -eq 1) 'Requires a CKD case with at least two existing LOTs.'
    $f = $fixture.Rows[0]
    $parameters = @{ '@package'=$f.InboundPackageID; '@lot'=$f.LotID; '@item'=$f.ItemNo; '@sibling'=$f.SiblingLotID }
    $null = Query @'
UPDATE dbo.WH_InboundPackage SET BoxBarcode=N'TXN-TEST-BOX-ALIAS' WHERE InboundPackageID=@package;
INSERT dbo.WH_InventoryTransaction
    (TransactionTime,TransactionType,ItemNo,LocationID,LotID,QtyBefore,QtyChange,QtyAfter,CreatedBy)
VALUES
    ('2099-01-01','IN',@item,'TXN-TEST-LOC',@lot,10,2.5,12.5,'transaction-test'),
    ('2099-01-01','OUT',@item,'TXN-TEST-LOC',@lot,12.5,-1.25,11.25,'transaction-test'),
    ('2099-01-01','ADJ',@item,'TXN-TEST-LOC',@lot,11.25,0.5,11.75,'transaction-test'),
    ('2099-01-01','IN',@item,'TXN-TEST-LOC',@sibling,0,1,1,'transaction-test');
'@ $parameters
    Check ((Search $f.LotCode).Rows.Count -eq 3) 'LOT search failed.'
    Check ((Search $f.ItemNo).Rows.Count -ge 4) 'Part search failed.'
    Check ((Search 'TXN-TEST-LOC').Rows.Count -eq 4) 'Location search failed.'
    $boxes = Search 'TXN-TEST-BOX-ALIAS'
    Check ($boxes.Rows.Count -eq 3) 'Box alias did not resolve LOT history.'
    Check ((Search $f.CaseNo).Rows.Count -ge 4) 'Case did not include sibling LOT history.'
    Check ((Search $f.DocumentBarcode).Rows.Count -ge 4) 'CKD document barcode did not resolve case.'
    Check ((Search 'TXN-NOT-FOUND-999').Rows.Count -eq 0) 'Unknown barcode returned history.'
    Check ($boxes.Select("DIRECTION='IN'")[0].QTY -eq 2.5) 'IN must show movement, not remaining stock.'
    Check ($boxes.Select("DIRECTION='OUT'")[0].QTY -eq 1.25) 'OUT must show positive released quantity.'
    Check ($boxes.Select("DIRECTION='ADJ'")[0].AFTER_QTY -eq 11.75) 'Adjustment snapshot lost precision.'
    Check (-not [string]::IsNullOrWhiteSpace($boxes.Rows[0].UNIT)) 'Quantity unit is missing.'
    $outside = Query "EXEC dbo.WH_PDA_TRANSACTION_LIST @SearchText='TXN-TEST-LOC', @DateFrom='2099-01-02', @DateTo='2099-01-02'"
    Check ($outside.Rows.Count -eq 0) 'Date filter included transactions outside the range.'
    'PASS: LOT, part, location, box alias, case, case barcode, unknown barcode, movement quantities, snapshot and date range.'
} finally {
    $tx.Rollback()
    $conn.Dispose()
}
