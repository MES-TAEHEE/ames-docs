-- =====================================================================
-- migrate_pda_fg_adjust.sql
-- PDA Finished Goods inventory quantity adjustment contract
--
-- Apply:
--   sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -b -d AMES_DEV -i dist\pda\migrate_pda_fg_adjust.sql
-- =====================================================================
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.FG_InventoryAdjust', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_InventoryAdjust
    (
        AdjustID int IDENTITY(1,1) NOT NULL,
        AdjustNo varchar(24) NULL,
        StockID int NOT NULL,
        ItemNo varchar(20) NULL,
        Location varchar(20) NULL,
        LotID int NULL,
        QtyBefore decimal(14,3) NOT NULL,
        Delta decimal(14,3) NOT NULL,
        QtyAfter decimal(14,3) NOT NULL,
        ReasonCode varchar(30) NULL,
        ReasonNote nvarchar(500) NULL,
        Status varchar(20) NULL,
        RequestedBy nvarchar(450) NULL,
        ApprovedBy nvarchar(450) NULL,
        CreatedBy varchar(50) NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_FG_InventoryAdjust_CreatedTS DEFAULT SYSDATETIME(),
        CONSTRAINT PK_FG_InventoryAdjust PRIMARY KEY CLUSTERED (AdjustID)
    );

    CREATE INDEX IX_FG_InventoryAdjust_Stock
        ON dbo.FG_InventoryAdjust (StockID, CreatedTS DESC);
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_ADJUST_SCAN_STOCK
    @ScanText nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Scan nvarchar(80) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
    DECLARE @StockID int;

    IF @Scan = N''
        THROW 51600, 'Finished goods Lot No is required.', 1;
    IF LEN(@Scan) < 3
       OR @Scan COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9-]%'
        THROW 51604, 'The barcode format is invalid.', 1;

    SELECT TOP (1)
        @StockID = F.StockID
    FROM dbo.FG_Inventory F
    LEFT JOIN dbo.tbl_Lot L ON L.LotID = F.LotID
    WHERE (UPPER(COALESCE(L.LotCode, N'')) = UPPER(@Scan)
        OR UPPER(COALESCE(F.StockNumber, N'')) = UPPER(@Scan))
      AND UPPER(COALESCE(F.Status, N'Available')) NOT IN
          (N'CANCELED', N'CANCELLED', N'SHIPPED', N'DELIVERED', N'CLOSED')
    ORDER BY CASE WHEN COALESCE(F.Qty, 0) > 0 THEN 0 ELSE 1 END, F.StockID DESC;

    IF @StockID IS NULL
       AND EXISTS
       (
           SELECT 1
           FROM dbo.WH_Inventory W
           JOIN dbo.tbl_Lot WL ON WL.LotID = W.LotID
           WHERE UPPER(WL.LotCode) = UPPER(@Scan)
             AND UPPER(COALESCE(W.Status, N'Received')) NOT IN
                 (N'CANCELED', N'CANCELLED', N'RELEASED', N'PICKED')
       )
        THROW 51605, 'Warehouse material cannot be adjusted in Finished Goods Adjust.', 1;

    IF @StockID IS NULL
        THROW 51601, 'The specified finished goods Lot No could not be found.', 1;

    SELECT
        N'FG' AS RECEIVE_TYPE,
        N'N' AS YN,
        COALESCE(NULLIF(L.LotCode, N''), F.StockNumber) AS LOTNO,
        COALESCE(NULLIF(L.LotCode, N''), F.StockNumber) AS BARCODE,
        N'dbo.FG_Inventory' AS SOURCE_TABLE,
        CAST(NULL AS nvarchar(50)) AS NOTENO,
        CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
        CAST(NULL AS nvarchar(30)) AS CASE_NO,
        CAST(NULL AS nvarchar(30)) AS INVOICE_NO,
        CAST(NULL AS nvarchar(30)) AS CONTAINER_NO,
        F.ItemNo AS PARTNO,
        I.ItemName AS PARTNM,
        COALESCE(F.Qty, 0) AS QTY,
        I.DefaultUOM AS UNIT,
        CAST(NULL AS nvarchar(30)) AS PONO,
        CAST(NULL AS int) AS PONO_SEQ,
        CAST(NULL AS nvarchar(30)) AS VENDCD,
        CAST(NULL AS nvarchar(100)) AS VENDNM,
        CONVERT(date, L.ProducedAt) AS PROD_DATE,
        CAST(NULL AS date) AS DELI_DATE,
        CONVERT(date, F.StockTS) AS ARRIV_DATE,
        CAST(NULL AS date) AS SHIP_DATE,
        CAST(NULL AS date) AS PACK_DATE,
        F.Location AS RECEIVED_LOCATION,
        COALESCE(F.Status, N'Available') AS RECEIVED_STATUS
    FROM dbo.FG_Inventory F
    LEFT JOIN dbo.tbl_Lot L ON L.LotID = F.LotID
    LEFT JOIN dbo.MD_Item I ON I.ItemNo = F.ItemNo
    WHERE F.StockID = @StockID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FG_PDA_ADJUST_SAVE_QTY
    @ScanText nvarchar(80),
    @DeltaQty decimal(18,3),
    @ReasonCode nvarchar(30),
    @ReasonNote nvarchar(500) = NULL,
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
    DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @Supervisor nvarchar(450) = COALESCE(
        NULLIF(LTRIM(RTRIM(@SupervisorEmployeeNo)), N''),
        NULLIF(LTRIM(RTRIM(@SupervisorUserId)), N''),
        @User
    );

    IF @Scan = N''
        THROW 51610, 'Finished goods Lot No is required.', 1;
    IF COALESCE(@DeltaQty, 0) = 0
        THROW 51611, 'Adjustment quantity must be different from zero.', 1;
    IF @Reason = N''
        THROW 51612, 'Reason code is required.', 1;

    DECLARE
        @StockID int,
        @ItemNo varchar(20),
        @Location varchar(20),
        @LotID int,
        @BeforeQty decimal(18,3),
        @AfterQty decimal(18,3),
        @LotCode varchar(80);

    BEGIN TRAN;

    SELECT TOP (1)
        @StockID = F.StockID,
        @ItemNo = F.ItemNo,
        @Location = F.Location,
        @LotID = F.LotID,
        @BeforeQty = COALESCE(F.Qty, 0),
        @LotCode = COALESCE(NULLIF(L.LotCode, N''), F.StockNumber)
    FROM dbo.FG_Inventory F WITH (UPDLOCK, ROWLOCK)
    LEFT JOIN dbo.tbl_Lot L ON L.LotID = F.LotID
    WHERE (UPPER(COALESCE(L.LotCode, N'')) = UPPER(@Scan)
        OR UPPER(COALESCE(F.StockNumber, N'')) = UPPER(@Scan))
      AND UPPER(COALESCE(F.Status, N'Available')) NOT IN
          (N'CANCELED', N'CANCELLED', N'SHIPPED', N'DELIVERED', N'CLOSED')
    ORDER BY CASE WHEN COALESCE(F.Qty, 0) > 0 THEN 0 ELSE 1 END, F.StockID DESC;

    IF @StockID IS NULL
       AND EXISTS
       (
           SELECT 1
           FROM dbo.WH_Inventory W
           JOIN dbo.tbl_Lot WL ON WL.LotID = W.LotID
           WHERE UPPER(WL.LotCode) = UPPER(@Scan)
             AND UPPER(COALESCE(W.Status, N'Received')) NOT IN
                 (N'CANCELED', N'CANCELLED', N'RELEASED', N'PICKED')
       )
        THROW 51615, 'Warehouse material cannot be adjusted in Finished Goods Adjust.', 1;

    IF @StockID IS NULL
        THROW 51614, 'The specified finished goods Lot No could not be found.', 1;

    SET @AfterQty = @BeforeQty + @DeltaQty;
    IF @AfterQty < 0
        THROW 51617, 'After Qty cannot be below zero.', 1;

    UPDATE dbo.FG_Inventory
       SET Qty = @AfterQty,
           Status = N'Available',
           ModifiedTS = SYSDATETIME(),
           ModifiedBy = @User
     WHERE StockID = @StockID;

    IF @LotID IS NOT NULL
    BEGIN
        UPDATE dbo.tbl_Lot
           SET RemainingQty =
               (
                   SELECT COALESCE(SUM(COALESCE(F.Qty, 0)), 0)
                   FROM dbo.FG_Inventory F
                   WHERE F.LotID = @LotID
                     AND UPPER(COALESCE(F.Status, N'Available')) NOT IN
                         (N'CANCELED', N'CANCELLED', N'SHIPPED', N'DELIVERED', N'CLOSED')
               ),
               ModifiedTS = SYSDATETIME(),
               ModifiedBy = @User
         WHERE LotID = @LotID;
    END;

    INSERT INTO dbo.FG_InventoryAdjust
        (AdjustNo, StockID, ItemNo, Location, LotID, QtyBefore, Delta, QtyAfter,
         ReasonCode, ReasonNote, Status, RequestedBy, ApprovedBy, CreatedBy, CreatedTS)
    VALUES
        (CONCAT('FGADJ-', FORMAT(SYSDATETIME(), 'yyMMddHHmmss')),
         @StockID, @ItemNo, @Location, @LotID, @BeforeQty, @DeltaQty, @AfterQty,
         CONVERT(varchar(30), @Reason), @Note, N'Posted', @User, @Supervisor, @User, SYSDATETIME());

    COMMIT TRAN;

    EXEC dbo.FG_PDA_ADJUST_SCAN_STOCK @ScanText = @LotCode;
END;
GO
