/* Idempotent FIFO Release test data for the PDA WH-003 screen. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

DECLARE @PickSlipNo nvarchar(40) = N'PDA-FIFO-TEST-01';
DECLARE @SeedUser nvarchar(80) = N'pda-fifo-seed';

DECLARE @Lots TABLE
(
    LotCode varchar(50) PRIMARY KEY,
    ItemNo varchar(20) NOT NULL,
    LocationID varchar(20) NOT NULL,
    ProducedAt datetime2 NOT NULL,
    ReceivedAt datetime2 NOT NULL
);

INSERT @Lots (LotCode, ItemNo, LocationID, ProducedAt, ReceivedAt)
VALUES
('PDA-FIFO-81710-NNB-01','81710-PI000NNB','B0-08-A1','2020-01-01T07:00:00','2020-01-01T08:00:00'),
('PDA-FIFO-81710-NNB-02','81710-PI000NNB','B0-08-B1','2020-01-02T07:00:00','2020-01-02T08:00:00'),
('PDA-FIFO-81710-NNB-03','81710-PI000NNB','B0-08-C1','2020-01-03T07:00:00','2020-01-03T08:00:00'),
('PDA-FIFO-81710-NNB-04','81710-PI000NNB','B0-08-D1','2020-01-04T07:00:00','2020-01-04T08:00:00'),
('PDA-FIFO-81710-YGN-01','81710-PI000YGN','B0-08-A1','2020-02-01T07:00:00','2020-02-01T08:00:00'),
('PDA-FIFO-81710-YGN-02','81710-PI000YGN','B0-08-B1','2020-02-02T07:00:00','2020-02-02T08:00:00'),
('PDA-FIFO-81710-YGN-03','81710-PI000YGN','B0-08-C1','2020-02-03T07:00:00','2020-02-03T08:00:00'),
('PDA-FIFO-81710-YGN-04','81710-PI000YGN','B0-08-D1','2020-02-04T07:00:00','2020-02-04T08:00:00'),
('PDA-FIFO-82301-NNB-01','82301-PI000NNB','B0-08-A1','2020-03-01T07:00:00','2020-03-01T08:00:00'),
('PDA-FIFO-82301-NNB-02','82301-PI000NNB','B0-08-B1','2020-03-02T07:00:00','2020-03-02T08:00:00'),
('PDA-FIFO-82301-NNB-03','82301-PI000NNB','B0-08-C1','2020-03-03T07:00:00','2020-03-03T08:00:00'),
('PDA-FIFO-82301-NNB-04','82301-PI000NNB','B0-08-D1','2020-03-04T07:00:00','2020-03-04T08:00:00');

IF EXISTS
(
    SELECT 1 FROM @Lots S
    WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Item I WHERE I.ItemNo = S.ItemNo)
)
    THROW 51700, 'One or more FIFO test items do not exist in MD_Item.', 1;

DELETE T
FROM dbo.WH_InventoryTransaction T
WHERE T.RefDocType = 'PICK_SLIP'
  AND T.RefDocID IN
      (SELECT ReleaseScheduleID FROM dbo.WH_ReleaseSchedule WHERE PickSlipNo = @PickSlipNo);

DELETE P
FROM dbo.WH_ReleasePicking P
WHERE P.ReleaseScheduleID IN
      (SELECT ReleaseScheduleID FROM dbo.WH_ReleaseSchedule WHERE PickSlipNo = @PickSlipNo);

DELETE FROM dbo.WH_ReleasePickAllocation WHERE PickSlipNo = @PickSlipNo;
DELETE FROM dbo.WH_ReleaseSchedule WHERE PickSlipNo = @PickSlipNo;

INSERT dbo.tbl_Lot
    (LotCode,ItemNo,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,InventoryStatus,
     QualityFlag,CurrentLocationID,CreatedBy,CreatedTS)
SELECT S.LotCode,S.ItemNo,'WH',10,10,S.ProducedAt,'Received','STORED','PASS',S.LocationID,@SeedUser,SYSDATETIME()
FROM @Lots S
WHERE NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot L WHERE L.LotCode=S.LotCode);

UPDATE L
   SET ItemNo=S.ItemNo, BatchSize=10, RemainingQty=10, ProducedAt=S.ProducedAt,
       Status='Received', InventoryStatus='STORED', QualityFlag='PASS',
       CurrentLocationID=S.LocationID, ModifiedBy=@SeedUser, ModifiedTS=SYSDATETIME()
FROM dbo.tbl_Lot L
INNER JOIN @Lots S ON S.LotCode=L.LotCode;

UPDATE I
   SET ItemNo=S.ItemNo, LocationID=S.LocationID, OnHandQty=10, ReservedQty=0,
       LastReceivedAt=S.ReceivedAt, Status='Received', ModifiedBy=@SeedUser, ModifiedTS=SYSDATETIME()
FROM dbo.WH_Inventory I
INNER JOIN dbo.tbl_Lot L ON L.LotID=I.LotID
INNER JOIN @Lots S ON S.LotCode=L.LotCode;

INSERT dbo.WH_Inventory
    (ItemNo,LocationID,LotID,OnHandQty,ReservedQty,LastReceivedAt,Status,CreatedBy,CreatedTS)
SELECT S.ItemNo,S.LocationID,L.LotID,10,0,S.ReceivedAt,'Received',@SeedUser,SYSDATETIME()
FROM @Lots S
INNER JOIN dbo.tbl_Lot L ON L.LotCode=S.LotCode
WHERE NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory I WHERE I.LotID=L.LotID);

INSERT dbo.WH_ReleaseSchedule
    (WoID,ItemNo,DemandQty,PickedQty,RequiredAt,Priority,Status,CreatedBy,CreatedTS,
     PickSlipNo,ReqLocation,ReqSeqNo,ReqUserId)
VALUES
(NULL,'81710-PI000NNB',2,0,SYSDATETIME(),2,'Open',@SeedUser,SYSDATETIME(),@PickSlipNo,N'B0-09-A2',1,N'PDA TEST'),
(NULL,'81710-PI000YGN',1,0,SYSDATETIME(),2,'Open',@SeedUser,SYSDATETIME(),@PickSlipNo,N'B0-09-A2',2,N'PDA TEST'),
(NULL,'82301-PI000NNB',3,0,SYSDATETIME(),2,'Open',@SeedUser,SYSDATETIME(),@PickSlipNo,N'B0-09-A2',3,N'PDA TEST');

COMMIT TRANSACTION;

EXEC dbo.WH_RELEASE_BUILD_PICK_ALLOCATIONS @PickSlipNo=@PickSlipNo, @CreatedBy=@SeedUser;
EXEC dbo.WH_PDA_RELEASE_SLIP_STATUS @PickSlipNo=@PickSlipNo;
EXEC dbo.WH_PDA_RELEASE_PICK_LINES @PickSlipNo=@PickSlipNo;
