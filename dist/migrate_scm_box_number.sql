-- New boxes: BX-{VendorID}-{creation date yyyyMMdd}-{vendor/day sequence, minimum 4 digits}.
-- Keep existing BOX-* numbers, including voided labels, unchanged.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.SCM_BoxNumberSequence','U') IS NULL
    CREATE TABLE dbo.SCM_BoxNumberSequence (
        VendorID varchar(20) NOT NULL REFERENCES dbo.MD_Vendor(VendorID),
        NumberDate date NOT NULL,
        LastNumber int NOT NULL CHECK (LastNumber>0),
        CONSTRAINT PK_SCM_BoxNumberSequence PRIMARY KEY(VendorID,NumberDate)
    );
IF COL_LENGTH('dbo.SCM_DeliveryBox','IssuedBoxNumber') IS NULL
BEGIN
    ALTER TABLE dbo.SCM_DeliveryBox ADD IssuedBoxNumber varchar(64) NULL;
    DROP INDEX UX_SCM_DeliveryBox_Number ON dbo.SCM_DeliveryBox;
    ALTER TABLE dbo.SCM_DeliveryBox DROP COLUMN BoxNumber;
    EXEC(N'ALTER TABLE dbo.SCM_DeliveryBox ADD BoxNumber AS
        (CONVERT(varchar(64),COALESCE(IssuedBoxNumber,''BOX-''+CONVERT(varchar(20),BoxID)))) PERSISTED;');
    EXEC(N'CREATE UNIQUE INDEX UX_SCM_DeliveryBox_Number ON dbo.SCM_DeliveryBox(BoxNumber);');
END;
COMMIT;
