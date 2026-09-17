SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

MERGE dbo.SYS_Screen AS target
USING (VALUES
    ('WH-002', 'WEB', 'WH', N'피킹 오더', N'Picking Orders', 'wh/picking-orders', 'WH-002', 4)
) AS source (ScreenCode, ModuleCode, ProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder)
ON target.ScreenCode = source.ScreenCode
WHEN MATCHED THEN UPDATE SET
    ModuleCode = source.ModuleCode,
    ProcessCode = source.ProcessCode,
    ScreenName = source.ScreenName,
    ScreenNameEn = source.ScreenNameEn,
    HRef = source.HRef,
    LidLabel = source.LidLabel,
    SortOrder = source.SortOrder,
    IsVisible = 1,
    ModifiedBy = 'seed',
    ModifiedTS = SYSDATETIME()
WHEN NOT MATCHED THEN INSERT
    (ScreenCode, ModuleCode, ProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
VALUES
    (source.ScreenCode, source.ModuleCode, source.ProcessCode, source.ScreenName, source.ScreenNameEn,
     source.HRef, source.LidLabel, source.SortOrder, 1, 'seed', SYSDATETIME());

DECLARE @ReaderRoleId nvarchar(450) = COALESCE(
    (SELECT TOP (1) Id FROM dbo.AspNetRoles WHERE Name = 'WH Pick Reader'
     ORDER BY CASE WHEN NormalizedName = 'WH PICK READER' THEN 0 ELSE 1 END),
    LOWER(CONVERT(nvarchar(450), NEWID())));
DECLARE @DeniedRoleId nvarchar(450) = COALESCE(
    (SELECT TOP (1) Id FROM dbo.AspNetRoles WHERE Name = 'WH Pick Denied'
     ORDER BY CASE WHEN NormalizedName = 'WH PICK DENIED' THEN 0 ELSE 1 END),
    LOWER(CONVERT(nvarchar(450), NEWID())));

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id = @ReaderRoleId)
    INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (@ReaderRoleId, 'WH Pick Reader', 'WH PICK READER', CONVERT(nvarchar(max), NEWID()));

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id = @DeniedRoleId)
    INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (@DeniedRoleId, 'WH Pick Denied', 'WH PICK DENIED', CONVERT(nvarchar(max), NEWID()));

UPDATE dbo.AspNetRoles SET NormalizedName = 'WH PICK READER' WHERE Id = @ReaderRoleId;
UPDATE dbo.AspNetRoles SET NormalizedName = 'WH PICK DENIED' WHERE Id = @DeniedRoleId;

DELETE FROM dbo.SYS_RolePermission
WHERE RoleName IN ('WH Pick Reader', 'WH Pick Denied')
  AND ScreenCode IN ('WH-002', 'WH-01', 'WH-006');

INSERT dbo.SYS_RolePermission
    (RoleID, RoleName, ModuleCode, ProcessCode, ScreenCode, PermissionLevel,
     IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
VALUES
    (@ReaderRoleId, 'WH Pick Reader', 'WEB', 'WH', 'WH-002', 'R', 0, SYSDATETIME(), 'seed', SYSDATETIME()),
    (@ReaderRoleId, 'WH Pick Reader', 'WEB', 'WH', 'WH-01', 'R', 0, SYSDATETIME(), 'seed', SYSDATETIME()),
    (@DeniedRoleId, 'WH Pick Denied', 'WEB', 'WH', 'WH-01', 'R', 0, SYSDATETIME(), 'seed', SYSDATETIME());

DECLARE @PasswordHash nvarchar(max) = 'AQAAAAIAAYagAAAAEHKykSfJZ+8O5k9f5x35+nHkkITXaOWEs5aVl02vQ+Y/CwO6PCh/gIQJUn9cNv+J6w==';
DECLARE @ReaderEmail nvarchar(256) = 'whpick.read@ames.local';
DECLARE @DeniedEmail nvarchar(256) = 'whpick.none@ames.local';
DECLARE @ReaderUserId nvarchar(450) = COALESCE(
    (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedEmail = UPPER(@ReaderEmail)),
    LOWER(CONVERT(nvarchar(450), NEWID())));
DECLARE @DeniedUserId nvarchar(450) = COALESCE(
    (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedEmail = UPPER(@DeniedEmail)),
    LOWER(CONVERT(nvarchar(450), NEWID())));

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @ReaderUserId)
    INSERT dbo.AspNetUsers
        (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
         PasswordHash, SecurityStamp, ConcurrencyStamp,
         PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES
        (@ReaderUserId, @ReaderEmail, UPPER(@ReaderEmail), @ReaderEmail, UPPER(@ReaderEmail), 1,
         @PasswordHash, CONVERT(nvarchar(max), NEWID()), CONVERT(nvarchar(max), NEWID()), 0, 0, 1, 0);
ELSE
    UPDATE dbo.AspNetUsers SET PasswordHash = @PasswordHash, EmailConfirmed = 1 WHERE Id = @ReaderUserId;

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @DeniedUserId)
    INSERT dbo.AspNetUsers
        (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
         PasswordHash, SecurityStamp, ConcurrencyStamp,
         PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES
        (@DeniedUserId, @DeniedEmail, UPPER(@DeniedEmail), @DeniedEmail, UPPER(@DeniedEmail), 1,
         @PasswordHash, CONVERT(nvarchar(max), NEWID()), CONVERT(nvarchar(max), NEWID()), 0, 0, 1, 0);
ELSE
    UPDATE dbo.AspNetUsers SET PasswordHash = @PasswordHash, EmailConfirmed = 1 WHERE Id = @DeniedUserId;

DELETE FROM dbo.AspNetUserRoles WHERE UserId IN (@ReaderUserId, @DeniedUserId);
INSERT dbo.AspNetUserRoles (UserId, RoleId)
VALUES (@ReaderUserId, @ReaderRoleId), (@DeniedUserId, @DeniedRoleId);

DELETE r
FROM dbo.AspNetRoles r
WHERE r.Name IN ('WH Pick Reader', 'WH Pick Denied')
  AND r.Id NOT IN (@ReaderRoleId, @DeniedRoleId)
  AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.RoleId = r.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission rp WHERE rp.RoleID = r.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.AspNetRoleClaims rc WHERE rc.RoleId = r.Id);

COMMIT TRANSACTION;

SELECT u.Email, r.Name AS RoleName, p.ScreenCode, p.PermissionLevel
FROM dbo.AspNetUsers u
JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
LEFT JOIN dbo.SYS_RolePermission p ON p.RoleID = r.Id
WHERE u.Id IN (@ReaderUserId, @DeniedUserId)
ORDER BY u.Email, p.ScreenCode;
