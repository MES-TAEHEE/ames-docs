SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.SCM_DeliveryNote') IS NULL
BEGIN
 CREATE TABLE dbo.SCM_DeliveryNote(
  NoteID int IDENTITY PRIMARY KEY,
  NoteNumber varchar(30) NOT NULL UNIQUE,
  VendorID varchar(20) NOT NULL REFERENCES dbo.MD_Vendor(VendorID),
  Snapshot nvarchar(max) NOT NULL CHECK(ISJSON(Snapshot)=1),
  IssuedAt datetime2(7) NOT NULL,
  IssuedBy varchar(20) NOT NULL,
  IssuedUserID nvarchar(450) NOT NULL);
 CREATE TABLE dbo.SCM_DeliveryNoteDelivery(
  DeliveryID int NOT NULL PRIMARY KEY REFERENCES dbo.SCM_Delivery(DeliveryID),
  NoteID int NOT NULL REFERENCES dbo.SCM_DeliveryNote(NoteID));
 CREATE INDEX IX_SCM_DeliveryNoteDelivery_Note ON dbo.SCM_DeliveryNoteDelivery(NoteID);
END;
COMMIT;
