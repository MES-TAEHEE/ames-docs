param(
    [string]$BaseUrl = 'http://localhost:5210',
    [Parameter(Mandatory)][string]$EmployeeNo,
    [Parameter(Mandatory)][string]$Pin,
    [Parameter(Mandatory)][string]$LotNo
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$login = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{
    employeeNo=$EmployeeNo; pin=$Pin; terminalId='ADJUST_TEST'; lineId='WH'; shiftCode='D'
} | ConvertTo-Json)
if (-not $login.token) { throw 'Test login failed.' }

$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $login.token)
try {
    $response = $client.GetAsync("$BaseUrl/api/wh/adjust/supervisors").Result
    $response.EnsureSuccessStatusCode() | Out-Null
    $json = $response.Content.ReadAsStringAsync().Result
    $supervisors = $json | ConvertFrom-Json
    if (-not ($supervisors | Where-Object EmployeeNo -eq $EmployeeNo)) { throw 'Test employee is not eligible for adjustment approval.' }
    if ($json -match 'pin|hash') { throw 'Supervisor list exposed PIN data.' }

    # Delta zero guarantees these requests cannot modify stock, even for valid credentials.
    $cases = @(
        @{Supervisor=''; Pin=$Pin; Expected='Select a supervisor.'},
        @{Supervisor='__UNKNOWN_SUPERVISOR__'; Pin=$Pin; Expected='The PIN does not match the selected supervisor.'},
        @{Supervisor=$EmployeeNo; Pin=($Pin+'0'); Expected='The PIN does not match the selected supervisor.'},
        @{Supervisor=$EmployeeNo; Pin=$Pin; Expected='Adjustment quantity must be different from zero.'}
    )
    foreach ($case in $cases) {
        $body = @{
            barcode=$LotNo; deltaQty=0; reasonCode='COUNT_DIFF'; supervisorPin=$case.Pin
            supervisorEmployeeNo=$case.Supervisor
        } | ConvertTo-Json
        $content = [System.Net.Http.StringContent]::new($body, [System.Text.Encoding]::UTF8, 'application/json')
        $response = $client.PostAsync("$BaseUrl/api/wh/adjust/save", $content).Result
        $response.EnsureSuccessStatusCode() | Out-Null
        $result = $response.Content.ReadAsStringAsync().Result | ConvertFrom-Json
        if ($result.success -or $result.message -ne $case.Expected) { throw "Unexpected validation: $($result.message)" }
        Write-Output "PASS: $($case.Expected)"
    }
} finally {
    $client.PostAsync("$BaseUrl/api/auth/logout", $null).Result.Dispose()
    $client.Dispose()
}
