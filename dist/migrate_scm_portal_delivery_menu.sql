-- PORTAL-003: delivery management; PORTAL-004: grouped delivery notes.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
UPDATE dbo.SYS_Screen SET ScreenName=N'딜리버리 노트 조회·발행',ScreenNameEn=N'Delivery Notes',
    HRef='portal/delivery-notes',LidLabel='PORTAL-004',SortOrder=4 WHERE ScreenCode='PORTAL-004';
IF @@ROWCOUNT=0
    INSERT dbo.SYS_Screen(ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy)
    VALUES('PORTAL-004','WEB','PORTAL',N'딜리버리 노트 조회·발행',N'Delivery Notes','portal/delivery-notes','PORTAL-004',4,1,'scm-screen');
UPDATE dbo.SYS_Screen SET ScreenName=N'납품서 관리',ScreenNameEn=N'Delivery Management',
    HRef='portal/deliveries',LidLabel='PORTAL-003',SortOrder=3 WHERE ScreenCode='PORTAL-003';
IF @@ROWCOUNT=0
    INSERT dbo.SYS_Screen(ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy)
    VALUES('PORTAL-003','WEB','PORTAL',N'납품서 관리',N'Delivery Management','portal/deliveries','PORTAL-003',3,1,'scm-screen');
COMMIT;
