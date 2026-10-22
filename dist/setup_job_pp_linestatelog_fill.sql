/* ------------------------------------------------------------------
   setup_job_pp_linestatelog_fill.sql  (2026-10-22)

   SQL Server 에이전트 작업 "[AMES] Line State Fill"
     1분마다(아래 @IntervalMin) <대상 DB>.dbo.SP_PP_LineStateLog_Fill 실행
     → 활성 라인 × 최근 24시간의 분마다 라인 상태(RUN/DOWN/IDLE·계획 가동/정지·WO)를 비가동 로그·POP 실적/라벨·라인 시간 패턴에서
       분류해 PP_LineStateLog 에 넣거나 값이 바뀐 분만 덮어쓴다(dist/migrate_pp_linestatelog_fill.sql 참조)
     PP-ODM 비가동 모니터와 PP-OEE(5분 작업 "[AMES] Line OEE Fill")가 이 표를 읽는다
     실패해도 재시도하지 않는다 — 다음 1분 실행이 재시도다. 결과는 SYS_InterfaceMonitor 의 STATEFILL 행(SYS-Interfaces)과 작업 기록에 남는다

   · 간격을 바꾸려면 아래 @IntervalMin 만 고치고 다시 실행한다(1~59분) — PP-OEE 작업(5분)보다 짧게 둔다
   · 대상 DB 는 실행할 때 선택한 DB 다(DB_NAME()). master·msdb 등 시스템 DB 면 중단한다
       SSMS:   sa 로 접속 → 쿼리 창의 데이터베이스를 AMES_DEV 로 두고 ① dist\migrate_pp_linestatelog_fill.sql ② 이 파일 순으로 실행(F5)
       sqlcmd: sqlcmd -S <서버> -E -C -f 65001 -b -d AMES_DEV -i dist\setup_job_pp_linestatelog_fill.sql
   · sysadmin 으로 실행한다 — 앱 계정 ames_app 은 msdb 작업 권한이 없다
     (서버 PC 에서는 dist\setup-line-oee-job.ps1 이 에이전트 서비스 시작과 OEE·상태 작업 둘 다 등록한다)
   · 먼저 dist/migrate_pp_linestatelog_fill.sql(프로시저·인덱스·설정)을 적용할 것 — 프로시저가 없으면 등록을 멈춘다
   · 작업 소유자는 sa(SID 0x01 의 로그인 — 이름을 바꿔도 찾는다). 실행한 사람의 Windows 계정이 소유하면 그 계정이 지워지거나
     비활성화될 때 작업이 "소유자 확인 불가"로 조용히 실패한다. sa 로그인이 비활성이어도 작업 실행에는 지장이 없다
   · 재실행 안전: 같은 이름의 작업이 있으면 지우고 다시 만든다(실행 이력도 지워진다).
     기존 작업의 사용 여부(enabled)는 그대로 이어 받는다 — 일부러 꺼 둔 작업을 다시 켜지 않는다(처음 만들 때는 사용)
     삭제부터 서버 지정까지 한 트랜잭션이라 중간에 실패하면 기존 작업이 그대로 남는다
   · 에이전트 서비스가 꺼져 있어도 등록은 된다(경고만 뜬다). 작업이 돌려면 서비스가 실행 중이어야 한다(관리자 PowerShell):
       기본 인스턴스   Set-Service SQLSERVERAGENT -StartupType Automatic; Start-Service SQLSERVERAGENT
       명명 인스턴스   Set-Service 'SQLAgent$MSSQLSERVER01' -StartupType Automatic; Start-Service 'SQLAgent$MSSQLSERVER01'
       Linux 컨테이너  환경변수 MSSQL_AGENT_ENABLED=true 로 컨테이너를 만든다
   · Express 에디션에는 에이전트가 없다 — Windows 작업 스케줄러로 1분마다 다음을 실행한다
       sqlcmd -S <서버> -E -C -d AMES_DEV -b -Q "EXEC dbo.SP_PP_LineStateLog_Fill"
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @IntervalMin int     = 1;                      -- 실행 간격(분) — 여기만 고친다
DECLARE @Db          sysname = DB_NAME();
DECLARE @JobName     sysname = N'[AMES] Line State Fill';
DECLARE @Owner       sysname = SUSER_SNAME(0x01);

IF @IntervalMin IS NULL OR @IntervalMin < 1 OR @IntervalMin > 59
    THROW 50009, N'@IntervalMin must be between 1 and 59.', 1;
IF @Db IN (N'master', N'msdb', N'model', N'tempdb')
    THROW 50010, N'Run this script with -d <AMES database> (e.g. -d AMES_DEV): the job step runs in the database this script is executed in.', 1;
IF OBJECT_ID(N'dbo.SP_PP_LineStateLog_Fill', N'P') IS NULL
    THROW 50011, N'dbo.SP_PP_LineStateLog_Fill not found in this database. Apply dist/migrate_pp_linestatelog_fill.sql first.', 1;
IF @Owner IS NULL
    THROW 50012, N'The sa login (SID 0x01) was not found.', 1;

USE msdb;

DECLARE @Enabled tinyint = ISNULL((SELECT TOP (1) enabled FROM msdb.dbo.sysjobs WHERE name = @JobName), 1);
DECLARE @ScheduleName sysname = CONCAT(N'Every ', @IntervalMin, N' min');
DECLARE @rc int;

BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
        EXEC msdb.dbo.sp_delete_job @job_name = @JobName, @delete_unused_schedule = 1;

    EXEC @rc = msdb.dbo.sp_add_job
         @job_name         = @JobName,
         @enabled          = @Enabled,
         @owner_login_name = @Owner,
         @description      = N'Classifies line state per minute (PP_LineStateLog) for the last 24 hours from downtime logs, POP results/labels and line time patterns (dbo.SP_PP_LineStateLog_Fill). Rows whose values did not change are left alone.';
    IF @rc <> 0 THROW 50013, N'sp_add_job failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobstep
         @job_name          = @JobName,
         @step_name         = N'Fill line state',
         @subsystem         = N'TSQL',
         @database_name     = @Db,
         @command           = N'EXEC dbo.SP_PP_LineStateLog_Fill;',
         @retry_attempts    = 0,        -- 다음 실행이 재시도다
         @retry_interval    = 0,
         @on_success_action = 1,
         @on_fail_action    = 2;
    IF @rc <> 0 THROW 50014, N'sp_add_jobstep failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobschedule
         @job_name               = @JobName,
         @name                   = @ScheduleName,
         @freq_type              = 4,             -- 매일
         @freq_interval          = 1,             -- 1일마다
         @freq_subday_type       = 4,             -- 분 단위
         @freq_subday_interval   = @IntervalMin,
         @active_start_time      = 0,             -- 00:00:00
         @active_end_time        = 235959;
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
-- EXEC msdb.dbo.sp_start_job @job_name = N'[AMES] Line State Fill';
GO

SELECT j.name, j.enabled, SUSER_SNAME(j.owner_sid) AS owner, st.database_name, st.command,
       s.name AS schedule_name, s.freq_type, s.freq_subday_type, s.freq_subday_interval, s.active_start_time, s.active_end_time
FROM   msdb.dbo.sysjobs j
JOIN   msdb.dbo.sysjobsteps st     ON st.job_id = j.job_id
JOIN   msdb.dbo.sysjobschedules js ON js.job_id = j.job_id
JOIN   msdb.dbo.sysschedules s     ON s.schedule_id = js.schedule_id
WHERE  j.name = N'[AMES] Line State Fill';
