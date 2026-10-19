-- Restore Sheet as an individual quantity unit; preserve existing metadata and quantities.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
IF EXISTS(SELECT 1 FROM dbo.MD_Uom WITH(UPDLOCK,HOLDLOCK) WHERE UOMCode='SH')
    UPDATE dbo.MD_Uom SET ActiveFlag=1,ModifiedBy='uom-restore',ModifiedTS=SYSDATETIME()
    WHERE UOMCode='SH' AND ISNULL(ActiveFlag,0)=0;
ELSE
    INSERT dbo.MD_Uom(UOMCode,UOMName,UOMCategory,BaseFlag,ConvFactor,DecimalPrec,Symbol,ActiveFlag,CreatedBy,CreatedTS)
    VALUES('SH','Sheet','QTY',0,1,3,'sh',1,'uom-restore',SYSDATETIME());
COMMIT;
