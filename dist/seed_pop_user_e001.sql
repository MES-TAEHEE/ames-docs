-- ════════════════════════════════════════════════════════════════════════
--  seed_pop_user_e001.sql
--  POP INJ 테스트 운영자 E001 시드 (POP + Web 양쪽 로그인)
--
--    POP INJ 터미널 : EmployeeNo E001 + 4자리 PIN 1234   (SYS_UserProfile.PinHash)
--    사무실 Web 포탈: e001@ames.local + 비밀번호 Dev2026! (AspNetUsers.PasswordHash)
--
--  두 비밀값은 서로 다른 컬럼에 저장되어 한쪽을 바꿔도 다른 쪽을 덮어쓰지 않는다
--  ([[migrate_pin_to_profile]] 참고). 두 해시 모두 PBKDF2-HMACSHA256(반복 10000,
--  salt 16B, 포맷 마커 0x01)로 ASP.NET Identity v3 호환이라, PasswordHash 는
--  Web SignInManager 에서 그대로 검증된다.
--
--  해시 재생성(값 변경 시): AMES.Data.Security.PinHasher.Hash("<평문>") 사용.
--    PinHash      = PinHasher.Hash("1234")
--    PasswordHash = PinHasher.Hash("Dev2026!")
--
--  Web 로그인 폼은 NormalizedUserName 으로 사용자를 찾으므로(SignInManager →
--  FindByNameAsync) UserName 을 이메일로 둔다. POP 는 EmployeeNo 로 조회하므로
--  UserName 변경에 영향받지 않는다.
--
--  Admin 롤도 함께 부여한다 — 개발 DB 에 시드된 유일한 롤이며, 롤이 없으면 Web
--  NavMenu 가 비어 로그인해도 볼 화면이 없다([[web-nav-empty-admin-role]]).
--  E001 을 Web 에서 일반 운영자로 두려면 아래 Step 3 의 AspNetUserRoles 행을 지운다.
--
--  적용:  sqlcmd -S localhost,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 \
--             -i dist/seed_pop_user_e001.sql
--         (-f 65001 = UTF-8; 없으면 한글 주석이 깨짐)
--
--  멱등(idempotent): 여러 번 실행해도 안전 (MERGE 사용).
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
SET NOCOUNT ON;

DECLARE @UserId       NVARCHAR(450) = 'user-e001';
DECLARE @Email        NVARCHAR(256) = 'e001@ames.local';
DECLARE @PasswordHash NVARCHAR(MAX) = 'AQAAAAEAACcQAAAAEL/atwvsf+tTxO9zkwPxqqP8kUfof8Uc4XOcX0sNeMcIYggdiP5e7CC4AMVOGUKBLQ=='; -- Dev2026!
DECLARE @PinHash      NVARCHAR(200) = 'AQAAAAEAACcQAAAAEMhE4oO16DG2fFWEz38QTjFvcJH3M4Ps1CyikXqjzmYk4UKQB9mpPR8hQnU8/epCLA=='; -- 1234

-- ── Step 1: AspNetUsers — Web 로그인 계정 (이메일 + 비밀번호) ────────────────
MERGE dbo.AspNetUsers AS tgt
USING (SELECT @UserId AS Id) AS src ON tgt.Id = src.Id
WHEN MATCHED THEN UPDATE SET
    UserName           = @Email,
    NormalizedUserName = UPPER(@Email),
    Email              = @Email,
    NormalizedEmail    = UPPER(@Email),
    EmailConfirmed     = 1,
    PasswordHash       = @PasswordHash,
    LockoutEnabled     = 1,
    AccessFailedCount  = 0
WHEN NOT MATCHED THEN INSERT
    (Id, UserName, NormalizedUserName, Email, NormalizedEmail,
     EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
     PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
VALUES
    (@UserId, @Email, UPPER(@Email), @Email, UPPER(@Email),
     1, @PasswordHash, REPLACE(CAST(NEWID() AS NVARCHAR(50)), '-', ''),
     REPLACE(CAST(NEWID() AS NVARCHAR(50)), '-', ''),
     0, 0, 1, 0);
PRINT CONCAT('AspNetUsers E001: ', @@ROWCOUNT, '행 처리됨');
GO

-- ── Step 2: SYS_UserProfile — POP 운영자 프로필 (EmployeeNo + PIN) ──────────
DECLARE @UserId  NVARCHAR(450) = 'user-e001';
DECLARE @PinHash NVARCHAR(200) = 'AQAAAAEAACcQAAAAEMhE4oO16DG2fFWEz38QTjFvcJH3M4Ps1CyikXqjzmYk4UKQB9mpPR8hQnU8/epCLA=='; -- 1234

MERGE dbo.SYS_UserProfile AS tgt
USING (SELECT @UserId AS UserID) AS src ON tgt.UserID = src.UserID
WHEN MATCHED THEN UPDATE SET
    EmployeeNo       = 'E001',
    EmployeeName     = N'Kim Min-jun',
    Department       = 'Production',
    DefaultShift     = 'DAY',
    AssignedLines    = N'["LINE-INJ-01"]',
    PinHash          = @PinHash,
    AccountStatus    = 'Active',
    FailedLoginCount = 0,
    ModifiedTS       = SYSDATETIME()
WHEN NOT MATCHED THEN INSERT
    (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
     AssignedLines, PinHash, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
VALUES
    (@UserId, 'E001', N'Kim Min-jun', 'Production', 'SEH-US-01', 'DAY',
     N'["LINE-INJ-01"]', @PinHash, 'Active', 0, 'seed', SYSDATETIME());
PRINT CONCAT('SYS_UserProfile E001: ', @@ROWCOUNT, '행 처리됨');
GO

-- ── Step 3: Admin 롤 부여 (Web NavMenu 표시용) ──────────────────────────────
DECLARE @UserId     NVARCHAR(450) = 'user-e001';
DECLARE @AdminRoleId NVARCHAR(450);
SELECT @AdminRoleId = Id FROM dbo.AspNetRoles WHERE NormalizedName = 'ADMIN';

IF @AdminRoleId IS NULL
    PRINT N'⚠ Admin 롤 미발견 — seed_admin_permissions.sql 먼저 적용 필요';
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @UserId AND RoleId = @AdminRoleId)
BEGIN
    INSERT INTO dbo.AspNetUserRoles (UserId, RoleId) VALUES (@UserId, @AdminRoleId);
    PRINT 'E001 → Admin 롤 할당 완료';
END
ELSE
    PRINT 'E001 Admin 롤 이미 할당됨';
GO

-- ── 결과 확인 ──────────────────────────────────────────────────────────────
SELECT u.UserName, u.Email, p.EmployeeNo, p.EmployeeName,
       p.AssignedLines, p.AccountStatus,
       CASE WHEN u.PasswordHash IS NOT NULL THEN 'SET' ELSE 'NULL' END AS WebPw,
       CASE WHEN p.PinHash      IS NOT NULL THEN 'SET' ELSE 'NULL' END AS PopPin,
       (SELECT COUNT(*) FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id) AS Roles
FROM   dbo.SYS_UserProfile p
JOIN   dbo.AspNetUsers     u ON u.Id = p.UserID
WHERE  p.EmployeeNo = 'E001';
GO
