-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_aps_screen.sql
--  PP-APS APS 생산계획 (pp/aps-plan) — SYS_Screen 등록 + Admin 권한
--
--  ApsPlan.razor(/pp/aps-plan) 의 메뉴·권한 행. PP 그룹의 PP-006 구매 요청(6) 과
--  PP-CAL 캘린더(8) 사이 — SortOrder 7 은 비어 있다(구 PP-007 WO 릴리스 삭제 자리, 재사용하지 않는다).
--  메뉴는 SYS_Screen 에서 동적으로 읽으므로 이 행이 없으면 화면은 있어도 메뉴에 나오지 않고 권한도 없다.
--  HRef 'pp/aps-plan' 은 ApsPlan.razor 의 @page 라우트에서 앞 '/' 를 뺀 값 — 권한·메뉴 키라 바꾸면 깨진다.
--
--  스키마 변경 없음(데이터만). 순서 무관(seed_admin_permissions 이후 — Admin 역할 필요), 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/migrate_pp_aps_screen.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'PP-APS')
BEGIN
    INSERT INTO dbo.SYS_Screen
        (ScreenCode, ModuleCode, ProcessCode, SubProcessCode,
         ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES
        ('PP-APS', 'WEB', 'PP', NULL,
         N'APS 생산계획', N'APS Production Plan', 'pp/aps-plan', 'PP-APS', 7, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen PP-APS 등록 (pp/aps-plan, SortOrder 7)';
END
ELSE
    PRINT N'· SYS_Screen PP-APS 이미 존재';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleName = 'Admin' AND ScreenCode = 'PP-APS')
BEGIN
    DECLARE @AdminRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Admin');
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES
        (@AdminRoleId, 'Admin', 'WEB', 'PP-APS', 'REA', 1, SYSDATETIME(), 'seed', SYSDATETIME());
    PRINT N'✓ SYS_RolePermission Admin/PP-APS (REA)';
END
ELSE
    PRINT N'· SYS_RolePermission Admin/PP-APS 이미 존재';
GO

SELECT ScreenCode, ProcessCode, HRef, LidLabel, SortOrder, IsVisible FROM dbo.SYS_Screen WHERE ScreenCode = 'PP-APS';
SELECT RoleName, ScreenCode, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode = 'PP-APS';
GO
