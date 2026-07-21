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
                L.LOCATION_NO,
                L.LOCATION_NM,
                L.WHCD,
                L.WHNM,
                L.AREACD,
                L.AREANM,
                L.ZONECD,
                L.ZONENM,
                L.RACK_X,
                L.RACK_Y,
                L.RACK_Z,
                CASE WHEN COALESCE(L.USE_YN, N'Y') = N'Y' THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS USE_YN,
                COUNT(S.LOTNO) AS LOT_COUNT,
                COUNT(DISTINCT S.PARTNO) AS PART_COUNT,
                COALESCE(SUM(S.QTY), 0) AS TOTAL_QTY
            FROM SIS_TEST.WMS1040 L
            LEFT JOIN SIS_TEST.WMS2020 S ON S.LOCATION_NO = L.LOCATION_NO
            WHERE (@IncludeInactive = 1 OR COALESCE(L.USE_YN, N'Y') = N'Y')
              AND (@Search IS NULL
                   OR L.LOCATION_NO LIKE @Search
                   OR L.LOCATION_NM LIKE @Search
                   OR L.AREACD LIKE @Search
                   OR L.AREANM LIKE @Search
                   OR L.ZONECD LIKE @Search
                   OR L.ZONENM LIKE @Search
                   OR L.WHCD LIKE @Search
                   OR L.WHNM LIKE @Search)
            GROUP BY L.LOCATION_NO, L.LOCATION_NM, L.WHCD, L.WHNM, L.AREACD, L.AREANM,
                     L.ZONECD, L.ZONENM, L.RACK_X, L.RACK_Y, L.RACK_Z, L.USE_YN
            ORDER BY L.AREACD, L.ZONECD, TRY_CONVERT(int, L.RACK_X), L.RACK_X,
                     TRY_CONVERT(int, L.RACK_Y), L.RACK_Y, TRY_CONVERT(int, L.RACK_Z), L.RACK_Z,
                     L.LOCATION_NO;
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
            "SELECT 1 FROM SIS_TEST.WMS1040 WHERE LOCATION_NO = @LocationNo;", conn);
        cmd.Parameters.Add("@LocationNo", SqlDbType.NVarChar, 50).Value = locationNo;
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
        var (corcd, bizcd) = GetDefaultCompany();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO SIS_TEST.WMS1040
                (CORCD, BIZCD, LOCATION_NO, LOCATION_NM, WHCD, WHNM,
                 AREACD, AREANM, ZONECD, ZONENM, RACK_X, RACK_Y, RACK_Z, USE_YN)
            VALUES
                (@Corcd, @Bizcd, @LocationNo, @LocationName, @WhCode, @WhName,
                 @AreaCode, @AreaName, @ZoneCode, @ZoneName, @RackX, @RackY, @RackZ, @UseYn);
            """, conn);
        AddLocationParameters(cmd, corcd, bizcd, locationNo, locationName, whCode, whName,
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
            UPDATE SIS_TEST.WMS1040
            SET LOCATION_NM = @LocationName,
                WHCD = @WhCode,
                WHNM = @WhName,
                AREACD = @AreaCode,
                AREANM = @AreaName,
                ZONECD = @ZoneCode,
                ZONENM = @ZoneName,
                RACK_X = @RackX,
                RACK_Y = @RackY,
                RACK_Z = @RackZ,
                USE_YN = @UseYn
            WHERE LOCATION_NO = @LocationNo;
            """, conn);
        cmd.Parameters.Add("@LocationNo", SqlDbType.NVarChar, 50).Value = locationNo;
        AddNullable(cmd, "@LocationName", SqlDbType.NVarChar, 120, locationName);
        AddNullable(cmd, "@WhCode", SqlDbType.NVarChar, 20, whCode);
        AddNullable(cmd, "@WhName", SqlDbType.NVarChar, 120, whName);
        AddNullable(cmd, "@AreaCode", SqlDbType.NVarChar, 20, areaCode);
        AddNullable(cmd, "@AreaName", SqlDbType.NVarChar, 120, areaName);
        AddNullable(cmd, "@ZoneCode", SqlDbType.NVarChar, 20, zoneCode);
        AddNullable(cmd, "@ZoneName", SqlDbType.NVarChar, 120, zoneName);
        AddNullable(cmd, "@RackX", SqlDbType.NVarChar, 20, rackX);
        AddNullable(cmd, "@RackY", SqlDbType.NVarChar, 20, rackY);
        AddNullable(cmd, "@RackZ", SqlDbType.NVarChar, 20, rackZ);
        cmd.Parameters.Add("@UseYn", SqlDbType.NVarChar, 1).Value = useYn ? "Y" : "N";
        cmd.ExecuteNonQuery();
    }

    public void DeleteLocation(string locationNo)
    {
        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand(
            "SELECT COUNT(1) FROM SIS_TEST.WMS2020 WHERE LOCATION_NO = @LocationNo AND COALESCE(QTY, 0) <> 0;", conn);
        check.Parameters.Add("@LocationNo", SqlDbType.NVarChar, 50).Value = locationNo;
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Location has inventory and cannot be deleted.");

        using var cmd = new SqlCommand("DELETE FROM SIS_TEST.WMS1040 WHERE LOCATION_NO = @LocationNo;", conn);
        cmd.Parameters.Add("@LocationNo", SqlDbType.NVarChar, 50).Value = locationNo;
        cmd.ExecuteNonQuery();
    }

    public List<PickingOrderRow> ListPickingOrders(string? search = null, bool includeClosed = true)
    {
        var like = Like(search);
        return Query("""
            SELECT
                O.PICK_SLIPNO,
                O.REQ_DATE,
                O.REQ_LOCATION,
                O.SEQNO,
                O.PARTNO,
                COALESCE(O.REQ_BOX_QTY, 0) AS REQ_BOX_QTY,
                O.REQ_USERID,
                O.REQ_TIME,
                O.PRINT_DATE,
                O.CLOSE_YN,
                O.CLOSE_DATE,
                COALESCE(P.PICKED_QTY, 0) AS PICKED_QTY,
                CASE
                    WHEN O.CLOSE_DATE IS NOT NULL OR COALESCE(O.CLOSE_YN, N'N') = N'Y' THEN N'Closed'
                    WHEN COALESCE(P.PICKED_QTY, 0) >= COALESCE(O.REQ_BOX_QTY, 0) AND COALESCE(O.REQ_BOX_QTY, 0) > 0 THEN N'Picked'
                    WHEN COALESCE(P.PICKED_QTY, 0) > 0 THEN N'Partial'
                    ELSE N'Open'
                END AS STATUS
            FROM SIS_TEST.WMS3050 O
            OUTER APPLY (
                SELECT SUM(A.QTY) AS PICKED_QTY
                FROM SIS_TEST.PDA_WH_RELEASE_PICK_AUDIT A
                WHERE A.PICK_SLIPNO = O.PICK_SLIPNO
                  AND (A.PARTNO = O.PARTNO OR O.PARTNO IS NULL)
            ) P
            WHERE (@IncludeClosed = 1 OR (O.CLOSE_DATE IS NULL AND COALESCE(O.CLOSE_YN, N'N') <> N'Y'))
              AND (@Search IS NULL
                   OR O.PICK_SLIPNO LIKE @Search
                   OR O.REQ_LOCATION LIKE @Search
                   OR O.PARTNO LIKE @Search
                   OR O.REQ_USERID LIKE @Search)
            ORDER BY O.REQ_DATE DESC, O.PICK_SLIPNO DESC, O.SEQNO;
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
        var (corcd, bizcd) = GetDefaultCompany();
        var slipNo = string.IsNullOrWhiteSpace(pickSlipNo)
            ? $"PS{DateTime.Now:yyMMddHHmmss}"
            : pickSlipNo.Trim();

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO SIS_TEST.WMS3050
                (CORCD, BIZCD, REQ_DATE, REQ_LOCATION, SEQNO, PARTNO,
                 REQ_BOX_QTY, REQ_USERID, REQ_TIME, PICK_SLIPNO, PRINT_DATE, CLOSE_YN)
            VALUES
                (@Corcd, @Bizcd, @ReqDate, @ReqLocation, @SeqNo, @PartNo,
                 @ReqBoxQty, @ReqUserId, @ReqTime, @PickSlipNo, SYSDATETIME(), NULL);
            """, conn);
        cmd.Parameters.Add("@Corcd", SqlDbType.NVarChar, 10).Value = corcd;
        cmd.Parameters.Add("@Bizcd", SqlDbType.NVarChar, 10).Value = bizcd;
        cmd.Parameters.Add("@ReqDate", SqlDbType.NVarChar, 20).Value = reqDate;
        cmd.Parameters.Add("@ReqLocation", SqlDbType.NVarChar, 50).Value = reqLocation;
        cmd.Parameters.Add("@SeqNo", SqlDbType.Int).Value = seqNo;
        cmd.Parameters.Add("@PartNo", SqlDbType.NVarChar, 50).Value = partNo;
        cmd.Parameters.Add("@ReqBoxQty", SqlDbType.Decimal).Value = reqBoxQty;
        cmd.Parameters["@ReqBoxQty"].Precision = 18;
        cmd.Parameters["@ReqBoxQty"].Scale = 3;
        cmd.Parameters.Add("@ReqUserId", SqlDbType.NVarChar, 80).Value = reqUserId;
        cmd.Parameters.Add("@ReqTime", SqlDbType.NVarChar, 20).Value = DateTime.Now.ToString("HHmmss");
        cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 30).Value = slipNo;
        cmd.ExecuteNonQuery();
        return slipNo;
    }

    public List<PartOptionRow> ListPartOptions()
    {
        return Query("""
            SELECT TOP (300) PARTNO
            FROM (
                SELECT PARTNO FROM SIS_TEST.WMS2020 WHERE PARTNO IS NOT NULL AND PARTNO <> N''
                UNION
                SELECT PARTNO FROM SIS_TEST.WMS3050 WHERE PARTNO IS NOT NULL AND PARTNO <> N''
            ) P
            ORDER BY PARTNO;
            """, r => new PartOptionRow(GetString(r, "PARTNO") ?? ""));
    }

    public List<LocationMapRow> ListLocationMap(string? areaCode = null, string? rackZ = null)
    {
        return Query("""
            SELECT
                L.LOCATION_NO,
                L.LOCATION_NM,
                L.AREACD,
                L.AREANM,
                L.ZONECD,
                L.ZONENM,
                L.RACK_X,
                L.RACK_Y,
                L.RACK_Z,
                COUNT(S.LOTNO) AS LOT_COUNT,
                COUNT(DISTINCT S.PARTNO) AS PART_COUNT,
                COALESCE(SUM(S.QTY), 0) AS TOTAL_QTY,
                CASE
                    WHEN COALESCE(SUM(S.QTY), 0) = 0 THEN N'Empty'
                    WHEN COUNT(DISTINCT S.PARTNO) > 1 THEN N'Mixed'
                    ELSE N'Stocked'
                END AS STATUS
            FROM SIS_TEST.WMS1040 L
            LEFT JOIN SIS_TEST.WMS2020 S ON S.LOCATION_NO = L.LOCATION_NO
            WHERE COALESCE(L.USE_YN, N'Y') = N'Y'
              AND (@AreaCode IS NULL OR L.AREACD = @AreaCode)
              AND (@RackZ IS NULL OR L.RACK_Z = @RackZ)
            GROUP BY L.LOCATION_NO, L.LOCATION_NM, L.AREACD, L.AREANM,
                     L.ZONECD, L.ZONENM, L.RACK_X, L.RACK_Y, L.RACK_Z
            ORDER BY L.AREACD, L.ZONECD, TRY_CONVERT(int, L.RACK_X), L.RACK_X,
                     TRY_CONVERT(int, L.RACK_Y), L.RACK_Y, TRY_CONVERT(int, L.RACK_Z), L.RACK_Z,
                     L.LOCATION_NO;
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
                    L.AREACD,
                    MAX(L.AREANM) AS AREANM,
                    ROW_NUMBER() OVER (ORDER BY L.AREACD) AS RN
                FROM SIS_TEST.WMS1040 L
                WHERE COALESCE(L.USE_YN, N'Y') = N'Y'
                  AND NULLIF(L.AREACD, N'') IS NOT NULL
                GROUP BY L.AREACD
            )
            SELECT
                A.AREACD,
                A.AREANM,
                COALESCE(M.X_PCT, CAST(4 + ((A.RN - 1) % 3) * 31 AS decimal(5,2))) AS X_PCT,
                COALESCE(M.Y_PCT, CAST(8 + ((A.RN - 1) / 3) * 28 AS decimal(5,2))) AS Y_PCT,
                COALESCE(M.W_PCT, CAST(27 AS decimal(5,2))) AS W_PCT,
                COALESCE(M.H_PCT, CAST(22 AS decimal(5,2))) AS H_PCT
            FROM Areas A
            LEFT JOIN SIS_TEST.WH_AREA_LAYOUT M ON M.AREACD = A.AREACD
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
            MERGE SIS_TEST.WH_AREA_LAYOUT AS tgt
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
            IF OBJECT_ID(N'SIS_TEST.WH_AREA_LAYOUT', N'U') IS NULL
            BEGIN
                CREATE TABLE SIS_TEST.WH_AREA_LAYOUT (
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

    private (string Corcd, string Bizcd) GetDefaultCompany()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT TOP (1) CORCD, BIZCD
            FROM SIS_TEST.WMS1040
            WHERE CORCD IS NOT NULL AND BIZCD IS NOT NULL
            ORDER BY CORCD, BIZCD;
            """, conn);
        using var rdr = cmd.ExecuteReader();
        return rdr.Read()
            ? (rdr.GetString(0), rdr.GetString(1))
            : ("5010", "5011");
    }

    private static void AddLocationParameters(
        SqlCommand cmd,
        string corcd,
        string bizcd,
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
        cmd.Parameters.Add("@Corcd", SqlDbType.NVarChar, 10).Value = corcd;
        cmd.Parameters.Add("@Bizcd", SqlDbType.NVarChar, 10).Value = bizcd;
        cmd.Parameters.Add("@LocationNo", SqlDbType.NVarChar, 50).Value = locationNo;
        AddNullable(cmd, "@LocationName", SqlDbType.NVarChar, 120, locationName);
        AddNullable(cmd, "@WhCode", SqlDbType.NVarChar, 20, whCode);
        AddNullable(cmd, "@WhName", SqlDbType.NVarChar, 120, whName);
        AddNullable(cmd, "@AreaCode", SqlDbType.NVarChar, 20, areaCode);
        AddNullable(cmd, "@AreaName", SqlDbType.NVarChar, 120, areaName);
        AddNullable(cmd, "@ZoneCode", SqlDbType.NVarChar, 20, zoneCode);
        AddNullable(cmd, "@ZoneName", SqlDbType.NVarChar, 120, zoneName);
        AddNullable(cmd, "@RackX", SqlDbType.NVarChar, 20, rackX);
        AddNullable(cmd, "@RackY", SqlDbType.NVarChar, 20, rackY);
        AddNullable(cmd, "@RackZ", SqlDbType.NVarChar, 20, rackZ);
        cmd.Parameters.Add("@UseYn", SqlDbType.NVarChar, 1).Value = useYn ? "Y" : "N";
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
