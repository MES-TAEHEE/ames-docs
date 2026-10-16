# Execute the actual endpoint query against session-local fixture tables only.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Get-Content -Raw (Join-Path $root 'src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs')
$section = $source.Substring($source.IndexOf('g.MapGet("/qc-completed"'))
$sql = [regex]::Match($section, 'const string sql = """([\s\S]*?)""";').Groups[1].Value
if (!$sql) { throw 'QC Waiting query missing.' }
foreach ($table in 'tbl_Lot','PP_WorkOrder','MD_Item','QC_Inspection','FG_Inventory') { $sql=$sql.Replace("dbo.$table","#$table") }
$line=Get-Content (Join-Path $root 'src/04_Api/AMES.Api/appsettings.json') | Where-Object {$_ -match '^\s*"AMES"\s*:'} | Select-Object -First 1
$conn=[System.Data.SqlClient.SqlConnection]::new([regex]::Match($line,'"AMES"\s*:\s*"([^"]+)"').Groups[1].Value)
$conn.Open()
try {
    $cmd=$conn.CreateCommand()
    $cmd.CommandText=@'
CREATE TABLE #tbl_Lot (LotID int,LotCode varchar(40),WoID int,ItemNo varchar(20),RemainingQty decimal(14,3),BatchSize decimal(14,3),ProducedAt datetime2,ExpiryDate date,QualityFlag varchar(20));
CREATE TABLE #PP_WorkOrder (WoID int,WoNumber varchar(40),ItemNo varchar(20),CompletedQty decimal(14,3),OrderQty decimal(14,3));
CREATE TABLE #MD_Item (ItemNo varchar(20),ItemName nvarchar(100),DefaultUOM varchar(10));
CREATE TABLE #QC_Inspection (InspectionID int IDENTITY,InspectionNo varchar(40),LotID int,WoID int,CustomerCode varchar(20),BatchQty decimal(14,3),InsEndTS datetime2,InsStartTS datetime2,CreatedTS datetime2,Verdict varchar(20));
CREATE TABLE #FG_Inventory (StockID int IDENTITY,LotID int,WoID int,CustomerCode varchar(20),Location varchar(20),Status varchar(20),StockTS datetime2);
CREATE TABLE #MD_PackagingSpec (PackSpecID varchar(20),ItemID varchar(20),PackType varchar(20),ActiveFlag bit);
INSERT #PP_WorkOrder VALUES(1,'SHARED-WO','PART',999,999);
INSERT #MD_Item VALUES('PART',N'Test part','EA');
;WITH N AS (SELECT TOP(151) ROW_NUMBER() OVER(ORDER BY object_id) AS ID FROM sys.all_objects)
INSERT #tbl_Lot SELECT ID,CONCAT('QC-TEST-',ID),1,'PART',10,10,'2026-09-01','2027-09-01','PASS' FROM N;
INSERT #QC_Inspection (LotID,WoID,BatchQty,InsEndTS,Verdict)
SELECT LotID,1,10,DATEADD(minute,LotID,CONVERT(datetime2,'2026-09-02')),'PASS' FROM #tbl_Lot WHERE LotID<=150;
DELETE #QC_Inspection WHERE LotID=2;
INSERT #QC_Inspection (LotID,WoID,BatchQty,InsEndTS,Verdict) VALUES
(1,1,10,'2026-09-03','FAIL'), -- latest failure must override old PASS
(6,1,10,'2026-09-01','FAIL'), -- old failure must not override latest PASS
(7,1,10,'2026-09-02T00:07:00','FAIL'), -- same timestamp: higher inspection ID wins
(NULL,1,10,'2026-09-04','PASS'); -- WO-only PASS must not qualify LOT 2 or 151
INSERT #FG_Inventory (LotID,WoID,Status,StockTS) VALUES (3,1,'AVAILABLE',SYSDATETIME()),(5,1,'CANCELED',SYSDATETIME());
INSERT #MD_PackagingSpec VALUES('PACK','PART','LOCATION',1);
'@
    [void]$cmd.ExecuteNonQuery()
    $cmd.CommandText=$sql
    $table=[System.Data.DataTable]::new()
    $reader=$cmd.ExecuteReader(); $table.Load($reader)
    $ids=@($table.Rows | ForEach-Object {[int]$_.LotID})
    if ($ids.Count -ne 146) { throw "Expected 146 waiting LOTs, got $($ids.Count)." }
    foreach ($id in 1,2,3,7,151) { if ($ids -contains $id) {throw "LOT $id must be excluded."} }
    foreach ($id in 4,5,6,150) { if ($ids -notcontains $id) {throw "LOT $id must remain visible."} }
    if ($ids[0] -ne 4 -or $ids[-1] -ne 150) {throw 'Oldest-first order changed.'}
    Write-Output 'PASS: 146 rows (no 100-row cap), latest verdict/tie-break, LOT-only QC/stock checks, canceled stock and oldest-first order.'

    $putAwaySection=$source.Substring($source.IndexOf('private static PutAwayScanRow? FindPutAwayScanRow'))
    $putAwaySql=[regex]::Match($putAwaySection,'new SqlCommand\("""([\s\S]*?)""", conn, tx\)').Groups[1].Value
    if (!$putAwaySql) {throw 'Put-Away scan query missing.'}
    foreach ($table in 'tbl_Lot','PP_WorkOrder','MD_Item','QC_Inspection','FG_Inventory','MD_PackagingSpec') {$putAwaySql=$putAwaySql.Replace("dbo.$table","#$table")}
    function Scan([string]$barcode) {
        $cmd.CommandText=$putAwaySql
        $cmd.Parameters.Clear()
        [void]$cmd.Parameters.Add('@Barcode',[System.Data.SqlDbType]::NVarChar,80)
        $cmd.Parameters['@Barcode'].Value=$barcode
        $result=[System.Data.DataTable]::new()
        $reader=$cmd.ExecuteReader(); $result.Load($reader)
        return ,$result
    }
    if ((Scan 'SHARED-WO').Rows.Count -ne 0) {throw 'WO barcode must not resolve a LOT.'}
    foreach ($case in @(
        @{Barcode='QC-TEST-1'; Passed=0; Stocked=$false},
        @{Barcode='QC-TEST-2'; Passed=0; Stocked=$false},
        @{Barcode='QC-TEST-3'; Passed=1; Stocked=$true},
        @{Barcode='QC-TEST-4'; Passed=1; Stocked=$false},
        @{Barcode='QC-TEST-6'; Passed=1; Stocked=$false},
        @{Barcode='QC-TEST-7'; Passed=0; Stocked=$false}
    )) {
        $row=(Scan $case.Barcode).Rows[0]
        if ([int]$row.IsQcPassed -ne $case.Passed -or ($row.ExistingStockID -ne [DBNull]::Value) -ne $case.Stocked) {throw "Put-Away rule failed: $($case.Barcode)"}
    }
    Write-Output 'PASS: Put-Away uses LOT barcode, latest LOT QC verdict and same-LOT inventory only.'
} finally { $conn.Dispose() }
