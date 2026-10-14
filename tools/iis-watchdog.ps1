<#
.SYNOPSIS
  IIS(AMES.Web·AMES.Api)가 기동에 실패한 채 남아 있으면 그 앱풀만 재활용해 되살린다.

.DESCRIPTION
  in-process 호스팅(ANCM)은 앱이 기동에 실패하면 그 작업자 프로세스가 살아 있는 동안 계속
  HTTP 500.30(기동 예외)·500.31(.NET 런타임 못 찾음)·500.37(기동 시간 초과) 같은 ANCM 오류 페이지만 돌려주고
  저절로 다시 띄우지 않는다. 10-14 에 .NET 업데이트가 w3wp 를 내리는 도중 새 프로세스가 떠서
  로컬은 500.31, 개발서버는 재부팅 뒤 500.30 으로 서비스가 죽은 채 남았다.

  앱풀마다 그 사이트 바인딩의 http://localhost:{포트}/api/health 를 부르고, 응답이 ANCM 기동 실패 페이지
  ("HTTP Error 500.3x" 또는 "500.37")일 때만 앱풀을 재활용한 뒤 다시 불러 결과를 남긴다.
  그 밖의 응답은 건드리지 않는다 — 일반 500(앱 예외·DB 장애)·app_offline(배포 중)·응답 없음(기동 중)·
  멈춘 앱풀(배포 절차에서 일부러 멈춤)은 로그만 남긴다. 같은 앱풀을 10분 안에 다시 재활용하지 않는다
  (원인이 남아 있으면 10분마다 한 번만 시도).

  -Install   : 작업 스케줄러 「AMES IIS Watchdog」 등록(SYSTEM — 시작 2분 뒤·5분마다) 후 한 번 실행
  -Uninstall : 작업 삭제
  관리자 권한 필요. 로그: %ProgramData%\AMES\iis-watchdog.log

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\iis-watchdog.ps1 -Install
  powershell -ExecutionPolicy Bypass -File tools\iis-watchdog.ps1              # 지금 한 번 점검
#>
param(
    [string[]] $Pools       = @('AMES.Web', 'AMES.Api'),
    [string]   $ProbePath   = '/api/health',
    [int]      $TimeoutSec  = 100,     # 첫 요청은 기동(시드 포함)까지 기다린다 — ANCM startupTimeLimit 120초보다 짧게
    [int]      $CooldownMin = 10,
    [switch]   $Install,
    [switch]   $Uninstall
)
$ErrorActionPreference = 'Stop'
$TaskName  = 'AMES IIS Watchdog'
$LogDir    = Join-Path $env:ProgramData 'AMES'
$LogFile   = Join-Path $LogDir 'iis-watchdog.log'
$StateFile = Join-Path $LogDir 'iis-watchdog.state.json'

function Log([string]$m) {
    if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force $LogDir | Out-Null }
    $line = '{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $m
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
    Write-Host $line
}

Add-Type -Path "$env:windir\system32\inetsrv\Microsoft.Web.Administration.dll"

# 앱풀을 쓰는 사이트의 http 바인딩 포트와 물리 경로
function Get-PoolSite([string]$pool) {
    $sm = New-Object Microsoft.Web.Administration.ServerManager
    foreach ($site in $sm.Sites) {
        $app = $site.Applications | Where-Object { $_.Path -eq '/' -and $_.ApplicationPoolName -eq $pool } | Select-Object -First 1
        if (-not $app) { continue }
        $b = $site.Bindings | Where-Object { $_.Protocol -eq 'http' } | Select-Object -First 1
        if (-not $b) { return $null }
        return @{ Port = $b.EndPoint.Port; Dir = [Environment]::ExpandEnvironmentVariables($app.VirtualDirectories['/'].PhysicalPath) }
    }
    return $null
}

# 반환: @{ Code = 상태 코드(응답 없음 0); Body = 본문 }
# Invoke-WebRequest 는 Windows PowerShell 5.1 에서 오류 응답의 본문을 비운 채 넘겨 ANCM 페이지를 못 알아본다 — HttpClient 로 읽는다.
Add-Type -AssemblyName System.Net.Http
function Invoke-Probe([int]$port) {
    $c = New-Object System.Net.Http.HttpClient
    $c.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
    try {
        $r = $c.GetAsync("http://localhost:$port$ProbePath").GetAwaiter().GetResult()
        return @{ Code = [int]$r.StatusCode; Body = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
    } catch {
        return @{ Code = 0; Body = $_.Exception.Message }
    } finally { $c.Dispose() }
}

# ANCM 기동 실패 페이지 — "HTTP Error 500.30 - ASP.NET Core app failed to start" 등. 일반 500 은 아니다.
function Get-AncmFailure($probe) {
    if ($probe.Code -ne 500) { return $null }
    if ($probe.Body -match 'HTTP Error (500\.3\d)') { return $Matches[1] }
    return $null
}

function Read-State {
    if (-not (Test-Path $StateFile)) { return @{} }
    try {
        $o = Get-Content $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $h = @{}; foreach ($p in $o.PSObject.Properties) { $h[$p.Name] = [datetime]$p.Value }; return $h
    } catch { return @{} }
}

function Save-State($h) {
    $o = [ordered]@{}; foreach ($k in $h.Keys) { $o[$k] = $h[$k].ToString('o') }
    ($o | ConvertTo-Json) | Set-Content -Path $StateFile -Encoding UTF8
}

function Invoke-Watch {
    $state = Read-State
    foreach ($pool in $Pools) {
        $sm = New-Object Microsoft.Web.Administration.ServerManager
        $ap = $sm.ApplicationPools[$pool]
        if (-not $ap) { continue }
        if ($ap.State -ne 'Started') { Log "$pool : 앱풀이 $($ap.State) — 배포 등으로 일부러 멈춘 것일 수 있어 건드리지 않음"; continue }
        $site = Get-PoolSite $pool
        if (-not $site) { Log "$pool : http 바인딩 사이트 없음 — 건너뜀"; continue }
        if (Test-Path (Join-Path $site.Dir 'app_offline.htm')) { Log "$pool : app_offline.htm 있음(배포 중) — 건너뜀"; continue }

        $p = Invoke-Probe $site.Port
        $fail = Get-AncmFailure $p
        if (-not $fail) {
            if ($p.Code -ne 200) { Log "$pool : :$($site.Port)$ProbePath → $($p.Code) — 기동 실패 페이지가 아니라 재활용하지 않음" }
            continue
        }

        if ($state.ContainsKey($pool) -and ((Get-Date) - $state[$pool]).TotalMinutes -lt $CooldownMin) {
            Log "$pool : HTTP $fail — $($state[$pool].ToString('HH:mm')) 에 재활용했으므로 $CooldownMin 분 뒤 다시 시도"
            continue
        }
        Log "$pool : HTTP $fail (기동 실패 상태) — 앱풀 재활용"
        $ap.Recycle() | Out-Null
        $state[$pool] = Get-Date
        Save-State $state
        Start-Sleep -Seconds 2
        $p2 = Invoke-Probe $site.Port
        $fail2 = Get-AncmFailure $p2
        if ($p2.Code -eq 200) { Log "$pool : 재활용 뒤 정상(200)" }
        elseif ($fail2)      { Log "$pool : 재활용 뒤에도 HTTP $fail2 — 원인이 남아 있음(이벤트 뷰어 > 응용 프로그램 > IIS AspNetCore Module V2 확인)" }
        else                 { Log "$pool : 재활용 뒤 응답 $($p2.Code)" }
    }
}

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false; Log "작업 「$TaskName」 삭제" }
    return
}

if ($Install) {
    $script = $MyInvocation.MyCommand.Path
    $arg = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arg
    $boot = New-ScheduledTaskTrigger -AtStartup; $boot.Delay = 'PT2M'
    $every = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5)
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 8)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($boot, $every) -Principal $principal -Settings $settings `
        -Description 'IIS(AMES.Web·Api)가 기동 실패(HTTP 500.3x)로 남으면 그 앱풀을 재활용 — tools\iis-watchdog.ps1' -Force | Out-Null
    Log "작업 「$TaskName」 등록 (시작 2분 뒤·5분마다, SYSTEM)"
}

Invoke-Watch
