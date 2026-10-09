/* ------------------------------------------------------------------
   setup_job_factory_calendar_fill.sql  (2026-10-03, 10-09 보강)

   SQL Server 에이전트 작업 "[AMES] Factory Calendar Fill"
     매월 1일 00:30(서버 시각)에 <대상 DB>.dbo.SP_SYS_FactoryCalendar_Fill @Months = 3 실행
     → 그날부터 3개월 안에서 SYS_FactoryCalendar 에 행이 없는 날짜만 채운다(dist/migrate_sys_public_holiday.sql 참조)
     실패하면 10분 간격으로 2번 더 — 최대 3번 시도한다

   · 대상 DB 는 실행할 때 -d 로 준 DB 다(DB_NAME()). master·msdb 등 시스템 DB 면 중단한다
       sqlcmd -S <서버> -E -C -f 65001 -b -d AMES_DEV -i dist\setup_job_factory_calendar_fill.sql
   · sysadmin 으로 실행한다 — 앱 계정 ames_app 은 msdb 작업 권한이 없다
     (서버 PC 에서는 dist\setup-factory-calendar-job.ps1 이 에이전트 서비스 시작까지 같이 한다)
   · 먼저 dist/migrate_sys_public_holiday.sql 을 적용할 것(프로시저가 없으면 작업 단계가 실패한다)
   · 작업 소유자는 sa(SID 0x01 의 로그인 — 이름을 바꿔도 찾는다). 실행한 사람의 Windows 계정이 소유하면 그 계정이 지워지거나
     비활성화될 때 작업이 "소유자 확인 불가"로 조용히 실패한다. sa 로그인이 비활성이어도 작업 실행에는 지장이 없다
   · 재실행 안전: 같은 이름(구 이름 "AMES - Factory Calendar Fill" 포함)의 작업이 있으면 지우고 다시 만든다(실행 이력도 지워진다).
     기존 작업의 사용 여부(enabled)는 그대로 이어 받는다 — 일부러 꺼 둔 작업을 다시 켜지 않는다(처음 만들 때는 사용)
     삭제부터 서버 지정까지 한 트랜잭션이라 중간에 실패하면 기존 작업이 그대로 남는다
   · 에이전트 서비스가 꺼져 있어도 등록은 된다(경고만 뜬다). 작업이 돌려면 서비스가 실행 중이어야 한다(관리자 PowerShell):
       기본 인스턴스   Set-Service SQLSERVERAGENT -StartupType Automatic; Start-Service SQLSERVERAGENT
       명명 인스턴스   Set-Service 'SQLAgent$MSSQLSERVER01' -StartupType Automatic; Start-Service 'SQLAgent$MSSQLSERVER01'
       Linux 컨테이너  환경변수 MSSQL_AGENT_ENABLED=true 로 컨테이너를 만든다
   · Express 에디션에는 에이전트가 없다 — Windows 작업 스케줄러로 매월 1일에 다음을 실행한다
       sqlcmd -S <서버> -E -C -d AMES_DEV -b -Q "EXEC dbo.SP_SYS_FactoryCalendar_Fill @Months = 3"
   · 실패(예: 범위 안 연도의 공휴일 미등록 → 오류 50002)는 작업 기록(SSMS > SQL Server 에이전트 > 작업 > 기록 보기)에 남는다
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Db      sysname = DB_NAME();
DECLARE @JobName sysname = N'[AMES] Factory Calendar Fill';
DECLARE @Owner   sysname = SUSER_SNAME(0x01);

IF @Db IN (N'master', N'msdb', N'model', N'tempdb')
    THROW 50010, N'Run this script with -d <AMES database> (e.g. -d AMES_DEV): the job step runs in the database this script is executed in.', 1;
IF OBJECT_ID(N'dbo.SP_SYS_FactoryCalendar_Fill', N'P') IS NULL
    THROW 50011, N'dbo.SP_SYS_FactoryCalendar_Fill not found in this database. Apply dist/migrate_sys_public_holiday.sql first.', 1;
IF @Owner IS NULL
    THROW 50012, N'The sa login (SID 0x01) was not found.', 1;

USE msdb;

DECLARE @Enabled tinyint = ISNULL((SELECT TOP (1) enabled FROM msdb.dbo.sysjobs
                                   WHERE name IN (@JobName, N'AMES - Factory Calendar Fill')
                                   ORDER BY CASE WHEN name = @JobName THEN 0 ELSE 1 END), 1);
DECLARE @rc int;

BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'AMES - Factory Calendar Fill')
        EXEC msdb.dbo.sp_delete_job @job_name = N'AMES - Factory Calendar Fill', @delete_unused_schedule = 1;
    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
        EXEC msdb.dbo.sp_delete_job @job_name = @JobName, @delete_unused_schedule = 1;

    EXEC @rc = msdb.dbo.sp_add_job
         @job_name         = @JobName,
         @enabled          = @Enabled,
         @owner_login_name = @Owner,
         @description      = N'Fills SYS_FactoryCalendar for the next 3 months on the 1st of every month from SYS_PublicHoliday and WORK_SHIFT (dbo.SP_SYS_FactoryCalendar_Fill). Existing dates are never changed.';
    IF @rc <> 0 THROW 50013, N'sp_add_job failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobstep
         @job_name          = @JobName,
         @step_name         = N'Fill 3 months',
         @subsystem         = N'TSQL',
         @database_name     = @Db,
         @command           = N'EXEC dbo.SP_SYS_FactoryCalendar_Fill @Months = 3;',
         @retry_attempts    = 2,        -- 첫 시도 + 재시도 2번 = 최대 3번
         @retry_interval    = 10,       -- 분
         @on_success_action = 1,
         @on_fail_action    = 2;
    IF @rc <> 0 THROW 50014, N'sp_add_jobstep failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobschedule
         @job_name               = @JobName,
         @name                   = N'Monthly day 1 00:30',
         @freq_type              = 16,  -- 매월
         @freq_interval          = 1,   -- 1일
         @freq_recurrence_factor = 1,   -- 1개월마다
         @active_start_time      = 3000;-- HHMMSS = 00:30:00
    IF @rc <> 0 THROW 50015, N'sp_add_jobschedule failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobserver @job_name = @JobName, @server_name = N'(local)';
    IF @rc <> 0 THROW 50016, N'sp_add_jobserver failed.', 1;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

-- 등록 직후 한 번 돌려 보려면:
-- EXEC msdb.dbo.sp_start_job @job_name = N'[AMES] Factory Calendar Fill';
GO

SELECT j.name, j.enabled, SUSER_SNAME(j.owner_sid) AS owner, st.database_name, st.retry_attempts, st.retry_interval,
       s.name AS schedule_name, s.freq_type, s.freq_interval, s.active_start_time
FROM   msdb.dbo.sysjobs j
JOIN   msdb.dbo.sysjobsteps st     ON st.job_id = j.job_id
JOIN   msdb.dbo.sysjobschedules js ON js.job_id = j.job_id
JOIN   msdb.dbo.sysschedules s     ON s.schedule_id = js.schedule_id
WHERE  j.name = N'[AMES] Factory Calendar Fill';
