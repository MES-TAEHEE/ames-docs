-- Register the existing hidden Web receipt test in the standard Permission Matrix.
-- No inventory, receipt, account, or schema changes.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SYS_Screen WITH(UPDLOCK,HOLDLOCK) WHERE ScreenCode='WH-PDA-TEST')
    INSERT dbo.SYS_Screen(ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,PermissionCriteria,SortOrder,IsVisible,CreatedBy,CreatedTS)
    VALUES('WH-PDA-TEST','WEB','WH',N'WH PDA 테스트','WH PDA Test','wh/picking-orders/inbound-test','TEST','__X',98,0,'migration',SYSDATETIME());
INSERT dbo.SYS_RolePermission(RoleID,RoleName,ModuleCode,ScreenCode,PermissionLevel,IsSystemRole,EffectiveTS,CreatedBy,CreatedTS)
SELECT R.Id,R.Name,'WEB','WH-PDA-TEST','REX',1,SYSDATETIME(),'migration',SYSDATETIME()
FROM dbo.AspNetRoles R WHERE R.Id='ROLE-SYSADMIN'
    AND NOT EXISTS(SELECT 1 FROM dbo.SYS_RolePermission P WHERE P.RoleID=R.Id AND P.ModuleCode='WEB' AND P.ScreenCode='WH-PDA-TEST');
COMMIT;
