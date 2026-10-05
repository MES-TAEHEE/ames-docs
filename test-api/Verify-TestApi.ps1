param([string]$BaseUrl = 'http://192.168.1.68:5220')
$ErrorActionPreference = 'Stop'
if (-not $env:SRM_TEST_API_KEY) { throw 'Set SRM_TEST_API_KEY to run verification.' }
$fixture = Get-Content "$PSScriptRoot/SrmMockApi/Data/PO_7700_310471_EN_data.json" -Raw | ConvertFrom-Json
$defaults = @{APIKEY=$env:SRM_TEST_API_KEY; CORCD='7700'; BIZCD='7710'; PURC_ORG='1A7700'; VENDCD='310471'; PO_DATE_BEG='2026-08-01'; PO_DATE_TO='2026-09-30'}
function Request([hashtable]$Overrides, [string]$Path='/Service/WEBSRV_INQUERY_PO.ashx') {
    $values=$defaults.Clone()
    foreach($name in $Overrides.Keys){if($null -eq $Overrides[$name]){$values.Remove($name)}else{$values[$name]=$Overrides[$name]}}
    $query=($values.GetEnumerator()|ForEach-Object{[uri]::EscapeDataString($_.Key)+'='+[uri]::EscapeDataString($_.Value)})-join '&'
    Invoke-WebRequest -Uri ($BaseUrl+$Path+'?'+$query) -SkipHttpErrorCheck
}
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$r=Request @{}
Assert ($r.StatusCode -eq 200) 'Full request failed.'
$rows=$r.Content|ConvertFrom-Json
Assert ($rows.Count -eq 426) 'Full response count mismatch.'
for($i=0;$i -lt $fixture.Count;$i++){
    Assert (@($rows[$i].PSObject.Properties).Count -eq 45) 'Field count mismatch.'
    foreach($p in $fixture[$i].PSObject.Properties){
        Assert ($null -ne $rows[$i].PSObject.Properties[$p.Name]) 'Missing response field.'
        Assert ([object]::Equals($p.Value,$rows[$i].PSObject.Properties[$p.Name].Value)) ('Value mismatch: '+$p.Name)
    }
}
'PASS: 426 rows / 45 fields / all values match fixture'
$r=Request @{PO_DATE_BEG='2026-06-01';PO_DATE_TO='2026-07-02'}
Assert ($r.StatusCode -eq 200 -and $r.Content.Trim() -eq '[]') 'Provided date range must return empty array.'
'PASS: supplied June-July date range returns []'
$r=Request @{PO_DATE_BEG='2026-08-31';PO_DATE_TO='2026-08-31'}
$expected=@($fixture|Where-Object PO_DATE -eq '2026-08-31').Count
Assert (@($r.Content|ConvertFrom-Json).Count -eq $expected) 'Inclusive date range mismatch.'
'PASS: same-day date range includes matching orders'
foreach($entry in @(@{CORCD='wrong'},@{BIZCD='wrong'},@{PURC_ORG='wrong'},@{VENDCD='wrong'},@{VENDCD="310471' OR 1=1--"})){
    $r=Request $entry
    Assert ($r.StatusCode -eq 200 -and $r.Content.Trim() -eq '[]') 'Scope/filter mismatch.'
}
'PASS: company, business, organization, vendor filters and SQL parameter handling'
foreach($entry in @(@{APIKEY='wrong'},@{APIKEY=$null})){
    $r=Request $entry
    Assert ($r.StatusCode -eq 401) 'Invalid key must return 401.'
}
'PASS: invalid/missing key returns 401'
foreach($entry in @(@{VENDCD=$null},@{PO_DATE_BEG='2026-99-01'},@{PO_DATE_BEG='2026-10-01'},@{UNSUPPORTED='1'})){
    $r=Request $entry
    Assert ($r.StatusCode -eq 400) 'Invalid query must return 400.'
}
'PASS: missing fields, invalid/reversed dates, unsupported parameters return 400'
$r=Request @{} '/api/purchase-orders'
Assert ($r.StatusCode -eq 200 -and @($r.Content|ConvertFrom-Json).Count -eq 426) 'Alias mismatch.'
$health=Invoke-RestMethod ($BaseUrl+'/api/health')
Assert ($health.mode -eq 'database' -and $health.records -eq 426) 'DB health failed.'
'PASS: alias and database health'
'All checks passed.'
