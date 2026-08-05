-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Migration: MD_Station  −WCID (작업장 참조 축 제거)                     ║
-- ║  migrate_station_wcid.sql 로 추가했던 WCID 컬럼을 되돌림(반정규화 폐기) ║
-- ║  물리 라인(LineID)만 권위 축으로 유지, 작업장 그룹은 미사용으로 결정     ║
-- ╚══════════════════════════════════════════════════════════════════════╝
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.MD_Station', 'WCID') IS NOT NULL
    ALTER TABLE dbo.MD_Station DROP COLUMN [WCID];
GO

PRINT 'MD_Station: WCID 컬럼 제거 완료.';
GO
