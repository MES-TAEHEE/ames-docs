SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @Permissions TABLE
(
    RoleID nvarchar(450) NULL,
    RoleName varchar(40) NULL,
    ScreenCode varchar(20) NOT NULL,
    PermissionLevel varchar(10) NULL,
    IsSystemRole bit NULL,
    EffectiveTS datetime2 NULL,
    ModifiedBy varchar(20) NULL,
    CreatedBy varchar(20) NOT NULL,
    CreatedTS datetime2 NULL,
    ModifiedTS datetime2 NULL
);

-- Preserve permissions by screen meaning before screen codes are resequenced.
INSERT INTO @Permissions
    (RoleID, RoleName, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS,
     ModifiedBy, CreatedBy, CreatedTS, ModifiedTS)
SELECT p.RoleID, p.RoleName,
       CASE s.HRef
           WHEN 'fg/customer-returns' THEN 'FG-01'
           WHEN 'fg/shipment-plan' THEN 'FG-02'
           WHEN 'fg/shipments' THEN 'FG-03'
           WHEN 'fg/history' THEN 'FG-04'
       END,
       p.PermissionLevel, p.IsSystemRole, p.EffectiveTS,
       p.ModifiedBy, p.CreatedBy, p.CreatedTS, p.ModifiedTS
FROM dbo.SYS_RolePermission p
JOIN dbo.SYS_Screen s ON s.ScreenCode = p.ScreenCode
WHERE s.HRef IN ('fg/customer-returns', 'fg/shipment-plan', 'fg/shipments', 'fg/history');

-- New Shipment Plan inherits Shipment access when it has no prior permissions.
IF NOT EXISTS (SELECT 1 FROM @Permissions WHERE ScreenCode = 'FG-02')
BEGIN
    INSERT INTO @Permissions
        (RoleID, RoleName, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS,
         ModifiedBy, CreatedBy, CreatedTS, ModifiedTS)
    SELECT p.RoleID, p.RoleName, 'FG-02', p.PermissionLevel, p.IsSystemRole, p.EffectiveTS,
           p.ModifiedBy, p.CreatedBy, p.CreatedTS, p.ModifiedTS
    FROM dbo.SYS_RolePermission p
    JOIN dbo.SYS_Screen s ON s.ScreenCode = p.ScreenCode
    WHERE s.HRef = 'fg/shipments';
END;

DELETE p
FROM dbo.SYS_RolePermission p
LEFT JOIN dbo.SYS_Screen s ON s.ScreenCode = p.ScreenCode
WHERE p.ScreenCode IN ('FG-001','FG-002','FG-003','FG-004','FG-005','FG-006',
                       'FG-01','FG-02','FG-03','FG-04','FG-05','FG-06')
   OR (s.ProcessCode = 'FG' AND s.HRef IN
       ('fg/inventory','fg/location-map','fg/customer-returns','fg/shipment-plan','fg/shipments','fg/history'));

DELETE FROM dbo.SYS_Screen
WHERE ScreenCode IN ('FG-001','FG-002','FG-003','FG-004','FG-005','FG-006',
                     'FG-01','FG-02','FG-03','FG-04','FG-05','FG-06')
   OR (ProcessCode = 'FG' AND HRef IN
       ('fg/inventory','fg/location-map','fg/customer-returns','fg/shipment-plan','fg/shipments','fg/history'));

INSERT INTO dbo.SYS_Screen
    (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn,
     HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
VALUES
    ('FG-01', 'WEB', 'FG', NULL, N'고객사 리턴', N'Customer Returns', 'fg/customer-returns', 'FG-01', 1, 1, 'menu-migration', SYSDATETIME()),
    ('FG-02', 'WEB', 'FG', NULL, N'출하 계획',   N'Shipment Plan',    'fg/shipment-plan',    'FG-02', 2, 1, 'menu-migration', SYSDATETIME()),
    ('FG-03', 'WEB', 'FG', NULL, N'출하 목록',   N'Shipments',        'fg/shipments',        'FG-03', 3, 1, 'menu-migration', SYSDATETIME()),
    ('FG-04', 'WEB', 'FG', NULL, N'작업 이력',   N'History',          'fg/history',          'FG-04', 4, 1, 'menu-migration', SYSDATETIME());

;WITH Deduplicated AS
(
    SELECT *, ROW_NUMBER() OVER
        (PARTITION BY COALESCE(RoleName, RoleID), ScreenCode ORDER BY CreatedTS) AS rn
    FROM @Permissions
)
INSERT INTO dbo.SYS_RolePermission
    (RoleID, RoleName, ModuleCode, ProcessCode, ScreenCode, PermissionLevel,
     IsSystemRole, EffectiveTS, ModifiedBy, CreatedBy, CreatedTS, ModifiedTS)
SELECT RoleID, RoleName, 'WEB', 'FG', ScreenCode, PermissionLevel,
       IsSystemRole, EffectiveTS, ModifiedBy, CreatedBy, CreatedTS, ModifiedTS
FROM Deduplicated
WHERE rn = 1;

-- Admin = 시스템 역할 고정 ID ROLE-SYSADMIN(이름은 바뀔 수 있다 — dist/migrate_system_roles.sql)
DECLARE @AdminRoleID nvarchar(450) = (SELECT TOP (1) Id FROM dbo.AspNetRoles WHERE Id = N'ROLE-SYSADMIN');
IF @AdminRoleID IS NOT NULL
BEGIN
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ProcessCode, ScreenCode, PermissionLevel,
         IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    SELECT @AdminRoleID, (SELECT Name FROM dbo.AspNetRoles WHERE Id = @AdminRoleID), 'WEB', 'FG', s.ScreenCode, 'REA', 1,
           SYSDATETIME(), 'menu-migration', SYSDATETIME()
    FROM dbo.SYS_Screen s
    WHERE s.ProcessCode = 'FG'
      AND NOT EXISTS
          (SELECT 1 FROM dbo.SYS_RolePermission p WHERE p.RoleID = @AdminRoleID AND p.ScreenCode = s.ScreenCode);
END;

COMMIT TRANSACTION;

SELECT ScreenCode, ScreenName, ScreenNameEn, HRef, SortOrder, IsVisible
FROM dbo.SYS_Screen
WHERE ProcessCode = 'FG'
ORDER BY SortOrder, ScreenCode;
