-- ════════════════════════════════════════════════════════════════════════
--  migrate_md_line_supervisor_screen.sql
--  MD-033 라인 책임자 관리 (md/fd/line-supervisors) — SYS_Screen 등록 + Admin 권한
--
--  MD_LineSupervisor(migrate_andon_workflow.sql, 컬럼명은 migrate_employee_no_rename.sql) 의
--  등록·수정·삭제 화면. MD > 기반정보(FD) 그룹의 현장 작업자 관리(MD-032) 다음에 둔다.
--  메뉴는 SYS_Screen 에서 동적으로 읽으므로 이 행이 없으면 화면은 있어도 메뉴에 나오지 않고 권한도 없다.
--
--  스키마 변경 없음(데이터만). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -C -f 65001 -I -b -i dist/migrate_md_line_supervisor_screen.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'MD-033')
BEGIN
    DECLARE @sort int = ISNULL((SELECT MAX(SortOrder) FROM dbo.SYS_Screen WHERE ProcessCode = 'MD' AND SubProcessCode = 'FD'), 0) + 1;
    INSERT INTO dbo.SYS_Screen
        (ScreenCode, ModuleCode, ProcessCode, SubProcessCode,
         ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES
        ('MD-033', 'WEB', 'MD', 'FD',
         N'라인 책임자 관리', N'Line Supervisor Master', 'md/fd/line-supervisors', 'MD-033', @sort, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen MD-033 등록 (md/fd/line-supervisors)';
END
ELSE
    PRINT N'· SYS_Screen MD-033 이미 존재';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleName = 'Admin' AND ScreenCode = 'MD-033')
BEGIN
    DECLARE @AdminRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Admin');
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES
        (@AdminRoleId, 'Admin', 'WEB', 'MD-033', 'REA', 1, SYSDATETIME(), 'seed', SYSDATETIME());
    PRINT N'✓ SYS_RolePermission Admin/MD-033 (REA)';
END
ELSE
    PRINT N'· SYS_RolePermission Admin/MD-033 이미 존재';
GO

SELECT ScreenCode, SubProcessCode, HRef, SortOrder, IsVisible FROM dbo.SYS_Screen WHERE ScreenCode IN ('MD-032', 'MD-033') ORDER BY SortOrder;
SELECT RoleName, ScreenCode, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode = 'MD-033';
GO
