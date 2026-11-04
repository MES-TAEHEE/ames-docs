/* ------------------------------------------------------------------
   setup_job_pp_linestatelog_cleanup.sql  (2026-11-04)

   SQL Server 에이전트 작업 "[AMES] Line State Cleanup"
     매일 @RunTime(기본 04:00, 서버 시각)에 <대상 DB>.dbo.usp_Cleanup_PP_LineStateLog @KeepDays = @KeepDays 를 실행해
     PP_LineStateLog 에서 @KeepDays(기본 7)일보다 오래된 분 기록을 지운다
     06:00 [AMES] Line State Recalc(이틀치 재분류)보다 앞서 끝나며, 지우는 범위와 겹치지 않는다
     실패하면 10분 간격으로 2번 더(최대 3번)

   · 시각·보관 일수를 바꾸려면 아래 @RunTime(HHMMSS)·@KeepDays(3~365)만 고치고 다시 실행한다
   · 대상 DB 는 실행할 때 선택한 DB 다(DB_NAME()). master·msdb 등 시스템 DB 면 중단한다
       SSMS:   sa 로 접속 → 쿼리 창의 데이터베이스를 AMES 로 두고 실행(F5)
       sqlcmd: sqlcmd -S <서버> -U sa -P <pw> -C -f 65001 -b -d AMES -i dist\setup_job_pp_linestatelog_cleanup.sql
   · sysadmin 으로 실행한다 — 앱 계정 ames_app 은 msdb 작업 권한이 없다
   · 먼저 dist/migrate_pp_linestatelog_cleanup.sql(프로시저)을 적용할 것 — 없으면 등록을 멈춘다
   · 작업 소유자는 sa(SID 0x01). 재실행 안전: 같은 이름의 작업이 있으면 지우고 다시 만들며 사용 여부는 이어 받는다.
     삭제부터 서버 지정까지 한 트랜잭션이라 중간에 실패하면 기존 작업이 그대로 남는다
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @RunTime  int     = 040000;                    -- HHMMSS, 서버 시각 — 여기만 고친다
DECLARE @KeepDays int     = 7;                         -- 보관 일수
DECLARE @Db       sysname = DB_NAME();
DECLARE @JobName  sysname = N'[AMES] Line State Cleanup';
DECLARE @Owner    sysname = SUSER_SNAME(0x01);

IF @RunTime IS NULL OR @RunTime < 0 OR @RunTime > 235959
    THROW 50007, N'@RunTime must be HHMMSS between 000000 and 235959.', 1;
IF @KeepDays IS NULL OR @KeepDays < 3 OR @KeepDays > 365
    THROW 50008, N'@KeepDays must be between 3 and 365.', 1;
IF @Db IN (N'master', N'msdb', N'model', N'tempdb')
    THROW 50010, N'Run this script with -d <AMES database> (e.g. -d AMES): the job step runs in the database this script is executed in.', 1;
IF OBJECT_ID(N'dbo.usp_Cleanup_PP_LineStateLog', N'P') IS NULL
    THROW 50011, N'dbo.usp_Cleanup_PP_LineStateLog not found in this database. Apply dist/migrate_pp_linestatelog_cleanup.sql first.', 1;
IF @Owner IS NULL
    THROW 50012, N'The sa login (SID 0x01) was not found.', 1;

USE msdb;

DECLARE @Enabled tinyint = ISNULL((SELECT TOP (1) enabled FROM msdb.dbo.sysjobs WHERE name = @JobName), 1);
DECLARE @ScheduleName sysname = CONCAT(N'Daily ', RIGHT(CONCAT(N'00000', @RunTime), 6));
DECLARE @Cmd nvarchar(max) = CONCAT(N'EXEC dbo.usp_Cleanup_PP_LineStateLog @KeepDays = ', @KeepDays, N';');
DECLARE @rc int;

BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
        EXEC msdb.dbo.sp_delete_job @job_name = @JobName, @delete_unused_schedule = 1;

    EXEC @rc = msdb.dbo.sp_add_job
         @job_name         = @JobName,
         @enabled          = @Enabled,
         @owner_login_name = @Owner,
         @description      = N'Daily cleanup: deletes PP_LineStateLog minute rows older than the retention days (dbo.usp_Cleanup_PP_LineStateLog).';
    IF @rc <> 0 THROW 50013, N'sp_add_job failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobstep
         @job_name          = @JobName,
         @step_name         = N'Delete old line state rows',
         @subsystem         = N'TSQL',
         @database_name     = @Db,
         @command           = @Cmd,
         @retry_attempts    = 2,
         @retry_interval    = 10,
         @on_success_action = 1,
         @on_fail_action    = 2;
    IF @rc <> 0 THROW 50014, N'sp_add_jobstep failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobschedule
         @job_name               = @JobName,
         @name                   = @ScheduleName,
         @freq_type              = 4,             -- 매일
         @freq_interval          = 1,
         @freq_subday_type       = 1,             -- 지정 시각에 1회
         @active_start_time      = @RunTime;
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
-- EXEC msdb.dbo.sp_start_job @job_name = N'[AMES] Line State Cleanup';
GO

SELECT j.name, j.enabled, SUSER_SNAME(j.owner_sid) AS owner, st.step_name, st.database_name, st.command,
       s.name AS schedule_name, s.active_start_time
FROM   msdb.dbo.sysjobs j
JOIN   msdb.dbo.sysjobsteps st     ON st.job_id = j.job_id
JOIN   msdb.dbo.sysjobschedules js ON js.job_id = j.job_id
JOIN   msdb.dbo.sysschedules s     ON s.schedule_id = js.schedule_id
WHERE  j.name = N'[AMES] Line State Cleanup';
