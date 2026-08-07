/*
    PDA Release scan test data
    - Pick Slip: PDA-REL-TEST-01
    - Request: 3 boxes; physical quantity: 4 EA + 4 EA + 2 EA = 10 EA
    - FIFO allocation: PDA-REL-LOT-001 (4 EA) -> 002 (4 EA) -> 003 (2 EA)
    - Valid Part scan: 81710-PI000YGN resolves to the next planned LOT
    - FIFO violation before the first pick: PDA-REL-LOT-002 or PDA-REL-LOT-003
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

DECLARE @PickSlipNo nvarchar(40) = N'PDA-REL-TEST-01';
DECLARE @ItemNo varchar(20) = '81710-PI000YGN';

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @ItemNo)
    THROW 50001, 'Required test item 81710-PI000YGN was not found in MD_Item.', 1;

DELETE W
FROM dbo.WH_Inventory W
INNER JOIN dbo.tbl_Lot L ON L.LotID = W.LotID
WHERE L.LotCode IN ('PDA-REL-LOT-001', 'PDA-REL-LOT-002', 'PDA-REL-LOT-003');

DELETE FROM dbo.tbl_Lot
WHERE LotCode IN ('PDA-REL-LOT-001', 'PDA-REL-LOT-002', 'PDA-REL-LOT-003');

IF OBJECT_ID(N'dbo.WH_ReleasePickAllocation', N'U') IS NOT NULL
    DELETE FROM dbo.WH_ReleasePickAllocation
    WHERE PickSlipNo = @PickSlipNo;

DELETE FROM dbo.WH_ReleaseSchedule
WHERE PickSlipNo = @PickSlipNo;

INSERT INTO dbo.WH_ReleaseSchedule
(
    WoID, ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status,
    CreatedBy, PickSlipNo, ReqLocation, ReqSeqNo, ReqUserId
)
VALUES
(
    NULL, @ItemNo, 3, 0, SYSDATETIME(), 2, 'Open',
    'pda-release-test', @PickSlipNo, N'B0-08-A1', 1, N'PDA TEST'
);

INSERT INTO dbo.tbl_Lot
(
    LotCode, ItemNo, ProcessCode, BatchSize, RemainingQty, ProducedAt,
    Status, QualityFlag, CurrentLocationID, CreatedBy
)
VALUES
('PDA-REL-LOT-001', @ItemNo, 'WH', 4, 4, '2026-08-01T08:00:00', 'Received', 'PASS', 'B0-08-A1', 'pda-release-test'),
('PDA-REL-LOT-002', @ItemNo, 'WH', 4, 4, '2026-08-02T08:00:00', 'Received', 'PASS', 'B0-08-B1', 'pda-release-test'),
('PDA-REL-LOT-003', @ItemNo, 'WH', 2, 2, '2026-08-03T08:00:00', 'Received', 'PASS', 'B0-08-C1', 'pda-release-test');

INSERT INTO dbo.WH_Inventory
(
    ItemNo, LocationID, LotID, OnHandQty, ReservedQty, LastReceivedAt,
    Status, CreatedBy
)
SELECT
    L.ItemNo,
    CASE L.LotCode
        WHEN 'PDA-REL-LOT-001' THEN 'B0-08-A1'
        WHEN 'PDA-REL-LOT-002' THEN 'B0-08-B1'
        ELSE 'B0-08-C1'
    END,
    L.LotID,
    CASE L.LotCode WHEN 'PDA-REL-LOT-001' THEN 4 WHEN 'PDA-REL-LOT-002' THEN 4 ELSE 2 END,
    0,
    CASE L.LotCode
        WHEN 'PDA-REL-LOT-001' THEN '2026-08-01T08:00:00'
        WHEN 'PDA-REL-LOT-002' THEN '2026-08-02T08:00:00'
        ELSE '2026-08-03T08:00:00'
    END,
    'Received',
    'pda-release-test'
FROM dbo.tbl_Lot L
WHERE L.LotCode IN ('PDA-REL-LOT-001', 'PDA-REL-LOT-002', 'PDA-REL-LOT-003');

COMMIT TRANSACTION;

EXEC dbo.WH_RELEASE_BUILD_PICK_ALLOCATIONS @PickSlipNo = @PickSlipNo, @CreatedBy = N'pda-release-test';
EXEC dbo.WH_PDA_RELEASE_SLIP_STATUS @PickSlipNo = @PickSlipNo;
EXEC dbo.WH_PDA_RELEASE_PICK_LINES @PickSlipNo = @PickSlipNo;
EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo = @PickSlipNo, @LotNo = @ItemNo;
EXEC dbo.WH_PDA_RELEASE_SCAN_LOT @PickSlipNo = @PickSlipNo, @LotNo = N'PDA-REL-LOT-002';
