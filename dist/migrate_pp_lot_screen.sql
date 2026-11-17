-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_lot_screen.sql  (2026-11-17)
--  PP-LOT LOT 조회 (pp/lot) — SYS_Screen 등록 + Admin 읽기 권한
--
--  LotLookup.razor(/pp/lot) 의 메뉴·권한 행. PP 그룹의 마지막(PP-OTD 13 다음 14).
--  조회 전용 화면이라 화면 기본 권한은 읽기만(`_XX` — 수정·승인 기능 없음), Admin 은 R 만 부여(RXX).
--  다른 역할은 SYS-004 에서 읽기를 열어 준다.
--  메뉴는 SYS_Screen 에서 동적으로 읽으므로 이 행이 없으면 화면은 있어도 메뉴에 나오지 않고 권한도 없다.
--  HRef 'pp/lot' 은 LotLookup.razor 의 @page 라우트에서 앞 '/' 를 뺀 값 — 권한·메뉴 키라 바꾸면 깨진다.
--
--  스키마 변경 없음(데이터만). seed_admin_permissions 이후(Admin 역할 필요), 재실행 안전. 신 Web 과 순서 무관(구 Web 은 이 행을 몰라 메뉴에 안 나올 뿐).
--  적용:  sqlcmd -S <서버> -U ames_app -d <DB> -f 65001 -I -b -i dist/migrate_pp_lot_screen.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'PP-LOT')
BEGIN
    INSERT INTO dbo.SYS_Screen
        (ScreenCode, ModuleCode, ProcessCode, SubProcessCode,
         ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES
        ('PP-LOT', 'WEB', 'PP', NULL,
         N'LOT 조회', N'LOT Lookup', 'pp/lot', 'LOT', 14, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen PP-LOT 등록 (pp/lot, SortOrder 14)';
END
ELSE
    PRINT N'· SYS_Screen PP-LOT 이미 존재';
GO

-- 메뉴·머리글에 보이는 이름은 LOT (화면 코드 PP-LOT 은 권한·감사 키라 그대로) — 이미 PP-LOT 으로 등록된 DB 도 맞춘다
UPDATE dbo.SYS_Screen SET LidLabel = 'LOT' WHERE ScreenCode = 'PP-LOT' AND LidLabel = 'PP-LOT';
GO

-- 화면 기본 권한(조회 전용) — 이 컬럼이 없는 옛 DB 는 건너뛴다
IF COL_LENGTH('dbo.SYS_Screen', 'PermissionCriteria') IS NOT NULL
    EXEC(N'UPDATE dbo.SYS_Screen SET PermissionCriteria = ''_XX'' WHERE ScreenCode = ''PP-LOT'' AND ISNULL(PermissionCriteria, ''___'') = ''___''');
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleID = N'ROLE-SYSADMIN' AND ScreenCode = 'PP-LOT')
BEGIN
    DECLARE @AdminRoleId NVARCHAR(450) = N'ROLE-SYSADMIN';
    DECLARE @AdminRoleName VARCHAR(40) = ISNULL((SELECT Name FROM dbo.AspNetRoles WHERE Id = @AdminRoleId), 'System Administrator');
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES
        (@AdminRoleId, @AdminRoleName, 'WEB', 'PP-LOT', 'RXX', 1, SYSDATETIME(), 'seed', SYSDATETIME());
    PRINT N'✓ SYS_RolePermission Admin/PP-LOT (RXX)';
END
ELSE
    PRINT N'· SYS_RolePermission Admin/PP-LOT 이미 존재';
GO

SELECT ScreenCode, ProcessCode, HRef, LidLabel, SortOrder, IsVisible FROM dbo.SYS_Screen WHERE ScreenCode = 'PP-LOT';
SELECT RoleName, ScreenCode, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode = 'PP-LOT';
GO
