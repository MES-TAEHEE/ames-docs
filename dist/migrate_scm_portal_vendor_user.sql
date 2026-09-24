-- Portal access is scoped by authenticated Identity user ID, never by a browser-supplied vendor.
-- Superseded 2026-09-25 by migrate_scm_portal_user_login.sql (login table). Both blocks below skip the new structure.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.SCM_PortalVendorUser','U') IS NULL
BEGIN
    CREATE TABLE dbo.SCM_PortalVendorUser (
        UserID nvarchar(450) NOT NULL,
        VendorID varchar(20) COLLATE Korean_Wansung_CI_AS NOT NULL,
        ActiveFlag bit NOT NULL DEFAULT(1),
        CreatedBy varchar(20) NOT NULL,
        CreatedTS datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT PK_SCM_PortalVendorUser PRIMARY KEY NONCLUSTERED(UserID,VendorID),
        CONSTRAINT FK_SCM_PortalVendorUser_User FOREIGN KEY(UserID) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_SCM_PortalVendorUser_Vendor FOREIGN KEY(VendorID) REFERENCES dbo.MD_Vendor(VendorID)
    );
END;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.SCM_PortalVendorUser') AND name='PK_SCM_PortalVendorUser' AND type=1)
   AND COL_LENGTH('dbo.SCM_PortalVendorUser','PasswordHash') IS NULL
BEGIN
    ALTER TABLE dbo.SCM_PortalVendorUser DROP CONSTRAINT PK_SCM_PortalVendorUser;
    ALTER TABLE dbo.SCM_PortalVendorUser ADD CONSTRAINT PK_SCM_PortalVendorUser PRIMARY KEY NONCLUSTERED(UserID,VendorID);
END;
COMMIT;
