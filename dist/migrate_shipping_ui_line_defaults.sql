-- Display numbering only: keep ScreenCode and role permissions attached to their existing pages.
-- Register real MD_Line locations; no inventory move, deletion or test parts.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
UPDATE dbo.SYS_Screen SET LidLabel=V.Label,SortOrder=V.Position
FROM dbo.SYS_Screen S JOIN (VALUES
    ('fg/shipping-orders','FG-02',2),('fg/shipment-plan','FG-03',3),
    ('fg/shipments','FG-04',4),('fg/history','FG-05',5)
) V(HRef,Label,Position) ON S.HRef=V.HRef AND S.ModuleCode='WEB';

SELECT LineID,UPPER(RTRIM(LotPrefix)) Prefix,LineNameEn
INTO #Lines FROM dbo.MD_Line
WHERE COALESCE(Status,'ACTIVE')='ACTIVE' AND LEN(RTRIM(LotPrefix))=2
    AND RTRIM(LotPrefix) COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z0-9]%'
    AND NULLIF(LTRIM(RTRIM(LineNameEn)),'') IS NOT NULL;
IF EXISTS(SELECT Prefix FROM #Lines GROUP BY Prefix HAVING COUNT(*)>1)
    THROW 51050,'Line prefixes must be unique before registering locations.',1;
DECLARE @WarehouseID varchar(50);
SELECT @WarehouseID=CodeID FROM dbo.MD_CodeItem WHERE GroupCode='WH_CODE' AND CodeValue='EOS' AND COALESCE(UseFlag,1)=1;
IF @WarehouseID IS NULL THROW 51050,'Register the active EOS warehouse before registering line locations.',1;
IF EXISTS(SELECT 1 FROM #Lines M JOIN dbo.MD_CodeItem C ON C.GroupCode='WH_AREA' AND C.CodeValue=M.Prefix
    WHERE COALESCE(C.ParentCodeID,'') NOT IN(@WarehouseID,'WH_CODE_LINE'))
    THROW 51050,'A line prefix is already used by another warehouse area.',1;
INSERT dbo.MD_CodeItem(CodeID,GroupCode,CodeValue,CodeName,CodeNameEn,ParentCodeID,Attribute1,UseFlag,SortOrder,CreatedBy)
SELECT CONCAT('WH_AREA_',M.Prefix),'WH_AREA',M.Prefix,M.LineNameEn,M.LineNameEn,@WarehouseID,M.Prefix,1,100,'migration'
FROM #Lines M WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem C WHERE C.GroupCode='WH_AREA' AND C.CodeValue=M.Prefix);
UPDATE C SET CodeName=M.LineNameEn,CodeNameEn=M.LineNameEn,Attribute1=M.Prefix,ParentCodeID=@WarehouseID
FROM dbo.MD_CodeItem C JOIN #Lines M ON M.Prefix=C.CodeValue
WHERE C.GroupCode='WH_AREA' AND C.ParentCodeID IN(@WarehouseID,'WH_CODE_LINE');
IF EXISTS(SELECT 1 FROM #Lines M JOIN dbo.MD_Location L ON L.LocationID=CONCAT(M.Prefix,'01A1F1')
    WHERE COALESCE(L.LineID,'')<>M.LineID)
    THROW 51050,'A default line location code is already in use.',1;
INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,Aisle,Bay,Slot,LocationType,LineID,MinimumQty,ActiveFlag,CreatedBy)
SELECT CONCAT(M.Prefix,'01A1F1'),M.LineNameEn,'EOS',M.Prefix,'A1','01','F1','LINE',M.LineID,0,1,'migration'
FROM #Lines M WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_Location L WHERE L.LineID=M.LineID);
DROP TABLE #Lines;
COMMIT;
