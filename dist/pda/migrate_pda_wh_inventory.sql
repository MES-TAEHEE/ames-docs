-- =====================================================================
-- AMES PDA Warehouse / Inventory read procedures
-- Naming rule: dbo.WH_PDA_<Workflow>_<Action>
-- =====================================================================

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_STATUS_LIST
    @SearchText nvarchar(80) = NULL,
    @StockDateFrom date = NULL,
    @StockDateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Q nvarchar(80) = NULLIF(LTRIM(RTRIM(@SearchText)), N'');

    ;WITH ActiveStock AS
    (
        SELECT
            W.ItemNo,
            MIN(W.InventoryID) AS INVENTORY_ID,
            SUM(COALESCE(W.OnHandQty, 0)) AS SUM_QTY,
            SUM(COALESCE(W.ReservedQty, 0)) AS RESERVED_QTY,
            MAX(W.LastReceivedAt) AS LAST_RECEIVED_DATE,
            COUNT(DISTINCT CASE WHEN COALESCE(W.OnHandQty, 0) > 0 THEN W.LotID END) AS LOT_COUNT,
            COUNT(DISTINCT CASE WHEN COALESCE(W.OnHandQty, 0) > 0 THEN W.LocationID END) AS LOCATION_COUNT
        FROM dbo.WH_Inventory W
        WHERE W.ItemNo IS NOT NULL
          AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
          AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
          AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
        GROUP BY W.ItemNo
    ),
    InventoryBase AS
    (
        SELECT
            COALESCE(S.INVENTORY_ID, 0) AS INVENTORY_ID,
            I.ItemNo AS PARTNO,
            I.ItemName AS PARTNM,
            PRI.LotCode AS LOTNO,
            COALESCE(PRI.LocationID, N'-') AS PRIMARY_LOCATION,
            COALESCE(S.SUM_QTY, 0) AS SUM_QTY,
            COALESCE(S.RESERVED_QTY, 0) AS RESERVED_QTY,
            S.LAST_RECEIVED_DATE,
            I.CarType AS VINCD,
            I.DefaultUOM AS UNIT,
            CAST(NULL AS decimal(18,3)) AS MIN_INV_DAY,
            COALESCE(I.MinStock, 0) AS MIN_INV_QTY,
            CAST(NULL AS decimal(18,3)) AS MAX_INV_DAY,
            COALESCE(I.MaxStock, 0) AS MAX_INV_QTY,
            COALESCE(S.LOT_COUNT, 0) AS LOT_COUNT,
            COALESCE(S.LOCATION_COUNT, 0) AS LOCATION_COUNT
        FROM dbo.MD_Item I
        LEFT JOIN ActiveStock S
               ON S.ItemNo = I.ItemNo
        OUTER APPLY
        (
            SELECT TOP (1)
                W.LocationID,
                LOT.LotCode
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Location L
                   ON L.LocationID = W.LocationID
            LEFT JOIN dbo.tbl_Lot LOT
                   ON LOT.LotID = W.LotID
            WHERE W.ItemNo = I.ItemNo
              AND COALESCE(W.OnHandQty, 0) > 0
              AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
              AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
              AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
            ORDER BY
                CASE
                    WHEN @Q IS NOT NULL
                     AND
                     (
                         W.LocationID LIKE N'%' + @Q + N'%'
                         OR L.LocationName LIKE N'%' + @Q + N'%'
                         OR L.ZoneCode LIKE N'%' + @Q + N'%'
                         OR L.Aisle LIKE N'%' + @Q + N'%'
                         OR L.Bay LIKE N'%' + @Q + N'%'
                         OR L.Slot LIKE N'%' + @Q + N'%'
                     )
                    THEN 0
                    ELSE 1
                END,
                W.LastReceivedAt DESC,
                W.InventoryID DESC
        ) PRI
        WHERE COALESCE(I.ActiveFlag, 1) = 1
          AND
          (
              S.ItemNo IS NOT NULL
              OR COALESCE(I.MinStock, 0) > 0
              OR COALESCE(I.MaxStock, 0) > 0
          )
          AND
          (
              @Q IS NULL
              OR I.ItemNo LIKE N'%' + @Q + N'%'
              OR I.ItemName LIKE N'%' + @Q + N'%'
              OR I.CarType LIKE N'%' + @Q + N'%'
              OR EXISTS
              (
                  SELECT 1
                  FROM dbo.tbl_Lot L
                  WHERE L.ItemNo = I.ItemNo
                    AND L.LotCode LIKE N'%' + @Q + N'%'
              )
              OR EXISTS
              (
                  SELECT 1
                  FROM dbo.WH_Inventory W
                  LEFT JOIN dbo.MD_Location L
                         ON L.LocationID = W.LocationID
                  WHERE W.ItemNo = I.ItemNo
                    AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
                    AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
                    AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
                    AND
                    (
                        W.LocationID LIKE N'%' + @Q + N'%'
                        OR L.LocationName LIKE N'%' + @Q + N'%'
                        OR L.ZoneCode LIKE N'%' + @Q + N'%'
                        OR L.Aisle LIKE N'%' + @Q + N'%'
                        OR L.Bay LIKE N'%' + @Q + N'%'
                        OR L.Slot LIKE N'%' + @Q + N'%'
                    )
              )
          )
    ),
    Statused AS
    (
        SELECT *,
            CASE
                WHEN SUM_QTY <= 0 THEN N'OUT'
                WHEN MIN_INV_QTY > 0 AND SUM_QTY < MIN_INV_QTY THEN N'BELOW_MIN'
                WHEN MAX_INV_QTY > 0 AND SUM_QTY > MAX_INV_QTY THEN N'OVER_MAX'
                ELSE N'NORMAL'
            END AS STATUS
        FROM InventoryBase
    )
    SELECT TOP (300)
        INVENTORY_ID,
        PARTNO,
        PARTNM,
        LOTNO,
        PRIMARY_LOCATION,
        SUM_QTY,
        RESERVED_QTY,
        LAST_RECEIVED_DATE,
        VINCD,
        UNIT,
        MIN_INV_DAY,
        MIN_INV_QTY,
        MAX_INV_DAY,
        MAX_INV_QTY,
        LOT_COUNT,
        LOCATION_COUNT,
        STATUS,
        CASE STATUS
            WHEN N'OUT' THEN N'Out'
            WHEN N'BELOW_MIN' THEN N'Below Min'
            WHEN N'OVER_MAX' THEN N'Over Max'
            ELSE N'Normal'
        END AS STATUSNM
    FROM Statused
    ORDER BY
        CASE STATUS
            WHEN N'OUT' THEN 1
            WHEN N'BELOW_MIN' THEN 2
            WHEN N'OVER_MAX' THEN 3
            ELSE 4
        END,
        PARTNO;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_SCAN_LOOKUP
    @ScanText nvarchar(80)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Scan nvarchar(80) = NULLIF(LTRIM(RTRIM(@ScanText)), N'');

    IF @Scan IS NULL
    BEGIN
        SELECT N'TEXT' AS SEARCH_KIND, N'' AS SEARCH_TEXT, CAST(NULL AS nvarchar(120)) AS DISPLAY_TEXT;
        RETURN;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MD_Location L
        WHERE UPPER(L.LocationID) = UPPER(@Scan)
          AND COALESCE(L.ActiveFlag, 1) = 1
    )
    BEGIN
        SELECT TOP (1)
            N'LOCATION' AS SEARCH_KIND,
            CAST(L.LocationID AS nvarchar(80)) AS SEARCH_TEXT,
            L.LocationName AS DISPLAY_TEXT
        FROM dbo.MD_Location L
        WHERE UPPER(L.LocationID) = UPPER(@Scan)
          AND COALESCE(L.ActiveFlag, 1) = 1;
        RETURN;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.tbl_Lot L
        WHERE UPPER(L.LotCode) = UPPER(@Scan)
           OR UPPER(L.ItemNo) = UPPER(@Scan)
    )
    BEGIN
        SELECT TOP (1)
            N'PART' AS SEARCH_KIND,
            CAST(L.ItemNo AS nvarchar(80)) AS SEARCH_TEXT,
            I.ItemName AS DISPLAY_TEXT
        FROM dbo.tbl_Lot L
        LEFT JOIN dbo.MD_Item I
               ON I.ItemNo = L.ItemNo
        WHERE UPPER(L.LotCode) = UPPER(@Scan)
           OR UPPER(L.ItemNo) = UPPER(@Scan)
        ORDER BY
            CASE WHEN UPPER(L.LotCode) = UPPER(@Scan) THEN 0 ELSE 1 END,
            L.LotID DESC;
        RETURN;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MD_Item I
        WHERE UPPER(I.ItemNo) = UPPER(@Scan)
          AND COALESCE(I.ActiveFlag, 1) = 1
    )
    BEGIN
        SELECT TOP (1)
            N'PART' AS SEARCH_KIND,
            CAST(I.ItemNo AS nvarchar(80)) AS SEARCH_TEXT,
            I.ItemName AS DISPLAY_TEXT
        FROM dbo.MD_Item I
        WHERE UPPER(I.ItemNo) = UPPER(@Scan)
          AND COALESCE(I.ActiveFlag, 1) = 1;
        RETURN;
    END;

    SELECT N'TEXT' AS SEARCH_KIND, @Scan AS SEARCH_TEXT, CAST(NULL AS nvarchar(120)) AS DISPLAY_TEXT;
END;
GO

CREATE OR ALTER PROCEDURE dbo.WH_PDA_INVENTORY_LOCATION_LIST
    @ItemNo nvarchar(40),
    @StockDateFrom date = NULL,
    @StockDateTo date = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PartNo nvarchar(40) = NULLIF(LTRIM(RTRIM(@ItemNo)), N'');

    SELECT
        ROW_NUMBER() OVER (ORDER BY COALESCE(L.LocationID, W.LocationID)) AS ROW_NO,
        W.ItemNo AS PARTNO,
        COALESCE(L.LocationID, W.LocationID, N'-') AS LOCATION_NO,
        L.LocationName AS LOCATION_NM,
        L.PlantCode AS WHCD,
        L.PlantCode AS WHNM,
        L.ZoneCode AS AREACD,
        L.ZoneCode AS AREANM,
        L.ZoneCode AS ZONECD,
        L.LocationName AS ZONENM,
        L.Aisle AS RACK_X,
        L.Bay AS RACK_Y,
        L.Slot AS RACK_Z,
        SUM(COALESCE(W.OnHandQty, 0)) AS SUM_QTY
    FROM dbo.WH_Inventory W
    LEFT JOIN dbo.MD_Location L
           ON L.LocationID = W.LocationID
    WHERE @PartNo IS NOT NULL
      AND W.ItemNo = @PartNo
      AND COALESCE(W.OnHandQty, 0) > 0
      AND UPPER(COALESCE(W.Status, N'Received')) NOT IN (N'CANCELED', N'RELEASED', N'PICKED')
      AND (@StockDateFrom IS NULL OR CONVERT(date, W.LastReceivedAt) >= @StockDateFrom)
      AND (@StockDateTo IS NULL OR CONVERT(date, W.LastReceivedAt) <= @StockDateTo)
    GROUP BY
        W.ItemNo,
        W.LocationID,
        L.LocationID,
        L.LocationName,
        L.PlantCode,
        L.ZoneCode,
        L.Aisle,
        L.Bay,
        L.Slot
    ORDER BY COALESCE(L.LocationID, W.LocationID);
END;
GO
