-- ════════════════════════════════════════════════════════════════════════
-- migrate_pp002_href_fix.sql — PP-002 화면 HRef 드리프트 정정
--   SYS_Screen PP-002 가 pp/sap-import('SAP 연동')로 드리프트되어 실제 페이지
--   SupplyPlanImport.razor(@page pp/supply-plan-import)와 불일치 → 메뉴 링크 404.
--   페이지 기준으로 HRef·명칭을 정정한다. (권한은 ScreenCode 키라 영향 없음)
-- idempotent. 적용: sqlcmd -f 65001 -b -i dist/migrate_pp002_href_fix.sql
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
UPDATE dbo.SYS_Screen
   SET HRef         = 'pp/supply-plan-import',
       ScreenName   = N'공급계획 가져오기',
       ScreenNameEn = N'Supply Plan Import',
       ModifiedBy   = 'seed',
       ModifiedTS   = SYSDATETIME()
 WHERE ScreenCode = 'PP-002'
   AND (HRef <> 'pp/supply-plan-import' OR ScreenName <> N'공급계획 가져오기');
PRINT CONCAT(N'PP-002 정정 행수: ', @@ROWCOUNT);
GO
SELECT ScreenCode, HRef, ScreenName, ScreenNameEn FROM dbo.SYS_Screen WHERE ScreenCode = 'PP-002';
GO
