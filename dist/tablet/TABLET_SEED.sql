-- Local tablet map samples, matching TabletInventoryService.DemoRows().
-- Additive and repeatable: never reset existing stock or replace item masters.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Actor varchar(50)='tablet-map-demo';
DECLARE @Stock TABLE(LocationID varchar(20),LotCode varchar(40),ItemNo varchar(20),ItemName nvarchar(100),Qty decimal(18,3));
INSERT @Stock VALUES
 ('1F-A-02','LOT-2608-101','96230-PI000','FEEDER CABLE',30),
 ('1F-D-05','LOT-2608-102','86631-PI000','REAR BUMPER BEAM',24),
 ('1F-H-12','LOT-2608-103','85740-PI000','LUGGAGE SIDE TRIM',36),
 ('1F-L-18','LOT-2608-104','85710-PI000','TAIL GATE UPPER TRIM',18),
 ('2F-A-03','LOT-2608-202','85820-PI000','QUARTER INNER TRIM',28),
 ('2F-C-06','LOT-2608-201','81710-PI000NNB','TRIM ASSY-TAIL GATE, LWR',42),
 ('2F-F-10','LOT-2608-203','85830-PI000','BACK PANEL TRIM',55),
 ('2F-I-14','LOT-2608-204','85750-PI000','LUGGAGE FLOOR TRIM',20),
 ('2F-M-19','LOT-2608-205','85760-PI000','PACKAGE TRAY TRIM',16),
 ('3F-B-04','LOT-2608-302','82302-PI000NNB','FRONT DOOR TRIM, RH',52),
 ('3F-G-11','LOT-2608-301','82301-PI000NNB','PNL ASSY-FR DR TRIM COMPL, LH',64),
 ('3F-J-15','LOT-2608-303','83301-PI000','REAR DOOR TRIM, LH',44),
 ('3F-M-20','LOT-2608-304','83302-PI000','REAR DOOR TRIM, RH',40),
 ('4F-A-02','LOT-2608-022','85770-PI000','CARGO SCREEN COVER',22),
 ('4F-B-04','LOT-2608-001','96230-PI000','FEEDER CABLE',48),
 ('4F-D-07','LOT-2607-014','81710-PI000NNB','TRIM ASSY-TAIL GATE, LWR',75),
 ('4F-D-07','LOT-2608-002','81710-PI000NNB','TRIM ASSY-TAIL GATE, LWR',25),
 ('4F-F-10','LOT-2608-023','85890-PI000','TRUNK SIDE FINISHER',26),
 ('4F-H-13','LOT-2606-118','82301-PI000NNB','PNL ASSY-FR DR TRIM COMPL, LH',90),
 ('4F-L-16','LOT-2608-021','82710-DW000WK','CAP-SIDE MT''G',32),
 ('4F-M-20','LOT-2608-024','85780-PI000','LUGGAGE BOARD ASSY',14);

IF EXISTS(SELECT 1 FROM @Stock S JOIN dbo.tbl_Lot L ON L.LotCode=S.LotCode WHERE L.CreatedBy<>@Actor)
    THROW 51800,'A tablet sample LOT belongs to other data. No samples were changed.',1;
IF EXISTS(SELECT 1 FROM dbo.MD_Location WHERE LocationID LIKE '[1-4]F-[A-M]-[0-9][0-9]' AND CreatedBy<>@Actor)
    THROW 51801,'Tablet map location identifiers are already in use. No samples were changed.',1;

;WITH Cols AS (SELECT 1 AS N UNION ALL SELECT N+1 FROM Cols WHERE N<20),
Rows AS (SELECT 65 AS N UNION ALL SELECT N+1 FROM Rows WHERE N<77)
INSERT dbo.MD_Location(LocationID,LocationName,ZoneCode,Aisle,Bay,Slot,Capacity,LocationType,PlantCode,ActiveFlag,CreatedBy)
SELECT CONCAT(F.N,'F-',CHAR(R.N),'-',RIGHT(CONCAT('0',C.N),2)),
       CONCAT(F.N,'F Material Row ',CHAR(R.N),' Location ',RIGHT(CONCAT('0',C.N),2)),
       'MATERIAL',CHAR(R.N),RIGHT(CONCAT('0',C.N),2),'01',1000,'RACK','EOS',1,@Actor
FROM (VALUES(1),(2),(3),(4)) F(N) CROSS JOIN Rows R CROSS JOIN Cols C
WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_Location L WHERE L.LocationID=CONCAT(F.N,'F-',CHAR(R.N),'-',RIGHT(CONCAT('0',C.N),2)));

INSERT dbo.MD_Item(ItemNo,ItemName,ItemType,DefaultUOM,ActiveFlag,CreatedBy)
SELECT DISTINCT S.ItemNo,S.ItemName,'RM','EA',1,@Actor FROM @Stock S
WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_Item I WHERE I.ItemNo=S.ItemNo);

INSERT dbo.tbl_Lot(LotCode,ItemNo,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,InventoryStatus,QualityFlag,CurrentLocationID,CreatedBy)
SELECT S.LotCode,S.ItemNo,'WH',S.Qty,S.Qty,CONVERT(date,'2026-08-01'),'Received','STORED','PASS',S.LocationID,@Actor
FROM @Stock S WHERE NOT EXISTS(SELECT 1 FROM dbo.tbl_Lot L WHERE L.LotCode=S.LotCode);

INSERT dbo.WH_Inventory(ItemNo,LocationID,LotID,OnHandQty,ReservedQty,LastReceivedAt,Status,CreatedBy)
SELECT S.ItemNo,S.LocationID,L.LotID,S.Qty,0,CONVERT(date,'2026-08-01'),'Received',@Actor
FROM @Stock S JOIN dbo.tbl_Lot L ON L.LotCode=S.LotCode AND L.CreatedBy=@Actor
WHERE NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory W WHERE W.LotID=L.LotID);
COMMIT TRANSACTION;

SELECT LEFT(LocationID,2) AS Floor,COUNT(*) AS Locations FROM dbo.MD_Location WHERE CreatedBy=@Actor GROUP BY LEFT(LocationID,2) ORDER BY Floor;
SELECT LEFT(LocationID,2) AS Floor,COUNT(*) AS Lots,COUNT(DISTINCT LocationID) AS StockedLocations,SUM(OnHandQty) AS Qty
FROM dbo.WH_Inventory WHERE CreatedBy=@Actor GROUP BY LEFT(LocationID,2) ORDER BY Floor;
