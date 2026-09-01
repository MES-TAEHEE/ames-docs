<#
.SYNOPSIS
    AMES.Web 게시 도우미.

.DESCRIPTION
    Package (기본)
        개발서버로 복사할 배포 패키지를 publish\AMES.Web 에 만든다.
        로컬 IIS 를 전혀 건드리지 않으므로 파일 잠금 오류가 날 수 없다.

    Local
        같은 패키지를 만든 뒤 로컬 IIS 폴더에 반영한다.
        app_offline.htm 을 먼저 떨어뜨려 ANCM 이 앱을 내리게 하므로
        w3wp 의 DLL 잠금이 풀린다 — 앱풀을 멈출 관리자 권한이 필요 없다.

.EXAMPLE
    pwsh tools\publish-web.ps1                          # 개발서버 복사용 패키지 생성
    pwsh tools\publish-web.ps1 -Zip                     # 패키지 + zip
    pwsh tools\publish-web.ps1 -Target Local            # 로컬 IIS 반영 (개발서버 DB)
    pwsh tools\publish-web.ps1 -Target Local -DbTarget Local   # 로컬 IIS 반영 (비상: 로컬 DB)
#>
[CmdletBinding()]
param(
    [ValidateSet('Package', 'Local')]
    [string]$Target = 'Package',

    [string]$Configuration = 'Release',

    # 로컬 IIS 배포 폴더 (IIS 사이트 물리 경로와 반드시 같아야 한다)
    [string]$LiveDir = 'C:\inetpub\wwwroot\Source\AMES.Web',

    # Server = appsettings 그대로(개발서버 DB) / Local = 비상용 로컬 DB 로 덮어쓰기.
    # -Target Local 일 때만 의미가 있다. appsettings 파일은 건드리지 않고
    # web.config 의 환경변수로 덮어쓰므로 소스가 오염되지 않는다.
    # 이름을 Db 로 줄이면 안 된다 — 공통 매개변수 -Debug 의 별칭 'db' 와 충돌한다.
    [ValidateSet('Server', 'Local')]
    [string]$DbTarget = 'Server',

    [string]$LocalDbConn = 'Server=localhost\MSSQLSERVER01;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=30;',

    [switch]$Zip
)

$ErrorActionPreference = 'Stop'

$Repo    = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Repo 'src\06_Web\AMES.Web\AMES.Web.csproj'
$PkgDir  = Join-Path $Repo 'publish\AMES.Web'

if (-not (Test-Path $Project)) { throw "프로젝트를 찾을 수 없습니다: $Project" }

function Write-Step($msg) { Write-Host "`n[$([DateTime]::Now.ToString('HH:mm:ss'))] $msg" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------
# 1. 패키지 생성 (항상 수행)
# ---------------------------------------------------------------------------
Write-Step "패키지 생성 -> $PkgDir"

# -o 는 기존 파일을 지우지 않는다. 이전 빌드 잔재가 섞이지 않도록 비우고 시작한다.
if (Test-Path $PkgDir) { Remove-Item $PkgDir -Recurse -Force }
New-Item -ItemType Directory -Path $PkgDir -Force | Out-Null

# IIS.pubxml 과 동일한 조건: framework-dependent / win-x64 / Production
& dotnet publish $Project `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -p:EnvironmentName=Production `
    -o $PkgDir `
    --nologo -v m

if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (exit $LASTEXITCODE)" }

$fileCount = (Get-ChildItem $PkgDir -Recurse -File | Measure-Object).Count
Write-Host "  파일 $fileCount 개" -ForegroundColor Green

# 배포 사고 1순위 — 접속 문자열을 눈으로 확인하고 넘어간다.
# 이 파일들은 // 주석이 섞인 JSONC 라 ConvertFrom-Json 이 못 읽는다. 주석 줄을 걸러내고 찾는다.
function Get-ActiveConn([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*//') { continue }
        if ($line -match '"AMES"\s*:\s*"([^"]+)"') { return $Matches[1] }
    }
    return $null
}

# IIS 는 ASPNETCORE_ENVIRONMENT=Production 이므로 Production 이 base 를 덮어쓴다
$conn = Get-ActiveConn (Join-Path $PkgDir 'appsettings.Production.json')
if (-not $conn) { $conn = Get-ActiveConn (Join-Path $PkgDir 'appsettings.json') }
Write-Host "  적용될 접속 문자열: $conn" -ForegroundColor Yellow

$api = Get-Content (Join-Path $PkgDir 'appsettings.json') -Raw | Select-String '"ApiBaseUrl"\s*:\s*"([^"]+)"'
if ($api) { Write-Host "  ApiBaseUrl        : $($api.Matches.Groups[1].Value)" -ForegroundColor Yellow }

# ---------------------------------------------------------------------------
# 2. zip (선택)
# ---------------------------------------------------------------------------
if ($Zip) {
    $stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
    $zipPath = Join-Path $Repo "publish\AMES.Web_$stamp.zip"
    Write-Step "압축 -> $zipPath"
    Compress-Archive -Path (Join-Path $PkgDir '*') -DestinationPath $zipPath -Force
    Write-Host "  $([Math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 3. 로컬 IIS 반영 (-Target Local 일 때만)
# ---------------------------------------------------------------------------
if ($Target -eq 'Local') {

    if (-not (Test-Path $LiveDir)) { throw "IIS 배포 폴더가 없습니다: $LiveDir" }

    $offline = Join-Path $LiveDir 'app_offline.htm'
    Write-Step "앱 정지 (app_offline.htm)"
    Set-Content -Path $offline -Encoding utf8 -Value @'
<!doctype html><html lang="ko"><head><meta charset="utf-8"><title>배포 중</title></head>
<body style="font-family:sans-serif;padding:3rem"><h1>배포 중입니다</h1><p>잠시 후 다시 시도해 주세요.</p></body></html>
'@

    # ANCM 이 앱을 내리고 파일 핸들을 놓을 때까지 기다린다
    $dll = Join-Path $LiveDir 'AMES.Web.dll'
    $freed = $false
    foreach ($i in 1..30) {
        Start-Sleep -Milliseconds 500
        if (-not (Test-Path $dll)) { $freed = $true; break }
        try { $fs = [IO.File]::Open($dll, 'Open', 'ReadWrite', 'None'); $fs.Close(); $freed = $true; break }
        catch { }
    }
    if (-not $freed) {
        Remove-Item $offline -Force -ErrorAction SilentlyContinue
        throw "15초 안에 파일 잠금이 풀리지 않았습니다. 앱풀을 수동으로 중지한 뒤 다시 시도하세요."
    }
    Write-Host "  잠금 해제됨" -ForegroundColor Green

    Write-Step "복사 -> $LiveDir"
    # /MIR 로 잔재까지 정리하되 app_offline.htm 은 지우지 않는다
    & robocopy $PkgDir $LiveDir /MIR /XF app_offline.htm /NFL /NDL /NJH /NJS /R:2 /W:2 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy 실패 (exit $LASTEXITCODE)" }
    Write-Host "  파일 $((Get-ChildItem $LiveDir -Recurse -File).Count) 개" -ForegroundColor Green

    if ($DbTarget -eq 'Local') {
        # 환경변수가 appsettings 보다 우선순위가 높다. web.config 는 게시할 때마다
        # 새로 생성되므로, 배포 직후 여기서 주입해야 남는다.
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
        $r = Invoke-WebRequest -Uri 'http://localhost/Account/Login' -UseBasicParsing -TimeoutSec 60
        Write-Host "  HTTP $($r.StatusCode) OK" -ForegroundColor Green
    } catch {
        Write-Warning "기동 확인 실패: $($_.Exception.Message)"
        Write-Warning "원인을 보려면 web.config 의 stdoutLogEnabled 를 잠시 true 로 바꾸고 logs\stdout*.log 를 확인하세요."
    }
}

Write-Step "완료"
if ($Target -eq 'Package') {
    Write-Host @"

  패키지 : $PkgDir

  개발서버 반영 절차
    1. 서버에서 앱풀 중지 (또는 배포 폴더에 app_offline.htm 배치)
    2. 이 폴더를 서버 배포 경로에 복사 (기존 파일 덮어쓰기)
    3. app_offline.htm 제거 / 앱풀 시작

  서버 사전 조건
    - ASP.NET Core 10 Hosting Bundle 설치  (9.x 만 있으면 HTTP 500.31)
    - 앱풀: 관리 코드 없음(No Managed Code), Load User Profile = True
    - 배포 폴더에 앱풀 계정 읽기/실행 권한
"@ -ForegroundColor Gray
}
