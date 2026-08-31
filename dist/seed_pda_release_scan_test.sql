/*
    PDA Release scan test data
    - Pick Slip: 2026082801
    - Request: 3 boxes; physical quantity: 4 EA + 4 EA + 2 EA = 10 EA
    - FIFO LOTs: 5011LL260701000001 -> 5011LL260715000002 -> 5011LL260801000003
    - Direct outgoing LOT: 5011LL260820000010
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

DECLARE @PickSlipNo nvarchar(40) = N'2026082801';
DECLARE @ItemNo varchar(20) = '81710-PI000NNB';
DECLARE @DirectItemNo varchar(20) = '82301-PI000NNB';

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @ItemNo)
    THROW 50001, 'Required test item 81710-PI000NNB was not found in MD_Item.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @DirectItemNo)
    THROW 50002, 'Required test item 82301-PI000NNB was not found in MD_Item.', 1;

IF OBJECT_ID(N'dbo.WH_ReleasePickAllocation', N'U') IS NOT NULL
    DELETE FROM dbo.WH_ReleasePickAllocation
    WHERE PickSlipNo IN (@PickSlipNo, N'PDA-REL-TEST-01');

DELETE FROM dbo.WH_ReleaseSchedule
WHERE PickSlipNo IN (@PickSlipNo, N'PDA-REL-TEST-01');

DELETE W
FROM dbo.WH_Inventory W
INNER JOIN dbo.tbl_Lot L ON L.LotID = W.LotID
WHERE L.LotCode IN
(
    'PDA-REL-LOT-001', 'PDA-REL-LOT-002', 'PDA-REL-LOT-003',
    '5011LL260701000001', '5011LL260715000002', '5011LL260801000003',
    '5011LL260820000010'
);

DELETE FROM dbo.tbl_Lot
WHERE LotCode IN
(
    'PDA-REL-LOT-001', 'PDA-REL-LOT-002', 'PDA-REL-LOT-003',
    '5011LL260701000001', '5011LL260715000002', '5011LL260801000003',
    '5011LL260820000010'
);

INSERT INTO dbo.WH_ReleaseSchedule
(
    WoID, ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status,
    CreatedBy, PickSlipNo, ReqLocation, ReqSeqNo, ReqUserId
)
VALUES
(
    NULL, @ItemNo, 3, 0, SYSDATETIME(), 2, 'Open',
    'pda-release-test', @PickSlipNo, N'LINE-A', 1, N'PDA TEST'
);

INSERT INTO dbo.tbl_Lot
(
    LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt,
    Status, InventoryStatus, QualityFlag, CurrentLocationID, CreatedBy
)
VALUES
('5011LL260701000001', @ItemNo, 'WH', 4, 4, '2026-07-01T08:00:00', 'Received', 'RECEIVED', 'PASS', 'B0-10-A1', 'pda-release-test'),
('5011LL260715000002', @ItemNo, 'WH', 4, 4, '2026-07-15T08:00:00', 'Received', 'RECEIVED', 'PASS', 'B0-10-B1', 'pda-release-test'),
('5011LL260801000003', @ItemNo, 'WH', 2, 2, '2026-08-01T08:00:00', 'Received', 'RECEIVED', 'PASS', 'B0-09-D2', 'pda-release-test'),
('5011LL260820000010', @DirectItemNo, 'WH', 24, 24, '2026-08-20T08:00:00', 'Received', 'RECEIVED', 'PASS', 'B0-10-A1', 'pda-release-test');

INSERT INTO dbo.WH_Inventory
(
    ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt,
    Status, CreatedBy
)
SELECT
    L.ItemNo,
    CASE L.LotCode
        WHEN '5011LL260701000001' THEN 'B0-10-A1'
        WHEN '5011LL260715000002' THEN 'B0-10-B1'
        WHEN '5011LL260801000003' THEN 'B0-09-D2'
        ELSE 'B0-10-A1'
    END,
    L.LotID,
    CASE L.LotCode
        WHEN '5011LL260701000001' THEN 4
        WHEN '5011LL260715000002' THEN 4
        WHEN '5011LL260801000003' THEN 2
        ELSE 24
    END,
    0,
    CASE L.LotCode
        WHEN '5011LL260701000001' THEN '2026-07-01T08:00:00'
        WHEN '5011LL260715000002' THEN '2026-07-15T08:00:00'
        WHEN '5011LL260801000003' THEN '2026-08-01T08:00:00'
        ELSE '2026-08-20T08:00:00'
    END,
    'Received',
    'pda-release-test'
FROM dbo.tbl_Lot L
WHERE L.LotCode IN
(
    '5011LL260701000001', '5011LL260715000002', '5011LL260801000003',
    '5011LL260820000010'
);

COMMIT TRANSACTION;

EXEC dbo.WH_RELEASE_BUILD_PICK_ALLOCATIONS @PickSlipNo = @PickSlipNo, @CreatedBy = N'pda-release-test';
EXEC dbo.WH_PDA_RELEASE_SLIP_STATUS @PickSlipNo = @PickSlipNo;
EXEC dbo.WH_PDA_RELEASE_PICK_LINES @PickSlipNo = @PickSlipNo;
EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo = @PickSlipNo, @LotNo = N'5011LL260701000001';
EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo = @PickSlipNo, @LotNo = N'5011LL260715000002';
