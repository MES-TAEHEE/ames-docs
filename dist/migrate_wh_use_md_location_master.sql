-- Remove the duplicate Warehouse Location Master menu.
-- MD-018 (/md/fd/location) remains the canonical Location Master.
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.SYS_RolePermission', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.SYS_RolePermission
    WHERE ScreenCode = 'WH-001';
END;

IF OBJECT_ID(N'dbo.SYS_Screen', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.SYS_Screen
    WHERE ScreenCode = 'WH-001'
      AND ModuleCode = 'WEB'
      AND ProcessCode = 'WH';
END;
