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
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PickSlipNo') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD PickSlipNo nvarchar(40) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqLocation') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqLocation nvarchar(40) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqSeqNo') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqSeqNo int NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqUserId') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqUserId nvarchar(80) NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PrintDate') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD PrintDate datetime2 NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'CloseDate') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD CloseDate datetime2 NULL;
GO

IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'CloseUserId') IS NULL
    ALTER TABLE dbo.WH_ReleaseSchedule ADD CloseUserId nvarchar(80) NULL;
GO

UPDATE dbo.WH_ReleaseSchedule
   SET PickSlipNo = CONCAT(N'RS-', ReleaseScheduleID)
 WHERE NULLIF(PickSlipNo, N'') IS NULL;
GO

UPDATE dbo.WH_ReleaseSchedule
   SET Status = 'Closed',
       CloseDate = COALESCE(CloseDate, ModifiedTS, SYSDATETIME())
 WHERE UPPER(COALESCE(Status, N'')) IN (N'RELEASED', N'PICKED')
   AND COALESCE(PickedQty, 0) >= COALESCE(DemandQty, 0);
GO

-- One row represents one planned LOT/container (one requested box). This keeps
-- box-based Pick Slip quantities separate from the physical EA quantity in LOT.
IF OBJECT_ID(N'dbo.WH_ReleasePickAllocation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_ReleasePickAllocation
    (
        PickAllocationID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PickSlipNo nvarchar(40) NOT NULL,
        ReleaseScheduleID int NOT NULL,
        ItemNo varchar(20) NOT NULL,
        AllocationSeq int NOT NULL,
        LocationID varchar(20) NOT NULL,
        LotID int NOT NULL,
        LotNo varchar(50) NOT NULL,
        ProductionDate datetime2 NULL,
        ReceivedDate datetime2 NULL,
        AllocatedQty decimal(14,3) NOT NULL,
        AllocatedBoxQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_AllocatedBoxQty DEFAULT (1),
        PickedQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_PickedQty DEFAULT (0),
        PickedBoxQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_PickedBoxQty DEFAULT (0),
        Status varchar(20) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_Status DEFAULT ('Open'),
        CreatedBy nvarchar(80) NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_CreatedTS DEFAULT (SYSDATETIME()),
        ModifiedBy nvarchar(80) NULL,
        ModifiedTS datetime2 NULL,
        CONSTRAINT UQ_WH_ReleasePickAllocation_Lot UNIQUE (ReleaseScheduleID, LotID)
    );

    CREATE INDEX IX_WH_ReleasePickAllocation_Slip
        ON dbo.WH_ReleasePickAllocation (PickSlipNo, ReleaseScheduleID, AllocationSeq);
END;
GO

IF COL_LENGTH(N'dbo.WH_ReleasePickAllocation', N'ReceivedDate') IS NULL
    ALTER TABLE dbo.WH_ReleasePickAllocation ADD ReceivedDate datetime2 NULL;
GO

-- =====================================================================
--  LOT warehouse lifecycle status
--  Production status (tbl_Lot.Status) remains separate from warehouse state.
-- =====================================================================
IF COL_LENGTH(N'dbo.tbl_Lot', N'InventoryStatus') IS NULL
    ALTER TABLE dbo.tbl_Lot ADD InventoryStatus varchar(30) NULL;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints DC
    INNER JOIN sys.columns C
        ON C.object_id = DC.parent_object_id AND C.column_id = DC.parent_column_id
    WHERE DC.parent_object_id = OBJECT_ID(N'dbo.tbl_Lot')
      AND C.name = N'InventoryStatus'
)
    ALTER TABLE dbo.tbl_Lot
        ADD CONSTRAINT DF_tbl_Lot_InventoryStatus DEFAULT ('CREATED') FOR InventoryStatus;
GO

UPDATE L
   SET InventoryStatus =
       CASE
           WHEN UPPER(COALESCE(L.Status, '')) = 'RELEASED'
             OR COALESCE(W.OnHandQty, 0) <= 0 AND W.InventoryID IS NOT NULL THEN 'RELEASED'
           WHEN W.InventoryID IS NOT NULL AND NULLIF(W.LocationID, '') IS NOT NULL THEN 'STORED'
           WHEN W.InventoryID IS NOT NULL THEN 'RECEIVED'
           ELSE 'CREATED'
       END
FROM dbo.tbl_Lot L
OUTER APPLY
(
    SELECT TOP (1) I.InventoryID, I.OnHandQty, I.LocationID
    FROM dbo.WH_Inventory I
    WHERE I.LotID = L.LotID
    ORDER BY I.InventoryID DESC
) W
WHERE NULLIF(L.InventoryStatus, '') IS NULL;
GO

IF OBJECT_ID(N'dbo.WH_LotStatusHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_LotStatusHistory
    (
        LotStatusHistoryID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LotID int NOT NULL,
        LotNo varchar(50) NULL,
        BeforeStatus varchar(30) NULL,
        AfterStatus varchar(30) NOT NULL,
        ReasonCode varchar(30) NULL,
        ReferenceNo nvarchar(40) NULL,
        ChangedBy nvarchar(80) NULL,
        ChangedAt datetime2 NOT NULL CONSTRAINT DF_WH_LotStatusHistory_ChangedAt DEFAULT (SYSDATETIME())
    );
    CREATE INDEX IX_WH_LotStatusHistory_Lot
        ON dbo.WH_LotStatusHistory (LotID, ChangedAt DESC);
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_SET_LOT_STATUS
    @LotNo nvarchar(50),
    @Status varchar(30),
    @ReasonCode varchar(30) = NULL,
    @ReferenceNo nvarchar(40) = NULL,
    @ChangedBy nvarchar(80) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NormalizedStatus varchar(30) = UPPER(LTRIM(RTRIM(@Status)));
    IF @NormalizedStatus NOT IN
       ('CREATED','RECEIVED','STORED','RELEASED','RECEIPT_CANCELLED',
        'RELEASE_CANCELLED','RETURN_RECEIVED','DEFECTIVE','DISPOSED')
        THROW 51610, 'Unsupported LOT inventory status.', 1;

    DECLARE @LotID int, @BeforeStatus varchar(30);
    SELECT TOP (1)
        @LotID = LotID,
        @BeforeStatus = COALESCE(NULLIF(InventoryStatus, ''), 'CREATED')
    FROM dbo.tbl_Lot WITH (UPDLOCK, HOLDLOCK)
    WHERE UPPER(LotCode) = UPPER(LTRIM(RTRIM(@LotNo)));

    IF @LotID IS NULL
        THROW 51611, 'LOT was not found.', 1;

    IF @BeforeStatus = @NormalizedStatus
        RETURN;

    UPDATE dbo.tbl_Lot
       SET InventoryStatus = @NormalizedStatus,
           ModifiedBy = COALESCE(NULLIF(@ChangedBy, ''), ModifiedBy),
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID;

    INSERT dbo.WH_LotStatusHistory
        (LotID, LotNo, BeforeStatus, AfterStatus, ReasonCode, ReferenceNo, ChangedBy)
    VALUES
        (@LotID, @LotNo, @BeforeStatus, @NormalizedStatus, @ReasonCode, @ReferenceNo, @ChangedBy);
END;
GO

-- Port of MES.PKG_PDA_COM.GET_FIFO_VIEW for AMES_DEV.
-- Legacy GET_FIFO_VIEW compares WMS2020.RCV_DATE. LOTs with the same receipt
-- date are peers and may be selected in any order.
CREATE OR ALTER PROCEDURE dbo.WH_PDA_FIFO_VIEW
    @LotNo nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LotID int, @ItemNo varchar(20), @ReceivedDate datetime2;
    SELECT TOP (1)
        @LotID = L.LotID,
        @ItemNo = L.ItemNo,
        @ReceivedDate = W.LastReceivedAt
    FROM dbo.tbl_Lot L
    LEFT JOIN dbo.WH_Inventory W ON W.LotID = L.LotID
    WHERE UPPER(L.LotCode) = UPPER(LTRIM(RTRIM(@LotNo)));

    SELECT
        Older.LotID AS LOT_ID,
        Older.LotCode AS LOTNO,
        Older.ItemNo AS PARTNO,
        W.LocationID AS LOCATION_NO,
        COALESCE(W.OnHandQty, 0) AS QTY,
        W.LastReceivedAt AS RCV_DATE,
        Older.InventoryStatus AS LOT_STATUS
    FROM dbo.tbl_Lot Older
    INNER JOIN dbo.WH_Inventory W ON W.LotID = Older.LotID
    WHERE Older.ItemNo = @ItemNo
      AND Older.LotID <> @LotID
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(Older.InventoryStatus, 'STORED')) IN ('RECEIVED','STORED','RETURN_RECEIVED','RELEASE_CANCELLED')
      AND (@ReceivedDate IS NULL OR W.LastReceivedAt < @ReceivedDate)
    ORDER BY W.LastReceivedAt, W.LocationID, Older.LotCode;
END;
GO

IF COL_LENGTH(N'dbo.WH_ReleasePickAllocation', N'AllocatedBoxQty') IS NULL
    ALTER TABLE dbo.WH_ReleasePickAllocation
        ADD AllocatedBoxQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_AllocatedBoxQty_Existing DEFAULT (1);

IF COL_LENGTH(N'dbo.WH_ReleasePickAllocation', N'PickedBoxQty') IS NULL
    ALTER TABLE dbo.WH_ReleasePickAllocation
        ADD PickedBoxQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_PickedBoxQty_Existing DEFAULT (0);
GO

UPDATE dbo.WH_ReleasePickAllocation
   SET AllocatedBoxQty = 1
 WHERE COALESCE(AllocatedBoxQty, 0) = 0;
GO

-- Build the exact pick plan at Pick Slip creation.  The sequence intentionally
-- follows the legacy APG_WM20231 rule: earliest production date, then location.
CREATE OR ALTER PROCEDURE dbo.WH_RELEASE_BUILD_PICK_ALLOCATIONS
    @PickSlipNo nvarchar(40),
    @CreatedBy nvarchar(80) = N'web'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Slip nvarchar(40) = UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo, N''))));
    IF @Slip = N'' THROW 51500, 'Pick Slip No is required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.WH_ReleasePickAllocation A
        WHERE A.PickSlipNo = @Slip
          AND COALESCE(A.PickedQty, 0) > 0
    )
        THROW 51501, 'A Pick Slip with completed picks cannot be reallocated.', 1;

    -- Legacy WMS3050.REQ_BOX_QTY is a count of LOT/container labels. One
    -- available LOT therefore satisfies one requested box, regardless of EA.
    IF EXISTS
    (
        SELECT 1
        FROM dbo.WH_ReleaseSchedule RS
        OUTER APPLY
        (
            SELECT COUNT(*) AS AvailableBoxQty
            FROM dbo.WH_Inventory W
            INNER JOIN dbo.tbl_Lot T
                    ON T.LotID = W.LotID
            WHERE W.ItemNo = RS.ItemNo
              AND COALESCE(W.OnHandQty, 0) > 0
              AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
        ) Available
        WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = @Slip
          AND COALESCE(RS.DemandQty, 0) > COALESCE(Available.AvailableBoxQty, 0)
    )
        THROW 51502, 'Insufficient FIFO LOT containers for the requested box quantity.', 1;

    DELETE FROM dbo.WH_ReleasePickAllocation
    WHERE PickSlipNo = @Slip;

    ;WITH Lines AS
    (
        SELECT RS.ReleaseScheduleID, RS.ItemNo, COALESCE(RS.DemandQty, 0) AS DemandQty
        FROM dbo.WH_ReleaseSchedule RS
        WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = @Slip
    ),
    OrderedLots AS
    (
        SELECT
            L.ReleaseScheduleID,
            L.ItemNo,
            L.DemandQty,
            W.LocationID,
            W.LotID,
            T.LotCode,
            T.ProducedAt,
            W.LastReceivedAt,
            COALESCE(W.OnHandQty, 0) AS OnHandQty,
            ROW_NUMBER() OVER
            (
                PARTITION BY L.ReleaseScheduleID
                ORDER BY COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31')),
                         W.LocationID,
                         T.LotCode
            ) AS AllocationSeq
        FROM Lines L
        INNER JOIN dbo.WH_Inventory W
                ON W.ItemNo = L.ItemNo
        INNER JOIN dbo.tbl_Lot T
                ON T.LotID = W.LotID
        WHERE COALESCE(W.OnHandQty, 0) > 0
          AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ),
    Planned AS
    (
        SELECT
            ReleaseScheduleID,
            ItemNo,
            DemandQty,
            LocationID,
            LotID,
            LotCode,
            ProducedAt,
            LastReceivedAt,
            OnHandQty AS AllocatedQty,
            CAST(1 AS decimal(14,3)) AS AllocatedBoxQty,
            AllocationSeq
        FROM OrderedLots
    )
    INSERT INTO dbo.WH_ReleasePickAllocation
    (
        PickSlipNo, ReleaseScheduleID, ItemNo, AllocationSeq, LocationID, LotID, LotNo,
        ProductionDate, ReceivedDate, AllocatedQty, AllocatedBoxQty, PickedQty, PickedBoxQty, Status, CreatedBy
    )
    SELECT
        @Slip, ReleaseScheduleID, ItemNo, AllocationSeq, LocationID, LotID, LotCode,
        ProducedAt, LastReceivedAt, AllocatedQty, AllocatedBoxQty, 0, 0, 'Open', @CreatedBy
    FROM Planned
    WHERE AllocationSeq <= DemandQty;
END;
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
    DECLARE @ReleaseScheduleID int;

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

    SELECT TOP (1)
        @ReleaseScheduleID = RS.ReleaseScheduleID
    FROM dbo.WH_ReleaseSchedule RS
    WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = @Slip
       OR UPPER(CONCAT(N'RS-', RS.ReleaseScheduleID)) = @Slip
       OR RS.ReleaseScheduleID = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''))
    ORDER BY RS.ReleaseScheduleID;

    IF @ReleaseScheduleID IS NULL
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
        COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) AS PICK_SLIPNO,
        CAST(1 AS bit) AS EXISTS_FLAG,
        CASE WHEN UPPER(COALESCE(RS.Status, N'')) IN (N'CLOSED', N'RELEASED', N'PICKED', N'CANCELED', N'CANCELLED')
                  OR RS.CloseDate IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IS_CLOSED,
        (
            SELECT COUNT(*)
            FROM dbo.WH_ReleaseSchedule L
            WHERE COALESCE(NULLIF(L.PickSlipNo, N''), CONCAT(N'RS-', L.ReleaseScheduleID))
                = COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))
        ) AS LINE_COUNT,
        RS.ReqLocation AS REQ_LOCATION,
        CONVERT(date, RS.RequiredAt) AS REQ_DATE,
        RS.CloseDate AS CLOSE_DATE,
        CASE
            WHEN UPPER(COALESCE(RS.Status, N'')) IN (N'CLOSED', N'RELEASED', N'PICKED', N'CANCELED', N'CANCELLED')
                 OR RS.CloseDate IS NOT NULL THEN N'This Pick Slip has already been processed.'
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
    DECLARE @PickSlipKey nvarchar(40);

    SELECT TOP (1)
        @PickSlipKey = COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))
    FROM dbo.WH_ReleaseSchedule RS
    WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = @Slip
       OR UPPER(CONCAT(N'RS-', RS.ReleaseScheduleID)) = @Slip
       OR RS.ReleaseScheduleID = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''))
    ORDER BY RS.ReleaseScheduleID;

    IF @PickSlipKey IS NULL
        RETURN;

    ;WITH RequiredParts AS
    (
        SELECT
            RS.ReleaseScheduleID,
            COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) AS PICK_SLIPNO,
            RS.ItemNo,
            COALESCE(I.ItemName, RS.ItemNo) AS ItemName,
            COALESCE(RS.DemandQty, 0) AS DemandQty,
            COALESCE(RS.PickedQty, 0) AS PickedQty,
            COALESCE(NULLIF(RS.ReqUserId, N''), RS.CreatedBy) AS RequestUserId
        FROM dbo.WH_ReleaseSchedule RS
        LEFT JOIN dbo.MD_Item I
               ON I.ItemNo = RS.ItemNo
        WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
    ),
    AllocationLocations AS
    (
        SELECT
            A.ReleaseScheduleID,
            A.LocationID,
            ROW_NUMBER() OVER
            (
                PARTITION BY A.ReleaseScheduleID
                ORDER BY MIN(COALESCE(A.ReceivedDate, A.ProductionDate, CONVERT(datetime2, '9999-12-31'))),
                         A.LocationID
            ) AS RN
        FROM dbo.WH_ReleasePickAllocation A
        INNER JOIN RequiredParts R
                ON R.ReleaseScheduleID = A.ReleaseScheduleID
        GROUP BY A.ReleaseScheduleID, A.LocationID
    ),
    PickedPhysical AS
    (
        SELECT P.ReleaseScheduleID, SUM(COALESCE(P.PickedQty, 0)) AS PickedQty
        FROM dbo.WH_ReleasePicking P
        INNER JOIN RequiredParts R
                ON R.ReleaseScheduleID = P.ReleaseScheduleID
        GROUP BY P.ReleaseScheduleID
    ),
    Locations AS
    (
        SELECT
            ReleaseScheduleID,
            MAX(CASE WHEN RN = 1 THEN LocationID END) AS LOC_01,
            MAX(CASE WHEN RN = 2 THEN LocationID END) AS LOC_02,
            MAX(CASE WHEN RN = 3 THEN LocationID END) AS LOC_03
        FROM AllocationLocations
        WHERE RN <= 3
        GROUP BY ReleaseScheduleID
    )
    SELECT
        R.PICK_SLIPNO,
        R.ItemNo AS PARTNO,
        R.ItemName AS PARTNM,
        R.DemandQty AS REQ_BOX_QTY,
        R.PickedQty AS PICKED_BOX_QTY,
        COALESCE(P.PickedQty, 0) AS PICKED_QTY,
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
    LEFT JOIN PickedPhysical P
           ON P.ReleaseScheduleID = R.ReleaseScheduleID
    LEFT JOIN Locations L
           ON L.ReleaseScheduleID = R.ReleaseScheduleID
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
    DECLARE @ScanText nvarchar(50) = @Lot;
    DECLARE @PickSlipKey nvarchar(40);
    DECLARE @PickSlipOut nvarchar(40) = @Slip;

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
        @OldestLot varchar(40),
        @ExpectedLotID int,
        @ExpectedLot varchar(50),
        @ExpectedLocation varchar(20),
        @ExpectedQty decimal(14,3),
        @ExpectedReceivedDate datetime2,
        @HasAllocation bit = 0;

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
        @PickSlipKey = COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)),
        @PickSlipOut = COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))
    FROM dbo.WH_ReleaseSchedule RS
    WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID))) = @Slip
       OR UPPER(CONCAT(N'RS-', RS.ReleaseScheduleID)) = @Slip
       OR RS.ReleaseScheduleID = TRY_CONVERT(int, REPLACE(@Slip, N'RS-', N''))
    ORDER BY RS.ReleaseScheduleID;

    IF @PickSlipKey IS NULL
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'Pick Slip was not found.' AS MESSAGE;
        RETURN;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.WH_ReleaseSchedule RS
        WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
          AND UPPER(COALESCE(RS.Status, N'OPEN')) NOT IN (N'CLOSED', N'RELEASED', N'PICKED', N'CANCELED', N'CANCELLED')
    )
    BEGIN
        SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, NULL AS PARTNO, NULL AS PARTNM,
            CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
            NULL AS ZONECD, N'Closed' AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
            CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
            N'This Pick Slip has already been processed.' AS MESSAGE;
        RETURN;
    END;

    -- A material label can carry either a LOT No or a requested Part No.
    -- Resolve a Part No scan to its FIFO-eligible LOT before standard validation.
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode = @Lot)
       AND EXISTS
       (
           SELECT 1
           FROM dbo.WH_ReleaseSchedule RS
           WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
             AND UPPER(RS.ItemNo) = UPPER(@ScanText)
       )
    BEGIN
        SET @Lot = NULL;

        SELECT TOP (1) @Lot = A.LotNo
        FROM dbo.WH_ReleasePickAllocation A
        INNER JOIN dbo.WH_ReleaseSchedule RS
                ON RS.ReleaseScheduleID = A.ReleaseScheduleID
        WHERE A.PickSlipNo = @PickSlipKey
          AND UPPER(A.ItemNo) = UPPER(@ScanText)
           AND COALESCE(A.PickedBoxQty, 0) < A.AllocatedBoxQty
          AND UPPER(COALESCE(A.Status, N'OPEN')) <> N'CANCELED'
        ORDER BY A.AllocationSeq;

        IF @Lot IS NULL
        BEGIN
            SELECT TOP (1) @Lot = L.LotCode
            FROM dbo.WH_Inventory W
            INNER JOIN dbo.tbl_Lot L
                    ON L.LotID = W.LotID
            WHERE W.ItemNo = @ScanText
              AND COALESCE(W.OnHandQty, 0) > 0
              AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
            ORDER BY COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31')),
                     W.LocationID,
                     L.LotCode;
        END;

        IF @Lot IS NULL
        BEGIN
            SELECT @PickSlipOut AS PICK_SLIPNO, @ScanText AS LOTNO, @ScanText AS PARTNO, NULL AS PARTNM,
                CAST(0 AS decimal(18,3)) AS QTY, NULL AS UNIT, NULL AS LOCATION_NO, NULL AS LOCATION_NM,
                NULL AS ZONECD, NULL AS INV_STATUS, NULL AS PROD_DATE, NULL AS RCV_DATE,
                CAST(0 AS bit) AS IS_FIFO_SUGGESTED, CAST(0 AS bit) AS IS_VALID,
                N'No available LOT was found for this Part No.' AS MESSAGE;
            RETURN;
        END;
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

    SELECT TOP (1)
        @RequestedItemNo = RS.ItemNo,
        @DemandQty = COALESCE(RS.DemandQty, 0),
        @PickedQty = COALESCE(RS.PickedQty, 0),
        @SlipStatus = RS.Status
    FROM dbo.WH_ReleaseSchedule RS
    WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
      AND RS.ItemNo = @ItemNo
    ORDER BY RS.ReleaseScheduleID;

    IF @RequestedItemNo IS NULL OR @ItemNo <> @RequestedItemNo
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

    IF EXISTS
    (
        SELECT 1
        FROM dbo.WH_ReleasePickAllocation A
        WHERE A.PickSlipNo = @PickSlipKey
          AND A.ReleaseScheduleID =
              (SELECT TOP (1) RS.ReleaseScheduleID
               FROM dbo.WH_ReleaseSchedule RS
               WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
                 AND RS.ItemNo = @ItemNo
               ORDER BY RS.ReleaseScheduleID)
    )
    BEGIN
        SET @HasAllocation = 1;

        SELECT TOP (1)
            @ExpectedLotID = A.LotID,
            @ExpectedLot = A.LotNo,
            @ExpectedLocation = A.LocationID,
            @ExpectedQty = A.AllocatedQty - COALESCE(A.PickedQty, 0),
            @ExpectedReceivedDate = COALESCE(A.ReceivedDate, A.ProductionDate)
        FROM dbo.WH_ReleasePickAllocation A
        WHERE A.PickSlipNo = @PickSlipKey
          AND A.ItemNo = @ItemNo
           AND COALESCE(A.PickedBoxQty, 0) < A.AllocatedBoxQty
          AND UPPER(COALESCE(A.Status, N'OPEN')) <> N'CANCELED'
        ORDER BY A.AllocationSeq;

        IF @ExpectedLotID IS NULL
           OR (@LotID <> @ExpectedLotID
               AND COALESCE(@ReceivedAt, CONVERT(datetime2,'9999-12-31'))
                   > COALESCE(@ExpectedReceivedDate, CONVERT(datetime2,'9999-12-31')))
        BEGIN
            SELECT @PickSlipOut AS PICK_SLIPNO, @Lot AS LOTNO, @ItemNo AS PARTNO, @ItemName AS PARTNM,
                @Qty AS QTY, @Unit AS UNIT, @LocationID AS LOCATION_NO, @LocationName AS LOCATION_NM,
                @ZoneCode AS ZONECD, @InventoryStatus AS INV_STATUS, CONVERT(nvarchar(20), @ProducedAt, 23) AS PROD_DATE,
                CONVERT(nvarchar(20), @ReceivedAt, 23) AS RCV_DATE, CAST(0 AS bit) AS IS_FIFO_SUGGESTED,
                CAST(0 AS bit) AS IS_VALID,
                CONCAT(N'FIFO blocked. Pick the next box from location ', @ExpectedLocation,
                       N' (LOT ', @ExpectedLot, N', ', CONVERT(nvarchar(30), @ExpectedQty), N' EA) first.') AS MESSAGE;
            RETURN;
        END;

        -- Return the planned quantity for this scan. A LOT barcode is not always one EA.
        SET @Qty = @ExpectedQty;
    END;

    SELECT TOP (1)
        @OldestLot = L.LotCode
    FROM dbo.WH_Inventory W
    INNER JOIN dbo.tbl_Lot L
            ON L.LotID = W.LotID
    WHERE @HasAllocation = 0
      AND W.ItemNo = @ItemNo
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(W.Status, N'RECEIVED')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
    ORDER BY COALESCE(W.LastReceivedAt, CONVERT(datetime2, '9999-12-31')),
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
    DECLARE @ReleaseScheduleID int;
    DECLARE @PickSlipKey nvarchar(40);

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
        @InventoryQty decimal(18,3),
        @ResolvedLot nvarchar(50),
        @BeforeStatus varchar(20),
        @PickedTotal decimal(18,3),
        @DemandQty decimal(18,3);

    SELECT TOP (1)
        @ResolvedLot = LOTNO,
        @Qty = QTY
    FROM @Validation
    WHERE IS_VALID = 1;

    SELECT TOP (1)
        @LotID = L.LotID,
        @InventoryID = W.InventoryID,
        @ItemNo = W.ItemNo,
        @LocationID = W.LocationID,
        @InventoryQty = W.OnHandQty,
        @BeforeStatus = W.Status
    FROM dbo.tbl_Lot L
    INNER JOIN dbo.WH_Inventory W
            ON W.LotID = L.LotID
    WHERE L.LotCode = @ResolvedLot
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

    SELECT TOP (1)
        @PickSlipKey = V.PICK_SLIPNO
    FROM @Validation V
    WHERE V.IS_VALID = 1;

    SELECT TOP (1)
        @ReleaseScheduleID = RS.ReleaseScheduleID
    FROM dbo.WH_ReleaseSchedule RS
    WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipKey
      AND RS.ItemNo = @ItemNo
    ORDER BY RS.ReleaseScheduleID;

    IF @ReleaseScheduleID IS NULL
    BEGIN
        SELECT TOP (1)
            PICK_SLIPNO, LOTNO, PARTNO, PARTNM, QTY, UNIT, LOCATION_NO, LOCATION_NM,
            ZONECD, INV_STATUS, PROD_DATE, RCV_DATE, IS_FIFO_SUGGESTED,
            CAST(0 AS bit) AS IS_VALID,
            N'Pick Slip line was not found for this LOT.' AS MESSAGE
        FROM @Validation;
        RETURN;
    END;

    BEGIN TRANSACTION;

    UPDATE dbo.WH_Inventory
       SET OnHandQty = OnHandQty - @Qty,
           ReservedQty = 0,
           Status = CASE WHEN OnHandQty <= @Qty THEN 'Released' ELSE 'Received' END,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE InventoryID = @InventoryID;

    UPDATE dbo.tbl_Lot
       SET RemainingQty = CASE WHEN COALESCE(RemainingQty, 0) <= @Qty THEN 0 ELSE RemainingQty - @Qty END,
           Status = CASE WHEN COALESCE(RemainingQty, 0) <= @Qty THEN 'Released' ELSE 'Received' END,
           CurrentLocationID = CASE WHEN COALESCE(RemainingQty, 0) <= @Qty THEN NULL ELSE CurrentLocationID END,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE LotID = @LotID;

    UPDATE dbo.WH_ReleasePickAllocation
       SET PickedQty = COALESCE(PickedQty, 0) + @Qty,
            PickedBoxQty = COALESCE(PickedBoxQty, 0) + 1,
            Status = CASE WHEN COALESCE(PickedBoxQty, 0) + 1 >= AllocatedBoxQty THEN 'Picked' ELSE 'Partial' END,
           ModifiedBy = @User,
           ModifiedTS = SYSDATETIME()
     WHERE PickSlipNo = @PickSlipKey
       AND ReleaseScheduleID = @ReleaseScheduleID
       AND LotID = @LotID;

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
       SET PickedQty = COALESCE(PickedQty, 0) + 1,
            Status = CASE
                WHEN COALESCE(PickedQty, 0) + 1 >= COALESCE(DemandQty, 0) THEN 'Picked'
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

    INSERT INTO dbo.WH_InventoryTransaction
    (
        TransactionTime, TransactionType, ItemNo, LocationID, LotID, QtyBefore, QtyChange, QtyAfter,
        ReasonCode, RefDocType, RefDocID, OperatorID, Note, CreatedBy, CreatedTS
    )
    VALUES
    (
        SYSDATETIME(), 'OUT', @ItemNo, @LocationID, @LotID, @InventoryQty, -@Qty, @InventoryQty - @Qty,
        'RELEASE_PICK', 'PICK_SLIP', @ReleaseScheduleID, @User,
        CONCAT('PDA release pick ', @PickSlipKey), @User, SYSDATETIME()
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
