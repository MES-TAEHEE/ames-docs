-- ════════════════════════════════════════════════════════════════════════
--  migrate_portal.sql
--  외부 개방 화면(/portal) — 역할 ExternalCustomer · 공정 코드 PORTAL · 화면 PORTAL-001 등록 · 권한
--
--  · 외부 사용자 = SYS-001 에서 만든 Identity 계정에 ExternalCustomer 역할을 준 사람. 내부 로그인은 거부되고
--    /portal/login 에서만 로그인해 외부 화면만 본다(AMES.Web Services/PortalAuth.cs 정본).
--  · 내부 사용자는 내부 사이트 메뉴(외부 포탈 섹션)에서 같은 화면을 연다 — 권한은 SYS_RolePermission 그대로.
--  · 스키마 변경 없음(데이터만). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_portal.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1. 역할 ExternalCustomer ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = 'EXTERNALCUSTOMER')
BEGIN
    INSERT INTO dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (LOWER(CONVERT(varchar(36), NEWID())), 'ExternalCustomer', 'EXTERNALCUSTOMER', LOWER(CONVERT(varchar(36), NEWID())));
    PRINT N'✓ AspNetRoles ExternalCustomer';
END
ELSE
    PRINT N'· AspNetRoles ExternalCustomer 이미 존재';
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

-- ── 3. 화면 PORTAL-001 출하 계획 조회 ──────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'PORTAL-001')
BEGIN
    INSERT INTO dbo.SYS_Screen
        (ScreenCode, ModuleCode, ProcessCode, SubProcessCode,
         ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES
        ('PORTAL-001', 'WEB', 'PORTAL', NULL,
         N'출하 계획 조회', N'Shipment Plan', 'portal/shipment-plan', 'PORTAL-001', 1, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen PORTAL-001 등록 (portal/shipment-plan)';
END
ELSE
    PRINT N'· SYS_Screen PORTAL-001 이미 존재';
GO

-- ── 4. 권한: Admin REA, ExternalCustomer R ─────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleName = 'Admin' AND ScreenCode = 'PORTAL-001')
BEGIN
    DECLARE @AdminRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Admin');
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES (@AdminRoleId, 'Admin', 'WEB', 'PORTAL-001', 'REA', 1, SYSDATETIME(), 'seed', SYSDATETIME());
    PRINT N'✓ SYS_RolePermission Admin/PORTAL-001 (REA)';
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleName = 'ExternalCustomer' AND ScreenCode = 'PORTAL-001')
BEGIN
    DECLARE @ExtRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'ExternalCustomer');
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES (@ExtRoleId, 'ExternalCustomer', 'WEB', 'PORTAL-001', 'R', 0, SYSDATETIME(), 'seed', SYSDATETIME());
    PRINT N'✓ SYS_RolePermission ExternalCustomer/PORTAL-001 (R)';
END
GO

SELECT ScreenCode, ProcessCode, HRef, SortOrder, IsVisible FROM dbo.SYS_Screen WHERE ScreenCode LIKE 'PORTAL-%';
SELECT RoleName, ScreenCode, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode LIKE 'PORTAL-%' ORDER BY RoleName;
GO
