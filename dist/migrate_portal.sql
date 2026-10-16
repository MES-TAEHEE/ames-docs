-- ════════════════════════════════════════════════════════════════════════
--  migrate_portal.sql
--  외부 개방 화면(/portal) — 공정 코드 PORTAL · 구 출하 계획 화면 정리
--
--  · 09-25 부터 외부 사용자는 SCM_PortalVendorUser(SCM-004)다 — 역할 ExternalCustomer 는 만들지 않는다
--    (구 역할·계정·포탈 RBAC 행은 migrate_portal_legacy_cleanup.sql 이 지운다). 정본은 AMES.Web Services/PortalAuth.cs.
--  · 스키마 변경 없음(데이터만). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_portal.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 2. 공통코드 PROCESS 에 PORTAL (SYS-003 화면 등록 콤보·메뉴 섹션 키) ──
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'PROCESS' AND CodeValue = 'PORTAL')
BEGIN
    INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES ('PROCESS_PORTAL', 'PROCESS', 'PORTAL', N'외부 포탈', N'Partner Portal',
            ISNULL((SELECT MAX(SortOrder) FROM dbo.MD_CodeItem WHERE GroupCode = 'PROCESS'), 0) + 10, 1, 'seed', SYSDATETIME());
    PRINT N'✓ MD_CodeItem PROCESS/PORTAL';
END
ELSE
    PRINT N'· MD_CodeItem PROCESS/PORTAL 이미 존재';
GO

-- ── 3. 삭제된 출하 계획 화면과 권한 제거 ──
BEGIN TRANSACTION;
DELETE p FROM dbo.SYS_RolePermission p
JOIN dbo.SYS_Screen s ON s.ScreenCode=p.ScreenCode
WHERE s.ModuleCode='WEB' AND s.HRef IN ('portal/shipment-plan','/portal/shipment-plan');
DELETE FROM dbo.SYS_Screen
WHERE ModuleCode = 'WEB' AND HRef IN ('portal/shipment-plan', '/portal/shipment-plan');
COMMIT;
GO
SELECT ScreenCode, ProcessCode, HRef, SortOrder, IsVisible FROM dbo.SYS_Screen WHERE ScreenCode LIKE 'PORTAL-%';
SELECT RoleName, ScreenCode, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode LIKE 'PORTAL-%' ORDER BY RoleName;
GO
