-- =====================================================================
--  migrate_wh_location_master_audit.sql
--  Web Warehouse Location Master audit contract
--
--  Purpose:
--    - Ensure admin@ames.local has Admin role on rebuilt/remote databases.
--    - Ensure dbo.WH_OperationLog exists for web/PDA warehouse audit events.
--    - Ensure Log History can include LOCATION_MASTER_* events.
--
--  Apply:
--    sqlcmd -S .\SQLEXPRESS -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\migrate_wh_location_master_audit.sql
-- =====================================================================
SET NOCOUNT ON;
GO

-- =====================================================================
--  Admin role seed/backfill
-- =====================================================================
IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'ADMIN')
BEGIN
    INSERT INTO dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(nvarchar(450), NEWID()), N'Admin', N'ADMIN', CONVERT(nvarchar(max), NEWID()));
END;
GO

IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NOT NULL
BEGIN
    DECLARE @AdminUserId nvarchar(450);
    DECLARE @AdminRoleId nvarchar(450);

    SELECT TOP (1) @AdminUserId = Id
      FROM dbo.AspNetUsers
     WHERE NormalizedEmail = N'ADMIN@AMES.LOCAL'
        OR NormalizedUserName = N'ADMIN@AMES.LOCAL';

    SELECT TOP (1) @AdminRoleId = Id
      FROM dbo.AspNetRoles
     WHERE NormalizedName = N'ADMIN';

    IF @AdminUserId IS NOT NULL
       AND @AdminRoleId IS NOT NULL
       AND NOT EXISTS (
            SELECT 1
              FROM dbo.AspNetUserRoles
             WHERE UserId = @AdminUserId
               AND RoleId = @AdminRoleId
       )
    BEGIN
        INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
        VALUES (@AdminUserId, @AdminRoleId);
    END;
END;
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

IF COL_LENGTH(N'dbo.WH_OperationLog', N'EventTime') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD EventTime datetime2 NOT NULL DEFAULT SYSDATETIME();
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'EventType') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD EventType varchar(40) NOT NULL DEFAULT 'INFO';
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'ScreenCode') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD ScreenCode varchar(20) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'EmployeeNo') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD EmployeeNo nvarchar(40) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'EmployeeName') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD EmployeeName nvarchar(120) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'WorkerID') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD WorkerID nvarchar(450) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'TerminalID') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD TerminalID nvarchar(80) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'LineID') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD LineID nvarchar(40) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'ShiftCode') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD ShiftCode nvarchar(20) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'ScanType') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD ScanType varchar(30) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'ScanValue') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD ScanValue nvarchar(120) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'Result') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD Result varchar(20) NOT NULL DEFAULT 'INFO';
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'Message') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD Message nvarchar(500) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'ClientIP') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD ClientIP nvarchar(64) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'UserAgent') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD UserAgent nvarchar(300) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'RefDocType') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD RefDocType varchar(30) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'RefDocNo') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD RefDocNo nvarchar(80) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'LotNo') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD LotNo nvarchar(80) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'PartNo') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD PartNo nvarchar(80) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'LocationID') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD LocationID nvarchar(80) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'Qty') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD Qty decimal(14,3) NULL;
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'CreatedBy') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD CreatedBy varchar(50) NOT NULL DEFAULT 'system';
GO
IF COL_LENGTH(N'dbo.WH_OperationLog', N'CreatedTS') IS NULL
    ALTER TABLE dbo.WH_OperationLog ADD CreatedTS datetime2 NOT NULL DEFAULT SYSDATETIME();
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_OperationLog_Time' AND object_id = OBJECT_ID(N'dbo.WH_OperationLog'))
    CREATE INDEX IX_WH_OperationLog_Time ON dbo.WH_OperationLog (EventTime DESC, OperationLogID DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_OperationLog_Search' AND object_id = OBJECT_ID(N'dbo.WH_OperationLog'))
    CREATE INDEX IX_WH_OperationLog_Search ON dbo.WH_OperationLog (EventType, EmployeeNo, WorkerID, ScanValue);
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
           OR LocationID LIKE @Like
           OR RefDocNo LIKE @Like)
    ORDER BY EventTime DESC, OperationLogID DESC;
END;
GO
