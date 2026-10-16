<#
.SYNOPSIS
  로컬 IIS(AMES.Web·AMES.Api)의 DB 접속 경로를 네트워크에 맞춰 자동으로 고른다 — 사무실 LAN / Tailscale.

.DESCRIPTION
  배포본 appsettings 의 접속문자열(사무실 LAN IP, 예 192.168.0.132)에 닿으면 사무실로 보고 앱풀 환경변수
  ConnectionStrings__AMES 를 지운다(배포본 설정 그대로). 거기 닿지 않고 Tailscale 주소(-TailscaleHost)에 닿으면
  접속문자열의 서버만 Tailscale 주소로 바꾼 값을 앱풀 환경변수로 넣는다. 둘 다 안 닿으면 아무것도 바꾸지 않는다.
  바뀐 앱풀만 재활용한다. 파일(appsettings·web.config)은 건드리지 않으므로 게시(publish-web/api.ps1)와 무관하다
  — 앱풀 환경변수는 applicationHost.config 에 있어 다시 게시해도 남는다.

  -Install   : 작업 스케줄러 「AMES DB Route」 등록(SYSTEM — 네트워크 변경·시작·5분마다) 후 한 번 실행
  -Uninstall : 작업 삭제 + 환경변수 제거(배포본 설정으로 복귀)
  관리자 권한 필요. 로그: %ProgramData%\AMES\db-route.log

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\db-route.ps1 -Install
#>
param(
    [string]   $TailscaleHost = '100.92.74.65',     # desktop-poedibs.tail526f68.ts.net
    [string[]] $Pools         = @('AMES.Web', 'AMES.Api'),
    [int]      $ProbeMs       = 3000,
    [switch]   $Install,
    [switch]   $Uninstall
)
$ErrorActionPreference = 'Stop'
$TaskName = 'AMES DB Route'
$VarName  = 'ConnectionStrings__AMES'
$LogDir   = Join-Path $env:ProgramData 'AMES'
$LogFile  = Join-Path $LogDir 'db-route.log'

function Log([string]$m) {
    if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force $LogDir | Out-Null }
    $line = '{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $m
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
    Write-Host $line
}

function Test-Tcp([string]$h, [int]$port) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $t = $c.ConnectAsync($h, $port); return ($t.Wait($ProbeMs) -and $c.Connected) } catch { return $false } finally { $c.Close() }
}

# 배포본 접속문자열 — appsettings.Production.json → appsettings.json, 주석(//) 줄 제외
function Get-DeployedConnString([string]$dir) {
    foreach ($f in 'appsettings.Production.json', 'appsettings.json') {
        $p = Join-Path $dir $f
        if (-not (Test-Path $p)) { continue }
        $hit = Get-Content $p -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*//' -and $_ -match '"AMES"\s*:\s*"([^"]+)"' } | Select-Object -Last 1
        if ($hit -and $hit -match '"AMES"\s*:\s*"([^"]+)"') { return $Matches[1] }
    }
    return $null
}

function Split-Server([string]$cs) {
    if ($cs -notmatch '(?i)(?:^|;)\s*(?:Server|Data Source)\s*=\s*([^;]+)') { return $null }
    $s = $Matches[1].Trim()
    if ($s -match '\\') { return $null }                       # 명명 인스턴스(로컬 비상 DB)는 대상 아님
    $port = 1433
    if ($s -match '^(.+?),(\d+)$') { $s = $Matches[1]; $port = [int]$Matches[2] }
    return @{ Host = $s; Port = $port }
}

Add-Type -Path "$env:windir\system32\inetsrv\Microsoft.Web.Administration.dll"

function Get-PoolEnv($sm, [string]$pool) {
    $pools = $sm.GetApplicationHostConfiguration().GetSection('system.applicationHost/applicationPools').GetCollection()
    $p = $pools | Where-Object { $_.GetAttributeValue('name') -eq $pool } | Select-Object -First 1
    if (-not $p) { return $null }
    return ,$p.GetCollection('environmentVariables')   # 쉼표: 빈 컬렉션이 파이프라인에서 풀려 null 이 되지 않게
}

function Set-PoolVar([string]$pool, [string]$value) {   # $value = $null 이면 삭제
    $sm  = New-Object Microsoft.Web.Administration.ServerManager
    $vars = Get-PoolEnv $sm $pool
    if ($null -eq $vars) { Log "  $pool : 앱풀 없음 — 건너뜀"; return $false }
    $cur = $vars | Where-Object { $_.GetAttributeValue('name') -eq $VarName } | Select-Object -First 1
    $curVal = if ($cur) { $cur.GetAttributeValue('value') } else { $null }
    # [string] 매개변수는 $null 을 '' 로 받으므로 빈 값끼리는 같은 것으로 본다(아니면 사무실에서 5분마다 재활용)
    if ([string]::IsNullOrEmpty($curVal) -and [string]::IsNullOrEmpty($value)) { return $false }
    if ($curVal -eq $value) { return $false }
    if ($cur) { $vars.Remove($cur) }
    if ($value) { $e = $vars.CreateElement('add'); $e.SetAttributeValue('name', $VarName); $e.SetAttributeValue('value', $value); $vars.Add($e) | Out-Null }
    $sm.CommitChanges()
    $sm2 = New-Object Microsoft.Web.Administration.ServerManager
    if ($sm2.ApplicationPools[$pool].State -eq 'Started') { $sm2.ApplicationPools[$pool].Recycle() | Out-Null }
    return $true
}

function Get-PoolDir([string]$pool) {
    $sm = New-Object Microsoft.Web.Administration.ServerManager
    foreach ($site in $sm.Sites) { foreach ($app in $site.Applications) {
        if ($app.ApplicationPoolName -eq $pool) { return [Environment]::ExpandEnvironmentVariables($app.VirtualDirectories['/'].PhysicalPath) } } }
    return $null
}

function Invoke-Route {
    foreach ($pool in $Pools) {
        $dir = Get-PoolDir $pool
        if (-not $dir) { Log "$pool : 사이트 없음 — 건너뜀"; continue }
        $cs = Get-DeployedConnString $dir
        $srv = if ($cs) { Split-Server $cs } else { $null }
        if (-not $srv) { Log "$pool : 배포본 접속문자열의 서버를 읽지 못함 — 건너뜀"; continue }

        if (Test-Tcp $srv.Host $srv.Port) {
            $want = $null; $mode = "사무실($($srv.Host))"
        } elseif (Test-Tcp $TailscaleHost $srv.Port) {
            $server = if ($srv.Port -eq 1433) { $TailscaleHost } else { "$TailscaleHost,$($srv.Port)" }
            $want = [regex]::Replace($cs, '(?i)((?:^|;)\s*(?:Server|Data Source)\s*=\s*)[^;]+', "`${1}$server", 1)
            $mode = "Tailscale($TailscaleHost)"
        } else {
            Log "$pool : 사무실·Tailscale 모두 닿지 않음 — 변경 없음"; continue
        }
        if (Set-PoolVar $pool $want) { Log "$pool : $mode 로 전환 — 앱풀 재활용" }
    }
}

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false; Log "작업 「$TaskName」 삭제" }
    foreach ($pool in $Pools) { if (Set-PoolVar $pool $null) { Log "$pool : 환경변수 제거 — 배포본 설정으로 복귀" } }
    return
}

if ($Install) {
    $script = $MyInvocation.MyCommand.Path
    $arg = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`" -TailscaleHost $TailscaleHost"
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arg
    $cls = Get-CimClass -Namespace Root/Microsoft/Windows/TaskScheduler -ClassName MSFT_TaskEventTrigger
    $netEvt = New-CimInstance -CimClass $cls -ClientOnly -Property @{ Enabled = $true; Delay = 'PT20S'; Subscription =
        '<QueryList><Query Id="0" Path="Microsoft-Windows-NetworkProfile/Operational"><Select Path="Microsoft-Windows-NetworkProfile/Operational">*[System[(EventID=10000 or EventID=10001)]]</Select></Query></QueryList>' }
    $boot = New-ScheduledTaskTrigger -AtStartup; $boot.Delay = 'PT1M'
    $every = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5)
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($netEvt, $boot, $every) -Principal $principal -Settings $settings `
        -Description '로컬 IIS(AMES.Web·Api) DB 접속을 사무실 LAN / Tailscale 로 자동 전환 — tools\db-route.ps1' -Force | Out-Null
    Log "작업 「$TaskName」 등록 (네트워크 변경·시작·5분마다, SYSTEM)"
}

Invoke-Route
