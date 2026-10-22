/* ------------------------------------------------------------------
   setup_job_pp_linestatelog_recalc.sql  (2026-10-22)

   SQL Server 에이전트 작업 "[AMES] Line State Recalc"
     매일 @RunTime(기본 06:00, 서버 시각)에 두 단계를 차례로 실행한다
       ① <대상 DB>.dbo.SP_PP_LineStateLog_Fill @From = (오늘 − @DaysBack 일) 00:00  — 이틀치 + 오늘 지금까지 분을 다시 분류
       ② <대상 DB>.dbo.SP_PP_LineOEE_Fill @DaysBack = @DaysBack                  — 같은 날짜의 교대 OEE 스냅샷을 다시 집계
     이유: 매분 작업 "[AMES] Line State Fill" 은 최근 2시간만 보므로, 사람이 과거 시각으로 넣는 비가동(PP-DTL 등록·
     MNT-002 발생 시각)이 그 창 밖이면 반영되지 않는다. 하루 한 번 이틀치를 다시 분류하면 전날 야간 교대 몫까지
     늦어도 다음 날 아침에 맞춰지고, 그 결과로 OEE 도 다시 쓴다(10-22 사용자 결정 — 매분 24시간 재계산 대신)
     실패하면 10분 간격으로 2번 더(최대 3번), 결과는 SYS_InterfaceMonitor STATEFILL·OEEFILL 행과 작업 기록에 남는다

   · 시각·일수를 바꾸려면 아래 @RunTime(HHMMSS)·@DaysBack(1~30)만 고치고 다시 실행한다
   · 대상 DB 는 실행할 때 선택한 DB 다(DB_NAME()). master·msdb 등 시스템 DB 면 중단한다
       SSMS:   sa 로 접속 → 쿼리 창의 데이터베이스를 AMES_DEV 로 두고 실행(F5)
       sqlcmd: sqlcmd -S <서버> -E -C -f 65001 -b -d AMES_DEV -i dist\setup_job_pp_linestatelog_recalc.sql
   · sysadmin 으로 실행한다 — 앱 계정 ames_app 은 msdb 작업 권한이 없다
     (서버 PC 에서는 dist\setup-line-oee-job.ps1 이 에이전트 서비스 시작과 세 작업 등록을 같이 한다)
   · 먼저 dist/migrate_pp_linestatelog_fill.sql · dist/migrate_pp_lineoee_fill.sql(프로시저)을 적용할 것 — 없으면 등록을 멈춘다
   · 작업 소유자는 sa(SID 0x01 의 로그인). 재실행 안전: 같은 이름의 작업이 있으면 지우고 다시 만들며 사용 여부는 이어 받는다.
     삭제부터 서버 지정까지 한 트랜잭션이라 중간에 실패하면 기존 작업이 그대로 남는다
   · 에이전트 서비스가 꺼져 있어도 등록은 된다(경고만). Express 에디션은 Windows 작업 스케줄러로 매일 06:00 에 두 EXEC 를 돌린다
   · 손으로 더 긴 기간을 다시 분류하려면: EXEC dbo.SP_PP_LineStateLog_Fill @From = '2026-10-01', @To = '2026-10-22'; (최대 31일, 근무 시간 밖 권장)
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @RunTime  int     = 060000;                    -- HHMMSS, 서버 시각 — 여기만 고친다
DECLARE @DaysBack int     = 2;                         -- 다시 분류·집계할 일수(오늘 제외 앞 N일 + 오늘)
DECLARE @Db       sysname = DB_NAME();
DECLARE @JobName  sysname = N'[AMES] Line State Recalc';
DECLARE @Owner    sysname = SUSER_SNAME(0x01);

IF @RunTime IS NULL OR @RunTime < 0 OR @RunTime > 235959
    THROW 50007, N'@RunTime must be HHMMSS between 000000 and 235959.', 1;
IF @DaysBack IS NULL OR @DaysBack < 1 OR @DaysBack > 30
    THROW 50008, N'@DaysBack must be between 1 and 30.', 1;
IF @Db IN (N'master', N'msdb', N'model', N'tempdb')
    THROW 50010, N'Run this script with -d <AMES database> (e.g. -d AMES_DEV): the job steps run in the database this script is executed in.', 1;
IF OBJECT_ID(N'dbo.SP_PP_LineStateLog_Fill', N'P') IS NULL
    THROW 50011, N'dbo.SP_PP_LineStateLog_Fill not found in this database. Apply dist/migrate_pp_linestatelog_fill.sql first.', 1;
IF OBJECT_ID(N'dbo.SP_PP_LineOEE_Fill', N'P') IS NULL
    THROW 50011, N'dbo.SP_PP_LineOEE_Fill not found in this database. Apply dist/migrate_pp_lineoee_fill.sql first.', 1;
IF @Owner IS NULL
    THROW 50012, N'The sa login (SID 0x01) was not found.', 1;

USE msdb;

DECLARE @Enabled tinyint = ISNULL((SELECT TOP (1) enabled FROM msdb.dbo.sysjobs WHERE name = @JobName), 1);
DECLARE @ScheduleName sysname = CONCAT(N'Daily ', RIGHT(CONCAT(N'00000', @RunTime), 6));
DECLARE @Cmd1 nvarchar(max) = CONCAT(N'DECLARE @From datetime2(0) = CAST(DATEADD(DAY, -', @DaysBack, N', SYSDATETIME()) AS date);', NCHAR(13), NCHAR(10),
                                     N'EXEC dbo.SP_PP_LineStateLog_Fill @From = @From;');
DECLARE @Cmd2 nvarchar(max) = CONCAT(N'EXEC dbo.SP_PP_LineOEE_Fill @DaysBack = ', @DaysBack, N';');
DECLARE @rc int;

BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @JobName)
        EXEC msdb.dbo.sp_delete_job @job_name = @JobName, @delete_unused_schedule = 1;

    EXEC @rc = msdb.dbo.sp_add_job
         @job_name         = @JobName,
         @enabled          = @Enabled,
         @owner_login_name = @Owner,
         @description      = N'Daily catch-up: re-classifies PP_LineStateLog for the last days (dbo.SP_PP_LineStateLog_Fill) so downtime entered after the fact is reflected, then recomputes the shift OEE snapshots for the same days (dbo.SP_PP_LineOEE_Fill).';
    IF @rc <> 0 THROW 50013, N'sp_add_job failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobstep
         @job_name          = @JobName,
         @step_name         = N'Reclassify line state',
         @subsystem         = N'TSQL',
         @database_name     = @Db,
         @command           = @Cmd1,
         @retry_attempts    = 2,        -- 첫 시도 + 재시도 2번 = 최대 3번
         @retry_interval    = 10,       -- 분
         @on_success_action = 3,        -- 다음 단계로
         @on_fail_action    = 2;
    IF @rc <> 0 THROW 50014, N'sp_add_jobstep (1) failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobstep
         @job_name          = @JobName,
         @step_name         = N'Recompute line OEE',
         @subsystem         = N'TSQL',
         @database_name     = @Db,
         @command           = @Cmd2,
         @retry_attempts    = 2,
         @retry_interval    = 10,
         @on_success_action = 1,        -- 성공 종료
         @on_fail_action    = 2;
    IF @rc <> 0 THROW 50014, N'sp_add_jobstep (2) failed.', 1;

    EXEC @rc = msdb.dbo.sp_add_jobschedule
         @job_name               = @JobName,
         @name                   = @ScheduleName,
         @freq_type              = 4,             -- 매일
         @freq_interval          = 1,             -- 1일마다
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
-- EXEC msdb.dbo.sp_start_job @job_name = N'[AMES] Line State Recalc';
GO

SELECT j.name, j.enabled, SUSER_SNAME(j.owner_sid) AS owner, st.step_id, st.step_name, st.database_name, st.command,
       s.name AS schedule_name, s.freq_type, s.active_start_time
FROM   msdb.dbo.sysjobs j
JOIN   msdb.dbo.sysjobsteps st     ON st.job_id = j.job_id
JOIN   msdb.dbo.sysjobschedules js ON js.job_id = j.job_id
JOIN   msdb.dbo.sysschedules s     ON s.schedule_id = js.schedule_id
WHERE  j.name = N'[AMES] Line State Recalc'
ORDER  BY st.step_id;
