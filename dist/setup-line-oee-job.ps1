# 라인 상태·OEE 자동 집계 작업 설치 (SQL Server 가 있는 PC 에서 실행)
#   1) SQL Server 에이전트 서비스를 자동 시작으로 바꾸고 시작
#   2) dist\setup_job_pp_linestatelog_fill.sql   로 "[AMES] Line State Fill"(기본 1분·최근 2시간 — PP_LineStateLog 분 단위 상태 분류) 등록
#   3) dist\setup_job_pp_linestatelog_recalc.sql 로 "[AMES] Line State Recalc"(매일 06:00 — 이틀치 재분류 + OEE 재집계) 등록
#   4) dist\setup_job_pp_lineoee_fill.sql        로 "[AMES] Line OEE Fill"(기본 5분 — PP_LineOEE 교대 스냅샷) 등록
# 관리자 권한이 없으면 스스로 관리자 승인(UAC)을 요청해 다시 실행한다. 작업 등록은 현재 Windows 계정(sysadmin)으로 한다.
#
#   기본 인스턴스(개발서버):   powershell -ExecutionPolicy Bypass -File dist\setup-line-oee-job.ps1
#   명명 인스턴스(로컬 PC):    powershell -ExecutionPolicy Bypass -File dist\setup-line-oee-job.ps1 -Instance MSSQLSERVER01
#   DB 이름이 다르면          -Database <DB 이름>
# 선행 조건: 그 서버의 대상 DB 에 dist\migrate_pp_linestatelog_fill.sql · dist\migrate_pp_lineoee_fill.sql(프로시저·인덱스) 적용, sqlcmd 설치
#   간격·시각을 바꾸려면 각 setup_job_*.sql 첫머리의 @IntervalMin·@WindowHours·@RunTime 을 고치고 이 스크립트를 다시 실행한다
param(
    [string]$Instance = 'MSSQLSERVER',
    [string]$Database = 'AMES_DEV'
)
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-NoExit', '-File', "`"$PSCommandPath`"", '-Instance', $Instance, '-Database', $Database
    return
}

$service = if ($Instance -eq 'MSSQLSERVER') { 'SQLSERVERAGENT' } else { "SQLAgent`$$Instance" }
$server  = if ($Instance -eq 'MSSQLSERVER') { 'localhost' } else { "localhost\$Instance" }

Write-Host "[1/4] $service 자동 시작 + 시작"
Set-Service -Name $service -StartupType Automatic
if ((Get-Service -Name $service).Status -ne 'Running') { Start-Service -Name $service }
Get-Service -Name $service | Format-Table Name, Status, StartType -AutoSize

$jobs = @(
    @{ Step = '[2/4]'; File = 'setup_job_pp_linestatelog_fill.sql';   Name = '[AMES] Line State Fill';   Migration = 'migrate_pp_linestatelog_fill.sql' },
    @{ Step = '[3/4]'; File = 'setup_job_pp_linestatelog_recalc.sql'; Name = '[AMES] Line State Recalc'; Migration = 'migrate_pp_linestatelog_fill.sql / migrate_pp_lineoee_fill.sql' },
    @{ Step = '[4/4]'; File = 'setup_job_pp_lineoee_fill.sql';        Name = '[AMES] Line OEE Fill';     Migration = 'migrate_pp_lineoee_fill.sql' }
)
foreach ($j in $jobs) {
    $sqlFile = Join-Path $PSScriptRoot $j.File
    Write-Host "$($j.Step) 작업 등록 $($j.Name) ($server / $Database, Windows 인증)"
    & sqlcmd -S $server -E -C -f 65001 -b -W -l 15 -d $Database -i $sqlFile
    if ($LASTEXITCODE -ne 0) { throw "작업 등록 실패(sqlcmd 종료 코드 $LASTEXITCODE) — 이 Windows 계정이 sysadmin 인지, $Database 에 $($j.Migration) 이 적용됐는지 확인" }
}

Write-Host ''
Write-Host "완료 — 1분마다 $Database.dbo.SP_PP_LineStateLog_Fill(최근 2시간), 매일 06:00 이틀치 재분류 + OEE 재집계, 5분마다 $Database.dbo.SP_PP_LineOEE_Fill 이 실행된다(각 setup_job_*.sql 첫머리에서 조정)."
Write-Host "지금 한 번 실행: sqlcmd -S $server -E -C -Q `"EXEC msdb.dbo.sp_start_job @job_name = N'[AMES] Line State Fill'`""
