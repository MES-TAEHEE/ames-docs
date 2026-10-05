param([string]$Server = 'tcp:192.168.1.100,1433')
$ErrorActionPreference = 'Stop'
$fields = 'VEND,VENDNM,VINCD,VINNM,PO_DATE,PONO,PO_DELI_DATE,PARTNO,PARTNM,STR_LOC,STR_LOCNM,PO_UNIT,UNIT_PACK_QTY,PO_QTY,ELIKZ,DELI_CMP_CHK,PURC_ORG,PURC_ORGNM,PURC_PO_TYPE,PURC_PO_TYPENM,PURC_GRP,PURC_GRPNM,MAT_GRP,CUSTCD,CUSTNM,CUST_PONO,PMI,SD_PONO,SD_DELI_DATE,FTA_CERTI,UMSON,MAT_GRPNM,NATIONCD,RETPO,PSTYP,LOEKZ,AAC,UPDATE_DATE,DELI_QTY,DEF_QTY,ARRIV_QTY,GRN_QTY,REMAINQTY,CHK,BSTZD_NM'.Split(',')
$numeric = 'UNIT_PACK_QTY,PO_QTY,DELI_QTY,DEF_QTY,ARRIV_QTY,GRN_QTY,REMAINQTY'.Split(',')
$source = Get-Content -LiteralPath "$PSScriptRoot/SrmMockApi/Data/PO_7700_310471_EN_data.json" -Raw | ConvertFrom-Json
if ($source.Count -ne 426) { throw 'Expected 426 fixture rows.' }
$data = [System.Data.DataTable]::new()
[void]$data.Columns.Add('SourceRowNo', [int])
foreach ($field in $fields) {
    $type = if ($field -in $numeric) { [long] } else { [string] }
    [void]$data.Columns.Add($field, $type)
}
$position = 0
foreach ($item in $source) {
    if (@($item.PSObject.Properties).Count -ne $fields.Count) { throw 'Unexpected fixture fields.' }
    $row = $data.NewRow()
    $row.SourceRowNo = ++$position
    foreach ($field in $fields) {
        if ($null -eq $item.PSObject.Properties[$field]) { throw "Missing field: $field" }
        $value = $item.$field
        if ($null -eq $value) { $row[$field] = [DBNull]::Value }
        elseif ($field -in $numeric) {
            if ($value -isnot [long] -and $value -isnot [int]) { throw "Expected integer: $field" }
            $row[$field] = $value
        } else {
            if ($value -isnot [string] -or $value.Length -gt 256) { throw "Invalid string: $field" }
            $row[$field] = $value
        }
    }
    $data.Rows.Add($row)
}
$config = Get-Content -LiteralPath "$PSScriptRoot/../src/04_Api/AMES.Api/appsettings.json" -Raw | ConvertFrom-Json
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($config.ConnectionStrings.AMES)
$builder.set_DataSource($Server)
$builder.set_InitialCatalog('AMES_DEV')
$builder.set_ConnectTimeout(10)
$connection = [System.Data.SqlClient.SqlConnection]::new($builder.get_ConnectionString())
$transaction = $null
try {
    $connection.Open()
    $transaction = $connection.BeginTransaction()
    $command = $connection.CreateCommand()
    $command.Transaction = $transaction
    $definitions = foreach ($field in $fields) {
        $sqlType = if ($field -in $numeric) { 'bigint' } else { 'nvarchar(256)' }
        "[$field] $sqlType NULL"
    }
    $command.CommandText = "IF OBJECT_ID(N'dbo.TEST_SRM_PurchaseOrderResponse') IS NOT NULL THROW 50001, 'Test table already exists. No data was changed.', 1; CREATE TABLE dbo.TEST_SRM_PurchaseOrderResponse ([SourceRowNo] int NOT NULL PRIMARY KEY, " + ($definitions -join ',') + ', [LoadedAtUtc] datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME());'
    [void]$command.ExecuteNonQuery()
    $bulk = [System.Data.SqlClient.SqlBulkCopy]::new($connection, [System.Data.SqlClient.SqlBulkCopyOptions]::KeepNulls, $transaction)
    try {
        $bulk.DestinationTableName = 'dbo.TEST_SRM_PurchaseOrderResponse'
        foreach ($column in $data.Columns) { [void]$bulk.ColumnMappings.Add($column.ColumnName, $column.ColumnName) }
        $bulk.WriteToServer($data)
    } finally { $bulk.Dispose() }
    $command.CommandText = 'SELECT [SourceRowNo], ' + (($fields | ForEach-Object { "[$_]" }) -join ',') + ' FROM dbo.TEST_SRM_PurchaseOrderResponse ORDER BY SourceRowNo;'
    $actual = [System.Data.DataTable]::new()
    $actual.Load($command.ExecuteReader())
    if ($actual.Rows.Count -ne $data.Rows.Count) { throw 'Row count mismatch.' }
    for ($i = 0; $i -lt $data.Rows.Count; $i++) {
        foreach ($column in $data.Columns) {
            if (-not [object]::Equals($data.Rows[$i][$column.ColumnName], $actual.Rows[$i][$column.ColumnName])) {
                throw "Stored value mismatch at row $i, column $($column.ColumnName)."
            }
        }
    }
    $transaction.Commit()
    $transaction = $null
    [pscustomobject]@{Database='AMES_DEV';Table='dbo.TEST_SRM_PurchaseOrderResponse';Rows=$actual.Rows.Count;ResponseFields=$fields.Count;AllValuesVerified=$true} | ConvertTo-Json
} catch {
    if ($null -ne $transaction) { $transaction.Rollback() }
    throw
} finally { $connection.Dispose() }
