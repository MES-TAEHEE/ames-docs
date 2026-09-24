-- Immutable delivery-note snapshot; existing deliveries are not issued automatically.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.SCM_Delivery','NoteSnapshot') IS NULL
 ALTER TABLE dbo.SCM_Delivery ADD NoteSnapshot nvarchar(max) NULL,
  NoteIssuedAt datetime2(7) NULL, NoteIssuedBy varchar(20) NULL,
  NoteIssuedUserID nvarchar(450) NULL;
COMMIT;
