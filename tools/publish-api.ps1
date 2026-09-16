<#
.SYNOPSIS
    AMES.Api 게시 도우미 — publish-web.ps1 과 같은 방식.

.DESCRIPTION
    Package (기본)
        개발서버로 복사할 배포 패키지를 publish\AMES.Api 에 만든다.

    Local
        같은 패키지를 만든 뒤 로컬 IIS 폴더(C:\inetpub\wwwroot\Source\AMES.Api)에 반영한다.
        app_offline.htm 으로 ANCM 이 앱을 내리게 해 DLL 잠금을 푼다.
        IIS 사이트 AMES.Api(앱풀 AMES.Api, http *:5210 — Web 의 Services:ApiBaseUrl 과 같은 포트)가
        없으면 -EnsureIis 로 만든다(관리자 권한 필요). 앱풀은 Web 과 따로 둔다 —
        in-process 는 앱풀 하나에 앱 하나만 허용된다.

.EXAMPLE
    pwsh tools\publish-api.ps1                          # 개발서버 복사용 패키지 생성
    pwsh tools\publish-api.ps1 -Zip                     # 패키지 + zip
    pwsh tools\publish-api.ps1 -Target Local            # 로컬 IIS 반영 (개발서버 DB)
    pwsh tools\publish-api.ps1 -Target Local -EnsureIis # 사이트·앱풀이 없으면 만들고 반영
    pwsh tools\publish-api.ps1 -Target Local -DbTarget Local   # 로컬 IIS 반영 (비상: 로컬 DB)
#>
[CmdletBinding()]
param(
    [ValidateSet('Package', 'Local')]
    [string]$Target = 'Package',

    [string]$Configuration = 'Release',

    # 로컬 IIS 배포 폴더 (IIS 사이트 물리 경로와 반드시 같아야 한다)
    [string]$LiveDir = 'C:\inetpub\wwwroot\Source\AMES.Api',

    [string]$SiteName = 'AMES.Api',
    [string]$PoolName = 'AMES.Api',
    [int]   $Port     = 5210,

    # IIS 사이트·앱풀이 없으면 생성 (관리자 권한)
    [switch]$EnsureIis,

    [ValidateSet('Server', 'Local')]
    [string]$DbTarget = 'Server',

    [string]$LocalDbConn = 'Server=localhost\MSSQLSERVER01;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=30;',

    [switch]$Zip
)

$ErrorActionPreference = 'Stop'

$Repo    = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Repo 'src\04_Api\AMES.Api\AMES.Api.csproj'
$PkgDir  = Join-Path $Repo 'publish\AMES.Api'

if (-not (Test-Path $Project)) { throw "프로젝트를 찾을 수 없습니다: $Project" }

function Write-Step($msg) { Write-Host "`n[$([DateTime]::Now.ToString('HH:mm:ss'))] $msg" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------
# 1. 패키지 생성 (항상 수행)
# ---------------------------------------------------------------------------
Write-Step "패키지 생성 -> $PkgDir"

if (Test-Path $PkgDir) { Remove-Item $PkgDir -Recurse -Force }
New-Item -ItemType Directory -Path $PkgDir -Force | Out-Null

# Web 과 같은 조건: framework-dependent / win-x64 / Production
& dotnet publish $Project `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -p:EnvironmentName=Production `
    -o $PkgDir `
    --nologo -v m

if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (exit $LASTEXITCODE)" }

$count = (Get-ChildItem $PkgDir -Recurse -File).Count
Write-Host "  파일 $count 개" -ForegroundColor Green

function Get-ActiveConn([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $m = Get-Content $path -Raw | Select-String -Pattern '(?m)^\s*"AMES"\s*:\s*"([^"]+)"' -AllMatches
    if ($m.Matches.Count -gt 0) { return $m.Matches[0].Groups[1].Value }
    return $null
}
$conn = Get-ActiveConn (Join-Path $PkgDir 'appsettings.Production.json')
if (-not $conn) { $conn = Get-ActiveConn (Join-Path $PkgDir 'appsettings.json') }
if ($conn) { Write-Host "  적용될 접속 문자열: $conn" -ForegroundColor Yellow }

# ---------------------------------------------------------------------------
# 2. zip (선택)
# ---------------------------------------------------------------------------
if ($Zip) {
    $stamp   = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
    $zipPath = Join-Path $Repo "publish\AMES.Api_$stamp.zip"
    Write-Step "압축 -> $zipPath"
    Compress-Archive -Path (Join-Path $PkgDir '*') -DestinationPath $zipPath -Force
    Write-Host "  $([Math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 3. 로컬 IIS 반영 (-Target Local 일 때만)
# ---------------------------------------------------------------------------
if ($Target -eq 'Local') {

    if ($EnsureIis) {
        Write-Step "IIS 사이트·앱풀 확인 ($SiteName, http *:$Port)"
        Import-Module WebAdministration
        if (-not (Test-Path $LiveDir)) { New-Item -ItemType Directory -Path $LiveDir -Force | Out-Null }
        if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
            New-WebAppPool -Name $PoolName | Out-Null
            Write-Host "  앱풀 생성: $PoolName" -ForegroundColor Green
        }
        # 관리 코드 없음 · 64비트 · 프로필 로드(Data Protection 키 영속)
        Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ''
        Set-ItemProperty "IIS:\AppPools\$PoolName" -Name enable32BitAppOnWin64 -Value $false
        Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel.loadUserProfile -Value $true
        & "$env:windir\system32\inetsrv\appcmd.exe" set apppool $PoolName /processModel.setProfileEnvironment:true | Out-Null
        if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
            New-Website -Name $SiteName -PhysicalPath $LiveDir -ApplicationPool $PoolName -Port $Port | Out-Null
            Write-Host "  사이트 생성: $SiteName -> $LiveDir (:$Port)" -ForegroundColor Green
        }
        $acl = Get-Acl $LiveDir
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS AppPool\$PoolName", 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule); Set-Acl $LiveDir $acl
        Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue
        Start-Website -Name $SiteName -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path $LiveDir)) { throw "IIS 배포 폴더가 없습니다: $LiveDir (처음이면 -EnsureIis)" }

    $offline = Join-Path $LiveDir 'app_offline.htm'
    Write-Step "앱 정지 (app_offline.htm)"
    Set-Content -Path $offline -Encoding utf8 -Value '<!doctype html><html><body>deploying</body></html>'

    $dll = Join-Path $LiveDir 'AMES.Api.dll'
    $freed = $false
    foreach ($i in 1..30) {
        Start-Sleep -Milliseconds 500
        if (-not (Test-Path $dll)) { $freed = $true; break }
        try { $fs = [IO.File]::Open($dll, 'Open', 'ReadWrite', 'None'); $fs.Close(); $freed = $true; break }
        catch { }
    }
    if (-not $freed) {
        Remove-Item $offline -Force -ErrorAction SilentlyContinue
        throw "15초 안에 파일 잠금이 풀리지 않았습니다. 앱풀 $PoolName 을 수동으로 중지한 뒤 다시 시도하세요."
    }
    Write-Host "  잠금 해제됨" -ForegroundColor Green

    Write-Step "복사 -> $LiveDir"
    & robocopy $PkgDir $LiveDir /MIR /XF app_offline.htm /NFL /NDL /NJH /NJS /R:2 /W:2 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy 실패 (exit $LASTEXITCODE)" }
    Write-Host "  파일 $((Get-ChildItem $LiveDir -Recurse -File).Count) 개" -ForegroundColor Green

    if ($DbTarget -eq 'Local') {
        Write-Step "DB 오버라이드 -> 로컬"
        $wcPath = Join-Path $LiveDir 'web.config'
        $node = '<environmentVariable name="ConnectionStrings__AMES" value="{0}" />' -f [Security.SecurityElement]::Escape($LocalDbConn)
        $raw = (Get-Content $wcPath -Raw).Replace('</environmentVariables>', "  $node`r`n        </environmentVariables>")
        Set-Content -Path $wcPath -Value $raw -Encoding utf8
        Write-Host "  $LocalDbConn" -ForegroundColor Yellow
    }

    Write-Step "앱 재기동"
    Remove-Item $offline -Force

    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$Port/api/health" -UseBasicParsing -TimeoutSec 60
        Write-Host "  HTTP $($r.StatusCode) $($r.Content)" -ForegroundColor Green
    } catch {
        Write-Warning "기동 확인 실패: $($_.Exception.Message)"
        Write-Warning "이벤트 뷰어 > 응용 프로그램 > IIS AspNetCore Module V2 를 확인하세요."
    }
}

Write-Step "완료"
if ($Target -eq 'Package') {
    Write-Host "  개발서버 반영: 앱풀 중지(또는 app_offline.htm) -> $PkgDir\* 덮어쓰기 -> 재시작" -ForegroundColor DarkGray
}
