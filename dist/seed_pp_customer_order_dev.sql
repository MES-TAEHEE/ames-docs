-- ════════════════════════════════════════════════════════════════════════
--  seed_pp_customer_order_dev.sql
--  dev: BOM Master List ASSY 77 품번 × 3회차 = 가상 수주 231건(Confirmed) — PP-003 일괄 생성용.
--  · 납기 = 2026-10-06 / 10-13 / 10-20 주의 월~금(품번 순번으로 분산), 수량 = 40 + 8×(해시 % 21) → 40~200, 결정적.
--  · 고객: NE1A·ME1A·MV1A → CUS-SAV, NQ5A·LQ2 → CUS-GEO, LX3A·NX5A → CUS-AUB (seed_md_customer_seoyon 뒤).
--  · SoNumber = '41001' + 회차 + '0' + 순번3 (예 4100110001), SoLineNo 10. 같은 (SoNumber, SoLineNo) 가 있으면 건너뛴다.
--  · OrderDate 1회차 2026-09-29, 2·3회차 2026-09-30. 운영 DB 용이 아니다.
--  적용: sqlcmd -f 65001 -I -b -i dist/seed_pp_customer_order_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

IF (SELECT COUNT(*) FROM dbo.MD_Customer WHERE CustomerID IN ('CUS-SAV','CUS-GEO','CUS-AUB')) <> 3
    THROW 50130, N'고객 3곳(CUS-SAV/GEO/AUB) 이 없다 — seed_md_customer_seoyon.sql 먼저', 1;

DECLARE @Base date = '2026-10-06';
;WITH a AS (
    SELECT i.ItemNo, i.CarType, ROW_NUMBER() OVER (ORDER BY i.CarType, i.ItemNo) AS Rn
    FROM   dbo.MD_Item i WHERE i.ItemType = 'ASSY' AND ISNULL(i.ActiveFlag,1) = 1 AND i.RoutingType = 'A'),
k AS (SELECT 1 AS Wk UNION ALL SELECT 2 UNION ALL SELECT 3),
o AS (
    SELECT a.ItemNo, a.Rn, k.Wk,
           '41001' + CAST(k.Wk AS varchar(1)) + '0' + RIGHT('000' + CAST(a.Rn AS varchar(3)), 3) AS SoNumber,
           CASE a.CarType WHEN 'NE1A' THEN 'CUS-SAV' WHEN 'ME1A' THEN 'CUS-SAV' WHEN 'MV1A' THEN 'CUS-SAV'
                          WHEN 'NQ5A' THEN 'CUS-GEO' WHEN 'LQ2'  THEN 'CUS-GEO'
                          ELSE 'CUS-AUB' END AS CustomerID,
           40 + 8 * (ABS(CHECKSUM(a.ItemNo, k.Wk)) % 21) AS Qty,
           DATEADD(day, 7 * (k.Wk - 1) + ((a.Rn + k.Wk) % 5), @Base) AS Due,
           CASE k.Wk WHEN 1 THEN CAST('2026-09-29' AS date) ELSE CAST('2026-09-30' AS date) END AS OrderDate
    FROM a CROSS JOIN k)
INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, CustomerID, ItemNo, OrderQty, ShippedQty, OrderDate, RequestedDeliveryDate, PromisedDate, Status, CreatedBy, CreatedTS)
SELECT o.SoNumber, 10, o.CustomerID, o.ItemNo, o.Qty, 0, o.OrderDate, o.Due, o.Due, 'Confirmed', 'SEED-DEMO', SYSDATETIME()
FROM   o
WHERE  NOT EXISTS (SELECT 1 FROM dbo.PP_CustomerOrder x WHERE x.SoNumber = o.SoNumber AND x.SoLineNo = 10)
ORDER  BY o.Wk, o.Rn;
PRINT CONCAT(N'PP_CustomerOrder 추가: ', @@ROWCOUNT, N' 건');

COMMIT;

SELECT CustomerID, COUNT(*) AS Orders, MIN(RequestedDeliveryDate) AS FirstDue, MAX(RequestedDeliveryDate) AS LastDue,
       MIN(OrderQty) AS MinQty, MAX(OrderQty) AS MaxQty, SUM(OrderQty) AS TotalQty
FROM   dbo.PP_CustomerOrder WHERE CreatedBy = 'SEED-DEMO' GROUP BY CustomerID ORDER BY CustomerID;
GO
