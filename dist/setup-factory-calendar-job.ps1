# 공장 달력 자동 생성 작업 설치 (SQL Server 가 있는 PC 에서 실행)
#   1) SQL Server 에이전트 서비스를 자동 시작으로 바꾸고 시작
#   2) dist\setup_job_factory_calendar_fill.sql 로 에이전트 작업 "[AMES] Factory Calendar Fill"(매월 1일 00:30, 최대 3번 시도) 등록
# 관리자 권한이 없으면 스스로 관리자 승인(UAC)을 요청해 다시 실행한다. 작업 등록은 현재 Windows 계정(sysadmin)으로 한다.
#
#   기본 인스턴스(개발서버):   powershell -ExecutionPolicy Bypass -File dist\setup-factory-calendar-job.ps1
#   명명 인스턴스(로컬 PC):    powershell -ExecutionPolicy Bypass -File dist\setup-factory-calendar-job.ps1 -Instance MSSQLSERVER01
#   DB 이름이 다르면          -Database <DB 이름>
# 선행 조건: 그 서버의 대상 DB 에 dist\migrate_sys_public_holiday.sql 적용, sqlcmd 설치
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
$sqlFile = Join-Path $PSScriptRoot 'setup_job_factory_calendar_fill.sql'

Write-Host "[1/2] $service 자동 시작 + 시작"
Set-Service -Name $service -StartupType Automatic
if ((Get-Service -Name $service).Status -ne 'Running') { Start-Service -Name $service }
Get-Service -Name $service | Format-Table Name, Status, StartType -AutoSize

Write-Host "[2/2] 작업 등록 ($server / $Database, Windows 인증)"
& sqlcmd -S $server -E -C -f 65001 -b -W -l 15 -d $Database -i $sqlFile
if ($LASTEXITCODE -ne 0) { throw "작업 등록 실패(sqlcmd 종료 코드 $LASTEXITCODE) — 이 Windows 계정이 sysadmin 인지, $Database 에 migrate_sys_public_holiday.sql 이 적용됐는지 확인" }

Write-Host ''
Write-Host "완료 — 매월 1일 00:30 에 $Database.dbo.SP_SYS_FactoryCalendar_Fill @Months = 3 이 실행된다(실패 시 10분 간격 최대 3번)."
Write-Host "지금 한 번 실행: sqlcmd -S $server -E -C -Q `"EXEC msdb.dbo.sp_start_job @job_name = N'[AMES] Factory Calendar Fill'`""
