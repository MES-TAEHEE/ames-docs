-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_linestatelog_cleanup.sql
--  라인 상태 분 단위 기록 PP_LineStateLog 의 오래된 행 정리 프로시저 dbo.usp_Cleanup_PP_LineStateLog (11-04)
--
--  PP_LineStateLog 는 [AMES] Line State Fill 이 라인마다 분당 1행을 쌓는다(16라인 기준 하루 약 23,000행).
--  OEE 는 PP_LineOEE·MNT_OEELog 스냅샷으로 남고, 상태 기록을 다시 읽는 작업은 최근 이틀치뿐이다
--  ([AMES] Line State Recalc · SP_PP_LineOEE_Fill @DaysBack = 2). 그래서 @KeepDays(기본 7)일보다 오래된 행은 지운다.
--
--  · 기준은 그 행이 나타내는 분(MinuteTS)이다 — CreatedTS 는 NULL 이 될 수 있고 백필 행은 기록 시각이 분과 다르다
--  · 5,000행씩 나눠 지운다 — 한 번에 지우면 잠금이 테이블로 올라가 매분 분류 작업을 기다리게 한다
--  · @KeepDays 는 3~365. 3 미만은 이틀치 재분류·OEE 재집계 범위를 지우므로 거부한다
--  · @KeepDays 보다 긴 기간을 SP_PP_LineStateLog_Fill @From 으로 백필하면 다음 정리 때 다시 지워진다
--
--  재실행 안전(CREATE OR ALTER). 테이블 변경 없음. 실행 일정(에이전트 작업)은 이 파일이 만들지 않는다.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d <DB> -C -f 65001 -I -b -l 30 -i dist\migrate_pp_linestatelog_cleanup.sql
--  확인:  EXEC dbo.usp_Cleanup_PP_LineStateLog @DryRun = 1;    -- 지울 행 수만 보기
--         EXEC dbo.usp_Cleanup_PP_LineStateLog;                -- 7일보다 오래된 행 삭제
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Cleanup_PP_LineStateLog
    @KeepDays  int = 7,
    @BatchSize int = 5000,
    @DryRun    bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @KeepDays IS NULL OR @KeepDays < 3 OR @KeepDays > 365
        THROW 50041, N'@KeepDays must be between 3 and 365.', 1;
    IF @BatchSize IS NULL OR @BatchSize < 100 OR @BatchSize > 50000
        THROW 50042, N'@BatchSize must be between 100 and 50000.', 1;

    DECLARE @Cutoff  datetime2(0) = DATEADD(DAY, -@KeepDays, CAST(SYSDATETIME() AS datetime2(0)));
    DECLARE @Deleted int = 0, @n int = 1;

    IF @DryRun = 1
    BEGIN
        SELECT @Cutoff AS Cutoff, COUNT(*) AS RowsToDelete
        FROM   dbo.PP_LineStateLog
        WHERE  MinuteTS < @Cutoff;
        RETURN;
    END

    WHILE @n > 0
    BEGIN
        DELETE TOP (@BatchSize) FROM dbo.PP_LineStateLog
        WHERE  MinuteTS < @Cutoff;
        SET @n = @@ROWCOUNT;
        SET @Deleted += @n;
    END

    SELECT @Cutoff AS Cutoff, @Deleted AS RowsDeleted;
END;
GO
