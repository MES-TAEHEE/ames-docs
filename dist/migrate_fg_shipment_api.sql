-- Savannah FG shipment API configuration (truck loading confirm -> shipment POST).
-- Values are managed as common codes, matching the PP PO-sync configuration pattern.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

MERGE dbo.MD_CodeGroup AS tgt
USING (VALUES
    ('FG_SHIPMENT_SOURCE', N'FG 출하 API 고객사', N'FG Shipment API Sources', N'CodeValue=소스 키. Description=출하 요청 파라미터'),
    ('FG_SHIPMENT_URL',    N'FG 출하 API URL',    N'FG Shipment API Endpoints', N'CodeValue=소스 키. Description=Base URL'),
    ('FG_SHIPMENT_AUTH',   N'FG 출하 API 인증',   N'FG Shipment API Auth', N'Attribute1=Header:X-API-KEY. Description=API Key')
) AS src(GroupCode, GroupName, GroupNameEn, Description)
ON tgt.GroupCode=src.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode,GroupName,GroupNameEn,Description,UseFlag,CreatedBy,CreatedTS)
    VALUES (src.GroupCode,src.GroupName,src.GroupNameEn,src.Description,1,'migrate',SYSDATETIME());
GO

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('FG_SHIPMENT_SOURCE_SEMS','FG_SHIPMENT_SOURCE','SEMS',N'Seoyon E-Hwa Manufacturing Savannah',N'Seoyon E-Hwa Manufacturing Savannah',N'CUS-SAV',
     N'CORCD=7700;BIZCD=7710;VENDCD=310471;PURC_ORG=1A7700;PURC_PO_TYPE=1KMA;ARRIVAL_LEAD_DAYS=1;ARRIVAL_TIME=0930',1),
    ('FG_SHIPMENT_URL_SEMS','FG_SHIPMENT_URL','SEMS',N'Savannah Shipment API',N'Savannah Shipment API',NULL,
     N'http://192.168.1.68:5220',1),
    ('FG_SHIPMENT_AUTH_SEMS','FG_SHIPMENT_AUTH','SEMS',N'Savannah Shipment API 인증',N'Savannah Shipment API Auth',N'Header:X-API-KEY',
     N'vCTbfc7g7krgxEznQDsF7As3tcDHRELK',1)
) AS src(CodeID,GroupCode,CodeValue,CodeName,CodeNameEn,Attribute1,Description,SortOrder)
ON tgt.CodeID=src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID,GroupCode,CodeValue,CodeName,CodeNameEn,Attribute1,Description,SortOrder,UseFlag,CreatedBy,CreatedTS)
    VALUES (src.CodeID,src.GroupCode,src.CodeValue,src.CodeName,src.CodeNameEn,src.Attribute1,src.Description,src.SortOrder,1,'migrate',SYSDATETIME());
GO

SELECT GroupCode,CodeValue,CodeName,Attribute1,
       CASE WHEN GroupCode='FG_SHIPMENT_AUTH' THEN N'***' ELSE Description END AS Description
FROM dbo.MD_CodeItem
WHERE GroupCode IN ('FG_SHIPMENT_SOURCE','FG_SHIPMENT_URL','FG_SHIPMENT_AUTH')
ORDER BY GroupCode,SortOrder,CodeValue;
GO
