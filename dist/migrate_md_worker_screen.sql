-- ════════════════════════════════════════════════════════════════════════
--  migrate_md_worker_screen.sql
--  MD-032 현장 작업자 관리 (md/fd/workers) — SYS_Screen 등록 + Admin 권한
--
--  MD_Worker(migrate_md_worker.sql) 의 등록·수정 화면. MD > 기반정보(FD) 그룹의
--  단위 관리(MD-019) 다음에 둔다. 메뉴는 SYS_Screen 에서 동적으로 읽으므로
--  이 행이 없으면 화면은 있어도 메뉴에 나오지 않고 권한도 없다.
--
--  스키마 변경 없음(데이터만). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_md_worker_screen.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'MD-032')
BEGIN
    DECLARE @sort int = ISNULL((SELECT MAX(SortOrder) FROM dbo.SYS_Screen WHERE ProcessCode = 'MD' AND SubProcessCode = 'FD'), 0) + 1;
    INSERT INTO dbo.SYS_Screen
        (ScreenCode, ModuleCode, ProcessCode, SubProcessCode,
         ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES
        ('MD-032', 'WEB', 'MD', 'FD',
         N'현장 작업자 관리', N'Worker Master', 'md/fd/workers', 'MD-032', @sort, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen MD-032 등록 (md/fd/workers)';
END
ELSE
    PRINT N'· SYS_Screen MD-032 이미 존재';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleName = 'Admin' AND ScreenCode = 'MD-032')
BEGIN
    DECLARE @AdminRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Admin');
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES
        (@AdminRoleId, 'Admin', 'WEB', 'MD-032', 'REA', 1, SYSDATETIME(), 'seed', SYSDATETIME());
    PRINT N'✓ SYS_RolePermission Admin/MD-032 (REA)';
END
ELSE
    PRINT N'· SYS_RolePermission Admin/MD-032 이미 존재';
GO

SELECT ScreenCode, SubProcessCode, HRef, SortOrder, IsVisible FROM dbo.SYS_Screen WHERE ScreenCode = 'MD-032';
SELECT RoleName, ScreenCode, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode = 'MD-032';
GO
