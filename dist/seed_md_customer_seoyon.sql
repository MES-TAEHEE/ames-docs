-- ════════════════════════════════════════════════════════════════════════
--  seed_md_customer_seoyon.sql
--  고객 마스터 = Seoyon E-Hwa Manufacturing 세 플랜트(Savannah·Georgia·Auburn) — 2026-09-30 사용자 결정.
--  · CUS-SAV·CUS-GEO·CUS-AUB 를 없으면 넣고 있으면 이름·유형을 맞춘다(개발 DB 의 CUS-GEO 는 구 'SEYON E-HWA Birmingham').
--  · AMES_Schema.sql 이 넣는 SEMS·SEMG(같은 두 플랜트의 옛 ID)도 지운다 — 참조가 남아 있으면 그 행만 남기고 PRINT.
--  · 데모 OEM 고객(BYD·FORD·GM·HMMA·STEL) 삭제. 참조 행은 MD_ShipmentDest 삭제, MD_LabelTemplate 은 고객 지정 해제(NULL).
--    수주·Forecast 가 아직 참조하는 고객(예: 복제본의 APS 테스트 수주 → CUS-BYD)은 지우지 않고 PRINT 로 남긴다 —
--    cleanup_legacy_item_data_dev.sql 뒤에 적용할 것.
--  · 재실행 안전. 적용: sqlcmd -f 65001 -I -b -i dist/seed_md_customer_seoyon.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

-- 삭제 후보: 데모 OEM 5곳 + 스키마 기본 시드의 옛 플랜트 ID(SEMS·SEMG). 수주·Forecast 가 참조하면 그 행은 남긴다.
DECLARE @cand TABLE (CustomerID varchar(20) PRIMARY KEY);
INSERT INTO @cand VALUES ('CUS-BYD'), ('CUS-FORD'), ('CUS-GM'), ('CUS-HMMA'), ('CUS-STEL'), ('SEMS'), ('SEMG');
DECLARE @drop TABLE (CustomerID varchar(20) PRIMARY KEY);
INSERT INTO @drop
SELECT c.CustomerID FROM dbo.MD_Customer c JOIN @cand k ON k.CustomerID = c.CustomerID
WHERE  NOT EXISTS (SELECT 1 FROM dbo.PP_CustomerOrder o WHERE o.CustomerID = c.CustomerID)
  AND  NOT EXISTS (SELECT 1 FROM dbo.PP_Forecast    o WHERE o.CustomerID = c.CustomerID);
DECLARE @kept nvarchar(400) = (SELECT STRING_AGG(c.CustomerID, ', ') FROM dbo.MD_Customer c JOIN @cand k ON k.CustomerID = c.CustomerID
                               WHERE c.CustomerID NOT IN (SELECT CustomerID FROM @drop));
IF @kept IS NOT NULL PRINT CONCAT(N'수주·Forecast 가 참조해 남겨 둔 고객: ', @kept);
-- 라벨 템플릿의 고객 지정은 선택 항목 — 삭제 고객을 가리키면 고객 없음(NULL)으로 풀어 템플릿은 남긴다
UPDATE dbo.MD_LabelTemplate SET CustomerID = NULL, ModifiedBy = 'SEED-DEMO', ModifiedTS = SYSDATETIME()
WHERE  CustomerID IN (SELECT CustomerID FROM @drop);
PRINT CONCAT(N'MD_LabelTemplate 고객 해제: ', @@ROWCOUNT);

DELETE FROM dbo.MD_ShipmentDest WHERE CustomerID IN (SELECT CustomerID FROM @drop);
PRINT CONCAT(N'MD_ShipmentDest 삭제: ', @@ROWCOUNT);
DELETE FROM dbo.MD_Customer WHERE CustomerID IN (SELECT CustomerID FROM @drop);
PRINT CONCAT(N'MD_Customer 삭제: ', @@ROWCOUNT);

-- 세 플랜트 upsert — 새 DB 에는 없어서 넣고, 개발 DB 에는 있어서 이름·유형만 맞춘다
DECLARE @plant TABLE (CustomerID varchar(20) PRIMARY KEY, CustomerCode varchar(20), CustomerName nvarchar(100));
INSERT INTO @plant VALUES
  ('CUS-SAV', 'SAV', N'Seoyon E-Hwa Manufacturing Savannah'),
  ('CUS-GEO', 'GEO', N'Seoyon E-Hwa Manufacturing Georgia'),
  ('CUS-AUB', 'AUB', N'Seoyon E-Hwa Manufacturing Auburn');

UPDATE c
SET    CustomerName = p.CustomerName, CustomerNameEn = p.CustomerName, CustomerCode = p.CustomerCode,
       CustomerType = 'PLANT', Country = 'USA', Status = 'ACTIVE', ModifiedBy = 'SEED-DEMO', ModifiedTS = SYSDATETIME()
FROM   dbo.MD_Customer c JOIN @plant p ON p.CustomerID = c.CustomerID
WHERE  c.CustomerName <> p.CustomerName OR ISNULL(c.CustomerCode,'') <> p.CustomerCode OR ISNULL(c.CustomerType,'') <> 'PLANT' OR ISNULL(c.Status,'') <> 'ACTIVE';
PRINT CONCAT(N'플랜트 갱신: ', @@ROWCOUNT);

INSERT INTO dbo.MD_Customer (CustomerID, CustomerCode, CustomerName, CustomerNameEn, CustomerType, Country, CurrencyCode, Status, CreatedBy, CreatedTS)
SELECT p.CustomerID, p.CustomerCode, p.CustomerName, p.CustomerName, 'PLANT', 'USA', 'USD', 'ACTIVE', 'SEED-DEMO', SYSDATETIME()
FROM   @plant p WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Customer c WHERE c.CustomerID = p.CustomerID);
PRINT CONCAT(N'플랜트 추가: ', @@ROWCOUNT);

COMMIT;
SELECT CustomerID, CustomerCode, CustomerName, CustomerType, Status FROM dbo.MD_Customer ORDER BY CustomerID;
GO
