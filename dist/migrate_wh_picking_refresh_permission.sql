-- Refresh can generate picking orders, so expose Edit in the Permission Matrix.
-- Keep the registered screen identity and every existing role grant. No automatic Edit grant.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;

DECLARE @Code varchar(20),@Criteria varchar(10);
IF (SELECT COUNT(*) FROM dbo.SYS_Screen WITH(UPDLOCK,HOLDLOCK)
    WHERE ModuleCode='WEB' AND TRIM('/' FROM HRef)='wh/picking-orders')<>1
    THROW 51826,'Expected one registered WEB picking orders screen. No permissions were changed.',1;
SELECT @Code=ScreenCode,@Criteria=PermissionCriteria FROM dbo.SYS_Screen
WHERE ModuleCode='WEB' AND TRIM('/' FROM HRef)='wh/picking-orders';

IF SUBSTRING(COALESCE(@Criteria,''),2,1)='X'
BEGIN
    DECLARE @Before nvarchar(max)=(SELECT ScreenCode,PermissionCriteria FROM dbo.SYS_Screen WHERE ScreenCode=@Code FOR JSON PATH);
    UPDATE dbo.SYS_Screen SET PermissionCriteria=STUFF(@Criteria,2,1,'_'),
        ModifiedBy='wh-pick-refresh',ModifiedTS=SYSDATETIME() WHERE ScreenCode=@Code;
    DECLARE @After nvarchar(max)=(SELECT ScreenCode,PermissionCriteria FROM dbo.SYS_Screen WHERE ScreenCode=@Code FOR JSON PATH);
    INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ScreenCode,ActionType,TargetEntity,TargetID,
        BeforeValueJSON,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
    VALUES(SYSDATETIME(),'wh-pick-refresh','SYS','SYS-003','UPDATE','SYS_Screen',@Code,
        @Before,@After,'OK',N'Enable the Edit permission slot for picking recalculation; preserve role grants.',
        'wh-pick-refresh',SYSDATETIME());
END;

COMMIT TRANSACTION;
