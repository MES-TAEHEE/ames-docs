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

    SELECT TOP (100)
        CONCAT(N'RS-', RS.ReleaseScheduleID) AS PickSlipNo,
        CAST(NULL AS nvarchar(50)) AS DestinationLocation,
        CONVERT(date, RS.RequiredAt) AS RequiredDate,
        CONVERT(nvarchar(20), CONVERT(time(0), RS.RequiredAt)) AS RequiredTime,
        CAST(NULL AS datetime2) AS PrintedAt,
        CAST(NULL AS datetime2) AS ClosedAt,
        CAST(NULL AS nvarchar(50)) AS ClosedBy,
        1 AS MaterialLineCount,
        COALESCE(RS.DemandQty, 0) AS RequestedBoxQty,
        COALESCE(RS.PickedQty, 0) AS PickedBoxQty,
        COALESCE(RS.DemandQty, 0) AS RequestedQty,
        COALESCE(RS.PickedQty, 0) AS PickedQty,
        RS.ItemNo AS FirstMaterialNo,
        I.ItemName AS FirstMaterialName,
        CAST(NULL AS nvarchar(50)) AS SuggestedPickLocation,
        CAST(NULL AS nvarchar(50)) AS SuggestedPickZone,
        CASE
            WHEN COALESCE(RS.PickedQty, 0) >= COALESCE(RS.DemandQty, 0)
                 AND COALESCE(RS.DemandQty, 0) > 0 THEN N'Picked'
            WHEN COALESCE(RS.PickedQty, 0) > 0 THEN N'Partial'
            WHEN CONVERT(date, RS.RequiredAt) < CONVERT(date, GETDATE()) THEN N'Late'
            ELSE COALESCE(RS.Status, N'Open')
        END AS PickStatus
    FROM dbo.WH_ReleaseSchedule RS
    LEFT JOIN dbo.MD_Item I
           ON I.ItemNo = RS.ItemNo
    ORDER BY RS.RequiredAt, RS.ReleaseScheduleID;
END;
GO
