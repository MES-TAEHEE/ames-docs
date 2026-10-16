-- ════════════════════════════════════════════════════════════════════════
--  seed_md_bop_img_dev.sql
--  dev: IMG 스테이션 + BOP 시드
--
--  IMG-MAIN 좌측 품번 패널은 MD_Bop.StationCode 로 "이 스테이션에서 만드는 품번"을
--  고른다. dev DB 에는 IMG 라인 스테이션도 BOP 도 없어 로그인조차 안 된다.
--
--  · LINE-IMG-01 에 스테이션이 없으면 ST-IMG-01 을 만든다.
--  · 데모 품번 DR-TRM-LH-W 가 MD_Item 에 없으면 넣는다 — tools/seed_img_demo 가 만든 IMG WO 들이
--    이 품번을 가리키는데, MD_Item 재편(실제 부품 마스터) 이후 사라져 WO 가 Pop 에서 안 보인다.
--  · 그 라인에 공정 단계가 있는 WO 의 품번 + 데모 품번을 라인의 모든 스테이션에
--    (품번, 스테이션) 한 행씩 넣는다. RoutingType 'A', StepSeq 20.
--  · 이미 (품번, 스테이션) 이 있으면 건너뛴다 — 재실행 안전.
--  · MD_Item 에 없는 품번은 넣지 않는다.
--  · 운영 DB 용이 아니다. 운영 BOP 는 MD-005 화면에서 등록한다.
--
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/seed_md_bop_img_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Station WHERE LineID = 'LINE-IMG-01')
BEGIN
    INSERT INTO dbo.MD_Station (StationCode, StationName, StationNameEn, LineID, OrderSeq, Status, CreatedBy)
    VALUES ('ST-IMG-01', N'래핑 1호기', N'Wrapping 1', 'LINE-IMG-01', 10, 'ACTIVE', 'seed');
    PRINT 'MD_Station ST-IMG-01 inserted';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'DR-TRM-LH-W')
BEGIN
    INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory, DefaultUOM, RoutingType, ActiveFlag, CreatedBy)
    VALUES ('DR-TRM-LH-W', N'도어 트림 LH 래핑 (데모)', N'Door Trim LH Wrap (demo)', 'FINISHED', 'WRAP', 'EA', 'A', 1, 'seed');
    PRINT 'MD_Item DR-TRM-LH-W inserted';
END
GO

-- 완제품 라벨용 속성 (PGN · ALC · 장착위치). migrate_md_item_mount_pos.sql 이 먼저 적용돼 있어야 한다.
UPDATE dbo.MD_Item
SET    PGN = COALESCE(PGN, '8132'), ALC = COALESCE(ALC, '8046'), MountPos = COALESCE(MountPos, 'RL')
WHERE  ItemNo = 'DR-TRM-LH-W';
GO

-- 라벨 V 토큰은 WO 의 수주처(PP_CustomerOrder → MD_Customer.CustomerCode) 다.
-- 데모 IMG WO 는 수주 없이 만들어졌으므로 공장(PLANT) 고객 한 곳의 수주를 붙여 준다.
-- 사바나 공장(CUS-SAV)이 있으면 그것, 없으면 아무 PLANT 고객.
DECLARE @Cust varchar(20) =
    (SELECT TOP 1 CustomerID FROM dbo.MD_Customer
     WHERE  CustomerType = 'PLANT'
     ORDER  BY CASE WHEN CustomerID = 'CUS-SAV' THEN 0 ELSE 1 END, CustomerID);
IF @Cust IS NULL
    PRINT 'WARNING: no PLANT customer in MD_Customer — label V token will be empty';
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.PP_CustomerOrder WHERE SoNumber = 'SO-IMG-DEMO-001')
BEGIN
    INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, CustomerID, ItemNo, OrderQty, ShippedQty, OrderDate, RequestedDeliveryDate, Status, CreatedBy)
    VALUES ('SO-IMG-DEMO-001', 1, @Cust, 'DR-TRM-LH-W', 292, 0, CAST(SYSDATETIME() AS date), DATEADD(day, 14, CAST(SYSDATETIME() AS date)), 'OPEN', 'seed');
    PRINT CONCAT('PP_CustomerOrder SO-IMG-DEMO-001 inserted for ', @Cust);
END
ELSE
BEGIN
    -- 예전 실행이 MD_Customer 에 없는 고객을 넣었으면 고쳐 준다.
    UPDATE dbo.PP_CustomerOrder SET CustomerID = @Cust
    WHERE  SoNumber = 'SO-IMG-DEMO-001'
      AND  NOT EXISTS (SELECT 1 FROM dbo.MD_Customer c WHERE c.CustomerID = PP_CustomerOrder.CustomerID);
    IF @@ROWCOUNT > 0 PRINT CONCAT('PP_CustomerOrder SO-IMG-DEMO-001 customer fixed to ', @Cust);
END
UPDATE w
SET    w.SoID = so.SoID
FROM   dbo.PP_WorkOrder w
JOIN   dbo.PP_CustomerOrder so ON so.SoNumber = 'SO-IMG-DEMO-001'
WHERE  w.SoID IS NULL AND w.ItemNo = 'DR-TRM-LH-W'
  AND  EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID AND r.LineID = 'LINE-IMG-01');
PRINT CONCAT('IMG demo WOs linked to SO: ', @@ROWCOUNT);
GO

;WITH items AS (
    SELECT DISTINCT w.ItemNo
    FROM   dbo.PP_WorkOrderRouting r
    JOIN   dbo.PP_WorkOrder        w ON w.WoID = r.WoID
    WHERE  r.LineID = 'LINE-IMG-01'
    UNION
    SELECT 'DR-TRM-LH-W'
),
src AS (
    SELECT it.ItemNo, s.StationCode
    FROM   items it
    JOIN   dbo.MD_Item i ON i.ItemNo = it.ItemNo
    CROSS JOIN (SELECT StationCode FROM dbo.MD_Station WHERE LineID = 'LINE-IMG-01') s
)
INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
SELECT 'P' + LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 23),
       src.ItemNo, 'A', 20, src.StationCode, 1, 'seed'
FROM   src
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Bop b
                   WHERE  b.ItemNo = src.ItemNo AND b.StationCode = src.StationCode);

PRINT CONCAT('MD_Bop rows inserted: ', @@ROWCOUNT);
GO

SELECT b.StationCode, b.ItemNo, b.RoutingType, b.StepSeq, b.CreatedBy
FROM   dbo.MD_Bop b
JOIN   dbo.MD_Station s ON s.StationCode = b.StationCode
WHERE  s.LineID = 'LINE-IMG-01'
ORDER  BY b.StationCode, b.ItemNo;
GO
