-- Move the former Production Lines warehouse into EOS; preserve location IDs and stock.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
DECLARE @WarehouseID varchar(50),@LegacyID varchar(50),@Now datetime2=SYSDATETIME();
SELECT @WarehouseID=CodeID FROM dbo.MD_CodeItem WITH(UPDLOCK,HOLDLOCK)
WHERE GroupCode='WH_CODE' AND CodeValue='EOS' AND COALESCE(UseFlag,1)=1;
IF @WarehouseID IS NULL THROW 51050,'An active EOS warehouse is required.',1;
SELECT @LegacyID=CodeID FROM dbo.MD_CodeItem WITH(UPDLOCK,HOLDLOCK)
WHERE GroupCode='WH_CODE' AND CodeValue='LINE';

-- Do not silently take a line area registered to a different warehouse.
IF EXISTS(SELECT 1 FROM dbo.MD_Location L JOIN dbo.MD_CodeItem A ON A.GroupCode='WH_AREA' AND A.CodeValue=L.AreaCode
    WHERE L.WhCode='LINE' AND COALESCE(A.ParentCodeID,'')<>@WarehouseID
      AND (@LegacyID IS NULL OR COALESCE(A.ParentCodeID,'')<>@LegacyID))
    THROW 51050,'A production-line area belongs to another warehouse. No changes were saved.',1;

UPDATE dbo.MD_CodeItem SET ParentCodeID=@WarehouseID,ModifiedBy='migration',ModifiedTS=@Now
WHERE GroupCode='WH_AREA' AND ParentCodeID=@LegacyID;
UPDATE dbo.MD_Location SET WhCode='EOS',ModifiedBy='migration',ModifiedTS=@Now WHERE WhCode='LINE';
UPDATE L SET LocationName=M.LineNameEn,ModifiedBy='migration',ModifiedTS=@Now
FROM dbo.MD_Location L JOIN dbo.MD_Line M ON M.LineID=L.LineID
WHERE L.WhCode='EOS' AND NULLIF(LTRIM(RTRIM(M.LineNameEn)),'') IS NOT NULL
    AND COALESCE(L.LocationName,'')<>M.LineNameEn;
-- Preserve the old code for history, but no longer offer it as a separate warehouse.
IF @LegacyID IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE ParentCodeID=@LegacyID)
    AND NOT EXISTS(SELECT 1 FROM dbo.MD_Location WHERE WhCode='LINE')
    UPDATE dbo.MD_CodeItem SET UseFlag=0,ModifiedBy='migration',ModifiedTS=@Now WHERE CodeID=@LegacyID;
COMMIT;
