-- =====================================================================
--  migrate_pda_wh_release.sql
--  PDA Warehouse release/picking database contract
--
--  PDA-owned procedure names use the main AMES Warehouse style:
--    dbo.WH_PDA_RELEASE_...
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\migrate_pda_wh_release.sql
-- =====================================================================
SET NOCOUNT ON;
GO

-- =====================================================================
--  Release / Pick Slip status
--  Source: dbo.WH_ReleaseSchedule
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_SLIP_STATUS
    @PickSlipNo nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    DECLARE @ReleaseScheduleID int = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''));

    IF @Slip = N''
    BEGIN
        SELECT
            @Slip AS PICK_SLIPNO,
            CAST(0 AS bit) AS EXISTS_FLAG,
            CAST(0 AS bit) AS IS_CLOSED,
            CAST(0 AS int) AS LINE_COUNT,
            CAST(NULL AS nvarchar(40)) AS REQ_LOCATION,
            CAST(NULL AS date) AS REQ_DATE,
            CAST(NULL AS datetime2) AS CLOSE_DATE,
            N'Pick Slip No is required.' AS MESSAGE;
        RETURN;
    END;

    IF @ReleaseScheduleID IS NULL
       OR NOT EXISTS (SELECT 1 FROM dbo.WH_ReleaseSchedule WHERE ReleaseScheduleID = @ReleaseScheduleID)
    BEGIN
        SELECT
            @Slip AS PICK_SLIPNO,
            CAST(0 AS bit) AS EXISTS_FLAG,
            CAST(0 AS bit) AS IS_CLOSED,
            CAST(0 AS int) AS LINE_COUNT,
            CAST(NULL AS nvarchar(40)) AS REQ_LOCATION,
            CAST(NULL AS date) AS REQ_DATE,
            CAST(NULL AS datetime2) AS CLOSE_DATE,
            N'Pick Slip was not found.' AS MESSAGE;
        RETURN;
    END;

    SELECT
        CONCAT(N'RS-', RS.ReleaseScheduleID) AS PICK_SLIPNO,
        CAST(1 AS bit) AS EXISTS_FLAG,
        CASE WHEN UPPER(COALESCE(RS.Status, N'')) IN (N'CLOSED', N'CANCELED') THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IS_CLOSED,
        CAST(1 AS int) AS LINE_COUNT,
        CAST(NULL AS nvarchar(40)) AS REQ_LOCATION,
        CONVERT(date, RS.RequiredAt) AS REQ_DATE,
        CAST(NULL AS datetime2) AS CLOSE_DATE,
        CASE
            WHEN UPPER(COALESCE(RS.Status, N'')) IN (N'CLOSED', N'CANCELED') THEN N'Pick Slip is already closed.'
            ELSE N'Pick Slip is ready.'
        END AS MESSAGE
    FROM dbo.WH_ReleaseSchedule RS
    WHERE RS.ReleaseScheduleID = @ReleaseScheduleID;
END;
GO

-- =====================================================================
--  Release / Pick Slip lines
--  Source: dbo.WH_ReleaseSchedule, dbo.WH_Inventory
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_PICK_LINES
    @PickSlipNo nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    DECLARE @ReleaseScheduleID int = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''));

    IF @ReleaseScheduleID IS NULL
        RETURN;

    ;WITH RequiredParts AS
    (
        SELECT
            RS.ReleaseScheduleID,
            CONCAT(N'RS-', RS.ReleaseScheduleID) AS PICK_SLIPNO,
            RS.ItemNo,
            COALESCE(I.ItemName, RS.ItemNo) AS ItemName,
            COALESCE(RS.DemandQty, 0) AS DemandQty,
            COALESCE(RS.PickedQty, 0) AS PickedQty,
            RS.CreatedBy AS RequestUserId
        FROM dbo.WH_ReleaseSchedule RS
        LEFT JOIN dbo.MD_Item I
               ON I.ItemNo = RS.ItemNo
        WHERE RS.ReleaseScheduleID = @ReleaseScheduleID
    ),
    RankedLocations AS
    (
        SELECT
            W.ItemNo,
            W.LocationID,
            ROW_NUMBER() OVER
            (
                PARTITION BY W.ItemNo
                ORDER BY COALESCE(W.LastReceivedAt, L.ProducedAt, CONVERT(datetime2, '9999-12-31')),
                         W.LocationID,
                         L.LotCode
            ) AS RN
        FROM dbo.WH_Inventory W
        INNER JOIN dbo.tbl_Lot L
                ON L.LotID = W.LotID
        INNER JOIN RequiredParts R
                ON R.ItemNo = W.ItemNo
        WHERE COALESCE(W.OnHandQty, 0) > 0
          AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ),
    Locations AS
    (
        SELECT
            ItemNo,
            MAX(CASE WHEN RN = 1 THEN LocationID END) AS LOC_01,
            MAX(CASE WHEN RN = 2 THEN LocationID END) AS LOC_02,
            MAX(CASE WHEN RN = 3 THEN LocationID END) AS LOC_03
        FROM RankedLocations
        WHERE RN <= 3
        GROUP BY ItemNo
    )
    SELECT
        R.PICK_SLIPNO,
        R.ItemNo AS PARTNO,
        R.ItemName AS PARTNM,
        R.DemandQty AS REQ_BOX_QTY,
        R.PickedQty AS PICKED_BOX_QTY,
        R.PickedQty AS PICKED_QTY,
        R.RequestUserId AS REQ_USERID,
        L.LOC_01,
        L.LOC_02,
        L.LOC_03,
        CASE
            WHEN R.DemandQty > 0 AND R.PickedQty >= R.DemandQty THEN N'Picked'
            WHEN R.PickedQty > 0 THEN N'Partial'
            ELSE N'Open'
        END AS STATUS
    FROM RequiredParts R
    LEFT JOIN Locations L
           ON L.ItemNo = R.ItemNo
    ORDER BY R.ItemNo;
END;
GO

-- =====================================================================
--  Release / LOT scan validation
--  Source: dbo.WH_ReleaseSchedule, dbo.WH_Inventory, dbo.tbl_Lot
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_SCAN_LOT
    @PickSlipNo nvarchar(40),
    @LotNo nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    DECLARE @Lot nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotNo, N'')));
    DECLARE @ReleaseScheduleID int = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''));
    DECLARE @PickSlipOut nvarchar(40) = CASE WHEN @ReleaseScheduleID IS NULL THEN @Slip ELSE CONCAT(N'RS-', @ReleaseScheduleID) END;

    DECLARE
        @RequestedItemNo varchar(20),
        @DemandQty decimal(14,3),
        @PickedQty decimal(14,3),
        @SlipStatus varchar(20),
        @LotID int,
        @InventoryID int,
        @ItemNo varchar(20),
        @ItemName nvarchar(100),
        @Qty decimal(14,3),
        @Unit varchar(10),
        @LocationID varchar(20),
        @LocationName nvarchar(100),
        @ZoneCode varchar(20),
        @InventoryStatus varchar(20),
        @ProducedAt datetime2,
        @ReceivedAt datetime2,
        @OldestLot varchar(40);

    IF @Slip = N''
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'Pick Slip No is required.' AS MESSAGE;
        RETURN;
    END;

    IF @Lot = N''
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'LOT No is required.' AS MESSAGE;
        RETURN;
    END;

    SELECT TOP (1)
        @RequestedItemNo = RS.ItemNo,
        @DemandQty = COALESCE(RS.DemandQty, 0),
        @PickedQty = COALESCE(RS.PickedQty, 0),
        @SlipStatus = RS.Status
    FROM dbo.WH_ReleaseSchedule RS
    WHERE RS.ReleaseScheduleID = @ReleaseScheduleID;

    IF @RequestedItemNo IS NULL
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'Pick Slip was not found.' AS MESSAGE;
        RETURN;
    END;

    IF UPPER(COALESCE(@SlipStatus, N'')) IN (N'CLOSED', N'CANCELED')
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @RequestedItemNo AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, @SlipStatus AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'Pick Slip is already closed.' AS MESSAGE;
        RETURN;
    END;

    SELECT TOP (1)
        @InventoryID = W.InventoryID,
        @LotID = L.LotID,
        @ItemNo = W.ItemNo,
        @ItemName = I.ItemName,
        @Qty = COALESCE(W.OnHandQty, 0),
        @Unit = I.DefaultUOM,
        @LocationID = W.LocationID,
        @LocationName = LOC.LocationName,
        @ZoneCode = LOC.ZoneCode,
        @InventoryStatus = W.Status,
        @ProducedAt = L.ProducedAt,
        @ReceivedAt = W.LastReceivedAt
    FROM dbo.tbl_Lot L
    LEFT JOIN dbo.WH_Inventory W
           ON W.LotID = L.LotID
          AND COALESCE(W.OnHandQty, 0) > 0
          AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    LEFT JOIN dbo.MD_Item I
           ON I.ItemNo = COALESCE(W.ItemNo, L.ItemNo)
    LEFT JOIN dbo.MD_Location LOC
           ON LOC.LocationID = W.LocationID
    WHERE L.LotCode = @Lot
    ORDER BY W.InventoryID DESC, L.LotID DESC;

    IF @LotID IS NULL
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'LOT was not found.' AS MESSAGE;
        RETURN;
    END;

    IF @InventoryID IS NULL
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, @Unit AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
            NULL AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'LOT is not available for release.' AS MESSAGE;
        RETURN;
    END;

    IF @ItemNo <> @RequestedItemNo
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
            @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
            @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
            CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            N'Wrong item. This LOT is not requested by the selected Pick Slip.' AS MESSAGE;
        RETURN;
    END;

    IF @DemandQty > 0 AND @PickedQty >= @DemandQty
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
            @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
            @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
            CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            N'This item is already fully picked for the selected Pick Slip.' AS MESSAGE;
        RETURN;
    END;

    SELECT TOP (1)
        @OldestLot = L.LotCode
    FROM dbo.WH_Inventory W
    INNER JOIN dbo.tbl_Lot L
            ON L.LotID = W.LotID
    WHERE W.ItemNo = @ItemNo
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ORDER BY COALESCE(W.LastReceivedAt, L.ProducedAt, CONVERT(datetime2, '9999-12-31')),
             W.LocationID,
             L.LotCode;

    IF @OldestLot IS NOT NULL AND UPPER(@OldestLot) <> UPPER(@Lot)
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
            @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
            @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
            CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            CONCAT(N'FIFO violation. Pick LOT ', @OldestLot, N' first.') AS MESSAGE;
        RETURN;
    END;

    SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
        @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
        @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
        CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(1 AS bit) AS IS_FIFO_SUGGESTED,
        CAST(1 AS bit) AS IS_VALID, N'LOT is ready to pick.' AS MESSAGE;
END;
GO

-- =====================================================================
--  Release / Pick LOT
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_PICK_LOT
    @PickSlipNo nvarchar(40),
    @LotNo nvarchar(50),
    @UserId nvarchar(80),
    @TerminalId nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    DECLARE @Lot nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotNo, N'')));
    DECLARE @User nvarchar(80) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
    DECLARE @Terminal nvarchar(80) = COALESCE(NULLIF(LTRIM(RTRIM(@TerminalId)), N''), N'PDA');
    DECLARE @ReleaseScheduleID int = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''));

    DECLARE @Validation table
    (
        PICK_SLIPNO nvarchar(40),
        LOTNO nvarchar(50),
        PARTNO varchar(20) NULL,
        PARTNM nvarchar(100) NULL,
        QTY decimal(18,3),
        UNIT varchar(10) NULL,
        LOCATION_NO varchar(20) NULL,
        LOCATION_NM nvarchar(100) NULL,
        ZONECD varchar(20) NULL,
        INV_STATUS varchar(20) NULL,
        PROD_DATE nvarchar(20) NULL,
        RCV_DATE nvarchar(20) NULL,
        IS_FIFO_SUGGESTED bit,
        IS_VALID bit,
        MESSAGE nvarchar(200)
    );

    INSERT INTO @Validation
    EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo = @Slip, @LotNo = @Lot;

    IF NOT EXISTS (SELECT 1 FROM @Validation WHERE IS_VALID = 1)
    BEGIN
        SELECT TOP (1) * FROM @Validation;
        RETURN;
    END;

    DECLARE
        @LotID int,
        @InventoryID int,
        @ItemNo varchar(20),
        @LocationID varchar(20),
        @Qty decimal(18,3),
        @BeforeStatus varchar(20),
        @PickedTotal decimal(18,3),
        @DemandQty decimal(18,3);

    SELECT TOP (1)
        @LotID = L.LotID,
        @InventoryID = W.InventoryID,
        @ItemNo = W.ItemNo,
        @LocationID = W.LocationID,
        @Qty = W.OnHandQty,
        @BeforeStatus = W.Status
    FROM dbo.tbl_Lot L
    INNER JOIN dbo.WH_Inventory W
            ON W.LotID = L.LotID
    WHERE L.LotCode = @Lot
      AND W.ItemNo = (SELECT TOP (1) PARTNO FROM @Validation)
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ORDER BY W.InventoryID DESC;

    IF @InventoryID IS NULL
    BEGIN
        SELECT TOP (1)
            PICK_SLIPNO, LOTNO, PARTNO, PARTNM, QTY, UNIT, LOCATION_NO, LOCATION_NM,
            ZONECD, INV_STATUS, PROD_DATE, RCV_DATE, IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            N'LOT status changed before picking. Scan again.' AS MESSAGE
        FROM @Validation;
        RETURN;
    END;

    BEGIN TRANSACTION;

    UPDATE dbo.WH_Inventory
       SET OnHandQty = 0,
           ReservedQty = 0,
           Status = 'Released',
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE InventoryID = @InventoryID;

    UPDATE dbo.tbl_Lot
       SET RemainingQty = 0,
           Status = 'Released',
           CurrentLocationID = NULL,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID;

    INSERT INTO dbo.WH_ReleasePicking
    (
        PickingNo, ReleaseScheduleID, ItemNo, LocationID, LotID, PickedQty,
        PickedAt, PickedBy, TerminalID, FifoOverride, CreatedBy, CreatedTS
    )
    VALUES
    (
        CONCAT('PICK-', FORMAT(SYSDATETIME(), 'yyMMddHHmmssfff')),
        @ReleaseScheduleID, @ItemNo, @LocationID, @LotID, @Qty,
        SYSDATETIME(), @User, @Terminal, 0, @User, SYSDATETIME()
    );

    UPDATE dbo.WH_ReleaseSchedule
       SET PickedQty = COALESCE(PickedQty, 0) + @Qty,
           Status = CASE
               WHEN COALESCE(PickedQty, 0) + @Qty >= COALESCE(DemandQty, 0) THEN 'Picked'
               ELSE 'Partial'
           END,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE ReleaseScheduleID = @ReleaseScheduleID;

    SELECT
        @PickedTotal = COALESCE(PickedQty, 0),
        @DemandQty = COALESCE(DemandQty, 0)
    FROM dbo.WH_ReleaseSchedule
    WHERE ReleaseScheduleID = @ReleaseScheduleID;

    INSERT INTO dbo.WH_TransactionHistory
    (
        TxnTime, TxnType, ItemNo, LocationID, LotID, QtyBefore, Delta, QtyAfter,
        ReasonCode, RefDocType, RefDocID, OperatorID, Note, CreatedBy, CreatedTS
    )
    VALUES
    (
        SYSDATETIME(), 'OUT', @ItemNo, @LocationID, @LotID, @Qty, -@Qty, 0,
        'RELEASE_PICK', 'PICK_SLIP', @ReleaseScheduleID, @User,
        CONCAT('PDA release pick ', CONCAT('RS-', @ReleaseScheduleID)), @User, SYSDATETIME()
    );

    COMMIT TRANSACTION;

    SELECT TOP (1)
        PICK_SLIPNO,
        LOTNO,
        PARTNO,
        PARTNM,
        QTY,
        UNIT,
        LOCATION_NO,
        LOCATION_NM,
        ZONECD,
        'Released' AS INV_STATUS,
        PROD_DATE,
        RCV_DATE,
        CAST(1 AS bit) AS IS_FIFO_SUGGESTED,
        CAST(1 AS bit) AS IS_VALID,
        N'Release pick completed.' AS MESSAGE
    FROM @Validation;
END;
GO
