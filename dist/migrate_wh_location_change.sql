-- Distinguish zero-quantity put-away/relocation from inventory adjustments.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF EXISTS (
    SELECT 1 FROM dbo.WH_InventoryTransaction
    WHERE ReasonCode = 'PUT_AWAY' AND TransactionType = 'ADJ' AND QtyChange <> 0
)
    THROW 51901, 'Non-zero PUT_AWAY adjustments require manual review.', 1;

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction')
      AND name = N'CK_WH_InventoryTransaction_Type'
)
    ALTER TABLE dbo.WH_InventoryTransaction DROP CONSTRAINT CK_WH_InventoryTransaction_Type;

ALTER TABLE dbo.WH_InventoryTransaction WITH CHECK ADD CONSTRAINT CK_WH_InventoryTransaction_Type
    CHECK (TransactionType IN ('IN', 'OUT', 'ADJ', 'MOVE'));

UPDATE dbo.WH_InventoryTransaction
   SET TransactionType = 'MOVE'
 WHERE TransactionType = 'ADJ'
   AND ReasonCode = 'PUT_AWAY'
   AND QtyChange = 0;

COMMIT TRANSACTION;
