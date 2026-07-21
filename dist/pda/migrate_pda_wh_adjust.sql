-- =====================================================================
--  migrate_pda_wh_adjust.sql
--  PDA Warehouse inventory adjustment database contract
--
--  PDA-owned procedure names use the main AMES Warehouse style:
--    dbo.WH_PDA_ADJUST_...
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\migrate_pda_wh_adjust.sql
-- =====================================================================
SET NOCOUNT ON;
GO

-- =====================================================================
--  Adjust / scan current stock
--  Source: dbo.WH_Inventory, dbo.tbl_Lot, dbo.MD_Item
--  ScanText accepts:
--    - LOT No: resolves directly to that inventory LOT
--    - Part No: allowed only when the part has exactly one active LOT
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_ADJUST_SCAN_STOCK
    @ScanText nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Scan nvarchar(80) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
    DECLARE @LotID int;

    IF @Scan = N''
        THROW 51500, 'Lot or Part No is required.', 1;

    SELECT TOP (1)
        @LotID = L.LotID
    FROM dbo.tbl_Lot L
    WHERE UPPER(L.LotCode) = UPPER(@Scan)
    ORDER BY L.LotID DESC;

    IF @LotID IS NULL
    BEGIN
        DECLARE @Candidates TABLE (LotID int NOT NULL PRIMARY KEY);

        INSERT INTO @Candidates (LotID)
        SELECT DISTINCT W.LotID
        FROM dbo.WH_Inventory W
        WHERE W.LotID IS NOT NULL
          AND UPPER(W.ItemNo) = UPPER(@Scan)
          AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
          AND COALESCE(W.OnHandQty, 0) > 0;

        IF NOT EXISTS (SELECT 1 FROM @Candidates)
            THROW 51501, 'Lot or Part No was not found in current inventory.', 1;

        IF (SELECT COUNT(*) FROM @Candidates) > 1
            THROW 51502, 'Multiple active LOTs found for this Part No. Scan a LOT No.', 1;

        SELECT TOP (1) @LotID = LotID FROM @Candidates;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.WH_Inventory W
        WHERE W.LotID = @LotID
          AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    )
        THROW 51503, 'The scanned LOT is not in current inventory.', 1;

    ;WITH CurrentStock AS
    (
        SELECT TOP (1)
            W.InventoryID,
            W.ItemNo,
            W.LocationID,
            W.LotID,
            COALESCE(W.OnHandQty, 0) AS OnHandQty,
            W.Status AS InventoryStatus,
            W.LastReceivedAt,
            W.ExpiryDate,
            L.LotCode,
            L.ProcessCode,
            L.ProducedAt,
            L.Status AS LotStatus,
            I.ItemName,
            I.DefaultUOM
        FROM dbo.WH_Inventory W
        JOIN dbo.tbl_Lot L
          ON L.LotID = W.LotID
        LEFT JOIN dbo.MD_Item I
          ON I.ItemNo = W.ItemNo
        WHERE W.LotID = @LotID
          AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
        ORDER BY
            CASE WHEN COALESCE(W.OnHandQty, 0) > 0 THEN 0 ELSE 1 END,
            W.InventoryID DESC
    )
    SELECT
        CASE
            WHEN UPPER(COALESCE(ProcessCode, N'')) = N'CKD' THEN N'CKD'
            ELSE N'LOCAL'
        END AS RECEIVE_TYPE,
        N'N' AS YN,
        LotCode AS LOTNO,
        LotCode AS BARCODE,
        N'dbo.WH_Inventory/dbo.tbl_Lot' AS SOURCE_TABLE,
        CAST(NULL AS nvarchar(50)) AS NOTENO,
        CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
        CAST(NULL AS nvarchar(30)) AS CASE_NO,
        CAST(NULL AS nvarchar(30)) AS INVOICE_NO,
        CAST(NULL AS nvarchar(30)) AS CONTAINER_NO,
        ItemNo AS PARTNO,
        ItemName AS PARTNM,
        OnHandQty AS QTY,
        DefaultUOM AS UNIT,
        CAST(NULL AS nvarchar(30)) AS PONO,
        CAST(NULL AS int) AS PONO_SEQ,
        CAST(NULL AS nvarchar(30)) AS VENDCD,
        CAST(NULL AS nvarchar(100)) AS VENDNM,
        CONVERT(date, ProducedAt) AS PROD_DATE,
        CAST(NULL AS date) AS DELI_DATE,
        CONVERT(date, LastReceivedAt) AS ARRIV_DATE,
        CAST(NULL AS date) AS SHIP_DATE,
        CAST(NULL AS date) AS PACK_DATE,
        LocationID AS RECEIVED_LOCATION,
        COALESCE(InventoryStatus, LotStatus, N'Received') AS RECEIVED_STATUS
    FROM CurrentStock;
END;
GO

-- =====================================================================
--  Adjust / save quantity change
--  Target: dbo.WH_Inventory, dbo.tbl_Lot
--  Audit:  dbo.WH_InventoryAdjust, dbo.WH_InventoryTransaction
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_ADJUST_SAVE_QTY
    @ScanText nvarchar(80),
    @DeltaQty decimal(18,3),
    @ReasonCode nvarchar(30),
    @ReasonNote nvarchar(500) = NULL,
    @SupervisorPin nvarchar(40),
    @SupervisorUserId nvarchar(450) = NULL,
    @SupervisorEmployeeNo nvarchar(40) = NULL,
    @UserId nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Scan nvarchar(80) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
    DECLARE @Reason nvarchar(30) = UPPER(LTRIM(RTRIM(ISNULL(@ReasonCode, N''))));
    DECLARE @Note nvarchar(500) = NULLIF(LTRIM(RTRIM(@ReasonNote)), N'');
    DECLARE @Pin nvarchar(40) = LTRIM(RTRIM(ISNULL(@SupervisorPin, N'')));
    DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @Supervisor nvarchar(450) = COALESCE(
        NULLIF(LTRIM(RTRIM(@SupervisorEmployeeNo)), N''),
        NULLIF(LTRIM(RTRIM(@SupervisorUserId)), N''),
        @User
    );
    DECLARE @LotID int;

    IF @Scan = N''
        THROW 51510, 'Lot or Part No is required.', 1;
    IF COALESCE(@DeltaQty, 0) = 0
        THROW 51511, 'Adjustment quantity must be different from zero.', 1;
    IF @Reason = N''
        THROW 51512, 'Reason code is required.', 1;
    IF LEN(@Pin) < 4
        THROW 51513, 'Supervisor PIN must be at least 4 digits.', 1;

    SELECT TOP (1)
        @LotID = L.LotID
    FROM dbo.tbl_Lot L
    WHERE UPPER(L.LotCode) = UPPER(@Scan)
    ORDER BY L.LotID DESC;

    IF @LotID IS NULL
    BEGIN
        DECLARE @Candidates TABLE (LotID int NOT NULL PRIMARY KEY);

        INSERT INTO @Candidates (LotID)
        SELECT DISTINCT W.LotID
        FROM dbo.WH_Inventory W
        WHERE W.LotID IS NOT NULL
          AND UPPER(W.ItemNo) = UPPER(@Scan)
          AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
          AND COALESCE(W.OnHandQty, 0) > 0;

        IF NOT EXISTS (SELECT 1 FROM @Candidates)
            THROW 51514, 'Lot or Part No was not found in current inventory.', 1;

        IF (SELECT COUNT(*) FROM @Candidates) > 1
            THROW 51515, 'Multiple active LOTs found for this Part No. Scan a LOT No.', 1;

        SELECT TOP (1) @LotID = LotID FROM @Candidates;
    END;

    DECLARE
        @InventoryID int,
        @ItemNo varchar(20),
        @LocationID varchar(20),
        @LotCode varchar(40),
        @BeforeQty decimal(18,3),
        @AfterQty decimal(18,3),
        @AdjustID int;

    BEGIN TRAN;

    SELECT TOP (1)
        @InventoryID = W.InventoryID,
        @ItemNo = W.ItemNo,
        @LocationID = W.LocationID,
        @BeforeQty = COALESCE(W.OnHandQty, 0),
        @LotCode = L.LotCode
    FROM dbo.WH_Inventory W WITH (UPDLOCK, ROWLOCK)
    JOIN dbo.tbl_Lot L
      ON L.LotID = W.LotID
    WHERE W.LotID = @LotID
      AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ORDER BY
        CASE WHEN COALESCE(W.OnHandQty, 0) > 0 THEN 0 ELSE 1 END,
        W.InventoryID DESC;

    IF @InventoryID IS NULL
        THROW 51516, 'The scanned LOT is not in current inventory.', 1;

    SET @AfterQty = @BeforeQty + @DeltaQty;

    IF @AfterQty < 0
        THROW 51517, 'After Qty cannot be below zero.', 1;

    UPDATE dbo.WH_Inventory
       SET OnHandQty = @AfterQty,
           Status = N'Received',
           ModifiedTS = SYSDATETIME(),
           ModifiedBy = @User
     WHERE InventoryID = @InventoryID;

    UPDATE dbo.tbl_Lot
       SET RemainingQty = @AfterQty,
           CurrentLocationID = COALESCE(@LocationID, CurrentLocationID),
           Status = N'Received',
           ModifiedTS = SYSDATETIME(),
           ModifiedBy = @User
     WHERE LotID = @LotID;

    DECLARE @InsertedAdjust TABLE (AdjustID int NOT NULL);

    INSERT INTO dbo.WH_InventoryAdjust
        (AdjustNo, ItemNo, LocationID, LotID, QtyBefore, Delta, QtyAfter,
         ReasonCode, ReasonNote, Status, RequestedBy, ApprovedBy, CreatedBy, CreatedTS)
    OUTPUT INSERTED.AdjustID INTO @InsertedAdjust
    VALUES
        (CONCAT('ADJ-', FORMAT(SYSDATETIME(), 'yyMMddHHmmss')),
         @ItemNo, @LocationID, @LotID, @BeforeQty, @DeltaQty, @AfterQty,
         CONVERT(varchar(30), @Reason), @Note, N'Posted', @User, @Supervisor, @User, SYSDATETIME());

    SELECT TOP (1) @AdjustID = AdjustID FROM @InsertedAdjust;

    INSERT INTO dbo.WH_InventoryTransaction
        (TransactionType, ItemNo, LocationID, LotID, QtyBefore, QtyChange, QtyAfter,
         ReasonCode, RefDocType, RefDocID, OperatorID, ApproverID, Note, CreatedBy, CreatedTS)
    VALUES
        (N'ADJ', @ItemNo, @LocationID, @LotID, @BeforeQty, @DeltaQty, @AfterQty,
         CONVERT(varchar(30), @Reason), N'WH_InventoryAdjust', @AdjustID,
         @User, @Supervisor, @Note, @User, SYSDATETIME());

    COMMIT TRAN;

    EXEC dbo.WH_PDA_ADJUST_SCAN_STOCK @ScanText = @LotCode;
END;
GO
