using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed class FinishedGoodsRepository
{
    private readonly AmesConnectionFactory _factory;

    public FinishedGoodsRepository(AmesConnectionFactory factory) => _factory = factory;

    public record LocationRow(
        string LocationNo,
        string? LocationName,
        string? WarehouseCode,
        string? WarehouseName,
        string? AreaCode,
        string? ZoneCode,
        string? ColumnNo,
        string? RowNo,
        string? LevelNo,
        decimal Capacity,
        bool Active,
        int LotCount,
        int PartCount,
        decimal Qty,
        string Status);

    public record ReturnRow(
        int ReturnId,
        string ReturnNumber,
        string? RmaNo,
        string? CustomerCode,
        int? ShipmentOrderId,
        string? ShipmentOrderNumber,
        string? CustomerPo,
        string? ItemNo,
        decimal Qty,
        string? ReturnReason,
        string? Status,
        DateTime? ReceivedAt,
        string? ReceivedBy,
        bool CapaTriggered,
        string? ItemsJson);

    public record InventoryRow(
        int StockId,
        string? StockNumber,
        string ItemNo,
        string? ItemName,
        string? LotNo,
        decimal Qty,
        string? Unit,
        string? LocationNo,
        string? Status,
        DateTime? StockAt);

    public record ShipmentRow(
        int ShipmentOrderId,
        string? ShipOrderNumber,
        string? CustomerCode,
        string? CustomerPo,
        DateTime? ShipDate,
        string? Status,
        string? PickslipId,
        string? CarrierCode,
        string? Destination,
        int? LoadingId,
        string? LoadingNumber,
        string? LicensePlate,
        string? DriverName,
        string? DockNo,
        string? SealNo,
        DateTime? ConfirmedAt,
        DateTime? DepartureAt,
        string? OperatorId,
        string? OtdStatus,
        string? DeliveryNoteNumber,
        DateTime? DeliveryNoteIssuedAt,
        int LineCount,
        decimal OrderedQty);

    public record HistoryRow(
        DateTime EventAt,
        string EventType,
        string? ReferenceNo,
        string? ItemOrOrder,
        decimal Qty,
        string? Location,
        string? WorkerId,
        string? Status,
        string? Details);

    public List<LocationRow> ListLocations(
        string? search = null,
        string? warehouseCode = null,
        string? areaCode = null,
        string? zoneCode = null,
        string? levelNo = null,
        bool includeInactive = false)
    {
        EnsureFgLocationMasterTable();
        const string sql = """
            WITH FgStock AS
            (
                SELECT
                    Location,
                    COUNT(DISTINCT LotID) AS LotCount,
                    COUNT(DISTINCT ItemNo) AS PartCount,
                    SUM(CASE WHEN UPPER(ISNULL(Status, 'AVAILABLE')) NOT IN ('CANCELED', 'CANCELLED', 'SHIPPED')
                             THEN ISNULL(Qty, 0) ELSE 0 END) AS Qty
                FROM dbo.FG_Inventory
                WHERE Location IS NOT NULL
                GROUP BY Location
            )
            SELECT
                L.LocationID,
                L.LocationName,
                L.PlantCode AS WarehouseCode,
                COALESCE(NULLIF(W.WhName, ''), L.PlantCode) AS WarehouseName,
                L.ZoneCode AS AreaCode,
                L.LocationType AS ZoneCode,
                L.Aisle AS ColumnNo,
                L.Bay AS RowNo,
                L.Slot AS LevelNo,
                CAST(ISNULL(L.Capacity, 0) AS decimal(14,3)) AS Capacity,
                CAST(ISNULL(L.ActiveFlag, 1) AS bit) AS Active,
                ISNULL(S.LotCount, 0) AS LotCount,
                ISNULL(S.PartCount, 0) AS PartCount,
                CAST(ISNULL(S.Qty, 0) AS decimal(14,3)) AS Qty,
                CASE
                    WHEN ISNULL(L.ActiveFlag, 1) = 0 THEN 'INACTIVE'
                    WHEN ISNULL(S.Qty, 0) <= 0 THEN 'EMPTY'
                    WHEN ISNULL(L.Capacity, 0) > 0 AND S.Qty >= L.Capacity THEN 'FULL'
                    ELSE 'OCCUPIED'
                END AS Status
            FROM dbo.MD_Location L
            LEFT JOIN dbo.FG_LocationMaster FGM ON FGM.LocationID = L.LocationID
            LEFT JOIN dbo.WH_WarehouseMaster W ON W.WhCode = L.PlantCode
            LEFT JOIN FgStock S ON S.Location = L.LocationID
            WHERE
                (
                    FGM.LocationID IS NOT NULL
                    OR
                    UPPER(ISNULL(L.LocationType, '')) IN ('FG', 'FINISHED_GOODS', 'FINISHED GOODS')
                    OR UPPER(L.LocationID) LIKE 'FG%'
                    OR S.Location IS NOT NULL
                )
              AND (@IncludeInactive = 1 OR (ISNULL(L.ActiveFlag, 1) = 1 AND ISNULL(FGM.ActiveFlag, 1) = 1))
              AND (@WarehouseCode IS NULL OR L.PlantCode = @WarehouseCode)
              AND (@AreaCode IS NULL OR L.ZoneCode = @AreaCode)
              AND (@ZoneCode IS NULL OR L.LocationType = @ZoneCode)
              AND (@LevelNo IS NULL OR L.Slot = @LevelNo)
              AND (@Search IS NULL
                   OR L.LocationID LIKE @Search
                   OR L.LocationName LIKE @Search
                   OR L.PlantCode LIKE @Search
                   OR L.ZoneCode LIKE @Search
                   OR L.LocationType LIKE @Search)
            ORDER BY L.PlantCode, L.ZoneCode, L.LocationType,
                     TRY_CONVERT(int, L.Slot), L.Slot,
                     TRY_CONVERT(int, L.Bay), L.Bay,
                     TRY_CONVERT(int, L.Aisle), L.Aisle,
                     L.LocationID;
            """;

        return Query(sql, r => new LocationRow(
            GetString(r, "LocationID") ?? "",
            GetString(r, "LocationName"),
            GetString(r, "WarehouseCode"),
            GetString(r, "WarehouseName"),
            GetString(r, "AreaCode"),
            GetString(r, "ZoneCode"),
            GetString(r, "ColumnNo"),
            GetString(r, "RowNo"),
            GetString(r, "LevelNo"),
            GetDecimal(r, "Capacity"),
            GetBool(r, "Active"),
            GetInt(r, "LotCount"),
            GetInt(r, "PartCount"),
            GetDecimal(r, "Qty"),
            GetString(r, "Status") ?? "EMPTY"),
            ("@Search", Like(search)),
            ("@WarehouseCode", NullIfBlank(warehouseCode)),
            ("@AreaCode", NullIfBlank(areaCode)),
            ("@ZoneCode", NullIfBlank(zoneCode)),
            ("@LevelNo", NullIfBlank(levelNo)),
            ("@IncludeInactive", includeInactive));
    }

    public bool LocationExists(string locationNo)
    {
        EnsureFgLocationMasterTable();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_Location WHERE LocationID = @LocationID;", conn);
        cmd.Parameters.Add("@LocationID", SqlDbType.VarChar, 20).Value = locationNo.Trim();
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveLocation(
        string locationNo,
        string? locationName,
        string warehouseCode,
        string areaCode,
        string zoneCode,
        string? columnNo,
        string? rowNo,
        string? levelNo,
        decimal capacity,
        bool active,
        string modifiedBy)
    {
        EnsureFgLocationMasterTable();
        if (string.IsNullOrWhiteSpace(locationNo)) throw new ArgumentException("Location Code is required.");
        if (string.IsNullOrWhiteSpace(warehouseCode)) throw new ArgumentException("Warehouse is required.");
        if (string.IsNullOrWhiteSpace(areaCode)) throw new ArgumentException("Area is required.");
        if (string.IsNullOrWhiteSpace(zoneCode)) throw new ArgumentException("Zone is required.");

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            MERGE dbo.MD_Location AS target
            USING (SELECT @LocationID AS LocationID) AS source
               ON target.LocationID = source.LocationID
            WHEN MATCHED THEN UPDATE SET
                LocationName = @LocationName,
                PlantCode = @WarehouseCode,
                ZoneCode = @AreaCode,
                LocationType = @ZoneCode,
                Aisle = @ColumnNo,
                Bay = @RowNo,
                Slot = @LevelNo,
                Capacity = @Capacity,
                ActiveFlag = @Active,
                ModifiedBy = @ModifiedBy,
                ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (LocationID, LocationName, PlantCode, ZoneCode, LocationType, Aisle, Bay, Slot,
                 Capacity, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (@LocationID, @LocationName, @WarehouseCode, @AreaCode, @ZoneCode, @ColumnNo, @RowNo, @LevelNo,
                 @Capacity, @Active, @ModifiedBy, SYSDATETIME());

            MERGE dbo.FG_LocationMaster AS target
            USING (SELECT @LocationID AS LocationID) AS source
               ON target.LocationID = source.LocationID
            WHEN MATCHED THEN UPDATE SET
                ActiveFlag = @Active,
                ModifiedBy = @ModifiedBy,
                ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (LocationID, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (@LocationID, @Active, @ModifiedBy, SYSDATETIME());
            """, conn);
        AddText(cmd, "@LocationID", SqlDbType.VarChar, 20, locationNo, false);
        AddText(cmd, "@LocationName", SqlDbType.NVarChar, 120, locationName);
        AddText(cmd, "@WarehouseCode", SqlDbType.VarChar, 20, warehouseCode, false);
        AddText(cmd, "@AreaCode", SqlDbType.VarChar, 20, areaCode, false);
        AddText(cmd, "@ZoneCode", SqlDbType.VarChar, 20, zoneCode, false);
        AddText(cmd, "@ColumnNo", SqlDbType.VarChar, 10, columnNo);
        AddText(cmd, "@RowNo", SqlDbType.VarChar, 10, rowNo);
        AddText(cmd, "@LevelNo", SqlDbType.VarChar, 10, levelNo);
        var qty = cmd.Parameters.Add("@Capacity", SqlDbType.Decimal);
        qty.Precision = 14;
        qty.Scale = 3;
        qty.Value = Math.Max(0, capacity);
        cmd.Parameters.Add("@Active", SqlDbType.Bit).Value = active;
        AddText(cmd, "@ModifiedBy", SqlDbType.NVarChar, 120, modifiedBy, false);
        cmd.ExecuteNonQuery();
    }

    public void DeleteLocation(string locationNo)
    {
        EnsureFgLocationMasterTable();
        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand("""
            SELECT
                (SELECT COUNT(1) FROM dbo.FG_Inventory WHERE Location = @LocationID)
              + (SELECT COUNT(1) FROM dbo.WH_Inventory WHERE LocationID = @LocationID AND ISNULL(OnHandQty, 0) <> 0);
            """, conn);
        check.Parameters.Add("@LocationID", SqlDbType.VarChar, 20).Value = locationNo.Trim();
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("This location has inventory and cannot be deleted.");

        using var cmd = new SqlCommand("""
            DELETE FROM dbo.FG_LocationMaster WHERE LocationID = @LocationID;
            DELETE FROM dbo.MD_Location WHERE LocationID = @LocationID;
            """, conn);
        cmd.Parameters.Add("@LocationID", SqlDbType.VarChar, 20).Value = locationNo.Trim();
        cmd.ExecuteNonQuery();
    }

    public List<ReturnRow> ListReturns(
        string? search = null,
        string? status = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        const string sql = """
            SELECT
                R.ReturnID,
                R.ReturnNumber,
                R.RMANo,
                R.CustomerCode,
                R.OriginalShipmentOrderID,
                O.ShipOrderNumber,
                O.CustomerPO,
                JSON_VALUE(R.ItemsJSON, '$[0].itemNo') AS ItemNo,
                TRY_CONVERT(decimal(14,3), JSON_VALUE(R.ItemsJSON, '$[0].qty')) AS Qty,
                R.ReturnReason,
                R.Status,
                R.ReceivedAt,
                R.ReceivedBy,
                CAST(ISNULL(R.CapaTriggered, 0) AS bit) AS CapaTriggered,
                R.ItemsJSON
            FROM dbo.FG_CustomerReturn R
            LEFT JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID = R.OriginalShipmentOrderID
            WHERE (@Status IS NULL OR UPPER(R.Status) = UPPER(@Status))
              AND (@From IS NULL OR R.ReceivedAt >= @From)
              AND (@To IS NULL OR R.ReceivedAt < DATEADD(day, 1, @To))
              AND (@Search IS NULL
                   OR R.ReturnNumber LIKE @Search
                   OR R.RMANo LIKE @Search
                   OR R.CustomerCode LIKE @Search
                   OR R.ReturnReason LIKE @Search
                   OR R.ReceivedBy LIKE @Search
                   OR O.ShipOrderNumber LIKE @Search
                   OR R.ItemsJSON LIKE @Search)
            ORDER BY R.ReceivedAt DESC, R.ReturnID DESC;
            """;

        return Query(sql, r => new ReturnRow(
            GetInt(r, "ReturnID"),
            GetString(r, "ReturnNumber") ?? "",
            GetString(r, "RMANo"),
            GetString(r, "CustomerCode"),
            GetNullableInt(r, "OriginalShipmentOrderID"),
            GetString(r, "ShipOrderNumber"),
            GetString(r, "CustomerPO"),
            GetString(r, "ItemNo"),
            GetDecimal(r, "Qty"),
            GetString(r, "ReturnReason"),
            GetString(r, "Status"),
            GetDate(r, "ReceivedAt"),
            GetString(r, "ReceivedBy"),
            GetBool(r, "CapaTriggered"),
            GetString(r, "ItemsJSON")),
            ("@Search", Like(search)),
            ("@Status", NullIfBlank(status)),
            ("@From", from?.Date),
            ("@To", to?.Date));
    }

    public List<InventoryRow> ListInventory(string? locationNo = null, string? search = null)
    {
        const string sql = """
            SELECT TOP 500
                S.StockID,
                S.StockNumber,
                S.ItemNo,
                I.ItemName,
                L.LotCode AS LotNo,
                CAST(ISNULL(S.Qty, 0) AS decimal(14,3)) AS Qty,
                I.DefaultUOM AS Unit,
                S.Location,
                S.Status,
                S.StockTS
            FROM dbo.FG_Inventory S
            LEFT JOIN dbo.MD_Item I ON I.ItemNo = S.ItemNo
            LEFT JOIN dbo.tbl_Lot L ON L.LotID = S.LotID
            WHERE (@LocationNo IS NULL OR S.Location = @LocationNo)
              AND UPPER(ISNULL(S.Status, 'AVAILABLE')) NOT IN ('CANCELED', 'CANCELLED', 'SHIPPED')
              AND (@Search IS NULL
                   OR S.StockNumber LIKE @Search
                   OR S.ItemNo LIKE @Search
                   OR I.ItemName LIKE @Search
                   OR L.LotCode LIKE @Search
                   OR S.Location LIKE @Search)
            ORDER BY S.Location, S.StockTS, S.StockID;
            """;

        return Query(sql, r => new InventoryRow(
            GetInt(r, "StockID"),
            GetString(r, "StockNumber"),
            GetString(r, "ItemNo") ?? "",
            GetString(r, "ItemName"),
            GetString(r, "LotNo"),
            GetDecimal(r, "Qty"),
            GetString(r, "Unit"),
            GetString(r, "Location"),
            GetString(r, "Status"),
            GetDate(r, "StockTS")),
            ("@LocationNo", NullIfBlank(locationNo)),
            ("@Search", Like(search)));
    }

    public List<ShipmentRow> ListShipments(string? search = null, string? status = null, DateTime? from = null, DateTime? to = null)
    {
        const string sql = """
            WITH LineSummary AS
            (
                SELECT ShipmentOrderID, COUNT(*) AS LineCount, SUM(ISNULL(OrderedQty, 0)) AS OrderedQty
                FROM dbo.FG_ShipmentOrderLine
                GROUP BY ShipmentOrderID
            )
            SELECT
                O.ShipmentOrderID,
                O.ShipOrderNumber,
                O.CustomerCode,
                O.CustomerPO,
                O.ShipDate,
                O.Status,
                O.PickslipID,
                COALESCE(NULLIF(L.CarrierCode, ''), O.CarrierCode) AS CarrierCode,
                CONCAT_WS(' / ', NULLIF(O.DestPlant, ''), NULLIF(O.DestDock, '')) AS Destination,
                L.LoadingID,
                L.LoadingNumber,
                L.LicensePlate,
                L.DriverName,
                L.DockNo,
                L.SealNo,
                L.ConfirmedAt,
                L.DepartureTS,
                L.OperatorID,
                L.OTDStatus,
                D.DnNumber,
                D.IssuedAt,
                ISNULL(S.LineCount, 0) AS LineCount,
                CAST(ISNULL(S.OrderedQty, 0) AS decimal(14,3)) AS OrderedQty
            FROM dbo.FG_ShipmentOrder O
            LEFT JOIN LineSummary S ON S.ShipmentOrderID = O.ShipmentOrderID
            LEFT JOIN dbo.FG_LoadingConfirm L ON L.ShipmentOrderID = O.ShipmentOrderID
            OUTER APPLY
            (
                SELECT TOP 1 DN.DnNumber, DN.IssuedAt
                FROM dbo.FG_DeliveryNote DN
                WHERE DN.ShipmentOrderID = O.ShipmentOrderID
                ORDER BY DN.IssuedAt DESC, DN.DeliveryNoteID DESC
            ) D
            WHERE (L.LoadingID IS NOT NULL OR UPPER(ISNULL(O.Status, '')) IN ('LOADED', 'SHIPPED'))
              AND (@Status IS NULL
                   OR (@Status = 'LOADED' AND L.LoadingID IS NOT NULL AND UPPER(ISNULL(O.Status, '')) <> 'SHIPPED')
                   OR (@Status = 'SHIPPED' AND UPPER(ISNULL(O.Status, '')) = 'SHIPPED'))
              AND (@From IS NULL OR COALESCE(L.ConfirmedAt, L.DepartureTS, O.ModifiedTS, O.ConfirmedAt, O.CreatedTS) >= @From)
              AND (@To IS NULL OR COALESCE(L.ConfirmedAt, L.DepartureTS, O.ModifiedTS, O.ConfirmedAt, O.CreatedTS) < DATEADD(day, 1, @To))
              AND (@Search IS NULL
                   OR O.ShipOrderNumber LIKE @Search
                   OR O.PickslipID LIKE @Search
                   OR O.CustomerCode LIKE @Search
                   OR O.CustomerPO LIKE @Search
                   OR L.LoadingNumber LIKE @Search
                   OR L.LicensePlate LIKE @Search
                   OR L.DriverName LIKE @Search
                   OR D.DnNumber LIKE @Search)
            ORDER BY COALESCE(L.ConfirmedAt, L.DepartureTS, O.ModifiedTS, O.ConfirmedAt, O.CreatedTS) DESC,
                     O.ShipmentOrderID DESC;
            """;

        return Query(sql, r => new ShipmentRow(
            GetInt(r, "ShipmentOrderID"),
            GetString(r, "ShipOrderNumber"),
            GetString(r, "CustomerCode"),
            GetString(r, "CustomerPO"),
            GetDate(r, "ShipDate"),
            GetString(r, "Status"),
            GetString(r, "PickslipID"),
            GetString(r, "CarrierCode"),
            GetString(r, "Destination"),
            GetNullableInt(r, "LoadingID"),
            GetString(r, "LoadingNumber"),
            GetString(r, "LicensePlate"),
            GetString(r, "DriverName"),
            GetString(r, "DockNo"),
            GetString(r, "SealNo"),
            GetDate(r, "ConfirmedAt"),
            GetDate(r, "DepartureTS"),
            GetString(r, "OperatorID"),
            GetString(r, "OTDStatus"),
            GetString(r, "DnNumber"),
            GetDate(r, "IssuedAt"),
            GetInt(r, "LineCount"),
            GetDecimal(r, "OrderedQty")),
            ("@Search", Like(search)),
            ("@Status", NullIfBlank(status)?.ToUpperInvariant()),
            ("@From", from?.Date),
            ("@To", to?.Date));
    }

    public List<HistoryRow> ListHistory(string? search = null, string? eventType = null, DateTime? from = null, DateTime? to = null)
    {
        const string sql = """
            SELECT TOP 1000 H.EventAt, H.EventType, H.ReferenceNo, H.ItemOrOrder, H.Qty,
                   H.Location, H.WorkerID, H.Status, H.Details
            FROM
            (
                SELECT
                    COALESCE(P.ModifiedTS, P.CreatedTS) AS EventAt,
                    CAST('PUT AWAY' AS varchar(20)) AS EventType,
                    CAST(CONCAT('PA-', P.PutAwayID) AS nvarchar(80)) AS ReferenceNo,
                    CAST(P.ItemNo AS nvarchar(80)) AS ItemOrOrder,
                    CAST(ISNULL(P.Qty, 0) AS decimal(14,3)) AS Qty,
                    CAST(P.ActualLoc AS nvarchar(80)) AS Location,
                    CAST(COALESCE(P.OperatorID, P.CreatedBy) AS nvarchar(120)) AS WorkerID,
                    CAST(P.Status AS nvarchar(40)) AS Status,
                    CAST(CONCAT('Suggested ', ISNULL(P.SuggestedLoc, '-'), ' / Actual ', ISNULL(P.ActualLoc, '-')) AS nvarchar(300)) AS Details
                FROM dbo.FG_PutAway P

                UNION ALL

                SELECT
                    COALESCE(P.EndTS, P.StartTS, P.CreatedTS),
                    'PICK',
                    CAST(COALESCE(P.PickNumber, P.PickslipID, CONCAT('PICK-', P.PickID)) AS nvarchar(80)),
                    CAST(O.ShipOrderNumber AS nvarchar(80)),
                    CAST(ISNULL(P.PickedQty, 0) AS decimal(14,3)),
                    NULL,
                    CAST(COALESCE(P.PickerID, P.CreatedBy) AS nvarchar(120)),
                    CAST(P.Status AS nvarchar(40)),
                    CAST(CONCAT('FIFO violations ', ISNULL(P.FifoViolations, 0), ' / Ordered ', ISNULL(P.OrderedQty, 0)) AS nvarchar(300))
                FROM dbo.FG_PickingFifo P
                LEFT JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID = P.ShipmentOrderID

                UNION ALL

                SELECT
                    COALESCE(L.ConfirmedAt, L.DepartureTS, L.CreatedTS),
                    'LOADING',
                    CAST(COALESCE(L.LoadingNumber, CONCAT('LOAD-', L.LoadingID)) AS nvarchar(80)),
                    CAST(O.ShipOrderNumber AS nvarchar(80)),
                    CAST(0 AS decimal(14,3)),
                    CAST(L.DockNo AS nvarchar(80)),
                    CAST(COALESCE(L.OperatorID, L.CreatedBy) AS nvarchar(120)),
                    CAST(L.OTDStatus AS nvarchar(40)),
                    CAST(CONCAT('Truck ', ISNULL(L.LicensePlate, '-'), ' / Driver ', ISNULL(L.DriverName, '-'), ' / Seal ', ISNULL(L.SealNo, '-')) AS nvarchar(300))
                FROM dbo.FG_LoadingConfirm L
                LEFT JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID = L.ShipmentOrderID

                UNION ALL

                SELECT
                    COALESCE(O.ConfirmedAt, O.ModifiedTS, O.CreatedTS),
                    'SHIPPED',
                    CAST(O.ShipOrderNumber AS nvarchar(80)),
                    CAST(O.CustomerCode AS nvarchar(80)),
                    CAST(ISNULL(S.OrderedQty, 0) AS decimal(14,3)),
                    CAST(O.DestDock AS nvarchar(80)),
                    CAST(COALESCE(O.ConfirmedBy, O.ModifiedBy, O.CreatedBy) AS nvarchar(120)),
                    CAST(O.Status AS nvarchar(40)),
                    CAST(CONCAT('Destination ', ISNULL(O.DestPlant, '-'), ' / Carrier ', ISNULL(O.CarrierCode, '-')) AS nvarchar(300))
                FROM dbo.FG_ShipmentOrder O
                OUTER APPLY (SELECT SUM(ISNULL(OrderedQty, 0)) AS OrderedQty FROM dbo.FG_ShipmentOrderLine SL WHERE SL.ShipmentOrderID = O.ShipmentOrderID) S
                WHERE UPPER(ISNULL(O.Status, '')) = 'SHIPPED'

                UNION ALL

                SELECT
                    COALESCE(D.IssuedAt, D.CreatedTS),
                    'DELIVERY NOTE',
                    CAST(D.DnNumber AS nvarchar(80)),
                    CAST(O.ShipOrderNumber AS nvarchar(80)),
                    CAST(0 AS decimal(14,3)),
                    NULL,
                    CAST(COALESCE(D.IssuedBy, D.CreatedBy) AS nvarchar(120)),
                    CAST(D.EdiStatus AS nvarchar(40)),
                    CAST(CONCAT('Customer ', ISNULL(D.CustomerCode, '-'), ' / Revision ', ISNULL(D.Revision, 0)) AS nvarchar(300))
                FROM dbo.FG_DeliveryNote D
                LEFT JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID = D.ShipmentOrderID

                UNION ALL

                SELECT
                    COALESCE(R.ReceivedAt, R.CreatedTS),
                    'RETURN',
                    CAST(COALESCE(R.ReturnNumber, R.RMANo, CONCAT('RETURN-', R.ReturnID)) AS nvarchar(80)),
                    CAST(R.CustomerCode AS nvarchar(80)),
                    CAST(ISNULL(TRY_CONVERT(decimal(14,3), JSON_VALUE(R.ItemsJSON, '$[0].qty')), 0) AS decimal(14,3)),
                    NULL,
                    CAST(COALESCE(R.ReceivedBy, R.CreatedBy) AS nvarchar(120)),
                    CAST(R.Status AS nvarchar(40)),
                    CAST(R.ReturnReason AS nvarchar(300))
                FROM dbo.FG_CustomerReturn R
            ) H
            WHERE H.EventAt IS NOT NULL
              AND (@EventType IS NULL OR H.EventType = @EventType)
              AND (@From IS NULL OR H.EventAt >= @From)
              AND (@To IS NULL OR H.EventAt < DATEADD(day, 1, @To))
              AND (@Search IS NULL
                   OR H.ReferenceNo LIKE @Search
                   OR H.ItemOrOrder LIKE @Search
                   OR H.Location LIKE @Search
                   OR H.WorkerID LIKE @Search
                   OR H.Details LIKE @Search)
            ORDER BY H.EventAt DESC, H.ReferenceNo DESC;
            """;

        return Query(sql, r => new HistoryRow(
            GetDate(r, "EventAt") ?? DateTime.MinValue,
            GetString(r, "EventType") ?? "EVENT",
            GetString(r, "ReferenceNo"),
            GetString(r, "ItemOrOrder"),
            GetDecimal(r, "Qty"),
            GetString(r, "Location"),
            GetString(r, "WorkerID"),
            GetString(r, "Status"),
            GetString(r, "Details")),
            ("@Search", Like(search)),
            ("@EventType", NullIfBlank(eventType)?.ToUpperInvariant()),
            ("@From", from?.Date),
            ("@To", to?.Date));
    }

    private void EnsureFgLocationMasterTable()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.FG_LocationMaster', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.FG_LocationMaster
                (
                    LocationID varchar(20) NOT NULL,
                    ActiveFlag bit NOT NULL CONSTRAINT DF_FG_LocationMaster_ActiveFlag DEFAULT (1),
                    CreatedBy nvarchar(120) NOT NULL,
                    CreatedTS datetime2 NOT NULL CONSTRAINT DF_FG_LocationMaster_CreatedTS DEFAULT SYSDATETIME(),
                    ModifiedBy nvarchar(120) NULL,
                    ModifiedTS datetime2 NULL,
                    CONSTRAINT PK_FG_LocationMaster PRIMARY KEY CLUSTERED (LocationID)
                );
            END;
            """, conn);
        cmd.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, Func<SqlDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(map(reader));
        return rows;
    }

    private static void AddText(SqlCommand cmd, string name, SqlDbType type, int size, string? value, bool nullable = true)
    {
        var parameter = cmd.Parameters.Add(name, type, size);
        parameter.Value = nullable && string.IsNullOrWhiteSpace(value) ? DBNull.Value : (value ?? "").Trim();
    }

    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? GetString(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToString(reader[name]);
    private static bool GetBool(SqlDataReader reader, string name) => reader[name] != DBNull.Value && Convert.ToBoolean(reader[name]);
    private static int GetInt(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? 0 : Convert.ToInt32(reader[name]);
    private static int? GetNullableInt(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToInt32(reader[name]);
    private static decimal GetDecimal(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? 0 : Convert.ToDecimal(reader[name]);
    private static DateTime? GetDate(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToDateTime(reader[name]);
}
