-- Delivery Note printing is part of Delivery Management (003).
-- Preserve ScreenCode keys and existing role references; renumber display labels only.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
UPDATE dbo.SYS_Screen SET ScreenName=N'납품 생성',ScreenNameEn=N'Delivery Creation'
WHERE ModuleCode='WEB' AND HRef IN ('portal/due-orders','/portal/due-orders');
UPDATE dbo.SYS_Screen SET IsVisible=0
WHERE ModuleCode='WEB' AND HRef IN ('portal/delivery-notes','/portal/delivery-notes');
UPDATE dbo.SYS_Screen SET LidLabel='PORTAL-004',SortOrder=4
WHERE ModuleCode='WEB' AND HRef IN ('portal/receipts','/portal/receipts');
UPDATE dbo.SYS_Screen SET LidLabel='PORTAL-005',SortOrder=5
WHERE ModuleCode='WEB' AND HRef IN ('portal/packing-quantities','/portal/packing-quantities');
COMMIT TRANSACTION;
