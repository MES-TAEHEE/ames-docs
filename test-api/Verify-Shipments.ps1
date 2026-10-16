param([int]$Port = 15229, [switch]$Database)
$ErrorActionPreference = 'Stop'
# Standalone checks use an isolated store and never connect to the shared SQL database.
$project = Join-Path $PSScriptRoot 'SrmMockApi'
$output = Join-Path $project 'bin/shipment-check'
dotnet build (Join-Path $project 'SrmMockApi.csproj') --no-restore -c Release -o $output
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$run = Join-Path $project ('App_Data/check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$baseUrl = "http://127.0.0.1:$Port"
$key = [guid]::NewGuid().ToString('N')
$testPrefix = 'VERIFY-' + [guid]::NewGuid().ToString('N')
$shipmentId = $testPrefix + '-SHIP'
$goodsId = $testPrefix + '-GOODS'
$parallelId = $testPrefix + '-PARALLEL'
$headers = @{'X-API-KEY'=$key}
$savedEnv = @{}
foreach ($name in @('SRM_TEST_API_KEY','ConnectionStrings__SrmTestDatabase','SRM_TEST_SHIPMENT_PATH','SRM_TEST_SHIPMENT_STORAGE','SRM_TEST_CORCD','SRM_TEST_BIZCD')) {
    $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$server = $null
function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }
function Start-Server {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$output/SrmMockApi.dll`"", '--urls', $baseUrl) `
        -WorkingDirectory $project -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
    try {
        for ($i=0; $i -lt 60; $i++) {
            if ($process.HasExited) { throw 'Test server exited. See App_Data/check-*/stderr.log.' }
            try {
                $ready = Invoke-WebRequest "$baseUrl/swagger/v1/swagger.json" -TimeoutSec 1
                if ($ready.StatusCode -eq 200) { return $process }
            } catch { Start-Sleep -Milliseconds 200 }
        }
        throw 'Test server startup timeout.'
    } catch { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; throw }
}
function Send($payload, $requestHeaders=$headers) {
    Invoke-WebRequest "$baseUrl/api/shipments" -Method Post -Headers $requestHeaders `
        -ContentType 'application/json' -Body ($payload | ConvertTo-Json -Depth 10 -Compress) -SkipHttpErrorCheck
}
try {
    $env:SRM_TEST_API_KEY = $key
    $env:ConnectionStrings__SrmTestDatabase = 'Server=127.0.0.1,1;Database=Unused;User ID=unused;Password=unused;Connect Timeout=1;Encrypt=False'
    $env:SRM_TEST_SHIPMENT_STORAGE = 'File'
    if ($Database) {
        $config = Get-Content -LiteralPath "$PSScriptRoot/../src/04_Api/AMES.Api/appsettings.json" -Raw | ConvertFrom-Json
        $db = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($config.ConnectionStrings.AMES)
        $db.set_DataSource('tcp:192.168.1.100,1433'); $db.set_InitialCatalog('AMES_DEV'); $db.set_ConnectTimeout(10)
        $env:ConnectionStrings__SrmTestDatabase = $db.get_ConnectionString()
        $env:SRM_TEST_SHIPMENT_STORAGE = 'Database'
    }
    $env:SRM_TEST_SHIPMENT_PATH = Join-Path $run 'shipments'
    $env:SRM_TEST_CORCD = '7700'; $env:SRM_TEST_BIZCD = '7710'
    $server = Start-Server
    $schema = Invoke-RestMethod "$baseUrl/swagger/v1/swagger.json"
    Assert ($null -ne $schema.paths.'/Service/WEBSRV_INQUERY_PO.ashx') 'PO route missing.'
    Assert ($null -ne $schema.components.schemas.ShipmentRequest.properties.REQUEST_ID) 'Swagger field casing mismatch.'
    Assert ($null -ne $schema.paths.'/api/shipments'.post.requestBody.content.'application/json'.example.ITEMS) 'Swagger example missing.'
    $body = @{
        REQUEST_ID=$shipmentId;CORCD='7700';BIZCD='7710';VENDCD='310471';PURC_ORG='1A7700';PURC_PO_TYPE='1KMA'
        DELI_DATE='2026-09-17';ARRIV_DATE='2026-09-18';ARRIV_TIME='0930';TRUCK_NO='TEST';USER_ID='AMES_TEST'
        ITEMS=@(@{PONO='TEST-PO';PONO_SEQ='00010';UNIT_PACK_QTY=10;DELI_QTY=100;VEND_LOTNO='LOT-1';PRDT_DATE='2026-09-16';CHANGE_4M=''})
    }
    $r = Send $body @{}
    Assert ($r.StatusCode -eq 401) 'Missing API key must fail.'
    $r = Send $body @{'X-API-KEY'='wrong'}
    Assert ($r.StatusCode -eq 401) 'Wrong API key must fail.'
    $r = Send $body
    $first = $r.Content | ConvertFrom-Json
    Assert ($r.StatusCode -eq 200 -and $first.success -and -not $first.duplicate) 'Save failed.'
    Assert ($first.data.SAP_SIMULATED -and $first.data.SAP_STATUS -eq 'SIMULATED_SUCCESS' -and $first.data.TOTAL_DELI_QTY -eq 100) 'Receipt mismatch.'
    $r = Send $body
    $replay = $r.Content | ConvertFrom-Json
    Assert ($replay.duplicate -and $replay.data.DELI_NOTE -eq $first.data.DELI_NOTE) 'Replay created another delivery.'
    $different = $body.Clone(); $different.TRUCK_NO = 'CHANGED'
    Assert ((Send $different).StatusCode -eq 409) 'Changed payload must conflict.'
    foreach ($change in @(@{ARRIV_DATE='2026-09-16'},@{ARRIV_TIME='2460'},@{ITEMS=@()},@{ITEMS=$null},@{CORCD='wrong'},@{USER_ID=''},@{REQUEST_ID='../escape'},@{UNKNOWN='value'})) {
        $bad = $body.Clone(); foreach ($name in $change.Keys) { $bad[$name] = $change[$name] }
        Assert ((Send $bad).StatusCode -eq 400) 'Invalid request accepted.'
    }
    foreach ($quantity in @(0,-1)) {
        $bad = $body.Clone(); $bad.ITEMS=@($body.ITEMS[0].Clone()); $bad.ITEMS[0].DELI_QTY=$quantity
        Assert ((Send $bad).StatusCode -eq 400) 'Non-positive quantity accepted.'
    }
    $bad = $body.Clone(); $bad.ITEMS=@($body.ITEMS[0].Clone()); $bad.ITEMS[0].UNIT_PACK_QTY=[decimal]0.0000000000000000000000000001
    Assert ((Send $bad).StatusCode -eq 400) 'Extreme pack ratio must fail without overflow.'
    $malformed = Invoke-WebRequest "$baseUrl/api/shipments" -Method Post -Headers $headers -ContentType 'application/json' -Body '{' -SkipHttpErrorCheck
    Assert ($malformed.StatusCode -eq 400) 'Malformed JSON must fail.'
    $r = Invoke-WebRequest "$baseUrl/api/shipments" -Method Post -Headers $headers -ContentType 'text/plain' -Body '{}' -SkipHttpErrorCheck
    Assert ($r.StatusCode -eq 415) 'Wrong content type must fail.'
    $r = Invoke-WebRequest "$baseUrl/api/shipments" -Method Post -Headers $headers -ContentType 'application/json' -Body (' ' * 1048577) -SkipHttpErrorCheck
    Assert ($r.StatusCode -eq 413) 'Oversized body must fail.'
    $r = Invoke-WebRequest "$baseUrl/api/shipments/$shipmentId" -SkipHttpErrorCheck
    Assert ($r.StatusCode -eq 401) 'GET must require authentication.'
    $r = Invoke-WebRequest "$baseUrl/api/shipments/missing" -Headers $headers -SkipHttpErrorCheck
    Assert ($r.StatusCode -eq 404) 'Unknown request must return 404.'
    $goods = $body.Clone(); $goods.REQUEST_ID=$goodsId; $goods.PURC_PO_TYPE='1K10'
    $r = Send $goods
    Assert (($r.Content | ConvertFrom-Json).data.SAP_STATUS -eq 'NOT_REQUIRED') 'Goods branch mismatch.'
    'PASS: Swagger, authentication, save/replay/conflict, validation and goods branch'

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $parallel = $body.Clone(); $parallel.REQUEST_ID=$parallelId
        $tasks = @(1..8 | ForEach-Object {
            $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$baseUrl/api/shipments")
            $request.Headers.Add('X-API-KEY', $key)
            $request.Content = [System.Net.Http.StringContent]::new(($parallel | ConvertTo-Json -Depth 10), [Text.Encoding]::UTF8, 'application/json')
            $client.SendAsync($request)
        })
        $responses = @($tasks | ForEach-Object { $_.GetAwaiter().GetResult() })
        $receipts = @($responses | ForEach-Object {
            Assert ([int]$_.StatusCode -eq 200) 'Concurrent request failed.'
            $_.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        })
        Assert (@($receipts | Where-Object { -not $_.duplicate }).Count -eq 1) 'Concurrent duplicate accepted twice.'
        Assert (@($receipts.data.DELI_NOTE | Select-Object -Unique).Count -eq 1) 'Concurrent receipt IDs differ.'
    } finally { $client.Dispose() }
    'PASS: eight concurrent retries create one receipt'

    $server.Kill(); $server.WaitForExit(); $server = Start-Server
    $r = Send $body
    $replay = $r.Content | ConvertFrom-Json
    Assert ($replay.duplicate -and $replay.data.DELI_NOTE -eq $first.data.DELI_NOTE) 'Restart lost idempotency.'
    $r = Invoke-RestMethod "$baseUrl/api/shipments/$shipmentId" -Headers $headers
    Assert ($r.request.ITEMS[0].PONO_SEQ -ceq '00010' -and $r.data.DELI_NOTE -eq $first.data.DELI_NOTE) 'Stored request mismatch.'
    $r = Invoke-WebRequest "$baseUrl/Service/WEBSRV_INQUERY_PO.ashx" -SkipHttpErrorCheck
    Assert ($r.StatusCode -eq 401) 'Existing PO authentication changed.'
    $r = Invoke-WebRequest "$baseUrl/Service/WEBSRV_INQUERY_PO.ashx?APIKEY=$key" -SkipHttpErrorCheck
    Assert ($r.StatusCode -eq 400) 'Existing PO validation changed.'
    'PASS: process restart persistence and existing PO validation'
    if ($Database) {
        $connection = [System.Data.SqlClient.SqlConnection]::new($env:ConnectionStrings__SrmTestDatabase)
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = 'SELECT COUNT(*) FROM dbo.TEST_SRM_Shipment h INNER JOIN dbo.TEST_SRM_ShipmentItem i ON h.REQUEST_ID=i.REQUEST_ID WHERE h.REQUEST_ID IN (@a,@b,@c) AND i.PONO_SEQ=N''00010'' AND i.DELI_QTY=100 AND h.TOTAL_DELI_QTY=100;'
            [void]$command.Parameters.AddWithValue('@a',$shipmentId)
            [void]$command.Parameters.AddWithValue('@b',$goodsId)
            [void]$command.Parameters.AddWithValue('@c',$parallelId)
            Assert ($command.ExecuteScalar() -eq 3) 'SQL header/detail persistence mismatch.'
        } finally { $connection.Dispose() }
        'PASS: SQL header/detail rows, quantities and PO sequence persisted'
    }
    'All shipment checks passed.'
} finally {
    if ($server -and -not $server.HasExited) { $server.Kill(); $server.WaitForExit() }
    if ($Database -and $db) {
        $connection = [System.Data.SqlClient.SqlConnection]::new($db.get_ConnectionString())
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = 'SET XACT_ABORT ON; BEGIN TRANSACTION; DELETE FROM dbo.TEST_SRM_ShipmentItem WHERE REQUEST_ID IN (@a,@b,@c); DELETE FROM dbo.TEST_SRM_Shipment WHERE REQUEST_ID IN (@a,@b,@c); COMMIT;'
            [void]$command.Parameters.AddWithValue('@a',$shipmentId)
            [void]$command.Parameters.AddWithValue('@b',$goodsId)
            [void]$command.Parameters.AddWithValue('@c',$parallelId)
            [void]$command.ExecuteNonQuery()
            'Verification-only SQL rows cleaned up.'
        } finally { $connection.Dispose() }
    }
    foreach ($name in $savedEnv.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnv[$name], 'Process') }
}
