-- EOS purchasing / partner portal screen prototype.
-- Only screen metadata, process codes and role permissions are written.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Screens TABLE (
    Code varchar(20), Process varchar(10), Ko nvarchar(100),
    En nvarchar(100), Href varchar(200), SortOrder int, ExternalLevel varchar(10)
);
INSERT @Screens VALUES
('SCM-001','SCM',N'구매발주 관리',N'Purchase Orders','scm/purchase-orders',1,NULL),
('SCM-002','SCM',N'발주 진행 현황',N'Order Progress','scm/order-progress',2,NULL),
('SCM-003','SCM',N'발주품목 관리',N'Purchase Items','scm/purchase-items',3,NULL),
('PORTAL-001','PORTAL',N'발주 조회·수주 확인',N'Orders & Confirmation','portal/orders',1,'RE'),
('PORTAL-002','PORTAL',N'납기별 발주 현황',N'Orders by Due Date','portal/due-orders',2,'R'),
('PORTAL-003','PORTAL',N'납품서 등록',N'Create Delivery','portal/delivery-entry',3,'RE'),
('PORTAL-004','PORTAL',N'납품서 조회·수정',N'Delivery Notes','portal/deliveries',4,'RE'),
('PORTAL-005','PORTAL',N'입고·검수 결과',N'Receipt & Inspection','portal/receipts',5,'R');

-- Remove the retired screen by route; preserve ScreenID and permissions for the new screens.
DELETE p FROM dbo.SYS_RolePermission p
JOIN dbo.SYS_Screen s ON s.ScreenCode=p.ScreenCode
WHERE s.ModuleCode='WEB' AND s.HRef IN ('portal/shipment-plan','/portal/shipment-plan');
DELETE FROM dbo.SYS_Screen
WHERE ModuleCode='WEB' AND HRef IN ('portal/shipment-plan','/portal/shipment-plan');

DECLARE @OldCode varchar(20), @NewCode varchar(20), @Route varchar(200);
DECLARE portal_codes CURSOR LOCAL FAST_FORWARD FOR
SELECT Code,Href FROM @Screens WHERE Process='PORTAL' ORDER BY SortOrder;
OPEN portal_codes;
FETCH NEXT FROM portal_codes INTO @NewCode,@Route;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @OldCode=NULL;
    SELECT @OldCode=ScreenCode FROM dbo.SYS_Screen WHERE ModuleCode='WEB' AND HRef=@Route;
    IF @OldCode IS NOT NULL AND @OldCode<>@NewCode
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode=@NewCode)
            THROW 50004, 'Portal renumbering conflict. No changes applied.', 1;
        UPDATE dbo.SYS_RolePermission SET ScreenCode=@NewCode WHERE ScreenCode=@OldCode;
        UPDATE dbo.SYS_Screen SET ScreenCode=@NewCode,LidLabel=@NewCode
        WHERE ScreenCode=@OldCode AND ModuleCode='WEB' AND HRef=@Route;
    END;
    FETCH NEXT FROM portal_codes INTO @NewCode,@Route;
END;
CLOSE portal_codes;
DEALLOCATE portal_codes;
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
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Name='Admin')
    THROW 50003, 'Admin role is required.', 1;

-- 2026-09-25: portal users live in SCM_PortalVendorUser (SCM-004). No ExternalCustomer role, and PORTAL screens get no RBAC rows.

INSERT dbo.MD_CodeItem (CodeID,GroupCode,CodeValue,CodeName,CodeNameEn,SortOrder,UseFlag,CreatedBy,CreatedTS)
SELECT 'PROCESS_'+v.Code,'PROCESS',v.Code,v.Ko,v.En,v.SortOrder,1,'scm-screen',SYSDATETIME()
FROM (VALUES ('SCM',N'구매·발주 관리',N'Purchasing',85),('PORTAL',N'외부 포탈',N'Partner Portal',90)) v(Code,Ko,En,SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem c WHERE c.GroupCode='PROCESS' AND c.CodeValue=v.Code);

UPDATE s SET ProcessCode=n.Process,ScreenName=n.Ko,ScreenNameEn=n.En,
    LidLabel=n.Code,SortOrder=n.SortOrder,IsVisible=1,ModifiedBy='scm-screen',ModifiedTS=SYSDATETIME()
FROM dbo.SYS_Screen s JOIN @Screens n ON n.Code=s.ScreenCode;
INSERT dbo.SYS_Screen (ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy,CreatedTS)
SELECT n.Code,'WEB',n.Process,n.Ko,n.En,n.Href,n.Code,n.SortOrder,1,'scm-screen',SYSDATETIME()
FROM @Screens n WHERE NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen s WHERE s.ScreenCode=n.Code);

INSERT dbo.SYS_RolePermission (RoleID,RoleName,ModuleCode,ProcessCode,ScreenCode,PermissionLevel,IsSystemRole,EffectiveTS,CreatedBy,CreatedTS)
SELECT r.Id,r.Name,'WEB',n.Process,n.Code,
    CASE WHEN r.Name='Admin' THEN 'REA' ELSE n.ExternalLevel END,
    CASE WHEN r.Name='Admin' THEN 1 ELSE 0 END,SYSDATETIME(),'scm-screen',SYSDATETIME()
FROM @Screens n CROSS JOIN dbo.AspNetRoles r
WHERE r.Name='Admin' AND n.Process<>'PORTAL'
AND NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission p WHERE p.RoleName=r.Name AND p.ScreenCode=n.Code);


COMMIT;
SELECT s.ScreenCode,s.ScreenName,s.HRef,s.IsVisible
FROM dbo.SYS_Screen s JOIN @Screens n ON n.Code=s.ScreenCode ORDER BY s.ScreenCode;
SELECT p.RoleName,p.ScreenCode,p.PermissionLevel
FROM dbo.SYS_RolePermission p JOIN @Screens n ON n.Code=p.ScreenCode ORDER BY p.RoleName,p.ScreenCode;
