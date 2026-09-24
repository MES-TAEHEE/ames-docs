-- Daily EOS purchase-order numbering. Run before using SCM-001.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.SCM_PurchaseOrderSequence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SCM_PurchaseOrderSequence (
        NumberDate date NOT NULL CONSTRAINT PK_SCM_PurchaseOrderSequence PRIMARY KEY,
        LastNumber int NOT NULL,
        CONSTRAINT CK_SCM_PurchaseOrderSequence_Range CHECK (LastNumber BETWEEN 1 AND 9999)
    );
END;
COMMIT;
