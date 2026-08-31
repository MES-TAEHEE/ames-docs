-- =====================================================================
--  migrate_pda_wh_schedule.sql
--  PDA Warehouse schedule database contract
--
--  PDA-owned objects use the main AMES schema/name style:
--    - Procedures: dbo.WH_PDA_...
--    - Tables:     dbo.WH_...
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\migrate_pda_wh_schedule.sql
-- =====================================================================
SET NOCOUNT ON;
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

-- =====================================================================
--  Schedule / Inbound
--  Source: dbo.WH_PurchaseOrder
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_SCHEDULE_INBOUND_LIST
    @CompanyCode nvarchar(10) = N'1000',
    @BusinessCode nvarchar(10) = N'5011',
    @ScheduleYear nvarchar(4),
    @ScheduleQuarter nvarchar(2),
    @SupplierCode nvarchar(10) = NULL,
    @LanguageCode nvarchar(10) = N'EN'
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (100)
        P.PoNumber AS PurchaseOrderNo,
        P.PoLineNo AS PurchaseOrderLineNo,
        COALESCE(V.VendorName, P.VendorID) AS SupplierName,
        P.ItemNo AS MaterialNo,
        I.ItemName AS MaterialName,
        I.CarType AS CarCode,
        COALESCE(P.UnitCode, I.DefaultUOM) AS UnitOfMeasure,
        COALESCE(P.OrderQty, 0) AS PurchaseOrderQty,
        COALESCE(P.ReceivedQty, 0) AS ReceivedQty,
        CASE
            WHEN COALESCE(P.OrderQty, 0) - COALESCE(P.ReceivedQty, 0) < 0 THEN 0
            ELSE COALESCE(P.OrderQty, 0) - COALESCE(P.ReceivedQty, 0)
        END AS RemainingQty,
        P.DueDate AS ExpectedArrivalDate,
        P.OrderDate AS PurchaseOrderCreatedDate,
        CASE
            WHEN COALESCE(P.OrderQty, 0) - COALESCE(P.ReceivedQty, 0) <= 0 THEN N'Complete'
            WHEN P.DueDate < CONVERT(date, GETDATE()) THEN N'Late'
            ELSE N'In Progress'
        END AS ReceiptStatus
    FROM dbo.WH_PurchaseOrder P
    LEFT JOIN dbo.MD_Item I
           ON I.ItemNo = P.ItemNo
    LEFT JOIN dbo.MD_Vendor V
           ON V.VendorID = P.VendorID
    WHERE (@SupplierCode IS NULL OR P.VendorID = @SupplierCode)
      AND (
            P.DueDate IS NULL
            OR (
                YEAR(P.DueDate) = TRY_CONVERT(int, @ScheduleYear)
                AND DATEPART(quarter, P.DueDate) = TRY_CONVERT(int, @ScheduleQuarter)
            )
      )
    ORDER BY P.DueDate, P.PoNumber, P.PoLineNo;
END;
GO

-- =====================================================================
--  Schedule / Release
--  Source: PP-007 WO Release / dbo.PP_WorkOrder
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_SCHEDULE_RELEASE_LIST
    @DueDateFrom date = NULL,
    @DueDateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (100)
        W.WoID AS WorkOrderId,
        W.WoNumber AS WorkOrderNo,
        W.ItemNo AS PartNo,
        I.ItemName AS PartName,
        COALESCE(W.OrderQty, 0) AS OrderQty,
        COALESCE(I.DefaultUOM, N'EA') AS Unit,
        W.DueDate,
        COALESCE(W.Status, N'Released') AS WorkOrderStatus,
        W.ReleasedAt,
        W.LineID
    FROM dbo.PP_WorkOrder W
    LEFT JOIN dbo.MD_Item I ON I.ItemNo = W.ItemNo
    WHERE W.Status IN (N'Released', N'In Progress')
      AND (@DueDateFrom IS NULL OR W.DueDate >= @DueDateFrom)
      AND (@DueDateTo IS NULL OR W.DueDate <= @DueDateTo)
    ORDER BY ISNULL(W.DueDate, CONVERT(date, '9999-12-31')),
             ISNULL(W.Priority, 5),
             W.WoID;
END;
GO
