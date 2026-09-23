-- ════════════════════════════════════════════════════════════════════════
--  seed_portal_dev.sql  (개발 전용 — 운영 금지)
--  외부 개방 화면 테스트 계정 adminExt@ames.local — 비밀번호는 admin@ames.local 과 같다(Dev2026!).
--  PasswordHash 는 admin 행을 그대로 복사한다(Identity V3 해시는 솔트가 해시 안에 있어 다른 사용자에게 붙여도 같은 비밀번호로 검증된다).
--  역할은 ExternalCustomer 하나뿐이라 내부 로그인은 거부되고 /portal/login 에서만 들어온다.
--  선행: dist/migrate_portal.sql (역할). 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/seed_portal_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @src NVARCHAR(450) = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName = 'ADMIN@AMES.LOCAL');
IF @src IS NULL BEGIN RAISERROR('admin@ames.local 이 없다 — seed_admin_permissions.sql 먼저', 16, 1); RETURN; END
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Name = 'ExternalCustomer') BEGIN RAISERROR('ExternalCustomer 역할이 없다 — migrate_portal.sql 먼저', 16, 1); RETURN; END

DECLARE @id NVARCHAR(450) = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName = 'ADMINEXT@AMES.LOCAL');
IF @id IS NULL
BEGIN
    SET @id = LOWER(CONVERT(varchar(36), NEWID()));
    INSERT INTO dbo.AspNetUsers
        (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
         PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
    SELECT @id, 'adminExt@ames.local', 'ADMINEXT@AMES.LOCAL', 'adminExt@ames.local', 'ADMINEXT@AMES.LOCAL', 1, PasswordHash,
           UPPER(REPLACE(CONVERT(varchar(36), NEWID()), '-', '')), LOWER(CONVERT(varchar(36), NEWID())),
           NULL, 0, 0, NULL, 1, 0
    FROM dbo.AspNetUsers WHERE Id = @src;
    PRINT N'✓ AspNetUsers adminExt@ames.local (비밀번호 = admin 과 동일)';
END
ELSE
    PRINT N'· AspNetUsers adminExt@ames.local 이미 존재';

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId WHERE ur.UserId = @id AND r.Name = 'ExternalCustomer')
BEGIN
    INSERT INTO dbo.AspNetUserRoles (UserId, RoleId) SELECT @id, Id FROM dbo.AspNetRoles WHERE Name = 'ExternalCustomer';
    PRINT N'✓ AspNetUserRoles adminExt → ExternalCustomer';
END
-- 외부 사용자는 외부 역할만 가진다(내부 역할이 섞이면 "외부 화면만" 이 깨진다)
DELETE ur FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId WHERE ur.UserId = @id AND r.Name <> 'ExternalCustomer';

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE UserID = @id)
BEGIN
    INSERT INTO dbo.SYS_UserProfile (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift, AccountStatus, FailedLoginCount, AssignedLines, CreatedBy, CreatedTS)
    VALUES (@id, 'EXT-0001', N'외부 테스트 고객', N'External', 'EOS-PLT-01', 'A', 'ACTIVE', 0, NULL, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_UserProfile adminExt (ACTIVE)';
END
GO

SELECT u.UserName, u.EmailConfirmed, u.LockoutEnabled, STRING_AGG(r.Name, ',') AS Roles, p.AccountStatus
FROM dbo.AspNetUsers u
LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
LEFT JOIN dbo.SYS_UserProfile p ON p.UserID = u.Id
WHERE u.NormalizedUserName = 'ADMINEXT@AMES.LOCAL'
GROUP BY u.UserName, u.EmailConfirmed, u.LockoutEnabled, p.AccountStatus;
GO
