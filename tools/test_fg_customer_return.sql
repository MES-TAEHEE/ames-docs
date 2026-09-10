SET NOCOUNT ON;
SET XACT_ABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;
    EXEC dbo.FG_PDA_PPT_TEST_RESET @Screen='return';

    DECLARE @Scan TABLE
    (
        Barcode varchar(80),StockID int,StockNumber varchar(80),LotID int,LotNo varchar(80),
        ShipmentOrderID int,ShipOrderNumber varchar(40),CustomerCode varchar(20),ItemNo varchar(20),
        ItemName nvarchar(120),ShippedAt datetime2,Qty decimal(12,3)
    );
    INSERT @Scan EXEC dbo.FG_PDA_RETURN_SCAN @Barcode='FG-PPT-STK-950001';
    IF NOT EXISTS(SELECT 1 FROM @Scan WHERE StockNumber='FG-PPT-STK-950001' AND Qty=12)
        THROW 52100, 'Return scan did not resolve the picked stock.', 1;

    EXEC dbo.FG_PDA_RETURN_RECEIVE
        @Barcode='FG-PPT-STK-950001',@ReturnReason='DEFECT',@Note=N'rollback test',@OperatorID=N'SCTEST2';

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.FG_Inventory S JOIN @Scan R ON R.StockID=S.StockID
        WHERE S.Status='RETURN_HOLD' AND S.HoldFlag=1 AND S.Location IS NULL
    ) THROW 52101, 'Return receipt did not quarantine the inventory.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.FG_CustomerReturn C JOIN @Scan R ON R.StockID=C.StockID
        WHERE C.StockID=R.StockID AND C.ReturnQty=R.Qty AND C.ReturnReason='DEFECT'
    ) THROW 52102, 'Return receipt did not persist the normalized return record.', 1;

    ROLLBACK TRANSACTION;
    PRINT 'PASS: Customer Return scan, receipt, inventory hold, and rollback.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
