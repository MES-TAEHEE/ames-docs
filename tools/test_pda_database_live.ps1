param([string]$BaseUrl = 'http://localhost:5210')
$ErrorActionPreference = 'Stop'
$checks = 0
function Request([string]$Path, [string]$Method = 'Get', $Body = $null) {
    $args = @{ Uri="$BaseUrl/api/$Path"; Headers=$script:headers; Method=$Method; TimeoutSec=30 }
    if ($null -ne $Body) { $args.ContentType='application/json'; $args.Body=($Body | ConvertTo-Json -Depth 12) }
    try { $result = Invoke-RestMethod @args }
    catch { throw "$Method $Path failed: $($_.ErrorDetails.Message) $($_.Exception.Message)" }
    if ($result.PSObject.Properties['success'] -and !$result.success) { throw "$Path returned failure: $($result.message)" }
    $script:checks++
    return $result
}
function ExpectFailure([string]$Path, $Body, [int]$Status) {
    try {
        $null = Invoke-RestMethod "$BaseUrl/api/$Path" -Headers $script:headers -Method Post -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 12) -TimeoutSec 30
    } catch {
        if ([int]$_.Exception.Response.StatusCode -ne $Status) { throw }
        $script:checks++
        return
    }
    throw "$Path unexpectedly succeeded"
}
foreach ($user in 'SCTEST1','SCTEST2') {
    $login = Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{
        employeeNo=$user; pin='0000'; terminalId='PDA-DB-VERIFY'; lineId='LINE-INJ-01'; shiftCode='DAY'
    } | ConvertTo-Json)
    if (!$login.token) { throw "$user login failed" }
    $script:headers = @{ Authorization="Bearer $($login.token)" }
    if ($user -eq 'SCTEST1') {
        $null = Request 'wh/inbound/test/simple-reset' Post
        foreach ($screen in 'release','inventory','adjust','history') { $null = Request "wh/test/ppt-reset/$screen" Post }
    } else {
        $null = Request 'wh/adjust/test/reset' Post
        $null = Request 'wh/transactions/test/reset' Post
    }
    foreach ($screen in 'qc','putaway','inventory','release','loading','return','adjust','history') {
        $null = Request "fg/test/ppt-reset/$screen" Post
    }
    foreach ($path in @(
        'wh/inventory','wh/inventory/lots','wh/locations','wh/warehouse-transactions',
        'wh/inbound/scan?mode=LOCAL&barcode=5011LL260828000001',
        'wh/release/schedule/PS-PPT-WH-01/status','wh/release/schedule/PS-PPT-WH-01/lines',
        'wh/release/schedule/PS-PPT-WH-01/fifo-lots',
        'wh/adjust/scan?scanText=5011LL260908830001',
        'fg/qc-completed','fg/inventory','fg/transactions','fg/orders',
        'fg/putaway/scan?barcode=5011FG260908910001',
        'fg/release/outgoing-slips/2609089001','fg/release/outgoing-slips/2609089001/lines',
        'fg/loading/order/scan?barcode=FG-PPT-SO-LOAD',
        'fg/return/scan?barcode=FG-PPT-STK-950001',
        'fg/adjust/scan?scanText=FG-PPT-STK-960001'
    )) { $null = Request $path }
    Write-Output "$user reset and screen queries passed."
}
# Exercise writes only on resettable scenario samples, then restore those samples.
try {
    ExpectFailure 'wh/inbound/receive-lot' @{ mode='LOCAL'; barcode='5011LL260908800001'; locationId='WH010201'; simulateFailure=$true } 503
    $null = Request 'wh/inbound/receive-lot' Post @{ mode='LOCAL'; barcode='5011LL260908800001'; locationId='WH010201' }
    $null = Request 'wh/inbound/move-location' Post @{ mode='LOCAL'; barcode='5011LL260908800001'; locationId='WH010101' }
    $null = Request 'wh/inbound/cancel' Post @{ mode='LOCAL'; barcode='5011LL260908800001' }
    $whFifo = Request 'wh/release/schedule/PS-PPT-WH-01/fifo-lots'
    $whLots = @($whFifo | ForEach-Object { @{ lotNo=$_.lotNo; qty=$_.qty } })
    if ($whLots.Count -ne 3) { throw 'WH release samples missing' }
    ExpectFailure 'wh/release/complete' @{ pickSlipNo='PS-PPT-WH-01'; outgoingType='PRODUCTION'; lots=$whLots; simulateFailure=$true } 503
    $null = Request 'wh/release/complete' Post @{ pickSlipNo='PS-PPT-WH-01'; outgoingType='PRODUCTION'; lots=$whLots }
    ExpectFailure 'wh/adjust/save' @{ barcode='5011LL260908830001'; deltaQty=1; reasonCode='COUNT_DIFF'; simulateFailure=$true } 503
    $whBefore = Request 'wh/adjust/scan?scanText=5011LL260908830001'
    if ($whBefore.qty -ne 10) { throw 'WH adjustment failure did not roll back' }
    $null = Request 'wh/adjust/save' Post @{ barcode='5011LL260908830001'; deltaQty=1; reasonCode='COUNT_DIFF'; reasonNote='Deployment verification' }
    $whQty = Request 'wh/adjust/scan?scanText=5011LL260908830001'
    if ($whQty.qty -ne 11) { throw 'WH adjustment balance mismatch' }
    $null = Request 'fg/adjust/save' Post @{ barcode='FG-PPT-STK-960001'; deltaQty=1; reasonCode='COUNT_DIFF'; reasonNote='Deployment verification' }
    $fgQty = Request 'fg/adjust/scan?scanText=FG-PPT-STK-960001'
    if ($fgQty.qty -ne 11) { throw 'FG adjustment balance mismatch' }
    $null = Request 'fg/putaway/confirm' Post @{ barcode='5011FG260908910001'; locationId='FG-PPT-A1'; storageMethod='LOCATION' }
    $slip = Request 'fg/release/outgoing-slips/2609089001'
    $lines = Request 'fg/release/outgoing-slips/2609089001/lines'
    $stock = Request 'fg/inventory?q=PPT-FG-REL'
    $lots = @($stock | Where-Object { $_.lotNo -like '5011FG26090893*' } | ForEach-Object {
        $s = $_
        $line = $lines | Where-Object { $_.partNo -eq $s.itemNo } | Select-Object -First 1
        @{ outgoingSlipLineId=$line.outgoingSlipLineId; stockId=$s.stockId; qty=$s.qty }
    })
    if ($lots.Count -ne 3) { throw 'FG release samples missing' }
    $null = Request 'fg/release/complete' Post @{ outgoingSlipId=$slip.outgoingSlipId; lots=$lots }
    $order = Request 'fg/loading/order/scan?barcode=FG-PPT-SO-LOAD'
    $null = Request 'fg/loading' Post @{ truckBarcode='TRUCK:PPT-VERIFY'; shipmentOrderId=$order.order.shipmentOrderId; stockIds=@($order.order.items | ForEach-Object { $_.stockId }) }
    $null = Request 'fg/return' Post @{ barcode='FG-PPT-STK-950001'; returnReason='DAMAGED_TRANSIT'; note='Deployment verification' }
    Write-Output 'Inbound, move, cancel, WH/FG adjustment, FG put-away, picking, loading and return writes passed.'
}
finally {
    # SCTEST1 owns the WH simple resets.
    $login = Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{
        employeeNo='SCTEST1'; pin='0000'; terminalId='PDA-DB-VERIFY'; lineId='LINE-INJ-01'; shiftCode='DAY'
    } | ConvertTo-Json)
    $script:headers = @{ Authorization="Bearer $($login.token)" }
    $null = Request 'wh/inbound/test/simple-reset' Post
    $null = Request 'wh/test/ppt-reset/adjust' Post
    $null = Request 'wh/test/ppt-reset/release' Post
    foreach ($screen in 'putaway','release','loading','return','adjust') { $null = Request "fg/test/ppt-reset/$screen" Post }
}
Write-Output "PASS: $checks authenticated requests."
