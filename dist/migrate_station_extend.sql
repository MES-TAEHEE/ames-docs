-- ============================================================================
-- 워크스테이션(StationCode) 확장 — LOT·불량·설비·PLC 신호 (11-18). 앞선 migrate_result_station.sql(실적)에 이어서.
--   · tbl_Lot.StationCode        : LOT 을 만든(원천 LOT 발행) 스테이션 — 발행 사번의 열린 POP 세션 스테이션, 없으면 확정(스캔) 때 세션 스테이션으로 채움.
--   · PR_DefectDetail.StationCode: 불량을 등록한 스테이션 — 그 실적(역분개 실적)의 스테이션, 없으면 LOT 의 스테이션.
--   · MD_Equipment.StationCode   : 설비가 속한 스테이션 — PLC 신호가 들어오면 설비 → 스테이션으로 귀속한다(라인에 스테이션이 하나뿐이면 아래에서 자동으로 채움).
--   · PP_EquipSignal·PR_EquipStatusLog·PR_InjCondLog.StationCode: PLC 연결 전이라 지금은 라인에 스테이션이 하나뿐일 때만 채워진다(FN_StationOfLine).
--     PLC 가 붙으면 설비 마스터의 StationCode 만 맞추면 그대로 동작한다(코드 변경 없음).
--   · 재실행 안전. 마이그레이션 -> 신 바이너리 순서. 끝에 migrate_audit_columns_last.sql 로 컬럼 순서 정리.
--   · 적용: sqlcmd -f 65001 -I -b -i dist\migrate_station_extend.sql
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH(N'dbo.tbl_Lot', N'StationCode') IS NULL
    ALTER TABLE dbo.tbl_Lot ADD StationCode varchar(20) COLLATE DATABASE_DEFAULT NULL;
GO
IF COL_LENGTH(N'dbo.PR_DefectDetail', N'StationCode') IS NULL
    ALTER TABLE dbo.PR_DefectDetail ADD StationCode varchar(20) COLLATE DATABASE_DEFAULT NULL;
GO
IF COL_LENGTH(N'dbo.MD_Equipment', N'StationCode') IS NULL
    ALTER TABLE dbo.MD_Equipment ADD StationCode varchar(20) COLLATE DATABASE_DEFAULT NULL;
GO
IF COL_LENGTH(N'dbo.PP_EquipSignal', N'StationCode') IS NULL
    ALTER TABLE dbo.PP_EquipSignal ADD StationCode varchar(20) COLLATE DATABASE_DEFAULT NULL;
GO
IF COL_LENGTH(N'dbo.PR_EquipStatusLog', N'StationCode') IS NULL
    ALTER TABLE dbo.PR_EquipStatusLog ADD StationCode varchar(20) COLLATE DATABASE_DEFAULT NULL;
GO
IF COL_LENGTH(N'dbo.PR_InjCondLog', N'StationCode') IS NULL
    ALTER TABLE dbo.PR_InjCondLog ADD StationCode varchar(20) COLLATE DATABASE_DEFAULT NULL;
GO
CREATE OR ALTER FUNCTION dbo.FN_POP_OpenStation(@Op VARCHAR(450), @Line VARCHAR(20))
RETURNS VARCHAR(20)
AS
BEGIN
    -- 사번(@Op)이 그 라인에서 열어 둔(로그아웃·만료 전) POP 세션의 스테이션(세션 TerminalID) — MD_Station 에 있는 코드만. 없으면 NULL.
    RETURN (SELECT TOP 1 s.TerminalID
            FROM   dbo.PR_PopSession s
            JOIN   dbo.MD_Station st ON st.StationCode = s.TerminalID
            WHERE  s.OperatorID = @Op AND s.LineID = @Line AND s.LoggedOutAt IS NULL AND s.ExpiresAt > SYSDATETIME()
            ORDER  BY s.SessionID DESC);
END;
GO
CREATE OR ALTER FUNCTION dbo.FN_StationOfLine(@Line VARCHAR(20))
RETURNS VARCHAR(20)
AS
BEGIN
    -- 라인에 활성 스테이션이 정확히 하나일 때 그 코드, 둘 이상이거나 없으면 NULL(PLC 신호처럼 스테이션을 모르는 라인 단위 기록용).
    RETURN (SELECT MIN(st.StationCode) FROM dbo.MD_Station st
            WHERE st.LineID = @Line AND ISNULL(st.Status, 'ACTIVE') <> 'INACTIVE'
            HAVING COUNT(*) = 1);
END;
GO
-- 기존 데이터 채우기(실적의 스테이션 기준)
EXEC(N'UPDATE l SET StationCode = (SELECT TOP 1 p.StationCode FROM dbo.PR_ProductionResult p WHERE p.LotID = l.LotID AND p.StationCode IS NOT NULL ORDER BY p.ResultID)
       FROM dbo.tbl_Lot l WHERE l.StationCode IS NULL;');
EXEC(N'UPDATE d SET StationCode = COALESCE((SELECT p.StationCode FROM dbo.PR_ProductionResult p WHERE p.ResultID = d.ResultID), (SELECT l.StationCode FROM dbo.tbl_Lot l WHERE l.LotID = d.LotID))
       FROM dbo.PR_DefectDetail d WHERE d.StationCode IS NULL;');
GO
EXEC(N'UPDATE e SET StationCode = dbo.FN_StationOfLine(e.LineID) FROM dbo.MD_Equipment e WHERE e.StationCode IS NULL AND e.LineID IS NOT NULL;');
EXEC(N'UPDATE g SET StationCode = dbo.FN_StationOfLine(g.LineId) FROM dbo.PP_EquipSignal g WHERE g.StationCode IS NULL;');
EXEC(N'UPDATE g SET StationCode = COALESCE((SELECT e.StationCode FROM dbo.MD_Equipment e WHERE e.EquipID = g.EquipID), dbo.FN_StationOfLine(g.LineID)) FROM dbo.PR_EquipStatusLog g WHERE g.StationCode IS NULL;');
EXEC(N'UPDATE g SET StationCode = dbo.FN_StationOfLine(g.LineID) FROM dbo.PR_InjCondLog g WHERE g.StationCode IS NULL;');
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_InsertRawLot_InsLot
    @LotCode VARCHAR(40),
    @ItemNo VARCHAR(20),
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @Parent INT,
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty, ParentLotID,
         ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS, StationCode)
    OUTPUT INSERTED.LotID, INSERTED.CreatedTS
    VALUES
        (@LotCode, @ItemNo, NULL, @LineID, @Proc, 1, 1, @Parent,
         SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME(), dbo.FN_POP_OpenStation(@By, @LineID));
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_InsertRawLot_InsLot
    @LotCode VARCHAR(40),
    @ItemNo VARCHAR(20),
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty, ParentLotID,
         ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS, StationCode)
    OUTPUT INSERTED.LotID, INSERTED.CreatedTS
    VALUES
        (@LotCode, @ItemNo, NULL, @LineID, @Proc, 1, 1, NULL,
         SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME(), dbo.FN_POP_OpenStation(@By, @LineID));
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_CreateManualRawLots_InsLot
    @LotCode VARCHAR(40),
    @ItemNo VARCHAR(20),
    @LineID VARCHAR(20),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.tbl_Lot
        (LotCode, ItemNo, WoID, LineID, ProcessCode, BatchSize, RemainingQty,
         ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS, StationCode)
    OUTPUT INSERTED.LotID, INSERTED.CreatedTS
    VALUES
        (@LotCode, @ItemNo, NULL, @LineID, 'INJ', 1, 1,
         SYSDATETIME(), 'RAW', 'PENDING', @By, SYSDATETIME(), dbo.FN_POP_OpenStation(@By, @LineID));
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ConfirmByLotCode_UpdLot
    @WoID INT,
    @Op NVARCHAR(450),
    @LotID INT,
    @Sess INT
AS
BEGIN
    UPDATE dbo.tbl_Lot
    SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME(),
           StationCode = COALESCE(StationCode, (SELECT TOP 1 s.TerminalID FROM dbo.PR_PopSession s JOIN dbo.MD_Station st ON st.StationCode = s.TerminalID WHERE s.SessionID = @Sess))
    WHERE  LotID = @LotID;

    UPDATE dbo.PR_InjLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_ConfirmByLotCode_UpdLot
    @WoID INT,
    @Op NVARCHAR(450),
    @LotID INT,
    @Sess INT
AS
BEGIN
    UPDATE dbo.tbl_Lot
    SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME(),
           StationCode = COALESCE(StationCode, (SELECT TOP 1 s.TerminalID FROM dbo.PR_PopSession s JOIN dbo.MD_Station st ON st.StationCode = s.TerminalID WHERE s.SessionID = @Sess))
    WHERE  LotID = @LotID;

    UPDATE dbo.PR_ImgLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_ConfirmByLotCode_UpdLot
    @WoID INT,
    @Op NVARCHAR(450),
    @LotID INT,
    @Sess INT
AS
BEGIN
    UPDATE dbo.tbl_Lot
    SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME(),
           StationCode = COALESCE(StationCode, (SELECT TOP 1 s.TerminalID FROM dbo.PR_PopSession s JOIN dbo.MD_Station st ON st.StationCode = s.TerminalID WHERE s.SessionID = @Sess))
    WHERE  LotID = @LotID;

    UPDATE dbo.PR_PntLot
    SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
           ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
           ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
    WHERE  LotID = @LotID;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_InsertDefectDetail
    @R INT,
    @W INT,
    @L INT,
    @P VARCHAR(10),
    @C VARCHAR(16),
    @N NVARCHAR(500),
    @Prior VARCHAR(16),
    @Rev INT,
    @Op VARCHAR(20),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_DefectDetail
        (ResultID, WoID, LotID, ProcessCode, DefectCode, Qty, ReasonNote,
         PriorStatus, ReversalResultID, DetectedAt, RegisteredBy, CreatedBy, CreatedTS, StationCode)
    OUTPUT INSERTED.DefectID
    VALUES
        (@R, @W, @L, @P, @C, 1, @N,
         @Prior, @Rev, SYSDATETIME(), @Op, @By, SYSDATETIME(),
         COALESCE((SELECT StationCode FROM dbo.PR_ProductionResult WHERE ResultID = @R), (SELECT StationCode FROM dbo.tbl_Lot WHERE LotID = @L)));
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_EQUIP_LogStatus
    @E VARCHAR(20),
    @L VARCHAR(20),
    @S VARCHAR(8),
    @R VARCHAR(30),
    @W INT,
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_EquipStatusLog
        (EquipID, LineID, Status, ReasonCode, WoID, StartedAt, CreatedBy, CreatedTS, StationCode)
    VALUES (@E, @L, @S, @R, @W, SYSDATETIME(), @By, SYSDATETIME(),
            COALESCE((SELECT StationCode FROM dbo.MD_Equipment WHERE EquipID = @E), dbo.FN_StationOfLine(@L)));

    UPDATE dbo.MD_Equipment SET Status = @S, ModifiedBy = @By, ModifiedTS = SYSDATETIME() WHERE EquipID = @E;
END;
GO
