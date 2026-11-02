-- Current WH/FG navigation. ScreenCode remains the permission identity; LidLabel is the display number.
-- Only screen metadata changes. No inventory, role grants, schema rebuild or seeds.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;

DECLARE @Before nvarchar(max)=(SELECT ScreenID,ScreenCode,HRef,ScreenName,ScreenNameEn,LidLabel,SortOrder,IsVisible
    FROM dbo.SYS_Screen WHERE ModuleCode='WEB' AND ProcessCode IN('WH','FG')
    ORDER BY ScreenID FOR JSON PATH,INCLUDE_NULL_VALUES);

DECLARE @Screens table(HRef varchar(200),ProcessCode varchar(20),ScreenName nvarchar(120),
    ScreenNameEn nvarchar(120),LidLabel varchar(20),SortOrder int,IsVisible bit);
INSERT @Screens VALUES
    ('wh/inventory','WH',N'재고 조회',N'Inventory Search','WH-01',1,1),
    ('wh/location-map','WH',N'로케이션 맵',N'Location Map','WH-02',2,1),
    ('wh/picking-orders','WH',N'피킹 오더',N'Picking Orders','WH-03',3,1),
    ('wh/log-history','WH',N'이력',N'History','WH-04',4,1),
    ('fg/customer-returns','FG',N'고객사 리턴',N'Customer Returns','FG-01',1,1),
    ('fg/shipping-orders','FG',N'출하 오더',N'Shipment Orders','FG-02',2,1),
    ('fg/shipment-plan','FG',N'출하 계획',N'Shipment Plan','FG-03',3,1),
    ('fg/shipments','FG',N'출하 목록',N'Shipments','FG-04',4,1);

IF EXISTS(SELECT 1 FROM @Screens V WHERE
    (SELECT COUNT(*) FROM dbo.SYS_Screen S WITH(UPDLOCK,HOLDLOCK)
     WHERE S.ModuleCode='WEB' AND S.ProcessCode=V.ProcessCode AND TRIM('/' FROM S.HRef)=V.HRef)<>1)
    THROW 51826,'Expected one registered WEB screen per WH/FG path. No screen metadata was changed.',1;

UPDATE S SET ScreenName=V.ScreenName,ScreenNameEn=V.ScreenNameEn,LidLabel=V.LidLabel,
    SortOrder=V.SortOrder,IsVisible=V.IsVisible,ModifiedBy='wh-fg-layout',ModifiedTS=SYSDATETIME()
FROM dbo.SYS_Screen S JOIN @Screens V ON TRIM('/' FROM S.HRef)=V.HRef
    AND S.ModuleCode='WEB' AND S.ProcessCode=V.ProcessCode
WHERE EXISTS(SELECT S.ScreenName,S.ScreenNameEn,S.LidLabel,S.SortOrder,S.IsVisible
    EXCEPT SELECT V.ScreenName,V.ScreenNameEn,V.LidLabel,V.SortOrder,V.IsVisible);

-- Keep the legacy history route and permission-only screens, but never expose them as menu entries.
UPDATE dbo.SYS_Screen SET IsVisible=0,ModifiedBy='wh-fg-layout',ModifiedTS=SYSDATETIME()
WHERE ModuleCode='WEB' AND ProcessCode IN('WH','FG') AND COALESCE(IsVisible,1)<>0
  AND TRIM('/' FROM HRef) IN('fg/history','wh/picking-orders/inbound-test','wh/inventory/defect');

DECLARE @After nvarchar(max)=(SELECT ScreenID,ScreenCode,HRef,ScreenName,ScreenNameEn,LidLabel,SortOrder,IsVisible
    FROM dbo.SYS_Screen WHERE ModuleCode='WEB' AND ProcessCode IN('WH','FG')
    ORDER BY ScreenID FOR JSON PATH,INCLUDE_NULL_VALUES);
IF @Before<>@After
    INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ScreenCode,ActionType,TargetEntity,TargetID,
        BeforeValueJSON,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
    VALUES(SYSDATETIME(),'wh-fg-layout','SYS','SYS-003','UPDATE','SYS_Screen','WH-FG-HISTORY',
        @Before,@After,'OK',N'Align WH/FG screen labels, order and visibility; preserve permission identities.',
        'wh-fg-layout',SYSDATETIME());

COMMIT TRANSACTION;
