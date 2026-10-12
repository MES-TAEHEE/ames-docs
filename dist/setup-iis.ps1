#Requires -RunAsAdministrator
<#
.SYNOPSIS
    AMES.Web IIS 배포 설정 스크립트
.DESCRIPTION
    1단계: ASP.NET Core Hosting Bundle .NET 10 설치 확인
    2단계: 별도 릴리스 폴더에 dotnet publish 실행 (기존 사이트 폴더 유지)
    3단계: IIS App Pool 및 Site 생성 (loadUserProfile · setProfileEnvironment 포함)
    4단계: 폴더 권한 설정 (배포 폴더 읽기/실행 + Data Protection 키 폴더 수정)

    실행 방법: PowerShell (관리자)에서 .\setup-iis.ps1
              특정 포트 지정: .\setup-iis.ps1 -Port 8080

    이미 운영 중인 서버에 앱풀 설정·키 폴더만 보강할 때:
              .\setup-iis.ps1 -ConfigOnly [-PoolName AMES.Web]
              게시·사이트 경로·바인딩·IIS 재시작은 건드리지 않고 앱풀만 재활용한다.
#>
param(
    [int]    $Port      = 5000,
    [string] $SiteName  = "AMES.Web",
    [string] $PoolName  = "AMES.Web",
    [string] $PublishTo = "C:\inetpub\ames-web",
    [string] $SrcProject = "$PSScriptRoot\..\src\06_Web\AMES.Web\AMES.Web.csproj",
    # AMES.Web 이 Data Protection 키를 두는 곳. appsettings 의 DataProtection:KeyPath 를 바꿨다면 같은 값을 준다.
    [string] $KeyDir    = (Join-Path $env:ProgramData "AMES\DataProtection-Keys\AMES.Web"),
    # 앱풀 설정과 키 폴더 권한만 적용한다 (게시·사이트·바인딩·iisreset 생략)
    [switch] $ConfigOnly
)

$ErrorActionPreference = "Stop"

# ── 색상 출력 헬퍼 ─────────────────────────────────────────────────────────
function Write-Step  { param($msg) Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-OK    { param($msg) Write-Host "    [OK] $msg" -ForegroundColor Green }
function Write-WARN  { param($msg) Write-Host "    [!!] $msg" -ForegroundColor Yellow }
function Write-FAIL  { param($msg) Write-Host "    [XX] $msg" -ForegroundColor Red; exit 1 }

if (-not $ConfigOnly) {
# ── 1. ANCM V2 설치 확인 ──────────────────────────────────────────────────
Write-Step "ASP.NET Core Hosting Bundle (ANCM V2) 확인"

$ancm = "C:\Windows\System32\inetsrv\aspnetcorev2.dll"
if (-not (Test-Path $ancm)) {
    Write-WARN "ANCM V2가 설치되어 있지 않습니다."
    Write-Host @"

  ★ 먼저 아래 Hosting Bundle을 설치하세요 ★

  .NET 10 ASP.NET Core Hosting Bundle:
  https://dotnet.microsoft.com/en-us/download/dotnet/10.0
  (Windows Hosting Bundle 항목 클릭 → 설치 → 이 스크립트 재실행)

  설치 후 IIS를 재시작하세요:
    iisreset /restart

"@ -ForegroundColor Yellow
    exit 1
}
Write-OK "ANCM V2 확인됨: $ancm"

# ── 2. Publish ─────────────────────────────────────────────────────────────
if (-not [System.IO.Path]::IsPathFullyQualified($PublishTo)) { Write-FAIL "PublishTo 는 절대 경로여야 합니다." }
$ReleaseRoot = "$PublishTo-releases"
$ReleasePath = Join-Path $ReleaseRoot (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
Write-Step "AMES.Web 새 릴리스 게시 → $ReleasePath"

if (-not (Test-Path $SrcProject)) {
    Write-FAIL "프로젝트 파일을 찾을 수 없습니다: $SrcProject"
}

dotnet publish $SrcProject `
    -p:PublishProfile=IIS `
    --configuration Release `
    --framework net10.0 `
    --no-self-contained `
    -r win-x64 `
    -o $ReleasePath

if ($LASTEXITCODE -ne 0) { Write-FAIL "dotnet publish 실패 (exit code $LASTEXITCODE)" }
Write-OK "게시 완료: $ReleasePath (기존 라이브 폴더 유지)"
}   # -not $ConfigOnly

# ── 3. IIS 모듈 로드 ─────────────────────────────────────────────────────
Write-Step "IIS WebAdministration 모듈 로드"

Import-Module WebAdministration
Write-OK "WebAdministration 로드 완료"

# ── 4. App Pool 생성/재사용 ───────────────────────────────────────────────
Write-Step "App Pool: $PoolName"

if (Test-Path "IIS:\AppPools\$PoolName") {
    Write-WARN "App Pool '$PoolName' 이미 존재 → 설정 업데이트"
} elseif ($ConfigOnly) {
    Write-FAIL "App Pool '$PoolName' 이 없습니다. -ConfigOnly 는 이미 있는 앱풀에만 씁니다 (-PoolName 확인)."
} else {
    New-WebAppPool -Name $PoolName | Out-Null
    Write-OK "App Pool '$PoolName' 생성"
}

# ASP.NET Core = No Managed Code
# -ConfigOnly 는 운영 중인 앱풀의 런타임·계정 설정을 바꾸지 않는다(사용자 지정 계정으로 도는 서버가 있을 수 있다).
if (-not $ConfigOnly) {
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name enable32BitAppOnWin64  -Value $false
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel.identityType -Value "ApplicationPoolIdentity"
}
# 사용자 프로필 로드 — 꺼져 있으면 Data Protection 이 ephemeral 키로 떨어져 재활용마다 전원 로그아웃된다.
# setProfileEnvironment 는 IIS 관리자 UI 에 없어 여기서만 켤 수 있다. AMES.Web 은 키를 $KeyDir 에 직접 두므로
# 필수는 아니지만, 그 폴더에 못 쓰는 상황의 폴백(프로필 경로)이 살아 있도록 같이 켜 둔다.
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel.loadUserProfile       -Value $true
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel.setProfileEnvironment -Value $true
Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue
if ($ConfigOnly) { Write-OK "App Pool 설정: loadUserProfile / setProfileEnvironment (런타임·계정 설정은 그대로)" }
else { Write-OK "App Pool 설정: No Managed Code / x64 / ApplicationPoolIdentity / loadUserProfile / setProfileEnvironment" }

if (-not $ConfigOnly) {
# ── 5. Site 생성/재사용 ───────────────────────────────────────────────────
$releaseAcl = Get-Acl $ReleasePath
$releasePoolModel = Get-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel
$releaseUser = if ($releasePoolModel.identityType -eq "SpecificUser" -and $releasePoolModel.userName) { $releasePoolModel.userName } else { "IIS AppPool\$PoolName" }
$releaseAcl.SetAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
    $releaseUser, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")))
Set-Acl $ReleasePath $releaseAcl
Write-Step "IIS Site: $SiteName (포트 $Port)"

$binding = "*:${Port}:"

if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
    Write-WARN "Site '$SiteName' 이미 존재 → 물리 경로만 업데이트 (기존 바인딩 유지)"
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $ReleasePath
} else {
    New-Website -Name $SiteName `
                -PhysicalPath $ReleasePath `
                -ApplicationPool $PoolName `
                -Port $Port `
                -IPAddress "*" | Out-Null
    Write-OK "Site '$SiteName' 생성 (http://localhost:$Port)"
}

# App Pool 연결
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $PoolName
Start-Website -Name $SiteName -ErrorAction SilentlyContinue
Write-OK "Site '$SiteName' → http://localhost:$Port"

# ── 6. 새 릴리스 접근 확인 ─────────────────────────────────────────────────
Write-OK "권한 부여: '$releaseUser' → ReadAndExecute"
}   # -not $ConfigOnly

# ── 6-1. Data Protection 키 폴더 ────────────────────────────────────────
# 배포 폴더 밖에 둔다 — 게시(robocopy /MIR)가 배포 폴더 안의 키를 지우기 때문이다.
Write-Step "Data Protection 키 폴더: $KeyDir"

if (-not (Test-Path $KeyDir)) { New-Item -ItemType Directory -Force -Path $KeyDir | Out-Null; Write-OK "폴더 생성" }
# 앱풀이 사용자 지정 계정으로 돌면 그 계정에, 아니면 앱풀 가상 계정에 준다
$poolModel = Get-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel
$keyUser   = if ($poolModel.identityType -eq "SpecificUser" -and $poolModel.userName) { $poolModel.userName } else { "IIS AppPool\$PoolName" }
$keyAcl  = Get-Acl $KeyDir
$keyRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $keyUser, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow"
)
$keyAcl.AddAccessRule($keyRule)   # Set 이 아니라 Add — 이미 더 넓은 권한이 있으면 줄이지 않는다
Set-Acl $KeyDir $keyAcl
Write-OK "권한 부여: '$keyUser' → Modify"
$keyCount = @(Get-ChildItem $KeyDir -Filter "key-*.xml" -ErrorAction SilentlyContinue).Count
if ($keyCount -gt 0) { Write-OK "기존 키 $keyCount 개 유지 (로그인 세션 보존)" }
else { Write-WARN "키 파일 없음 — 앱이 처음 뜰 때 만든다. 이때 로그인 사용자는 한 번 로그아웃된다." }

# ── 7. IIS 재시작 ────────────────────────────────────────────────────────
if ($ConfigOnly) {
    Write-Step "App Pool 재활용: $PoolName"
    Restart-WebAppPool -Name $PoolName
    Write-OK "재활용 완료 (다른 사이트·IIS 전체는 건드리지 않음)"
} else {
    Write-Step "App Pool 재활용: $PoolName"
    Restart-WebAppPool -Name $PoolName
    Write-OK "새 릴리스 활성화 (이전 릴리스는 롤백용으로 유지)"
}

# ── 8. 적용 결과 확인 ────────────────────────────────────────────────────
Write-Step "적용 결과"
$pm = Get-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel
Write-Host ("    loadUserProfile       = {0}" -f $pm.loadUserProfile)
Write-Host ("    setProfileEnvironment = {0}" -f $pm.setProfileEnvironment)
Write-Host ("    KeyDir                = {0}" -f $KeyDir)
if (-not ($pm.loadUserProfile -and $pm.setProfileEnvironment)) { Write-WARN "프로필 설정이 적용되지 않았습니다 — appcmd 로 직접 확인하세요." }

# ── 완료 ─────────────────────────────────────────────────────────────────
Write-Host @"

==========================================
  AMES.Web IIS $(if ($ConfigOnly) { "설정 보강" } else { "배포" }) 완료
  $(if ($ConfigOnly) { "App Pool: $PoolName" } else { "URL: http://localhost:$Port" })
==========================================
"@ -ForegroundColor Green
