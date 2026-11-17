SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
UPDATE dbo.SYS_Screen SET LidLabel='PORTAL-001',ScreenName=N'발주 조회',ScreenNameEn=N'Orders',SortOrder=1
WHERE ModuleCode='WEB' AND ProcessCode='PORTAL' AND HRef IN('portal/orders','/portal/orders');
UPDATE dbo.SYS_Screen SET IsVisible=0
WHERE ModuleCode='WEB' AND ProcessCode='PORTAL' AND HRef IN('portal/due-orders','/portal/due-orders');
UPDATE dbo.SYS_Screen SET LidLabel='PORTAL-002',SortOrder=2
WHERE ModuleCode='WEB' AND ProcessCode='PORTAL' AND HRef IN('portal/deliveries','/portal/deliveries');
UPDATE dbo.SYS_Screen SET LidLabel='PORTAL-003',SortOrder=3
WHERE ModuleCode='WEB' AND ProcessCode='PORTAL' AND HRef IN('portal/packing-quantities','/portal/packing-quantities');
COMMIT;
