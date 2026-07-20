-- ════════════════════════════════════════════════════════════════════════
-- migrate_mold_screens.sql — 금형 관리 화면 MD-031/032 등록 해제
--   MD-031 md/re/mold-item · MD-032 md/re/mold-line 별도 화면은
--   MD-007(md/re/mold) 금형 마스터의 탭(AmesTabs) 통합으로 대체됨.
-- idempotent. 적용: sqlcmd(ODBC17) -f 65001 -b -i dist/migrate_mold_screens.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;
GO
DELETE FROM dbo.SYS_RolePermission WHERE ScreenCode IN ('MD-031','MD-032');
DELETE FROM dbo.SYS_Screen         WHERE ScreenCode IN ('MD-031','MD-032');
GO
PRINT N'✓ MD-031/032 deregistered (merged into MD-007 tabs)';
GO
