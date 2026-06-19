#Requires -RunAsAdministrator
<#
.SYNOPSIS
    AMES.Web IIS 배포 설정 스크립트
.DESCRIPTION
    1단계: ASP.NET Core Hosting Bundle .NET 9 설치 확인
    2단계: dotnet publish 실행 (C:\inetpub\ames-web)
    3단계: IIS App Pool 및 Site 생성
    4단계: 폴더 권한 설정

    실행 방법: PowerShell (관리자)에서 .\setup-iis.ps1
              특정 포트 지정: .\setup-iis.ps1 -Port 8080
#>
param(
    [int]    $Port      = 5000,
    [string] $SiteName  = "AMES.Web",
    [string] $PoolName  = "AMES.Web",
    [string] $PublishTo = "C:\inetpub\ames-web",
    [string] $SrcProject = "$PSScriptRoot\..\src\06_Web\AMES.Web\AMES.Web.csproj"
)

$ErrorActionPreference = "Stop"

# ── 색상 출력 헬퍼 ─────────────────────────────────────────────────────────
function Write-Step  { param($msg) Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-OK    { param($msg) Write-Host "    [OK] $msg" -ForegroundColor Green }
function Write-WARN  { param($msg) Write-Host "    [!!] $msg" -ForegroundColor Yellow }
function Write-FAIL  { param($msg) Write-Host "    [XX] $msg" -ForegroundColor Red; exit 1 }

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
Write-Step "AMES.Web 게시 → $PublishTo"

if (-not (Test-Path $SrcProject)) {
    Write-FAIL "프로젝트 파일을 찾을 수 없습니다: $SrcProject"
}

dotnet publish $SrcProject `
    -p:PublishProfile=IIS `
    --configuration Release `
    --framework net10.0 `
    --no-self-contained `
    -r win-x64 `
    -o $PublishTo

if ($LASTEXITCODE -ne 0) { Write-FAIL "dotnet publish 실패 (exit code $LASTEXITCODE)" }
Write-OK "게시 완료: $PublishTo"

# ── 3. IIS 모듈 로드 ─────────────────────────────────────────────────────
Write-Step "IIS WebAdministration 모듈 로드"

Import-Module WebAdministration
Write-OK "WebAdministration 로드 완료"

# ── 4. App Pool 생성/재사용 ───────────────────────────────────────────────
Write-Step "App Pool: $PoolName"

if (Test-Path "IIS:\AppPools\$PoolName") {
    Write-WARN "App Pool '$PoolName' 이미 존재 → 설정 업데이트"
} else {
    New-WebAppPool -Name $PoolName | Out-Null
    Write-OK "App Pool '$PoolName' 생성"
}

# ASP.NET Core = No Managed Code
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name enable32BitAppOnWin64  -Value $false
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel.identityType -Value "ApplicationPoolIdentity"
Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue
Write-OK "App Pool 설정: No Managed Code / x64 / ApplicationPoolIdentity"

# ── 5. Site 생성/재사용 ───────────────────────────────────────────────────
Write-Step "IIS Site: $SiteName (포트 $Port)"

$binding = "*:${Port}:"

if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
    Write-WARN "Site '$SiteName' 이미 존재 → 물리 경로·바인딩 업데이트"
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PublishTo
    # 기존 바인딩 제거 후 재설정
    Get-WebBinding -Name $SiteName | Remove-WebBinding
    New-WebBinding -Name $SiteName -Protocol http -Port $Port -IPAddress "*"
} else {
    New-Website -Name $SiteName `
                -PhysicalPath $PublishTo `
                -ApplicationPool $PoolName `
                -Port $Port `
                -IPAddress "*" | Out-Null
    Write-OK "Site '$SiteName' 생성 (http://localhost:$Port)"
}

# App Pool 연결
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $PoolName
Start-Website -Name $SiteName -ErrorAction SilentlyContinue
Write-OK "Site '$SiteName' → http://localhost:$Port"

# ── 6. 폴더 권한 설정 ────────────────────────────────────────────────────
Write-Step "폴더 권한 설정: $PublishTo"

$acl     = Get-Acl $PublishTo
$appPoolUser = "IIS AppPool\$PoolName"
$rule    = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $appPoolUser, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow"
)
$acl.SetAccessRule($rule)
Set-Acl $PublishTo $acl
Write-OK "권한 부여: '$appPoolUser' → ReadAndExecute"

# ── 7. IIS 재시작 ────────────────────────────────────────────────────────
Write-Step "IIS 재시작"
iisreset /restart | Out-Null
Write-OK "IIS 재시작 완료"

# ── 완료 ─────────────────────────────────────────────────────────────────
Write-Host @"

==========================================
  AMES.Web IIS 배포 완료
  URL: http://localhost:$Port
==========================================
"@ -ForegroundColor Green
