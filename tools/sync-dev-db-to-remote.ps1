<#
.SYNOPSIS
    개발서버 AMES_DEV 를 원격 Docker MSSQL 로 통째로 복제한다.

.DESCRIPTION
    ① 원본(개발서버)을 bacpac 으로 Export
    ② 원격에 임시 이름(AMES_DEV_sync_yyyyMMddHHmmss)으로 Import — 실패해도 기존 AMES_DEV 는 그대로
    ③ 콜레이션·객체 수·테이블별 행 수를 원본과 비교, 다르면 중단
    ④ 기존 AMES_DEV → AMES_DEV_prev (이전 prev 는 삭제), 임시 DB → AMES_DEV
    ⑤ ames_app 사용자를 원격 로그인에 다시 매핑 + db_owner

    원격의 다른 DB(AMES_CI 등)는 건드리지 않는다.
    .bak 복원이 아니라 bacpac 을 쓰는 이유: 개발서버 디스크·우분투 SSH 없이 SQL 접속만으로 끝나고
    Windows → Linux 간 로그인 SID 불일치를 신경 쓸 일이 적다.

    SqlPackage 는 162.5.57 을 쓴다 — 170.x 는 .NET 10.0.11 요구로 이 PC 에서 안 돈다.
      dotnet tool install -g microsoft.sqlpackage --version 162.5.57

    원격 sa 비밀번호는 리포지토리에 두지 않는다 — 환경변수 AMES_REMOTE_SA_PWD 또는 실행 시 입력.

.EXAMPLE
    tools\sync-dev-db-to-remote.ps1                 # 전체 동기화
    tools\sync-dev-db-to-remote.ps1 -VerifyOnly     # Import·검증까지만 하고 임시 DB 삭제 (원격 AMES_DEV 불변)
#>
[CmdletBinding()]
param(
    [string]$SourceConn = 'Server=192.168.0.132,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=30;',

    [string]$RemoteServer = '98.95.142.192,1433',

    [string]$RemoteSaUser = 'sa',

    [string]$DbName = 'AMES_DEV',

    # 원격 DB 에 다시 매핑할 SQL 로그인 (원격 서버에 로그인이 미리 있어야 한다)
    [string]$AppLogin = 'ames_app',

    [switch]$VerifyOnly,

    [string]$WorkDir = (Join-Path $env:TEMP 'ames-db-sync')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Step($msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }

function Invoke-Sql([string]$conn, [string]$sql, [int]$timeoutSec = 600) {
    $c = New-Object System.Data.SqlClient.SqlConnection $conn
    try {
        $c.Open()
        $cmd = $c.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = $timeoutSec
        $t = New-Object System.Data.DataTable
        $t.Load($cmd.ExecuteReader())
        return , $t
    } finally { $c.Dispose() }
}

function Get-DbFingerprint([string]$conn) {
    # 원본은 ames_app(db_owner) 권한이라 서버 수준 뷰 없이 DB 안에서만 센다.
    # SSMS 다이어그램 지원 객체(sysdiagrams·sp_*diagram*·fn_diagramobjects)는 SqlPackage 가 bacpac 에 넣지 않으므로 같이 뺀다
    $meta = Invoke-Sql $conn @"
SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')) AS Collation,
       (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0 AND type IN ('U','V','P','FN','IF','TF','TR') AND name NOT LIKE '%diagram%') AS Objects,
       (SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.index_id > 0 AND t.name <> 'sysdiagrams') AS Indexes
"@
    $countSql = (Invoke-Sql $conn @"
SELECT STRING_AGG(CONVERT(nvarchar(max),
         N'SELECT N''' + REPLACE(s.name + N'.' + t.name, N'''', N'''''') + N''' AS T, COUNT_BIG(*) AS N FROM '
         + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)), N' UNION ALL ')
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0 AND t.name <> 'sysdiagrams'
"@).Rows[0][0]
    $counts = @{}
    if ($countSql -isnot [DBNull]) {
        foreach ($r in (Invoke-Sql $conn $countSql).Rows) { $counts[[string]$r.T] = [long]$r.N }
    }
    [pscustomobject]@{
        Collation = [string]$meta.Rows[0].Collation
        Objects   = [int]$meta.Rows[0].Objects
        Indexes   = [int]$meta.Rows[0].Indexes
        Counts    = $counts
    }
}

function Compare-Fingerprint($src, $dst) {
    $diffs = New-Object System.Collections.Generic.List[string]
    foreach ($p in 'Collation', 'Objects', 'Indexes') {
        if ($src.$p -ne $dst.$p) { $diffs.Add("$p : 원본 $($src.$p) / 원격 $($dst.$p)") }
    }
    foreach ($k in $src.Counts.Keys) {
        if (-not $dst.Counts.ContainsKey($k)) { $diffs.Add("테이블 없음 : $k") }
        elseif ($src.Counts[$k] -ne $dst.Counts[$k]) { $diffs.Add("행 수 : $k 원본 $($src.Counts[$k]) / 원격 $($dst.Counts[$k])") }
    }
    foreach ($k in $dst.Counts.Keys) {
        if (-not $src.Counts.ContainsKey($k)) { $diffs.Add("원격에만 있는 테이블 : $k") }
    }
    return , $diffs
}

# ---------------------------------------------------------------- 준비
$sqlpackage = (Get-Command sqlpackage -ErrorAction SilentlyContinue).Source
if (-not $sqlpackage) { throw 'sqlpackage 가 없습니다: dotnet tool install -g microsoft.sqlpackage --version 162.5.57' }

$saPwd = $env:AMES_REMOTE_SA_PWD
if (-not $saPwd) {
    $sec = Read-Host "원격 $RemoteServer 의 $RemoteSaUser 비밀번호" -AsSecureString
    $saPwd = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}

# Pooling=False: 풀에 남은 연결이 임시 DB 를 붙잡으면 이름 변경이 배타 잠금을 못 얻는다
function RemoteConn([string]$db) {
    "Server=$RemoteServer;Database=$db;User Id=$RemoteSaUser;Password=$saPwd;TrustServerCertificate=True;Encrypt=True;Connect Timeout=30;Pooling=False;"
}

function DbExists([string]$db) {
    [int](Invoke-Sql (RemoteConn 'master') "SELECT COUNT(*) AS N FROM sys.databases WHERE name = N'$db'").Rows[0].N -gt 0
}

$stamp   = Get-Date -Format 'yyyyMMddHHmmss'
$staging = "${DbName}_sync_$stamp"
$prev    = "${DbName}_prev"
# 교체 중 기존 DB 를 잠시 두는 이름 — 새 DB 가 자리 잡은 뒤에야 옛 prev 를 지우고 이 이름을 prev 로 바꾼다
$hold    = "${DbName}_hold_$stamp"
$bacpac  = Join-Path $WorkDir "$DbName-$stamp.bacpac"
New-Item -ItemType Directory -Force $WorkDir | Out-Null

Step '접속 확인'
$srcInfo = (Invoke-Sql $SourceConn "SELECT @@SERVERNAME AS S, DB_NAME() AS D").Rows[0]
Write-Host "원본: $($srcInfo.S) / $($srcInfo.D)"
$loginOk = (Invoke-Sql (RemoteConn 'master') "SELECT COUNT(*) AS N FROM sys.server_principals WHERE name = N'$AppLogin'").Rows[0].N
if ($loginOk -eq 0) { throw "원격 서버에 로그인 $AppLogin 이 없습니다 — 먼저 CREATE LOGIN 하세요." }
Write-Host "원격: $RemoteServer (임시 DB $staging)"

# 테이블마다 한 줄씩 찍어 화면을 덮으므로 로그 파일로 보내고, 실패하면 끝부분만 보인다
function Invoke-SqlPackage([string]$label) {
    $log = Join-Path $WorkDir "$label-$stamp.log"
    & $sqlpackage @args *> $log
    if ($LASTEXITCODE -ne 0) {
        Get-Content $log -Tail 30 | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        throw "$label 실패 (exit $LASTEXITCODE) — 전체 로그: $log"
    }
    Remove-Item $log -ErrorAction SilentlyContinue
}

$stagingCreated = $false
$swapped = $false
$succeeded = $false
try {
    Step "Export → $bacpac"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-SqlPackage 'Export' /Action:Export "/SourceConnectionString:$SourceConn" "/TargetFile:$bacpac"
    Write-Host ("완료 {0:N1} MB, {1:N0}초" -f ((Get-Item $bacpac).Length / 1MB), $sw.Elapsed.TotalSeconds)

    # 원본 지문은 Export 직후에 뜬다 — 그 사이 쓰기가 있으면 검증이 잡아낸다
    $srcFp = Get-DbFingerprint $SourceConn

    Step "Import → $staging"
    $sw.Restart()
    $stagingCreated = $true
    Invoke-SqlPackage 'Import' /Action:Import "/SourceFile:$bacpac" "/TargetConnectionString:$(RemoteConn $staging)"
    Write-Host ("완료 {0:N0}초" -f $sw.Elapsed.TotalSeconds)

    Step '검증'
    $dstFp = Get-DbFingerprint (RemoteConn $staging)
    $diffs = Compare-Fingerprint $srcFp $dstFp
    if ($diffs.Count -gt 0) {
        $diffs | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        throw "원본과 원격이 다릅니다 ($($diffs.Count)건). Export 중 원본에 쓰기가 있었다면 다시 실행하세요."
    }
    $rows = ($srcFp.Counts.Values | Measure-Object -Sum).Sum
    Write-Host "일치: 테이블 $($srcFp.Counts.Count)개 · 행 $rows · 객체 $($srcFp.Objects) · 인덱스 $($srcFp.Indexes) · $($srcFp.Collation)"

    if ($VerifyOnly) {
        Write-Host "`n-VerifyOnly: 원격 $DbName 은 그대로 두고 임시 DB 를 지웁니다." -ForegroundColor Green
        $succeeded = $true
        return
    }

    Step "사용자 매핑 ($AppLogin → db_owner)"
    Invoke-Sql (RemoteConn $staging) @"
IF DATABASE_PRINCIPAL_ID(N'$AppLogin') IS NULL
    CREATE USER [$AppLogin] FOR LOGIN [$AppLogin];
ELSE
    ALTER USER [$AppLogin] WITH LOGIN = [$AppLogin];
IF IS_ROLEMEMBER(N'db_owner', N'$AppLogin') = 0
    ALTER ROLE db_owner ADD MEMBER [$AppLogin];
"@ | Out-Null

    Step "교체: $DbName → $prev, $staging → $DbName"
    # 기존 DB 를 건드리기 전에 임시 DB 의 배타 잠금부터 확보한다 — 여기서 실패하면 아무것도 바뀌지 않는다
    Invoke-Sql (RemoteConn 'master') "ALTER DATABASE [$staging] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" | Out-Null

    $held = $false
    try {
        if (DbExists $DbName) {
            # SINGLE_USER 와 이름 변경을 한 배치로 묶어 다른 연결이 끼어들 틈을 줄인다
            Invoke-Sql (RemoteConn 'master') @"
ALTER DATABASE [$DbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [$DbName] MODIFY NAME = [$hold];
"@ | Out-Null
            $held = $true
        }
        Invoke-Sql (RemoteConn 'master') "ALTER DATABASE [$staging] MODIFY NAME = [$DbName];" | Out-Null
        $swapped = $true
    }
    catch {
        # 새 DB 가 자리 잡지 못했으면 기존 DB 를 원래 이름으로 되돌린다
        if ($held -and -not (DbExists $DbName)) {
            Invoke-Sql (RemoteConn 'master') "ALTER DATABASE [$hold] MODIFY NAME = [$DbName];" | Out-Null
            Write-Host "기존 $DbName 을 원래 이름으로 되돌렸습니다." -ForegroundColor Yellow
        }
        if (DbExists $DbName) { Invoke-Sql (RemoteConn 'master') "ALTER DATABASE [$DbName] SET MULTI_USER;" | Out-Null }
        throw
    }
    Invoke-Sql (RemoteConn 'master') "ALTER DATABASE [$DbName] SET MULTI_USER;" | Out-Null
    $succeeded = $true

    if ($held) {
        try {
            Invoke-Sql (RemoteConn 'master') @"
IF DB_ID(N'$prev') IS NOT NULL
BEGIN
    ALTER DATABASE [$prev] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$prev];
END
ALTER DATABASE [$hold] MODIFY NAME = [$prev];
ALTER DATABASE [$prev] SET MULTI_USER;
"@ | Out-Null
        } catch {
            Write-Host "동기화는 끝났지만 이전 사본 정리 실패 — 원격에 $hold 로 남아 있습니다: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    $check = (Invoke-Sql (RemoteConn $DbName) "SELECT DB_NAME() AS D, (SELECT COUNT(*) FROM sys.tables) AS T").Rows[0]
    Write-Host "`n완료: 원격 $($check.D) 테이블 $($check.T)개, 이전 사본은 $prev" -ForegroundColor Green
}
catch {
    Write-Host "`n실패: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "bacpac 은 남겨 둡니다: $bacpac"
    throw
}
finally {
    if ($stagingCreated -and -not $swapped) {
        try {
            Invoke-Sql (RemoteConn 'master') @"
IF DB_ID(N'$staging') IS NOT NULL
BEGIN
    ALTER DATABASE [$staging] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$staging];
END
"@ | Out-Null
            Write-Host "임시 DB $staging 삭제"
        } catch { Write-Host "임시 DB $staging 삭제 실패 — 수동 정리 필요: $($_.Exception.Message)" -ForegroundColor Yellow }
    }
    if ($succeeded) { Remove-Item $bacpac -ErrorAction SilentlyContinue }
}
