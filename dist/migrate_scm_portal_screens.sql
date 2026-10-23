-- EOS purchasing / partner portal screen prototype.
-- Only screen metadata, process codes and role permissions are written.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Screens TABLE (
    Code varchar(20), Process varchar(10), Ko nvarchar(100),
    En nvarchar(100), Href varchar(200), SortOrder int, ExternalLevel varchar(10), Visible bit
);
INSERT @Screens VALUES
('SCM-001','SCM',N'구매발주 관리',N'Purchase Orders','scm/purchase-orders',1,NULL,1),
('SCM-002','SCM',N'발주 진행 현황',N'Order Progress','scm/order-progress',2,NULL,1),
('SCM-003','SCM',N'납품 진행 현황',N'Delivery Progress','scm/delivery-progress',3,NULL,1),
('SCM-004','SCM',N'발주품목 관리',N'Purchase Items','scm/purchase-items',4,NULL,1),
('SCM-005','SCM',N'외부 사용자 관리',N'Portal Users','scm/portal-users',5,NULL,1),
('PORTAL-001','PORTAL',N'수주 확인',N'Order Confirmation','portal/orders',1,'RE',1),
('PORTAL-002','PORTAL',N'납품서 생성',N'Delivery Creation','portal/due-orders',2,'RE',1),
('PORTAL-003','PORTAL',N'납품 관리',N'Delivery Management','portal/deliveries',3,'RE',1),
('PORTAL-004','PORTAL',N'딜리버리 노트 조회·발행',N'Delivery Notes','portal/delivery-notes',4,'RE',0),
('PORTAL-005','PORTAL',N'입고 현황',N'Receipt Status','portal/receipts',5,'RE',0),
('PORTAL-006','PORTAL',N'포장 수량',N'Packing Quantities','portal/packing-quantities',6,'RE',1);

-- Remove the retired screen by route; preserve ScreenID and permissions for the new screens.
DELETE p FROM dbo.SYS_RolePermission p
JOIN dbo.SYS_Screen s ON s.ScreenCode=p.ScreenCode
WHERE s.ModuleCode='WEB' AND s.HRef IN ('portal/shipment-plan','/portal/shipment-plan');
DELETE FROM dbo.SYS_Screen
WHERE ModuleCode='WEB' AND HRef IN ('portal/shipment-plan','/portal/shipment-plan');

-- Move existing routes through temporary codes so swaps cannot collide.
DECLARE @Renumber TABLE(ScreenID int,OldCode varchar(20),TempCode varchar(20),NewCode varchar(20));
INSERT @Renumber SELECT S.ScreenID,S.ScreenCode,CONCAT('P-REN-',S.ScreenID),N.Code
FROM dbo.SYS_Screen S JOIN @Screens N ON S.ModuleCode='WEB' AND S.HRef=N.Href
WHERE S.ScreenCode<>N.Code;
IF EXISTS(SELECT 1 FROM @Renumber R JOIN dbo.SYS_Screen S ON S.ScreenCode=R.NewCode
    WHERE NOT EXISTS(SELECT 1 FROM @Renumber M WHERE M.ScreenID=S.ScreenID))
    THROW 50004,'Portal target code belongs to an unrelated screen. Nothing changed.',1;
UPDATE P SET ScreenCode=R.TempCode FROM dbo.SYS_RolePermission P JOIN @Renumber R ON R.OldCode=P.ScreenCode;
UPDATE S SET ScreenCode=R.TempCode FROM dbo.SYS_Screen S JOIN @Renumber R ON R.ScreenID=S.ScreenID;
UPDATE P SET ScreenCode=R.NewCode FROM dbo.SYS_RolePermission P JOIN @Renumber R ON R.TempCode=P.ScreenCode;
UPDATE S SET ScreenCode=R.NewCode,LidLabel=R.NewCode FROM dbo.SYS_Screen S JOIN @Renumber R ON R.ScreenID=S.ScreenID;
IF EXISTS (
    SELECT 1 FROM dbo.SYS_Screen s JOIN @Screens n ON s.ScreenCode=n.Code
    WHERE s.ModuleCode <> 'WEB' OR ISNULL(s.HRef,'') <> n.Href
)
    THROW 50001, 'Screen code conflict. No changes applied.', 1;
IF EXISTS (
    SELECT 1 FROM dbo.SYS_Screen s JOIN @Screens n ON s.HRef=n.Href
    WHERE s.ScreenCode <> n.Code
)
    THROW 50002, 'Screen route conflict. No changes applied.', 1;
-- Admin = 시스템 역할 고정 ID ROLE-SYSADMIN(이름은 바뀔 수 있다 — dist/migrate_system_roles.sql)
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id=N'ROLE-SYSADMIN')
    THROW 50003, 'Admin role is required.', 1;

-- 2026-09-25: portal users live in SCM_PortalVendorUser (SCM-004). No ExternalCustomer role, and PORTAL screens get no RBAC rows.

INSERT dbo.MD_CodeItem (CodeID,GroupCode,CodeValue,CodeName,CodeNameEn,SortOrder,UseFlag,CreatedBy,CreatedTS)
SELECT 'PROCESS_'+v.Code,'PROCESS',v.Code,v.Ko,v.En,v.SortOrder,1,'scm-screen',SYSDATETIME()
FROM (VALUES ('SCM',N'구매·발주 관리',N'Purchasing',85),('PORTAL',N'외부 포탈',N'Partner Portal',90)) v(Code,Ko,En,SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem c WHERE c.GroupCode='PROCESS' AND c.CodeValue=v.Code);

UPDATE s SET ProcessCode=n.Process,ScreenName=n.Ko,ScreenNameEn=n.En,
    LidLabel=CASE WHEN n.Code='PORTAL-006' THEN 'PORTAL-004' ELSE n.Code END,
    SortOrder=n.SortOrder,IsVisible=n.Visible,ModifiedBy='scm-screen',ModifiedTS=SYSDATETIME()
FROM dbo.SYS_Screen s JOIN @Screens n ON n.Code=s.ScreenCode;
INSERT dbo.SYS_Screen (ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy,CreatedTS)
SELECT n.Code,'WEB',n.Process,n.Ko,n.En,n.Href,
    CASE WHEN n.Code='PORTAL-006' THEN 'PORTAL-004' ELSE n.Code END,n.SortOrder,n.Visible,'scm-screen',SYSDATETIME()
FROM @Screens n WHERE NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen s WHERE s.ScreenCode=n.Code);

INSERT dbo.SYS_RolePermission (RoleID,RoleName,ModuleCode,ProcessCode,ScreenCode,PermissionLevel,IsSystemRole,EffectiveTS,CreatedBy,CreatedTS)
SELECT r.Id,r.Name,'WEB',n.Process,n.Code,
    CASE WHEN r.Id=N'ROLE-SYSADMIN' THEN 'REA' ELSE n.ExternalLevel END,
    CASE WHEN r.Id=N'ROLE-SYSADMIN' THEN 1 ELSE 0 END,SYSDATETIME(),'scm-screen',SYSDATETIME()
FROM @Screens n CROSS JOIN dbo.AspNetRoles r
WHERE r.Id=N'ROLE-SYSADMIN' AND n.Process<>'PORTAL'
AND NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission p WHERE p.RoleID=r.Id AND p.ScreenCode=n.Code);


COMMIT;
SELECT s.ScreenCode,s.ScreenName,s.HRef,s.IsVisible
FROM dbo.SYS_Screen s JOIN @Screens n ON n.Code=s.ScreenCode ORDER BY s.ScreenCode;
SELECT p.RoleName,p.ScreenCode,p.PermissionLevel
FROM dbo.SYS_RolePermission p JOIN @Screens n ON n.Code=p.ScreenCode ORDER BY p.RoleName,p.ScreenCode;
