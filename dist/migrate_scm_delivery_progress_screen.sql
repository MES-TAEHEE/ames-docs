-- Screen metadata only. Keep existing routes, ScreenIDs and role grants when renumbering.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Targets TABLE(Href varchar(200),Code varchar(20),Ko nvarchar(100),En nvarchar(100),SortOrder int);
    INSERT @Targets VALUES
        ('scm/delivery-progress','SCM-003',N'납품 진행 현황',N'Delivery Progress',3),
        ('scm/purchase-items','SCM-004',N'발주품목 관리',N'Purchase Items',4),
        ('scm/portal-users','SCM-005',N'외부 사용자 관리',N'Portal Users',5);
    DECLARE @Move TABLE(ScreenID int,OldCode varchar(20),TempCode varchar(20),NewCode varchar(20));
    INSERT @Move SELECT s.ScreenID,s.ScreenCode,CONCAT('SCM-MOVE-',s.ScreenID),t.Code
        FROM dbo.SYS_Screen s WITH(UPDLOCK,HOLDLOCK) JOIN @Targets t ON LTRIM(RTRIM(s.HRef))=t.Href AND s.ModuleCode='WEB'
        WHERE s.ScreenCode<>t.Code;
    IF EXISTS(SELECT 1 FROM dbo.SYS_Screen s JOIN @Targets t ON t.Code=s.ScreenCode
        WHERE ISNULL(s.HRef,'')<>t.Href AND NOT EXISTS(SELECT 1 FROM @Move m WHERE m.ScreenID=s.ScreenID))
        THROW 51860,'SCM target code belongs to an unrelated screen. No changes applied.',1;
    IF EXISTS(SELECT 1 FROM dbo.SYS_Screen s JOIN @Move m ON s.ScreenCode=m.TempCode)
        THROW 51861,'SCM temporary code conflict. No changes applied.',1;
    UPDATE p SET ScreenCode=m.TempCode FROM dbo.SYS_RolePermission p JOIN @Move m ON m.OldCode=p.ScreenCode;
    UPDATE s SET ScreenCode=m.TempCode FROM dbo.SYS_Screen s JOIN @Move m ON m.ScreenID=s.ScreenID;
    UPDATE p SET ScreenCode=m.NewCode FROM dbo.SYS_RolePermission p JOIN @Move m ON m.TempCode=p.ScreenCode;
    UPDATE s SET ScreenCode=m.NewCode FROM dbo.SYS_Screen s JOIN @Move m ON m.ScreenID=s.ScreenID;
    UPDATE s SET LidLabel=t.Code,SortOrder=t.SortOrder,ModifiedBy='SCM-DELIVERY',ModifiedTS=SYSDATETIME()
        FROM dbo.SYS_Screen s JOIN @Targets t ON t.Code=s.ScreenCode AND t.Href=s.HRef;
    IF NOT EXISTS(SELECT 1 FROM dbo.SYS_Screen WHERE HRef='scm/delivery-progress' AND ModuleCode='WEB')
        INSERT dbo.SYS_Screen(ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,
            PermissionCriteria,SortOrder,IsVisible,CreatedBy,CreatedTS)
        VALUES('SCM-003','WEB','SCM',N'납품 진행 현황',N'Delivery Progress','scm/delivery-progress','SCM-003','_XX',3,1,'SCM-DELIVERY',SYSDATETIME());
    IF NOT EXISTS(SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleID=N'ROLE-SYSADMIN' AND ScreenCode='SCM-003')
        INSERT dbo.SYS_RolePermission(RoleID,RoleName,ModuleCode,ProcessCode,ScreenCode,PermissionLevel,
            IsSystemRole,EffectiveTS,CreatedBy,CreatedTS)
        SELECT Id,Name,'WEB','SCM','SCM-003','RXX',1,SYSDATETIME(),'SCM-DELIVERY',SYSDATETIME()
        FROM dbo.AspNetRoles WHERE Id=N'ROLE-SYSADMIN';
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
