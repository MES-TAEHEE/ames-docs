param([switch]$Commit)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$config = Get-Content (Join-Path $root 'src/04_Api/AMES.Api/appsettings.json') | Where-Object { $_ -match '^\s*"AMES"\s*:' } | Select-Object -First 1
$cs = [regex]::Match($config, '"AMES"\s*:\s*"([^"]+)"').Groups[1].Value
if (!$cs) { throw 'Active AMES connection missing.' }
$conn = [System.Data.SqlClient.SqlConnection]::new($cs)
$conn.Open()
Write-Output "Target: $($conn.DataSource) / $($conn.Database)"
if ($Commit) {
    $backup = Join-Path ([IO.Path]::GetTempPath()) ('ames-pda-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    [void](New-Item -ItemType Directory -Path $backup)
    $list = $conn.CreateCommand()
    $list.CommandText = "SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND (name LIKE 'WH[_]%' OR name LIKE 'FG[_]%' OR name IN ('tbl_Lot','MD_Item','MD_Location','MD_Vendor','MD_CodeGroup','MD_CodeItem','PP_WorkOrder','QC_Inspection','SYS_UserProfile','SYS_Screen','AspNetUsers','AspNetUserRoles'));"
    $reader = $list.ExecuteReader()
    $tables = @()
    while ($reader.Read()) { $tables += $reader.GetString(0) }
    $reader.Close(); $list.Dispose()
    foreach ($table in $tables) {
        $data = [System.Data.DataTable]::new($table)
        $adapter = [System.Data.SqlClient.SqlDataAdapter]::new(('SELECT * FROM dbo.[' + $table.Replace(']',']]') + ']'), $conn)
        [void]$adapter.Fill($data)
        $data | Export-Clixml -Depth 8 -LiteralPath (Join-Path $backup ($table + '.xml'))
        $adapter.Dispose()
    }
    $modules = [System.Data.DataTable]::new('modules')
    $adapter = [System.Data.SqlClient.SqlDataAdapter]::new("SELECT o.name,m.definition FROM sys.sql_modules m JOIN sys.objects o ON o.object_id=m.object_id WHERE o.name LIKE 'WH[_]%' OR o.name LIKE 'FG[_]%'",$conn)
    [void]$adapter.Fill($modules)
    $modules | Export-Clixml -Depth 8 -LiteralPath (Join-Path $backup 'modules.xml')
    $adapter.Dispose()
    Write-Output "Pre-deployment data and procedure backup: $backup"
}
$tx = $conn.BeginTransaction()
try {
    foreach ($file in 'PDA_SCHEMA.sql','PDA_SEED.sql') {
        $script = Get-Content -Raw -Encoding UTF8 (Join-Path $root "dist/pda/$file")
        $batches = [regex]::Split($script, '(?im)^\s*GO\s*\r?$')
        if ($file -eq 'PDA_SCHEMA.sql') {
            $ddl = @($batches | Where-Object { $_ -notmatch '(?im)^CREATE OR ALTER PROCEDURE' })
            $procedures = @($batches | Where-Object { $_ -match '(?im)^CREATE OR ALTER PROCEDURE' })
            $batches = $ddl + $procedures
        }
        for ($i = 0; $i -lt $batches.Count; $i++) {
            if ([string]::IsNullOrWhiteSpace($batches[$i])) { continue }
            $cmd = $conn.CreateCommand()
            $cmd.Transaction = $tx
            $cmd.CommandTimeout = 120
            $cmd.CommandText = $batches[$i]
            try { [void]$cmd.ExecuteNonQuery() }
            catch { throw "$file batch $($i+1): $($_.Exception.Message)" }
            finally { $cmd.Dispose() }
        }
        Write-Output "${file}: all $($batches.Count) batches executed."
    }
    if ($Commit) { $tx.Commit(); Write-Output 'COMMITTED' }
    else { $tx.Rollback(); Write-Output 'VALIDATED AND ROLLED BACK' }
}
catch {
    try { $tx.Rollback() } catch {}
    throw
}
finally { $conn.Dispose() }
