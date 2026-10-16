-- ============================================================================
-- migrate_mold_change_plan.sql — 금형 교체 시간의 생산 계획 반영 (2026-09-14)
--   1. MD_Mold.MoldChangeMin        : 금형 기본 교체 시간(분). 라인별 값은 MD_MoldLine.PrepTime 이 우선.
--   2. PP_LineSchedule.MoldID       : WO 슬롯이 배치된 금형 / MC(교체) 행의 신금형.
--   3. dev 시드 금형 4종 기본 교체 시간 30분.
-- 재실행 안전. 순서 무관 — 단 migrate_pp_lineschedule_pm.sql(테이블 재생성 분기)보다 뒤에 적용할 것: 그 분기의 컬럼 목록에 MoldID 가 없다.
-- sqlcmd -f 65001 -I 로 적용.
-- ============================================================================
SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.MD_Mold', 'MoldChangeMin') IS NULL
    ALTER TABLE dbo.MD_Mold ADD MoldChangeMin INT NULL;
GO

IF COL_LENGTH('dbo.PP_LineSchedule', 'MoldID') IS NULL
    ALTER TABLE dbo.PP_LineSchedule ADD MoldID VARCHAR(20) NULL;
GO

-- dev 시드 금형(migrate_inj_agent.sql) 기본값. 운영은 MD-007 화면에서 입력.
UPDATE dbo.MD_Mold
SET    MoldChangeMin = 30
WHERE  MoldChangeMin IS NULL
  AND  MoldID IN ('LQ2-DTMD', 'LQ2-DTRU', 'MEA-DTRCT', 'NEA-FUC');
GO

PRINT 'migrate_mold_change_plan.sql applied.';
GO
