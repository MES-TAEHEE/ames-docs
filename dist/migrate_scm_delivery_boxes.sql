-- Persistent box labels. Existing documents are not backfilled from current master data.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.SCM_DeliveryLine','PackingQty') IS NULL
    ALTER TABLE dbo.SCM_DeliveryLine ADD PackingQty decimal(18,3) NULL;
IF OBJECT_ID('dbo.SCM_DeliveryBox','U') IS NULL
BEGIN
    CREATE TABLE dbo.SCM_DeliveryBox (
        BoxID bigint IDENTITY PRIMARY KEY,
        BoxNumber AS ('BOX-'+CONVERT(varchar(20),BoxID)) PERSISTED,
        DeliveryLineID int NOT NULL REFERENCES dbo.SCM_DeliveryLine(DeliveryLineID),
        BoxSeq int NOT NULL CHECK (BoxSeq>0),
        ItemNo varchar(20) NOT NULL,
        ItemName nvarchar(200) NOT NULL,
        UnitCode varchar(20) NOT NULL,
        Quantity decimal(18,3) NOT NULL CHECK (Quantity>0),
        ActiveFlag bit NOT NULL DEFAULT(1),
        CreatedTS datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        VoidedTS datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_SCM_DeliveryBox_Number ON dbo.SCM_DeliveryBox(BoxNumber);
    CREATE UNIQUE INDEX UX_SCM_DeliveryBox_ActiveSequence ON dbo.SCM_DeliveryBox(DeliveryLineID,BoxSeq) WHERE ActiveFlag=1;
END;
COMMIT;
