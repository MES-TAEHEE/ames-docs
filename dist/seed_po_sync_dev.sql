-- ════════════════════════════════════════════════════════════════════════
--  seed_po_sync_dev.sql  (dev 전용 — 운영 금지)
--  PO 자동수집 소스 SEMS(Seoyon E-Hwa Manufacturing Savannah) 1건. URL 은 자리표시자라 그대로는 호출이 실패한다 —
--  MD-26 공통코드에서 SW_POSYNC_URL(·필요하면 SW_POSYNC_AUTH) 을 실제 값으로 바꿔 쓴다.
--  CustomerID 는 MD_Customer 의 PLANT 고객 첫 행(CUS-SAV 우선). migrate_po_sync.sql 뒤에 적용.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/seed_po_sync_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @Cust varchar(20) =
    (SELECT TOP 1 CustomerID FROM dbo.MD_Customer
     WHERE  CustomerType = 'PLANT'
     ORDER  BY CASE WHEN CustomerID = 'CUS-SAV' THEN 0 ELSE 1 END, CustomerID);
IF @Cust IS NULL
    SET @Cust = (SELECT TOP 1 CustomerID FROM dbo.MD_Customer ORDER BY CustomerID);
IF @Cust IS NULL
BEGIN
    PRINT 'WARNING: MD_Customer is empty — SW_POSYNC_SOURCE SEMS not seeded';
    RETURN;
END

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('SW_POSYNC_SOURCE_SEMS', 'SW_POSYNC_SOURCE', 'SEMS', N'Seoyon E-Hwa Manufacturing Savannah', N'Seoyon E-Hwa Manufacturing Savannah', @Cust,
     N'CORCD=7700;BIZCD=7710;VENDCD=310471;PURC_ORG=1A7700;INTERVAL=30', 1),
    ('SW_POSYNC_URL_SEMS',    'SW_POSYNC_URL',    'SEMS', N'Seoyon E-Hwa Manufacturing Savannah PO API', N'Seoyon E-Hwa Manufacturing Savannah PO API', NULL,
     N'https://srm.example.invalid/api/mm31006/inquery', 1)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, Description, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, Description, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.Description, src.SortOrder, 1, 'seed', SYSDATETIME());
PRINT CONCAT('SW_POSYNC_SOURCE SEMS seeded for customer ', @Cust);
GO
