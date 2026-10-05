param([string]$BindAddress = '192.168.1.68', [int]$Port = 5220)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:SRM_TEST_API_KEY)) {
    $secureKey = Read-Host '테스트 API 키' -AsSecureString
    $env:SRM_TEST_API_KEY = [System.Net.NetworkCredential]::new('', $secureKey).Password
}
$config = Get-Content -LiteralPath "$PSScriptRoot/../src/04_Api/AMES.Api/appsettings.json" -Raw | ConvertFrom-Json
$connection = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($config.ConnectionStrings.AMES)
$connection.set_DataSource('tcp:192.168.1.100,1433')
$connection.set_InitialCatalog('AMES_DEV')
$connection.set_ConnectTimeout(10)
$env:ConnectionStrings__SrmTestDatabase = $connection.get_ConnectionString()
dotnet run --project "$PSScriptRoot\SrmMockApi\SrmMockApi.csproj" --no-launch-profile -- --urls "http://${BindAddress}:$Port"
