SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.SCM_Delivery','Version') IS NULL
 ALTER TABLE dbo.SCM_Delivery ADD Version rowversion;
IF COL_LENGTH('dbo.SCM_Delivery','ModifiedTS') IS NULL
 ALTER TABLE dbo.SCM_Delivery ADD ModifiedTS datetime2(7) NULL, ModifiedBy varchar(20) NULL, ModifiedUserID nvarchar(450) NULL;
COMMIT;
