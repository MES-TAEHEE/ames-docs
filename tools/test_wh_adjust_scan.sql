-- Run with sqlcmd -b -d AMES_DEV and the development connection options.
-- Validation-only calls; this script does not change inventory quantities.
SET NOCOUNT ON;

BEGIN TRY
    EXEC dbo.WH_PDA_ADJUST_SCAN_STOCK N'123';
    THROW 51990, 'Invalid barcode format was accepted.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 51504 OR ERROR_MESSAGE() <> N'The barcode format is invalid.' THROW;
END CATCH;

BEGIN TRY
    EXEC dbo.WH_PDA_ADJUST_SCAN_STOCK N'81710-PI000NNB';
    THROW 51991, 'Part No was accepted as a LOT barcode.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 51504 THROW;
END CATCH;

DECLARE @MissingLot nvarchar(80) = N'9999LL991231999999';
IF EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = @MissingLot)
    THROW 51992, 'Choose a different missing LOT test number.', 1;

BEGIN TRY
    EXEC dbo.WH_PDA_ADJUST_SCAN_STOCK @MissingLot;
    THROW 51993, 'A missing LOT was accepted.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 51501 OR ERROR_MESSAGE() <> N'The specified Lot No could not be found.' THROW;
END CATCH;

BEGIN TRY
    EXEC dbo.WH_PDA_ADJUST_SAVE_QTY @ScanText=N'123', @DeltaQty=1,
        @ReasonCode=N'COUNT_DIFF', @SupervisorPin=N'0000', @UserId=N'VALIDATION_TEST';
    THROW 51994, 'Save accepted an invalid barcode format.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 51518 THROW;
END CATCH;

DECLARE @ExistingLot nvarchar(80);
SELECT TOP (1) @ExistingLot=L.LotCode
FROM dbo.WH_Inventory W JOIN dbo.tbl_Lot L ON L.LotID=W.LotID
WHERE LEN(L.LotCode)=18
  AND L.LotCode COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Za-z0-9-]%'
  AND W.OnHandQty>0
  AND UPPER(COALESCE(W.Status,N'Received')) NOT IN (N'CANCELED',N'RELEASED',N'PICKED')
ORDER BY L.LotCode;

IF @ExistingLot IS NOT NULL
    EXEC dbo.WH_PDA_ADJUST_SCAN_STOCK @ExistingLot;
ELSE
    PRINT 'SKIP: No active 18-character LOT in this database.';

PRINT 'PASS: Adjust LOT barcode validation.';
