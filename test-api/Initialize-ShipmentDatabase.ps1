param([string]$Server = 'tcp:192.168.1.100,1433')
$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath "$PSScriptRoot/../src/04_Api/AMES.Api/appsettings.json" -Raw | ConvertFrom-Json
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($config.ConnectionStrings.AMES)
$builder.set_DataSource($Server)
$builder.set_InitialCatalog('AMES_DEV')
$builder.set_ConnectTimeout(10)
$connection = [System.Data.SqlClient.SqlConnection]::new($builder.get_ConnectionString())
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = Get-Content -LiteralPath "$PSScriptRoot/ShipmentTables.sql" -Raw
    $table = [System.Data.DataTable]::new()
    $table.Load($command.ExecuteReader())
    $table | Select-Object TableName, Rows | Format-Table
} finally { $connection.Dispose() }
