-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Migration: MD_Station  +WCID (작업장 그룹, 선택적 축)                 ║
-- ║  공정(Station)에 작업장(WorkCenter) 참조를 nullable 로 추가            ║
-- ║  LineID(물리 라인)는 권위 축으로 그대로 유지, WCID는 원가·능력 그룹    ║
-- ╚══════════════════════════════════════════════════════════════════════╝
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.MD_Station', 'WCID') IS NULL
    ALTER TABLE dbo.MD_Station ADD [WCID] VARCHAR(20) NULL;  -- FK -> MD_WorkCenter.WCID
GO

PRINT 'MD_Station: WCID 컬럼 추가 완료.';
GO
