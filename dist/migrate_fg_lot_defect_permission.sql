-- Separately configurable action. No stock, production history, or schema changes.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode='FG-DEFECT')
    INSERT dbo.SYS_Screen(ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,PermissionCriteria,SortOrder,IsVisible,CreatedBy,CreatedTS)
    VALUES('FG-DEFECT','WEB','WH',N'재고 불량 처리','Inventory Defect','wh/inventory/defect','FG-DEFECT','_X_',99,0,'migration',SYSDATETIME());
UPDATE dbo.SYS_Screen SET ProcessCode='WH',HRef='wh/inventory/defect',ScreenName=N'재고 불량 처리',ScreenNameEn='Inventory Defect',ModifiedBy='migration',ModifiedTS=SYSDATETIME()
WHERE ModuleCode='WEB' AND ScreenCode='FG-DEFECT'
  AND (ProcessCode<>'WH' OR HRef<>'wh/inventory/defect' OR ScreenName<>N'재고 불량 처리' OR ScreenNameEn<>'Inventory Defect');
INSERT dbo.SYS_RolePermission(RoleID,RoleName,ModuleCode,ScreenCode,PermissionLevel,IsSystemRole,EffectiveTS,CreatedBy,CreatedTS)
SELECT R.Id,R.Name,'WEB','FG-DEFECT','RXA',1,SYSDATETIME(),'migration',SYSDATETIME()
FROM dbo.AspNetRoles R WHERE R.Id='ROLE-SYSADMIN'
    AND NOT EXISTS(SELECT 1 FROM dbo.SYS_RolePermission P WHERE P.RoleID=R.Id AND P.ScreenCode='FG-DEFECT');
COMMIT;
