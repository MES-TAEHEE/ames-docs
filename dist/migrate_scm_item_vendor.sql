-- SCM-003: MATERIAL item / vendor mappings. No business mappings are seeded.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.SCM_ItemVendor', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SCM_ItemVendor (
        ItemNo varchar(20) COLLATE Korean_Wansung_CI_AS NOT NULL,
        VendorID varchar(20) COLLATE Korean_Wansung_CI_AS NOT NULL,
        ActiveFlag bit NOT NULL CONSTRAINT DF_SCM_ItemVendor_Active DEFAULT (1),
        CreatedBy varchar(20) NOT NULL,
        CreatedTS datetime2(7) NOT NULL CONSTRAINT DF_SCM_ItemVendor_Created DEFAULT (SYSDATETIME()),
        ModifiedBy varchar(20) NULL,
        ModifiedTS datetime2(7) NULL,
        CONSTRAINT PK_SCM_ItemVendor PRIMARY KEY (ItemNo, VendorID),
        CONSTRAINT FK_SCM_ItemVendor_Item FOREIGN KEY (ItemNo) REFERENCES dbo.MD_Item(ItemNo),
        CONSTRAINT FK_SCM_ItemVendor_Vendor FOREIGN KEY (VendorID) REFERENCES dbo.MD_Vendor(VendorID)
    );
    CREATE INDEX IX_SCM_ItemVendor_Vendor ON dbo.SCM_ItemVendor(VendorID, ActiveFlag, ItemNo);
END;
COMMIT;
