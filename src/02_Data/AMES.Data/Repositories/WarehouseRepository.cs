using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed class WarehouseRepository
{
    private const string DefaultNormalColor = "#16A34A";

    private readonly AmesConnectionFactory _factory;

    public WarehouseRepository(AmesConnectionFactory factory) => _factory = factory;

    public record WarehouseLocationRow(
        string LocationNo,
        string? LocationName,
        string? WhCode,
        string? WhName,
        string? AreaCode,
        string? AreaName,
        string? ZoneCode,
        string? ZoneName,
        string? RackX,
        string? RackY,
        string? RackZ,
        bool UseYn,
        int LotCount,
        int PartCount,
        decimal TotalQty);

    public record PickingOrderRow(
        string PickSlipNo,
        string? ReqDate,
        string? ReqLocation,
        int? SeqNo,
        string? PartNo,
        decimal ReqBoxQty,
        string? ReqUserId,
        string? ReqTime,
        DateTime? PrintDate,
        string? CloseYn,
        DateTime? CloseDate,
        string Status,
        decimal PickedQty);

    public record PartOptionRow(string PartNo);

    public record LocationMapRow(
        string LocationNo,
        string? LocationName,
        string? AreaCode,
        string? AreaName,
        string? ZoneCode,
        string? ZoneName,
        string? RackX,
        string? RackY,
        string? RackZ,
        int LotCount,
        int PartCount,
        decimal TotalQty,
        string Status);

    public record LocationAreaLayoutRow(
        string AreaCode,
        string? AreaName,
        decimal XPct,
        decimal YPct,
        decimal WPct,
        decimal HPct);

    public record OperationLogRow(
        long OperationLogId,
        DateTime? EventTime,
        string EventType,
        string? ScreenCode,
        string? EmployeeNo,
        string? EmployeeName,
        string? WorkerId,
        string? TerminalId,
        string? LineId,
        string? ShiftCode,
        string? ScanType,
        string? ScanValue,
        string Result,
        string? Message,
        string? ClientIp,
        string? RefDocType,
        string? RefDocNo,
        string? LotNo,
        string? PartNo,
        string? LocationId,
        decimal? Qty);

    public record InventorySettingRow(
        string ItemNo,
        string? ItemName,
        string? DefaultUom,
        decimal CurrentQty,
        decimal MinQty,
        decimal MaxQty,
        decimal ShortageQty,
        string Status,
        string StatusColor,
        int LocationCount,
        int LotCount,
        DateTime? ModifiedTs);

    public List<WarehouseLocationRow> ListLocations(string? search = null, bool includeInactive = false)
    {
        var like = Like(search);
        return Query("""
            SELECT
                L.LocationID AS LOCATION_NO,
                L.LocationName AS LOCATION_NM,
                L.PlantCode AS WHCD,
                L.PlantCode AS WHNM,
                COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) AS AREACD,
                COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) AS AREANM,
                L.ZoneCode AS ZONECD,
                L.LocationType AS ZONENM,
                L.Aisle AS RACK_X,
                L.Bay AS RACK_Y,
                L.Slot AS RACK_Z,
                CAST(COALESCE(L.ActiveFlag, 1) AS bit) AS USE_YN,
                COUNT(DISTINCT S.LotID) AS LOT_COUNT,
                COUNT(DISTINCT S.ItemNo) AS PART_COUNT,
                COALESCE(SUM(S.OnHandQty), 0) AS TOTAL_QTY
            FROM dbo.MD_Location L
            LEFT JOIN dbo.WH_Inventory S
                   ON S.LocationID = L.LocationID
                  AND COALESCE(S.OnHandQty, 0) <> 0
                  AND UPPER(COALESCE(S.Status, 'RECEIVED')) NOT IN ('CANCELED')
            WHERE (@IncludeInactive = 1 OR COALESCE(L.ActiveFlag, 1) = 1)
              AND (@Search IS NULL
                   OR L.LocationID LIKE @Search
                   OR L.LocationName LIKE @Search
                   OR L.ZoneCode LIKE @Search
                   OR L.LocationType LIKE @Search
                   OR L.PlantCode LIKE @Search)
            GROUP BY L.LocationID, L.LocationName, L.PlantCode, L.ZoneCode,
                     L.LocationType, L.Aisle, L.Bay, L.Slot, L.ActiveFlag
            ORDER BY COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode), L.ZoneCode,
                     TRY_CONVERT(int, L.Aisle), L.Aisle,
                     TRY_CONVERT(int, L.Bay), L.Bay,
                     TRY_CONVERT(int, L.Slot), L.Slot,
                     L.LocationID;
            """, r => new WarehouseLocationRow(
                GetString(r, "LOCATION_NO") ?? "",
                GetString(r, "LOCATION_NM"),
                GetString(r, "WHCD"),
                GetString(r, "WHNM"),
                GetString(r, "AREACD"),
                GetString(r, "AREANM"),
                GetString(r, "ZONECD"),
                GetString(r, "ZONENM"),
                GetString(r, "RACK_X"),
                GetString(r, "RACK_Y"),
                GetString(r, "RACK_Z"),
                GetBool(r, "USE_YN"),
                GetInt(r, "LOT_COUNT"),
                GetInt(r, "PART_COUNT"),
                GetDecimal(r, "TOTAL_QTY")),
            ("@Search", like),
            ("@IncludeInactive", includeInactive));
    }

    public bool LocationExists(string locationNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Location WHERE LocationID = @LocationNo;", conn);
        cmd.Parameters.Add("@LocationNo", SqlDbType.VarChar, 20).Value = locationNo;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertLocation(
        string locationNo,
        string? locationName,
        string? whCode,
        string? whName,
        string? areaCode,
        string? areaName,
        string? zoneCode,
        string? zoneName,
        string? rackX,
        string? rackY,
        string? rackZ,
        bool useYn)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_Location
                (LocationID, LocationName, ZoneCode, Aisle, Bay, Slot,
                 LocationType, PlantCode, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (@LocationNo, @LocationName, @ZoneCode, @RackX, @RackY, @RackZ,
                 @LocationType, @PlantCode, @UseYn, 'web', SYSDATETIME());
            """, conn);
        AddLocationParameters(cmd, locationNo, locationName, whCode,
            areaCode, areaName, zoneCode, zoneName, rackX, rackY, rackZ, useYn);
        cmd.ExecuteNonQuery();
    }

    public void UpdateLocation(
        string locationNo,
        string? locationName,
        string? whCode,
        string? whName,
        string? areaCode,
        string? areaName,
        string? zoneCode,
        string? zoneName,
        string? rackX,
        string? rackY,
        string? rackZ,
        bool useYn)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_Location
            SET LocationName = @LocationName,
                ZoneCode = @ZoneCode,
                Aisle = @RackX,
                Bay = @RackY,
                Slot = @RackZ,
                LocationType = @LocationType,
                PlantCode = @PlantCode,
                ActiveFlag = @UseYn,
                ModifiedBy = 'web',
                ModifiedTS = SYSDATETIME()
            WHERE LocationID = @LocationNo;
            """, conn);
        AddLocationParameters(cmd, locationNo, locationName, whCode,
            areaCode, areaName, zoneCode, zoneName, rackX, rackY, rackZ, useYn);
        cmd.ExecuteNonQuery();
    }

    public void DeleteLocation(string locationNo)
    {
        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.WH_Inventory WHERE LocationID = @LocationNo AND COALESCE(OnHandQty, 0) <> 0;", conn);
        check.Parameters.Add("@LocationNo", SqlDbType.VarChar, 20).Value = locationNo;
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Location has inventory and cannot be deleted.");

        using var cmd = new SqlCommand("DELETE FROM dbo.MD_Location WHERE LocationID = @LocationNo;", conn);
        cmd.Parameters.Add("@LocationNo", SqlDbType.VarChar, 20).Value = locationNo;
        cmd.ExecuteNonQuery();
    }

    public List<PickingOrderRow> ListPickingOrders(string? search = null, bool includeClosed = true)
    {
        var like = Like(search);
        return Query("""
            SELECT
                CONCAT(N'RS-', O.ReleaseScheduleID) AS PICK_SLIPNO,
                CONVERT(varchar(10), O.RequiredAt, 23) AS REQ_DATE,
                CAST(NULL AS nvarchar(50)) AS REQ_LOCATION,
                O.ReleaseScheduleID AS SEQNO,
                O.ItemNo AS PARTNO,
                COALESCE(O.DemandQty, 0) AS REQ_BOX_QTY,
                O.CreatedBy AS REQ_USERID,
                CONVERT(varchar(8), CONVERT(time(0), O.RequiredAt)) AS REQ_TIME,
                O.CreatedTS AS PRINT_DATE,
                CASE WHEN UPPER(COALESCE(O.Status, 'OPEN')) IN ('CLOSED', 'CANCELED') THEN N'Y' ELSE N'N' END AS CLOSE_YN,
                CAST(NULL AS datetime2) AS CLOSE_DATE,
                COALESCE(O.PickedQty, 0) AS PICKED_QTY,
                CASE
                    WHEN UPPER(COALESCE(O.Status, 'OPEN')) IN ('CLOSED', 'CANCELED') THEN N'Closed'
                    WHEN COALESCE(O.PickedQty, 0) >= COALESCE(O.DemandQty, 0) AND COALESCE(O.DemandQty, 0) > 0 THEN N'Picked'
                    WHEN COALESCE(O.PickedQty, 0) > 0 THEN N'Partial'
                    ELSE N'Open'
                END AS STATUS
            FROM dbo.WH_ReleaseSchedule O
            WHERE (@IncludeClosed = 1 OR UPPER(COALESCE(O.Status, 'OPEN')) NOT IN ('CLOSED', 'CANCELED'))
              AND (@Search IS NULL
                   OR CONCAT(N'RS-', O.ReleaseScheduleID) LIKE @Search
                   OR O.ItemNo LIKE @Search
                   OR O.CreatedBy LIKE @Search
                   OR O.Status LIKE @Search)
            ORDER BY O.RequiredAt DESC, O.ReleaseScheduleID DESC;
            """, r => new PickingOrderRow(
                GetString(r, "PICK_SLIPNO") ?? "",
                GetString(r, "REQ_DATE"),
                GetString(r, "REQ_LOCATION"),
                GetNullableInt(r, "SEQNO"),
                GetString(r, "PARTNO"),
                GetDecimal(r, "REQ_BOX_QTY"),
                GetString(r, "REQ_USERID"),
                GetString(r, "REQ_TIME"),
                GetDateTime(r, "PRINT_DATE"),
                GetString(r, "CLOSE_YN"),
                GetDateTime(r, "CLOSE_DATE"),
                GetString(r, "STATUS") ?? "Open",
                GetDecimal(r, "PICKED_QTY")),
            ("@Search", like),
            ("@IncludeClosed", includeClosed));
    }

    public string InsertPickingOrderLine(
        string? pickSlipNo,
        string reqDate,
        string reqLocation,
        int seqNo,
        string partNo,
        decimal reqBoxQty,
        string reqUserId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.WH_ReleaseSchedule
                (ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy, CreatedTS)
            OUTPUT INSERTED.ReleaseScheduleID
            VALUES
                (@PartNo, @ReqBoxQty, 0, @RequiredAt, @Priority, 'Open', @ReqUserId, SYSDATETIME());
            """, conn);
        var requiredAt = DateTime.TryParse(reqDate, out var parsedDate) ? parsedDate.Date : DateTime.Today;
        cmd.Parameters.Add("@PartNo", SqlDbType.VarChar, 20).Value = partNo;
        cmd.Parameters.Add("@ReqBoxQty", SqlDbType.Decimal).Value = reqBoxQty;
        cmd.Parameters["@ReqBoxQty"].Precision = 18;
        cmd.Parameters["@ReqBoxQty"].Scale = 3;
        cmd.Parameters.Add("@RequiredAt", SqlDbType.DateTime2).Value = requiredAt;
        cmd.Parameters.Add("@Priority", SqlDbType.TinyInt).Value = Math.Clamp(seqNo / 10, 1, 9);
        cmd.Parameters.Add("@ReqUserId", SqlDbType.VarChar, 50).Value = Truncate(reqUserId, 50);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return $"RS-{id}";
    }

    public List<PartOptionRow> ListPartOptions()
    {
        return Query("""
            SELECT TOP (300) PARTNO
            FROM (
                SELECT ItemNo AS PARTNO FROM dbo.WH_Inventory WHERE ItemNo IS NOT NULL AND ItemNo <> N''
                UNION
                SELECT ItemNo AS PARTNO FROM dbo.WH_ReleaseSchedule WHERE ItemNo IS NOT NULL AND ItemNo <> N''
                UNION
                SELECT ItemNo AS PARTNO FROM dbo.MD_Item WHERE ItemNo IS NOT NULL AND ItemNo <> N''
            ) P
            ORDER BY PARTNO;
            """, r => new PartOptionRow(GetString(r, "PARTNO") ?? ""));
    }

    public List<LocationMapRow> ListLocationMap(string? areaCode = null, string? rackZ = null)
    {
        return Query("""
            SELECT
                L.LocationID AS LOCATION_NO,
                L.LocationName AS LOCATION_NM,
                COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) AS AREACD,
                COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) AS AREANM,
                L.ZoneCode AS ZONECD,
                L.LocationType AS ZONENM,
                L.Aisle AS RACK_X,
                L.Bay AS RACK_Y,
                L.Slot AS RACK_Z,
                COUNT(DISTINCT S.LotID) AS LOT_COUNT,
                COUNT(DISTINCT S.ItemNo) AS PART_COUNT,
                COALESCE(SUM(S.OnHandQty), 0) AS TOTAL_QTY,
                CASE
                    WHEN COALESCE(SUM(S.OnHandQty), 0) = 0 THEN N'Empty'
                    WHEN COUNT(DISTINCT S.ItemNo) > 1 THEN N'Mixed'
                    ELSE N'Stocked'
                END AS STATUS
            FROM dbo.MD_Location L
            LEFT JOIN dbo.WH_Inventory S
                   ON S.LocationID = L.LocationID
                  AND COALESCE(S.OnHandQty, 0) <> 0
                  AND UPPER(COALESCE(S.Status, 'RECEIVED')) NOT IN ('CANCELED')
            WHERE COALESCE(L.ActiveFlag, 1) = 1
              AND (@AreaCode IS NULL OR COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = @AreaCode)
              AND (@RackZ IS NULL OR L.Slot = @RackZ)
            GROUP BY L.LocationID, L.LocationName, L.PlantCode, L.ZoneCode,
                     L.LocationType, L.Aisle, L.Bay, L.Slot
            ORDER BY COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode), L.ZoneCode,
                     TRY_CONVERT(int, L.Aisle), L.Aisle,
                     TRY_CONVERT(int, L.Bay), L.Bay,
                     TRY_CONVERT(int, L.Slot), L.Slot,
                     L.LocationID;
            """, r => new LocationMapRow(
                GetString(r, "LOCATION_NO") ?? "",
                GetString(r, "LOCATION_NM"),
                GetString(r, "AREACD"),
                GetString(r, "AREANM"),
                GetString(r, "ZONECD"),
                GetString(r, "ZONENM"),
                GetString(r, "RACK_X"),
                GetString(r, "RACK_Y"),
                GetString(r, "RACK_Z"),
                GetInt(r, "LOT_COUNT"),
                GetInt(r, "PART_COUNT"),
                GetDecimal(r, "TOTAL_QTY"),
                GetString(r, "STATUS") ?? "Empty"),
            ("@AreaCode", NullIfBlank(areaCode)),
            ("@RackZ", NullIfBlank(rackZ)));
    }

    public List<LocationAreaLayoutRow> ListLocationAreaLayouts()
    {
        EnsureAreaLayoutTable();

        return Query("""
            WITH Areas AS (
                SELECT
                    COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode, 'WH') AS AREACD,
                    MAX(COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode, 'WH')) AS AREANM,
                    ROW_NUMBER() OVER (ORDER BY COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode, 'WH')) AS RN
                FROM dbo.MD_Location L
                WHERE COALESCE(L.ActiveFlag, 1) = 1
                GROUP BY COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode, 'WH')
            )
            SELECT
                A.AREACD,
                A.AREANM,
                COALESCE(M.X_PCT, CAST(4 + ((A.RN - 1) % 3) * 31 AS decimal(5,2))) AS X_PCT,
                COALESCE(M.Y_PCT, CAST(8 + ((A.RN - 1) / 3) * 28 AS decimal(5,2))) AS Y_PCT,
                COALESCE(M.W_PCT, CAST(27 AS decimal(5,2))) AS W_PCT,
                COALESCE(M.H_PCT, CAST(22 AS decimal(5,2))) AS H_PCT
            FROM Areas A
            LEFT JOIN dbo.WH_AreaLayout M ON M.AREACD = A.AREACD
            ORDER BY A.AREACD;
            """, r => new LocationAreaLayoutRow(
                GetString(r, "AREACD") ?? "",
                GetString(r, "AREANM"),
                GetDecimal(r, "X_PCT"),
                GetDecimal(r, "Y_PCT"),
                GetDecimal(r, "W_PCT"),
                GetDecimal(r, "H_PCT")));
    }

    public void SaveLocationAreaLayout(
        string areaCode,
        decimal xPct,
        decimal yPct,
        decimal wPct,
        decimal hPct,
        string modifiedBy = "web")
    {
        if (string.IsNullOrWhiteSpace(areaCode))
            throw new ArgumentException("Area code is required.", nameof(areaCode));

        EnsureAreaLayoutTable();

        xPct = ClampDecimal(xPct, 0m, 92m);
        yPct = ClampDecimal(yPct, 0m, 92m);
        wPct = ClampDecimal(wPct, 8m, 100m - xPct);
        hPct = ClampDecimal(hPct, 8m, 100m - yPct);

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            MERGE dbo.WH_AreaLayout AS tgt
            USING (SELECT @AreaCode AS AREACD) AS src ON tgt.AREACD = src.AREACD
            WHEN MATCHED THEN UPDATE SET
                X_PCT = @XPct,
                Y_PCT = @YPct,
                W_PCT = @WPct,
                H_PCT = @HPct,
                MODIFIED_BY = @ModifiedBy,
                MODIFIED_TS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (AREACD, X_PCT, Y_PCT, W_PCT, H_PCT, MODIFIED_BY, MODIFIED_TS)
            VALUES
                (@AreaCode, @XPct, @YPct, @WPct, @HPct, @ModifiedBy, SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@AreaCode", SqlDbType.NVarChar, 20).Value = areaCode.Trim();
        AddDecimal(cmd, "@XPct", xPct);
        AddDecimal(cmd, "@YPct", yPct);
        AddDecimal(cmd, "@WPct", wPct);
        AddDecimal(cmd, "@HPct", hPct);
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.NVarChar, 80).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public List<OperationLogRow> ListOperationLogs(
        string? search = null,
        string? eventType = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("[dbo].[WH_WEB_LOG_HISTORY_LIST]", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        cmd.Parameters.Add("@SearchText", SqlDbType.NVarChar, 120).Value =
            string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim();
        cmd.Parameters.Add("@EventType", SqlDbType.VarChar, 40).Value =
            string.IsNullOrWhiteSpace(eventType) ? DBNull.Value : eventType.Trim().ToUpperInvariant();
        cmd.Parameters.Add("@DateFrom", SqlDbType.Date).Value =
            from.HasValue ? (object)from.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@DateTo", SqlDbType.Date).Value =
            to.HasValue ? (object)to.Value.Date : DBNull.Value;

        using var rdr = cmd.ExecuteReader();
        var list = new List<OperationLogRow>();
        while (rdr.Read())
        {
            list.Add(new OperationLogRow(
                GetLong(rdr, "OperationLogID"),
                GetDateTime(rdr, "EventTime"),
                GetString(rdr, "EventType") ?? "",
                GetString(rdr, "ScreenCode"),
                GetString(rdr, "EmployeeNo"),
                GetString(rdr, "EmployeeName"),
                GetString(rdr, "WorkerID"),
                GetString(rdr, "TerminalID"),
                GetString(rdr, "LineID"),
                GetString(rdr, "ShiftCode"),
                GetString(rdr, "ScanType"),
                GetString(rdr, "ScanValue"),
                GetString(rdr, "Result") ?? "INFO",
                GetString(rdr, "Message"),
                GetString(rdr, "ClientIP"),
                GetString(rdr, "RefDocType"),
                GetString(rdr, "RefDocNo"),
                GetString(rdr, "LotNo"),
                GetString(rdr, "PartNo"),
                GetString(rdr, "LocationID"),
                GetNullableDecimal(rdr, "Qty")));
        }

        return list;
    }

    public List<InventorySettingRow> ListInventorySettings(string? search = null, string? status = null)
    {
        var like = Like(search);
        return Query("""
            WITH Stock AS (
                SELECT
                    W.ItemNo,
                    SUM(COALESCE(W.OnHandQty, 0)) AS CURRENT_QTY,
                    COUNT(DISTINCT W.LocationID) AS LOCATION_COUNT,
                    COUNT(DISTINCT W.LotID) AS LOT_COUNT
                FROM dbo.WH_Inventory W
                WHERE W.ItemNo IS NOT NULL
                  AND UPPER(COALESCE(W.Status, 'RECEIVED')) NOT IN ('CANCELED')
                GROUP BY W.ItemNo
            ),
            SettingBase AS (
                SELECT
                    I.ItemNo,
                    I.ItemName,
                    I.DefaultUOM,
                    COALESCE(S.CURRENT_QTY, 0) AS CURRENT_QTY,
                    COALESCE(I.MinStock, 0) AS MIN_QTY,
                    COALESCE(I.MaxStock, 0) AS MAX_QTY,
                    CASE
                        WHEN COALESCE(I.MinStock, 0) > COALESCE(S.CURRENT_QTY, 0)
                            THEN COALESCE(I.MinStock, 0) - COALESCE(S.CURRENT_QTY, 0)
                        ELSE 0
                    END AS SHORTAGE_QTY,
                    COALESCE(S.LOCATION_COUNT, 0) AS LOCATION_COUNT,
                    COALESCE(S.LOT_COUNT, 0) AS LOT_COUNT,
                    I.ModifiedTS AS MODIFIED_TS
                FROM dbo.MD_Item I
                LEFT JOIN Stock S ON S.ItemNo = I.ItemNo
                WHERE COALESCE(I.ActiveFlag, 1) = 1
                  AND (@Search IS NULL
                       OR I.ItemNo LIKE @Search
                       OR I.ItemName LIKE @Search
                       OR I.ItemCategory LIKE @Search
                       OR I.CarType LIKE @Search)
            ),
            Statused AS (
                SELECT *,
                    CASE
                        WHEN MAX_QTY > 0 AND CURRENT_QTY > MAX_QTY THEN 'OVER_MAX'
                        WHEN SHORTAGE_QTY > 0 THEN 'BELOW_MIN'
                        ELSE 'NORMAL'
                    END AS STATUS
                FROM SettingBase
            )
            SELECT *,
                CASE STATUS
                    WHEN 'OVER_MAX' THEN '#2563EB'
                    WHEN 'BELOW_MIN' THEN '#F97316'
                    ELSE '#16A34A'
                END AS STATUS_COLOR
            FROM Statused
            WHERE (@Status IS NULL OR STATUS = @Status)
            ORDER BY
                CASE STATUS
                    WHEN 'BELOW_MIN' THEN 1
                    WHEN 'OVER_MAX' THEN 2
                    ELSE 3
                END,
                SHORTAGE_QTY DESC,
                ItemNo;
            """, r => new InventorySettingRow(
                GetString(r, "ItemNo") ?? "",
                GetString(r, "ItemName"),
                GetString(r, "DefaultUOM"),
                GetDecimal(r, "CURRENT_QTY"),
                GetDecimal(r, "MIN_QTY"),
                GetDecimal(r, "MAX_QTY"),
                GetDecimal(r, "SHORTAGE_QTY"),
                GetString(r, "STATUS") ?? "NORMAL",
                GetString(r, "STATUS_COLOR") ?? DefaultNormalColor,
                GetInt(r, "LOCATION_COUNT"),
                GetInt(r, "LOT_COUNT"),
                GetDateTime(r, "MODIFIED_TS")),
            ("@Search", like),
            ("@Status", NullIfBlank(status)));
    }

    public void SaveInventorySetting(
        string itemNo,
        decimal minQty,
        decimal maxQty,
        string modifiedBy = "web")
    {
        if (string.IsNullOrWhiteSpace(itemNo))
            throw new ArgumentException("Item No is required.", nameof(itemNo));
        if (minQty < 0 || maxQty < 0)
            throw new InvalidOperationException("Quantity thresholds cannot be negative.");
        if (maxQty > 0 && minQty > maxQty)
            throw new InvalidOperationException("Min Qty cannot be greater than Max Qty.");

        using var conn = _factory.OpenConnection();
        using var itemCmd = new SqlCommand("""
            UPDATE dbo.MD_Item
               SET MinStock = @MinQty,
                   MaxStock = @MaxQty,
                   ModifiedBy = @ModifiedBy,
                   ModifiedTS = SYSDATETIME()
             WHERE ItemNo = @ItemNo;
            """, conn);
        itemCmd.Parameters.Add("@ItemNo", SqlDbType.VarChar, 20).Value = itemNo.Trim();
        AddQtyDecimal(itemCmd, "@MinQty", minQty, scale: 4);
        AddQtyDecimal(itemCmd, "@MaxQty", maxQty, scale: 4);
        itemCmd.Parameters.Add("@ModifiedBy", SqlDbType.NVarChar, 80).Value = modifiedBy;
        if (itemCmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Item was not found.");
    }

    private void EnsureAreaLayoutTable()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WH_AreaLayout (
                    AREACD NVARCHAR(20) NOT NULL CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY,
                    X_PCT DECIMAL(5,2) NOT NULL,
                    Y_PCT DECIMAL(5,2) NOT NULL,
                    W_PCT DECIMAL(5,2) NOT NULL,
                    H_PCT DECIMAL(5,2) NOT NULL,
                    MODIFIED_BY NVARCHAR(80) NULL,
                    MODIFIED_TS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_LAYOUT_MODIFIED_TS DEFAULT SYSDATETIME()
                );
            END;
        """, conn);
        cmd.ExecuteNonQuery();
    }

    private static void AddLocationParameters(
        SqlCommand cmd,
        string locationNo,
        string? locationName,
        string? whCode,
        string? areaCode,
        string? areaName,
        string? zoneCode,
        string? zoneName,
        string? rackX,
        string? rackY,
        string? rackZ,
        bool useYn)
    {
        cmd.Parameters.Add("@LocationNo", SqlDbType.VarChar, 20).Value = Truncate(locationNo, 20);
        AddNullable(cmd, "@LocationName", SqlDbType.NVarChar, 120, locationName);
        AddNullable(cmd, "@PlantCode", SqlDbType.VarChar, 20, whCode);
        AddNullable(cmd, "@ZoneCode", SqlDbType.VarChar, 10, FirstNonBlank(zoneCode, areaCode));
        AddNullable(cmd, "@LocationType", SqlDbType.VarChar, 20, FirstNonBlank(zoneName, areaName));
        AddNullable(cmd, "@RackX", SqlDbType.VarChar, 5, rackX);
        AddNullable(cmd, "@RackY", SqlDbType.VarChar, 5, rackY);
        AddNullable(cmd, "@RackZ", SqlDbType.VarChar, 5, rackZ);
        cmd.Parameters.Add("@UseYn", SqlDbType.Bit).Value = useYn;
    }

    private List<T> Query<T>(string sql, Func<SqlDataReader, T> map, params (string Name, object? Val)[] p)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in p)
            cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        using var rdr = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return list;
    }

    private static string? Like(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string Truncate(string value, int maxLength)
    {
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static void AddNullable(SqlCommand cmd, string name, SqlDbType type, int size, string? value)
    {
        var p = cmd.Parameters.Add(name, type, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static void AddDecimal(SqlCommand cmd, string name, decimal value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 5;
        p.Scale = 2;
        p.Value = value;
    }

    private static void AddQtyDecimal(SqlCommand cmd, string name, decimal value, byte scale = 3)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 14;
        p.Scale = scale;
        p.Value = value;
    }

    private static decimal ClampDecimal(decimal value, decimal min, decimal max) =>
        Math.Min(Math.Max(value, min), max);

    private static string? GetString(SqlDataReader r, string name)
    {
        var value = r[name];
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    private static bool GetBool(SqlDataReader r, string name)
    {
        var value = r[name];
        return value != DBNull.Value && Convert.ToBoolean(value);
    }

    private static int GetInt(SqlDataReader r, string name)
    {
        var value = r[name];
        return value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static int? GetNullableInt(SqlDataReader r, string name)
    {
        var value = r[name];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static long GetLong(SqlDataReader r, string name)
    {
        var value = r[name];
        return value == DBNull.Value ? 0 : Convert.ToInt64(value);
    }

    private static decimal GetDecimal(SqlDataReader r, string name)
    {
        var value = r[name];
        return value == DBNull.Value ? 0m : Convert.ToDecimal(value);
    }

    private static decimal? GetNullableDecimal(SqlDataReader r, string name)
    {
        var value = r[name];
        return value == DBNull.Value ? null : Convert.ToDecimal(value);
    }

    private static DateTime? GetDateTime(SqlDataReader r, string name)
    {
        var value = r[name];
        return value == DBNull.Value ? null : Convert.ToDateTime(value);
    }
}
