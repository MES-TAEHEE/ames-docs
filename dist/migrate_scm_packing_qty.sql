-- PORTAL-006: vendor-specific packing quantity. No sample business values.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.SCM_ItemVendor','PackingQty') IS NULL
    ALTER TABLE dbo.SCM_ItemVendor ADD PackingQty decimal(18,3) NULL
        CONSTRAINT CK_SCM_ItemVendor_PackingQty CHECK (PackingQty IS NULL OR PackingQty > 0);
IF EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode='PORTAL-006'
    AND (HRef<>'portal/packing-quantities' OR ModuleCode<>'WEB'))
    THROW 50001,'Screen code conflict.',1;
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode='PORTAL-006')
    INSERT dbo.SYS_Screen(ScreenCode,ModuleCode,ProcessCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy,CreatedTS)
    VALUES('PORTAL-006','WEB','PORTAL',N'적입량 관리',N'Packing Quantities','portal/packing-quantities','PORTAL-006',6,1,'scm-screen',SYSDATETIME());
COMMIT;
