<#
.SYNOPSIS
    AMES.Pop 설치 패키지 — 다른 PC 에 복사만 하면 돌아가는 폴더(실행 파일 + 설정 파일 + wwwroot)를 publish\AMES.Pop 에 만든다.

.DESCRIPTION
    - .NET 런타임을 실행 파일 안에 넣는다(self-contained, win-x64, 단일 파일) — 대상 PC 에 .NET 설치가 필요 없다.
    - 설정 파일(appsettings.json, appsettings.Production.json)은 작업 폴더의 로컬 수정본이 아니라 **커밋된 버전(HEAD)** 을 담는다
      (개발 PC 의 임시 DB 주소·창 모드가 설치본에 섞이지 않게). 현장 PC 마다 다른 값(프린터 주소·스캐너 COM 포트·DB)은 대상 PC 에서 이 파일을 고친다.
    - 대상 PC 전제: Windows 10/11 x64 + WebView2 런타임(Windows 11 은 기본 포함, 없으면 Microsoft 에서 Evergreen 설치).

.EXAMPLE
    pwsh tools\publish-pop.ps1          # publish\AMES.Pop 폴더 생성
    pwsh tools\publish-pop.ps1 -Zip     # + publish\AMES.Pop_yyyyMMdd-HHmmss.zip
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$Zip
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\03_Pop\AMES.Pop\AMES.Pop.csproj'
$out  = Join-Path $root 'publish\AMES.Pop'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

dotnet publish $proj -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 ($LASTEXITCODE)" }

# 설정 파일은 커밋된 버전으로 덮어쓴다(개발 PC 의 로컬 수정본 제외). Development 설정은 설치본에 두지 않는다.
Push-Location $root
try {
    foreach ($f in 'appsettings.json', 'appsettings.Production.json') {
        $text = git show "HEAD:src/03_Pop/AMES.Pop/$f"
        if ($LASTEXITCODE -ne 0) { throw "git show 실패: $f" }
        [System.IO.File]::WriteAllText((Join-Path $out $f), ($text -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding($true)))
    }
} finally { Pop-Location }
# 설치본에 필요 없는 파일(개발용 설정·XML 문서·정적 자산 매니페스트)은 뺀다
foreach ($n in 'appsettings.Development.json', '*.xml', '*.staticwebassets.endpoints.json') {
    Get-ChildItem $out -File -Filter $n -ErrorAction SilentlyContinue | Remove-Item -Force
}

$files = Get-ChildItem $out -File | Select-Object -ExpandProperty Name
Write-Host "`n패키지: $out"
Write-Host ("  파일: " + ($files -join ', '))
Write-Host "  wwwroot: " -NoNewline; Write-Host ((Get-ChildItem (Join-Path $out 'wwwroot') -Recurse -File).Count.ToString() + "개")
Write-Host "대상 PC 에서 appsettings.Production.json(DB)·appsettings.json(프린터·스캐너)을 확인한 뒤 AMES.Pop.exe 를 실행하세요."

if ($Zip) {
    $zipPath = Join-Path $root ("publish\AMES.Pop_{0:yyyyMMdd-HHmmss}.zip" -f (Get-Date))
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zipPath
    Write-Host "zip: $zipPath"
}
