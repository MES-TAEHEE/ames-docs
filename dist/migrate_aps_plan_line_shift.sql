-- ============================================================================
--  PP_ApsPlanLineShift — APS 계획 행의 교대별 수량 (2026-10-07, 스펙 docs/superpowers/specs/2026-10-07-aps-shift-model-design.md §3)
--  사출 계획의 교대 분할이 주간/야간 2분할에서 "그 라인 APS 패턴의 교대(WORK_SHIFT 코드)별 · 가동 시간 비율" 로 바뀌었다.
--  PP_ApsPlanLine.PlanDay/PlanNight 는 파생값(첫 교대 / 나머지 합)으로 계속 채워진다. 정본은 PP_ApsRun 의 JSON(PlanShifts).
--  migrate_aps.sql 뒤에 적용, 재실행 안전. 이 표 없이 신 Web 을 올리면 PP-APS 저장이 Invalid object name 으로 실패한다.
--  적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <pw> -f 65001 -I -b -i dist\migrate_aps_plan_line_shift.sql
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID('dbo.PP_ApsPlanLine', 'U') IS NULL
    THROW 50000, N'PP_ApsPlanLine 이 없습니다 — migrate_aps.sql 을 먼저 적용하세요.', 1;
GO
IF OBJECT_ID('dbo.PP_ApsPlanLineShift', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_ApsPlanLineShift (
        PlanLineID int           NOT NULL,
        ShiftCode  varchar(10)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        Qty        decimal(14,3) NOT NULL DEFAULT ((0)),
        CONSTRAINT PK_PP_ApsPlanLineShift PRIMARY KEY CLUSTERED (PlanLineID, ShiftCode),
        CONSTRAINT FK_PP_ApsPlanLineShift_Line FOREIGN KEY (PlanLineID) REFERENCES dbo.PP_ApsPlanLine (PlanLineID) ON DELETE CASCADE
    );
    EXEC sys.sp_addextendedproperty @name = N'MS_Description',
         @value = N'APS 사출 계획 행의 교대별 수량(WORK_SHIFT 코드). PP_ApsPlanLine.PlanDay/PlanNight 는 이 행의 파생값(첫 교대 / 나머지 합). 정본은 PP_ApsRun 의 JSON(PlanShifts).',
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_ApsPlanLineShift';
    PRINT N'✓ PP_ApsPlanLineShift 생성';
END
ELSE
    PRINT N'· PP_ApsPlanLineShift 이미 존재';
GO
