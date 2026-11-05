-- Add only the Picking Request screen and copy the existing Picking Orders View/Edit grants.
-- No stock changes, table changes or role elevation beyond the existing Picking Orders grants.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRANSACTION;
DECLARE @Source varchar(20)=(SELECT TOP(1) ScreenCode FROM dbo.SYS_Screen
    WHERE ModuleCode='WEB' AND TRIM('/' FROM HRef)='wh/picking-orders');
IF @Source IS NULL THROW 51832,'Register Picking Orders before Picking Request.',1;
IF EXISTS(SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode='WH-05' AND (ModuleCode<>'WEB' OR TRIM('/' FROM HRef)<>'wh/picking-requests'))
    THROW 51832,'WH-05 belongs to another screen. No changes made.',1;
IF EXISTS(SELECT 1 FROM dbo.SYS_Screen WHERE ModuleCode='WEB' AND TRIM('/' FROM HRef)='wh/picking-requests' AND ScreenCode<>'WH-05')
    THROW 51832,'Picking Request already has another screen identity. No changes made.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.SYS_Screen WHERE ModuleCode='WEB' AND ScreenCode='WH-05')
    INSERT dbo.SYS_Screen(ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,PermissionCriteria,SortOrder,IsVisible,CreatedBy)
    VALUES('WH-05','WEB','WH',N'피킹 요청',N'Picking Request','wh/picking-requests','WH-05','__X',5,1,'wh-pick-request');
INSERT dbo.SYS_RolePermission(RoleID,RoleName,ModuleCode,ProcessCode,ScreenCode,PermissionLevel,IsSystemRole,EffectiveTS,CreatedBy)
SELECT P.RoleID,P.RoleName,'WEB','WH','WH-05',
    CASE WHEN CHARINDEX('E',COALESCE(P.PermissionLevel,''))>0 THEN 'REX'
         WHEN CHARINDEX('R',COALESCE(P.PermissionLevel,''))>0 THEN 'RXX' ELSE 'XXX' END,
    P.IsSystemRole,SYSDATETIME(),'wh-pick-request'
FROM dbo.SYS_RolePermission P
WHERE P.ScreenCode=@Source AND P.ModuleCode='WEB'
  AND NOT EXISTS(SELECT 1 FROM dbo.SYS_RolePermission D WHERE D.ScreenCode='WH-05' AND D.ModuleCode='WEB'
      AND (D.RoleID=P.RoleID OR D.RoleID IS NULL AND P.RoleID IS NULL AND D.RoleName=P.RoleName));
COMMIT TRANSACTION;
