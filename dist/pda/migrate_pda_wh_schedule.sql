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
--  Source: dbo.WH_ReleaseSchedule
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.WH_PDA_SCHEDULE_RELEASE_LIST
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Lines AS
    (
        SELECT
            COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) AS PickSlipNo,
            COALESCE(NULLIF(RS.ReqLocation, N''), N'-') AS DestinationLocation,
            RS.RequiredAt,
            RS.PrintDate,
            RS.CloseDate,
            RS.CloseUserId,
            RS.ReleaseScheduleID,
            RS.ItemNo,
            I.ItemName,
            COALESCE(RS.DemandQty, 0) AS DemandQty,
            COALESCE(RS.PickedQty, 0) AS PickedQty,
            CASE
                WHEN UPPER(COALESCE(RS.Status, N'OPEN')) IN (N'CLOSED', N'CANCELED') THEN N'Closed'
                WHEN COALESCE(RS.PickedQty, 0) >= COALESCE(RS.DemandQty, 0)
                     AND COALESCE(RS.DemandQty, 0) > 0 THEN N'Picked'
                WHEN COALESCE(RS.PickedQty, 0) > 0 THEN N'Partial'
                WHEN CONVERT(date, RS.RequiredAt) < CONVERT(date, GETDATE()) THEN N'Late'
                ELSE COALESCE(RS.Status, N'Open')
            END AS LineStatus
        FROM dbo.WH_ReleaseSchedule RS
        LEFT JOIN dbo.MD_Item I
               ON I.ItemNo = RS.ItemNo
    ),
    Grouped AS
    (
        SELECT
            PickSlipNo,
            MAX(DestinationLocation) AS DestinationLocation,
            MAX(RequiredAt) AS RequiredAt,
            MAX(PrintDate) AS PrintDate,
            MAX(CloseDate) AS CloseDate,
            MAX(CloseUserId) AS CloseUserId,
            COUNT(*) AS MaterialLineCount,
            SUM(DemandQty) AS RequestedBoxQty,
            SUM(PickedQty) AS PickedBoxQty,
            MIN(ReleaseScheduleID) AS FirstID,
            SUM(CASE WHEN LineStatus = N'Closed' THEN 1 ELSE 0 END) AS ClosedLines,
            SUM(CASE WHEN LineStatus = N'Picked' THEN 1 ELSE 0 END) AS PickedLines,
            SUM(CASE WHEN LineStatus = N'Partial' THEN 1 ELSE 0 END) AS PartialLines,
            SUM(CASE WHEN LineStatus = N'Late' THEN 1 ELSE 0 END) AS LateLines
        FROM Lines
        GROUP BY PickSlipNo
    )
    SELECT TOP (100)
        G.PickSlipNo,
        G.DestinationLocation,
        CONVERT(date, G.RequiredAt) AS RequiredDate,
        CONVERT(nvarchar(20), CONVERT(time(0), G.RequiredAt)) AS RequiredTime,
        G.PrintDate AS PrintedAt,
        G.CloseDate AS ClosedAt,
        G.CloseUserId AS ClosedBy,
        G.MaterialLineCount,
        G.RequestedBoxQty,
        G.PickedBoxQty,
        G.RequestedBoxQty AS RequestedQty,
        G.PickedBoxQty AS PickedQty,
        L.ItemNo AS FirstMaterialNo,
        L.ItemName AS FirstMaterialName,
        CAST(NULL AS nvarchar(50)) AS SuggestedPickLocation,
        CAST(NULL AS nvarchar(50)) AS SuggestedPickZone,
        CASE
            WHEN G.ClosedLines = G.MaterialLineCount THEN N'Closed'
            WHEN G.PickedLines = G.MaterialLineCount THEN N'Picked'
            WHEN G.PartialLines > 0 OR G.PickedLines > 0 THEN N'Partial'
            WHEN G.LateLines > 0 THEN N'Late'
            ELSE N'Open'
        END AS PickStatus
    FROM Grouped G
    LEFT JOIN Lines L
           ON L.PickSlipNo = G.PickSlipNo
          AND L.ReleaseScheduleID = G.FirstID
    ORDER BY G.RequiredAt, G.PickSlipNo;
END;
GO
