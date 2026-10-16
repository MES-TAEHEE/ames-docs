-- dist/seed_demand_plan_dev.sql (dev 전용 — 운영 금지) — seed_po_sync_dev.sql 과 같은 고객 선택, 소스 SEMS, URL 자리표시자
-- 적용: sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/seed_demand_plan_dev.sql
SET NOCOUNT ON; SET XACT_ABORT ON;
GO
DECLARE @Cust varchar(20) = (SELECT TOP 1 CustomerID FROM dbo.MD_Customer WHERE CustomerType = 'PLANT' ORDER BY CASE WHEN CustomerID = 'CUS-SAV' THEN 0 ELSE 1 END, CustomerID);
IF @Cust IS NULL SET @Cust = (SELECT TOP 1 CustomerID FROM dbo.MD_Customer ORDER BY CustomerID);
IF @Cust IS NULL BEGIN PRINT 'WARNING: MD_Customer is empty — SW_DPSYNC_SOURCE SEMS not seeded'; RETURN; END
MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('SW_DPSYNC_SOURCE_SEMS', 'SW_DPSYNC_SOURCE', 'SEMS', N'Seoyon E-Hwa Manufacturing Savannah', N'Seoyon E-Hwa Manufacturing Savannah', @Cust, N'CORCD=7700;BIZCD=7710;VENDCD=310471;PURC_ORG=1A7700', 1),
    ('SW_DPSYNC_URL_SEMS',    'SW_DPSYNC_URL',    'SEMS', N'Seoyon E-Hwa Manufacturing Savannah MIP API', N'Seoyon E-Hwa Manufacturing Savannah MIP API', NULL, N'https://srm.example.invalid/api/mm30011/mip', 1)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, Description, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, Description, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.Description, src.SortOrder, 1, 'seed', SYSDATETIME());
PRINT CONCAT('SW_DPSYNC_SOURCE SEMS seeded for customer ', @Cust);
GO
