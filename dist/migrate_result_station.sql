-- ============================================================================
-- 실적(PR_ProductionResult)에 워크스테이션(StationCode) 기록 — 라인 아래 어느 스테이션에서 쓴 실적인지 (11-18)
--   · POP 은 로그인 때 라인 → 스테이션을 고르고, 그 스테이션 코드가 세션의 TerminalID 로 들어간다.
--     실적 INSERT 프로시저 5개(INJ·IMG·PNT 확정, 확정 되돌리기, 재작업)가 세션 TerminalID 를 StationCode 로 같이 쓴다. 시그니처 변경 없음.
--   · 기존 실적은 세션 TerminalID 가 MD_Station 에 있으면 채운다(없으면 NULL — PDA·웹 입력 등).
--   · 재실행 안전. 마이그레이션 -> 신 바이너리 순서(이 컬럼 없이 신 SP 를 적용하지 말 것: 컬럼이 먼저 만들어진다).
--     구 바이너리는 SP 시그니처가 같아 그대로 동작한다.
--   · 적용: sqlcmd -f 65001 -I -b -i dist\migrate_result_station.sql  (끝에 migrate_audit_columns_last.sql 로 컬럼 순서 정리)
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH(N'dbo.PR_ProductionResult', N'StationCode') IS NULL
    ALTER TABLE dbo.PR_ProductionResult ADD StationCode varchar(20) COLLATE DATABASE_DEFAULT NULL;
GO
IF COL_LENGTH(N'dbo.PR_ProductionResult', N'StationCode') IS NOT NULL
    EXEC(N'UPDATE r SET StationCode = s.TerminalID
           FROM dbo.PR_ProductionResult r JOIN dbo.PR_PopSession s ON s.SessionID = r.SessionID
           WHERE r.StationCode IS NULL AND EXISTS (SELECT 1 FROM dbo.MD_Station st WHERE st.StationCode = s.TerminalID);');
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_IMG_ConfirmByLotCode_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @CT INT,
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS, StationCode)
    OUTPUT INSERTED.ResultID
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, @Proc, 1, @CT,
         @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME(),
         (SELECT TOP 1 TerminalID FROM dbo.PR_PopSession WHERE SessionID = @Sess));
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_INJ_ConfirmByLotCode_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @CT INT,
    @Mold VARCHAR(20),
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         MoldID, OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS, StationCode)
    OUTPUT INSERTED.ResultID
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, 'INJ', 1, @CT,
         @Mold, @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME(),
         (SELECT TOP 1 TerminalID FROM dbo.PR_PopSession WHERE SessionID = @Sess));
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_LOT_ReverseConfirmedResult_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @Mold VARCHAR(20),
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         MoldID, OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS, StationCode)
    OUTPUT INSERTED.ResultID
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, @Proc, -1, 0,
         @Mold, @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME(),
         (SELECT TOP 1 TerminalID FROM dbo.PR_PopSession WHERE SessionID = @Sess));
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_PNT_ConfirmByLotCode_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @Proc VARCHAR(10),
    @CT INT,
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS, StationCode)
    OUTPUT INSERTED.ResultID
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, @Proc, 1, @CT,
         @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME(),
         (SELECT TOP 1 TerminalID FROM dbo.PR_PopSession WHERE SessionID = @Sess));
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_POP_RWK_Rework_InsProductionResult
    @EntryNo VARCHAR(28),
    @WoID INT,
    @LotID INT,
    @LineID VARCHAR(20),
    @Op NVARCHAR(450),
    @Sess INT,
    @Now DATETIME2,
    @ProdDate DATE,
    @Shift VARCHAR(10),
    @By VARCHAR(20)
AS
BEGIN
    INSERT INTO dbo.PR_ProductionResult
        (EntryNo, WoID, LotID, LineID, ProcessCode, GoodQty, CycleSec,
         OperatorID, SessionID, DefectFlag, EntryAt, ProdDate, ShiftCode, CreatedBy, CreatedTS, StationCode)
    VALUES
        (@EntryNo, @WoID, @LotID, @LineID, 'RWK', 1, 0,
         @Op, @Sess, 0, @Now, @ProdDate, @Shift, @By, SYSDATETIME(),
         (SELECT TOP 1 TerminalID FROM dbo.PR_PopSession WHERE SessionID = @Sess));
END;
GO
