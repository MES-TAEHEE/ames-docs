-- =====================================================================
--  migrate_pda_wh_transactions.sql
--  PDA Warehouse transaction and operation log contract
--
--  Purpose:
--    dbo.WH_InventoryTransaction = business stock movement only
--      - IN  : received into warehouse inventory
--      - OUT : released/picked out of warehouse inventory
--      - ADJ : quantity adjustment
--
--    dbo.WH_OperationLog = warehouse operator audit log
--      - SCAN_* events
--      - RECEIVE / PICK / ADJUST / MOVE_LOCATION attempts
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\migrate_pda_wh_transactions.sql
-- =====================================================================
SET NOCOUNT ON;
GO

-- =====================================================================
--  Inventory transaction table
-- =====================================================================
IF OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_InventoryTransaction
    (
        TransactionID bigint IDENTITY(1,1) NOT NULL,
        TransactionTime datetime2 NOT NULL CONSTRAINT DF_WH_InventoryTransaction_Time DEFAULT SYSDATETIME(),
        TransactionType varchar(10) NOT NULL,
        ItemNo varchar(20) NULL,
        LocationID varchar(20) NULL,
        LotID int NULL,
        QtyBefore decimal(14,3) NULL,
        QtyChange decimal(14,3) NOT NULL CONSTRAINT DF_WH_InventoryTransaction_QtyChange DEFAULT 0,
        QtyAfter decimal(14,3) NULL,
        ReasonCode varchar(30) NULL,
        RefDocType varchar(30) NULL,
        RefDocID int NULL,
        OperatorID nvarchar(450) NULL,
        ApproverID nvarchar(450) NULL,
        Note nvarchar(500) NULL,
        CreatedBy varchar(50) NOT NULL CONSTRAINT DF_WH_InventoryTransaction_CreatedBy DEFAULT 'system',
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_WH_InventoryTransaction_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedTS datetime2 NULL,
        ModifiedBy nvarchar(450) NULL,
        CONSTRAINT PK_WH_InventoryTransaction PRIMARY KEY CLUSTERED (TransactionID),
        CONSTRAINT CK_WH_InventoryTransaction_Type CHECK (TransactionType IN ('IN', 'OUT', 'ADJ'))
    );
END;
GO

IF OBJECT_ID(N'dbo.WH_InventoryTransaction', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.WH_TransactionHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.WH_InventoryTransaction)
BEGIN
    INSERT INTO dbo.WH_InventoryTransaction
    (
        TransactionTime, TransactionType, ItemNo, LocationID, LotID,
        QtyBefore, QtyChange, QtyAfter, ReasonCode, RefDocType, RefDocID,
        OperatorID, ApproverID, Note, CreatedBy, CreatedTS, ModifiedTS, ModifiedBy
    )
    SELECT
        COALESCE(TxnTime, CreatedTS, SYSDATETIME()),
        CASE WHEN TxnType IN ('IN', 'OUT', 'ADJ') THEN TxnType ELSE 'ADJ' END,
        ItemNo,
        LocationID,
        LotID,
        QtyBefore,
        COALESCE(Delta, 0),
        QtyAfter,
        ReasonCode,
        RefDocType,
        RefDocID,
        OperatorID,
        ApproverID,
        Note,
        COALESCE(CreatedBy, 'system'),
        COALESCE(CreatedTS, SYSDATETIME()),
        ModifiedTS,
        ModifiedBy
    FROM dbo.WH_TransactionHistory;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InventoryTransaction_Time' AND object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    CREATE INDEX IX_WH_InventoryTransaction_Time ON dbo.WH_InventoryTransaction (TransactionTime DESC, TransactionID DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_InventoryTransaction_Search' AND object_id = OBJECT_ID(N'dbo.WH_InventoryTransaction'))
    CREATE INDEX IX_WH_InventoryTransaction_Search ON dbo.WH_InventoryTransaction (TransactionType, ItemNo, LocationID, LotID);
GO

-- =====================================================================
--  Operation log table
-- =====================================================================
IF OBJECT_ID(N'dbo.WH_OperationLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_OperationLog
    (
        OperationLogID bigint IDENTITY(1,1) NOT NULL,
        EventTime datetime2 NOT NULL CONSTRAINT DF_WH_OperationLog_EventTime DEFAULT SYSDATETIME(),
        EventType varchar(40) NOT NULL,
        ScreenCode varchar(20) NULL,
        EmployeeNo nvarchar(40) NULL,
        EmployeeName nvarchar(120) NULL,
        WorkerID nvarchar(450) NULL,
        TerminalID nvarchar(80) NULL,
        LineID nvarchar(40) NULL,
        ShiftCode nvarchar(20) NULL,
        ScanType varchar(30) NULL,
        ScanValue nvarchar(120) NULL,
        Result varchar(20) NOT NULL CONSTRAINT DF_WH_OperationLog_Result DEFAULT 'INFO',
        Message nvarchar(500) NULL,
        ClientIP nvarchar(64) NULL,
        UserAgent nvarchar(300) NULL,
        RefDocType varchar(30) NULL,
        RefDocNo nvarchar(80) NULL,
        LotNo nvarchar(80) NULL,
        PartNo nvarchar(80) NULL,
        LocationID nvarchar(80) NULL,
        Qty decimal(14,3) NULL,
        CreatedBy varchar(50) NOT NULL CONSTRAINT DF_WH_OperationLog_CreatedBy DEFAULT 'system',
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_WH_OperationLog_CreatedTS DEFAULT SYSDATETIME(),
        CONSTRAINT PK_WH_OperationLog PRIMARY KEY CLUSTERED (OperationLogID)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_OperationLog_Time' AND object_id = OBJECT_ID(N'dbo.WH_OperationLog'))
    CREATE INDEX IX_WH_OperationLog_Time ON dbo.WH_OperationLog (EventTime DESC, OperationLogID DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_OperationLog_Search' AND object_id = OBJECT_ID(N'dbo.WH_OperationLog'))
    CREATE INDEX IX_WH_OperationLog_Search ON dbo.WH_OperationLog (EventType, EmployeeNo, WorkerID, ScanValue);
GO

IF OBJECT_ID(N'dbo.WH_OperationLog', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.WH_OperationLog
     WHERE EventType IN ('LOGIN', 'LOGOUT');
END;
GO

-- =====================================================================
--  Operation log writer
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_OPERATION_LOG_WRITE
    @EventType varchar(40),
    @ScreenCode varchar(20) = NULL,
    @EmployeeNo nvarchar(40) = NULL,
    @EmployeeName nvarchar(120) = NULL,
    @WorkerID nvarchar(450) = NULL,
    @TerminalID nvarchar(80) = NULL,
    @LineID nvarchar(40) = NULL,
    @ShiftCode nvarchar(20) = NULL,
    @ScanType varchar(30) = NULL,
    @ScanValue nvarchar(120) = NULL,
    @Result varchar(20) = 'INFO',
    @Message nvarchar(500) = NULL,
    @ClientIP nvarchar(64) = NULL,
    @UserAgent nvarchar(300) = NULL,
    @RefDocType varchar(30) = NULL,
    @RefDocNo nvarchar(80) = NULL,
    @LotNo nvarchar(80) = NULL,
    @PartNo nvarchar(80) = NULL,
    @LocationID nvarchar(80) = NULL,
    @Qty decimal(14,3) = NULL,
    @CreatedBy varchar(50) = 'system'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NormalizedEventType varchar(40) = UPPER(LTRIM(RTRIM(@EventType)));
    IF @NormalizedEventType IN ('LOGIN', 'LOGOUT')
        RETURN;

    INSERT INTO dbo.WH_OperationLog
    (
        EventType, ScreenCode, EmployeeNo, EmployeeName, WorkerID,
        TerminalID, LineID, ShiftCode, ScanType, ScanValue,
        Result, Message, ClientIP, UserAgent, RefDocType, RefDocNo,
        LotNo, PartNo, LocationID, Qty, CreatedBy, CreatedTS
    )
    VALUES
    (
        @NormalizedEventType,
        NULLIF(LTRIM(RTRIM(@ScreenCode)), ''),
        NULLIF(LTRIM(RTRIM(@EmployeeNo)), ''),
        NULLIF(LTRIM(RTRIM(@EmployeeName)), ''),
        NULLIF(LTRIM(RTRIM(@WorkerID)), ''),
        NULLIF(LTRIM(RTRIM(@TerminalID)), ''),
        NULLIF(LTRIM(RTRIM(@LineID)), ''),
        NULLIF(LTRIM(RTRIM(@ShiftCode)), ''),
        NULLIF(UPPER(LTRIM(RTRIM(@ScanType))), ''),
        NULLIF(LTRIM(RTRIM(@ScanValue)), ''),
        COALESCE(NULLIF(UPPER(LTRIM(RTRIM(@Result))), ''), 'INFO'),
        NULLIF(LTRIM(RTRIM(@Message)), ''),
        NULLIF(LTRIM(RTRIM(@ClientIP)), ''),
        LEFT(NULLIF(LTRIM(RTRIM(@UserAgent)), ''), 300),
        NULLIF(UPPER(LTRIM(RTRIM(@RefDocType))), ''),
        NULLIF(LTRIM(RTRIM(@RefDocNo)), ''),
        NULLIF(LTRIM(RTRIM(@LotNo)), ''),
        NULLIF(LTRIM(RTRIM(@PartNo)), ''),
        NULLIF(LTRIM(RTRIM(@LocationID)), ''),
        @Qty,
        COALESCE(NULLIF(LTRIM(RTRIM(@CreatedBy)), ''), 'system'),
        SYSDATETIME()
    );
END;
GO

-- =====================================================================
--  PDA transaction list
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_TRANSACTION_LIST
    @SearchText nvarchar(120) = NULL,
    @DateFrom date = NULL,
    @DateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Search nvarchar(120) = NULLIF(LTRIM(RTRIM(@SearchText)), N'');
    DECLARE @Like nvarchar(130) = CASE WHEN @Search IS NULL THEN NULL ELSE N'%' + @Search + N'%' END;
    DECLARE @From date = COALESCE(@DateFrom, DATEADD(day, -30, CONVERT(date, SYSDATETIME())));
    DECLARE @To date = COALESCE(@DateTo, CONVERT(date, SYSDATETIME()));

    SELECT
        ROW_NUMBER() OVER (ORDER BY T.TransactionTime DESC, T.TransactionID DESC) AS ROW_NO,
        L.LotCode AS LOTNO,
        T.ItemNo AS PARTNO,
        CONVERT(nvarchar(10), T.TransactionTime, 23) AS WDATE,
        CONVERT(nvarchar(8), T.TransactionTime, 108) AS WTIME,
        T.LocationID AS LOCATION_NO,
        CASE WHEN T.TransactionType IN ('IN', 'OUT') THEN ABS(T.QtyChange)
             ELSE T.QtyChange END AS QTY,
        I.DefaultUOM AS UNIT,
        CASE T.TransactionType
            WHEN 'IN' THEN N'In'
            WHEN 'OUT' THEN N'Out'
            WHEN 'ADJ' THEN N'Adjust'
            ELSE T.TransactionType
        END AS STATUS,
        T.TransactionType AS DIRECTION,
        T.OperatorID AS WORKER_ID,
        T.ReasonCode AS REASON_CODE,
        T.Note AS REASON_NOTE,
        T.ApproverID AS SUPERVISOR,
        T.QtyBefore AS BEFORE_QTY,
        T.QtyChange AS DELTA_QTY,
        T.QtyAfter AS AFTER_QTY,
        CASE WHEN T.TransactionType = 'ADJ' THEN N'QTY BEFORE' ELSE NULL END AS BEFORE_STATUS,
        CASE WHEN T.TransactionType = 'ADJ' THEN N'QTY AFTER' ELSE NULL END AS AFTER_STATUS,
        T.LocationID AS BEFORE_LOCATION,
        T.LocationID AS AFTER_LOCATION,
        N'WH_InventoryTransaction' AS SOURCE,
        T.Note AS NOTE
    FROM dbo.WH_InventoryTransaction T
    LEFT JOIN dbo.tbl_Lot L
           ON L.LotID = T.LotID
    LEFT JOIN dbo.MD_Item I
           ON I.ItemNo = T.ItemNo
    WHERE T.TransactionTime >= @From
      AND T.TransactionTime < DATEADD(day, 1, @To)
      AND (@Like IS NULL
           OR L.LotCode LIKE @Like
           OR T.ItemNo LIKE @Like
           OR T.LocationID LIKE @Like
           OR EXISTS (
               SELECT 1
               FROM dbo.WH_InboundPackage P
               JOIN dbo.WH_InboundDocument D ON D.InboundDocumentID = P.InboundDocumentID
               WHERE P.LotID = T.LotID
                 AND (P.BoxBarcode LIKE @Like
                      OR D.CaseNo LIKE @Like
                      OR (D.ReceiveType = N'CKD' AND D.DocumentBarcode LIKE @Like))))
    ORDER BY T.TransactionTime DESC, T.TransactionID DESC;
END;
GO

-- =====================================================================
--  Web operation log history
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_WEB_LOG_HISTORY_LIST
    @SearchText nvarchar(120) = NULL,
    @EventType varchar(40) = NULL,
    @DateFrom date = NULL,
    @DateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Search nvarchar(120) = NULLIF(LTRIM(RTRIM(@SearchText)), N'');
    DECLARE @Like nvarchar(130) = CASE WHEN @Search IS NULL THEN NULL ELSE N'%' + @Search + N'%' END;
    DECLARE @OperationType varchar(40) = NULLIF(UPPER(LTRIM(RTRIM(@EventType))), '');

    SELECT TOP (500)
        OperationLogID,
        EventTime,
        EventType,
        ScreenCode,
        EmployeeNo,
        EmployeeName,
        WorkerID,
        TerminalID,
        LineID,
        ShiftCode,
        ScanType,
        ScanValue,
        Result,
        Message,
        ClientIP,
        RefDocType,
        RefDocNo,
        LotNo,
        PartNo,
        LocationID,
        Qty
    FROM dbo.WH_OperationLog
    WHERE EventType NOT IN ('LOGIN', 'LOGOUT')
      AND (
            @OperationType IS NULL
         OR (@OperationType = 'SCAN' AND EventType LIKE 'SCAN_%')
         OR (@OperationType = 'INBOUND' AND EventType IN ('RECEIVE', 'CANCEL_RECEIPT'))
         OR (@OperationType = 'RELEASE' AND EventType = 'RELEASE_PICK')
         OR (@OperationType = 'ADJUST' AND EventType = 'ADJUST_SAVE')
         OR (@OperationType = 'LOCATION' AND (EventType = 'MOVE_LOCATION' OR EventType LIKE 'LOCATION_MASTER_%'))
         OR EventType = @OperationType
      )
      AND (@DateFrom IS NULL OR EventTime >= @DateFrom)
      AND (@DateTo IS NULL OR EventTime < DATEADD(day, 1, @DateTo))
      AND (@Like IS NULL
           OR EventType LIKE @Like
           OR ScreenCode LIKE @Like
           OR EmployeeNo LIKE @Like
           OR EmployeeName LIKE @Like
           OR WorkerID LIKE @Like
           OR TerminalID LIKE @Like
           OR ScanValue LIKE @Like
           OR Message LIKE @Like
           OR LotNo LIKE @Like
           OR PartNo LIKE @Like
           OR LocationID LIKE @Like)
    ORDER BY EventTime DESC, OperationLogID DESC;
END;
GO

IF OBJECT_ID(N'dbo.SYS_Screen', N'U') IS NOT NULL
BEGIN
    DECLARE @UpdateWh004Sql nvarchar(max) = N'
        UPDATE dbo.SYS_Screen
           SET ScreenName = N''Log History'',
               ScreenNameEn = N''Log History'',
               HRef = N''wh/log-history'',
               ModifiedBy = N''migrate_pda_wh_transactions'',
               ModifiedTS = SYSDATETIME()
         WHERE ModuleCode = N''WEB''
           AND ScreenCode = N''WH-004''';

    IF COL_LENGTH(N'dbo.SYS_Screen', N'ProcessCode') IS NOT NULL
        SET @UpdateWh004Sql += N' AND ProcessCode = N''WH''';

    EXEC sys.sp_executesql @UpdateWh004Sql;
END;
GO
