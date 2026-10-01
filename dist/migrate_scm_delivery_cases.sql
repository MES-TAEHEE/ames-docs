-- Additive only. CASEs are prepared from PO lines before Delivery registration.
-- Existing Delivery Notes, boxes and receipts are retained.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.SCM_DeliveryCase',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SCM_DeliveryCase
    (
        CaseNo varchar(50) COLLATE DATABASE_DEFAULT NOT NULL CONSTRAINT PK_SCM_DeliveryCase PRIMARY KEY,
        PoNumber varchar(30) NULL,
        VendorID varchar(20) NULL,
        DeliveryID int NULL CONSTRAINT FK_SCM_DeliveryCase_Delivery REFERENCES dbo.SCM_Delivery(DeliveryID),
        NoteID int NULL CONSTRAINT FK_SCM_DeliveryCase_Note REFERENCES dbo.SCM_DeliveryNote(NoteID),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_SCM_DeliveryCase_CreatedAt DEFAULT SYSDATETIME(),
        CreatedBy nvarchar(450) NOT NULL
    );
    CREATE INDEX IX_SCM_DeliveryCase_Note ON dbo.SCM_DeliveryCase(NoteID);
END;
IF OBJECT_ID(N'dbo.SCM_CaseNumberSequence',N'SO') IS NULL
    EXEC(N'CREATE SEQUENCE dbo.SCM_CaseNumberSequence AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;');
IF COL_LENGTH(N'dbo.SCM_DeliveryCase',N'PoNumber') IS NULL
    ALTER TABLE dbo.SCM_DeliveryCase ADD PoNumber varchar(30) NULL;
IF COL_LENGTH(N'dbo.SCM_DeliveryCase',N'VendorID') IS NULL
    ALTER TABLE dbo.SCM_DeliveryCase ADD VendorID varchar(20) NULL;
IF COL_LENGTH(N'dbo.SCM_DeliveryCase',N'DeliveryID') IS NULL
    ALTER TABLE dbo.SCM_DeliveryCase ADD DeliveryID int NULL
        CONSTRAINT FK_SCM_DeliveryCase_Delivery REFERENCES dbo.SCM_Delivery(DeliveryID);
ALTER TABLE dbo.SCM_DeliveryCase ALTER COLUMN NoteID int NULL;
IF COL_LENGTH(N'dbo.SCM_DeliveryBox',N'CaseNo') IS NULL
    ALTER TABLE dbo.SCM_DeliveryBox ADD CaseNo varchar(50) COLLATE DATABASE_DEFAULT NULL;
IF COL_LENGTH(N'dbo.SCM_DeliveryBox',N'PoID') IS NULL
    ALTER TABLE dbo.SCM_DeliveryBox ADD PoID int NULL
        CONSTRAINT FK_SCM_DeliveryBox_PO REFERENCES dbo.WH_PurchaseOrder(PoID);
IF COL_LENGTH(N'dbo.SCM_DeliveryBox',N'PackingQty') IS NULL
    ALTER TABLE dbo.SCM_DeliveryBox ADD PackingQty decimal(18,3) NULL;
GO
-- Unassigned boxes belong to their PO and CASE; DeliveryLineID is set at registration.
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SCM_DeliveryBox') AND name=N'UX_SCM_DeliveryBox_ActiveSequence')
    DROP INDEX UX_SCM_DeliveryBox_ActiveSequence ON dbo.SCM_DeliveryBox;
ALTER TABLE dbo.SCM_DeliveryBox ALTER COLUMN DeliveryLineID int NULL;
CREATE UNIQUE INDEX UX_SCM_DeliveryBox_ActiveSequence ON dbo.SCM_DeliveryBox(DeliveryLineID,BoxSeq)
    WHERE ActiveFlag=1 AND DeliveryLineID IS NOT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_SCM_DeliveryBox_Case')
    ALTER TABLE dbo.SCM_DeliveryBox WITH CHECK ADD CONSTRAINT FK_SCM_DeliveryBox_Case
        FOREIGN KEY(CaseNo) REFERENCES dbo.SCM_DeliveryCase(CaseNo);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SCM_DeliveryBox') AND name=N'IX_SCM_DeliveryBox_Case')
    CREATE INDEX IX_SCM_DeliveryBox_Case ON dbo.SCM_DeliveryBox(CaseNo);
UPDATE b SET PoID=l.PoID,PackingQty=COALESCE(b.PackingQty,l.PackingQty)
FROM dbo.SCM_DeliveryBox b JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID
WHERE b.PoID IS NULL;
-- Preserve CASEs created by the earlier note-first implementation.
UPDATE k SET VendorID=n.VendorID
FROM dbo.SCM_DeliveryCase k JOIN dbo.SCM_DeliveryNote n ON n.NoteID=k.NoteID
WHERE k.VendorID IS NULL;
UPDATE k SET PoNumber=x.PoNumber,DeliveryID=x.DeliveryID
FROM dbo.SCM_DeliveryCase k
CROSS APPLY(SELECT MIN(d.PoNumber) PoNumber,MIN(d.DeliveryID) DeliveryID,
                  COUNT(DISTINCT d.DeliveryID) DeliveryCount
    FROM dbo.SCM_DeliveryBox b JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID
    JOIN dbo.SCM_Delivery d ON d.DeliveryID=l.DeliveryID WHERE b.CaseNo=k.CaseNo) x
WHERE k.PoNumber IS NULL AND x.DeliveryCount=1;
GO
