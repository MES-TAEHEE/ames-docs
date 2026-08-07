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

    public record WarehouseMasterRow(
        string WhCode,
        string? WhName,
        bool UseYn,
        int AreaCount,
        int LocationCount,
        decimal TotalQty);

    public record WarehouseAreaRow(
        string AreaCode,
        string? AreaName,
        bool UseYn,
        int LocationCount,
        decimal TotalQty,
        string? WhCode,
        string? WhName);

    public record WarehouseSectionRow(
        string AreaCode,
        string SectionCode,
        string? SectionName,
        bool UseYn,
        int LocationCount,
        decimal TotalQty,
        string? WhCode);

    public record PickingOrderRow(
        string PickSlipNo,
        string? ReqDate,
        string? ReqLocation,
        int? SeqNo,
        string? PartNo,
        string? PartName,
        decimal ReqBoxQty,
        string? ReqUserId,
        string? ReqTime,
        DateTime? PrintDate,
        string? CloseYn,
        DateTime? CloseDate,
        string Status,
        decimal PickedQty,
        string? Loc01,
        string? Loc02,
        string? Loc03);

    public record PickingSlipHeaderRow(
        string PickSlipNo,
        string? ReqDate,
        string? ReqLocation,
        string? ReqUserId,
        string? ReqTime,
        DateTime? PrintDate,
        string Status,
        int LineCount,
        decimal ReqBoxQty,
        decimal PickedQty,
        string? FirstPartNo,
        string? FirstPartName);

    public record PickingSlipLineRow(
        string PickSlipNo,
        int SeqNo,
        string? PartNo,
        string? PartName,
        decimal ReqBoxQty,
        decimal PickedBoxQty,
        decimal PickedQty,
        string? ReqUserId,
        string? LineCode,
        string? LineName,
        string? Loc01,
        decimal Loc01Qty,
        string? Loc02,
        decimal Loc02Qty,
        string? Loc03,
        decimal Loc03Qty,
        string Status);

    public record PickingSlipCandidateRow(
        DateTime ReqDate,
        string LineCode,
        string? LineName,
        string PartNo,
        string? PartName,
        string? Unit,
        decimal UnitPackQty,
        decimal ReqQty,
        decimal ReqBoxQty);

    public record CreatePickingSlipLine(string PartNo, string LineCode, decimal ReqBoxQty);

    public record PickingSlipLineOption(string LineCode, string LineName);

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

    public record LocationInventoryRow(
        string PartNo,
        string? PartName,
        string? Uom,
        string LocationNo,
        string? WhCode,
        string? AreaCode,
        string? AreaName,
        string? ZoneCode,
        string? ZoneName,
        string? RackX,
        string? RackY,
        string? RackZ,
        decimal Qty);

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

    public List<WarehouseLocationRow> ListLocations(string? search = null, bool includeInactive = false, string? areaCode = null, string? sectionCode = null, string? whCode = null)
    {
        EnsureWarehouseSectionTable();
        var like = Like(search);
        return Query("""
            SELECT
                L.LocationID AS LOCATION_NO,
                L.LocationName AS LOCATION_NM,
                L.PlantCode AS WHCD,
                COALESCE(NULLIF(W.WhName, ''), L.PlantCode) AS WHNM,
                COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) AS AREACD,
                COALESCE(NULLIF(A.AreaName, ''), COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode)) AS AREANM,
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
            LEFT JOIN dbo.WH_WarehouseMaster W
                   ON W.WhCode = L.PlantCode
            LEFT JOIN dbo.WH_AreaMaster A
                   ON A.AreaCode = COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode)
                  AND COALESCE(A.WhCode, L.PlantCode) = L.PlantCode
            LEFT JOIN dbo.WH_Inventory S
                   ON S.LocationID = L.LocationID
                  AND COALESCE(S.OnHandQty, 0) <> 0
                  AND UPPER(COALESCE(S.Status, 'RECEIVED')) NOT IN ('CANCELED')
            WHERE (@IncludeInactive = 1 OR COALESCE(L.ActiveFlag, 1) = 1)
              AND (@WhCode IS NULL OR L.PlantCode = @WhCode)
              AND (@AreaCode IS NULL OR COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = @AreaCode)
              AND (@SectionCode IS NULL OR COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') = @SectionCode)
              AND (@Search IS NULL
                   OR L.LocationID LIKE @Search
                   OR L.LocationName LIKE @Search
                   OR L.ZoneCode LIKE @Search
                   OR L.LocationType LIKE @Search
                   OR L.PlantCode LIKE @Search)
            GROUP BY L.LocationID, L.LocationName, L.PlantCode, W.WhName, L.ZoneCode, A.AreaName,
                     L.LocationType, L.Aisle, L.Bay, L.Slot, L.ActiveFlag
            ORDER BY L.PlantCode, COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode), L.ZoneCode,
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
            ("@IncludeInactive", includeInactive),
            ("@AreaCode", NullIfBlank(areaCode)),
            ("@SectionCode", NullIfBlank(sectionCode)),
            ("@WhCode", NullIfBlank(whCode)));
    }

    public List<WarehouseMasterRow> ListWarehouses(string? search = null, bool includeInactive = false)
    {
        EnsureWarehouseMasterTable();
        EnsureWarehouseAreaTable();
        var like = Like(search);
        return Query("""
            SELECT
                W.WhCode AS WHCD,
                W.WhName AS WHNM,
                CAST(COALESCE(W.ActiveFlag, 1) AS bit) AS USE_YN,
                COUNT(DISTINCT A.AreaCode) AS AREA_COUNT,
                COUNT(DISTINCT L.LocationID) AS LOCATION_COUNT,
                COALESCE(SUM(S.OnHandQty), 0) AS TOTAL_QTY
            FROM dbo.WH_WarehouseMaster W
            LEFT JOIN dbo.WH_AreaMaster A
                   ON A.WhCode = W.WhCode
                  AND COALESCE(A.ActiveFlag, 1) = 1
            LEFT JOIN dbo.MD_Location L
                   ON L.PlantCode = W.WhCode
                  AND COALESCE(L.ActiveFlag, 1) = 1
            LEFT JOIN dbo.WH_Inventory S
                   ON S.LocationID = L.LocationID
                  AND COALESCE(S.OnHandQty, 0) <> 0
                  AND UPPER(COALESCE(S.Status, 'RECEIVED')) NOT IN ('CANCELED')
            WHERE (@IncludeInactive = 1 OR COALESCE(W.ActiveFlag, 1) = 1)
              AND (@Search IS NULL
                   OR W.WhCode LIKE @Search
                   OR W.WhName LIKE @Search)
            GROUP BY W.WhCode, W.WhName, W.ActiveFlag
            ORDER BY W.WhCode;
            """, r => new WarehouseMasterRow(
                GetString(r, "WHCD") ?? "",
                GetString(r, "WHNM"),
                GetBool(r, "USE_YN"),
                GetInt(r, "AREA_COUNT"),
                GetInt(r, "LOCATION_COUNT"),
                GetDecimal(r, "TOTAL_QTY")),
            ("@Search", like),
            ("@IncludeInactive", includeInactive));
    }

    public bool WarehouseExists(string whCode)
    {
        EnsureWarehouseMasterTable();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.WH_WarehouseMaster WHERE WhCode = @WhCode;", conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = whCode.Trim();
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveWarehouse(string whCode, string? whName, bool useYn)
    {
        if (string.IsNullOrWhiteSpace(whCode))
            throw new ArgumentException("Warehouse code is required.", nameof(whCode));

        EnsureWarehouseMasterTable();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            MERGE dbo.WH_WarehouseMaster AS tgt
            USING (SELECT @WhCode AS WhCode) AS src
               ON tgt.WhCode = src.WhCode
            WHEN MATCHED THEN UPDATE SET
                WhName = @WhName,
                ActiveFlag = @UseYn,
                ModifiedBy = 'web',
                ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (WhCode, WhName, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (@WhCode, @WhName, @UseYn, 'web', SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = Truncate(whCode.Trim(), 20);
        AddNullable(cmd, "@WhName", SqlDbType.NVarChar, 120, whName);
        cmd.Parameters.Add("@UseYn", SqlDbType.Bit).Value = useYn;
        cmd.ExecuteNonQuery();
    }

    public void DeleteWarehouse(string whCode)
    {
        if (string.IsNullOrWhiteSpace(whCode))
            return;

        EnsureWarehouseMasterTable();
        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.MD_Location
            WHERE PlantCode = @WhCode;
            """, conn);
        check.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = whCode.Trim();
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Warehouse has locations and cannot be deleted.");

        using var area = new SqlCommand("DELETE FROM dbo.WH_AreaMaster WHERE WhCode = @WhCode;", conn);
        area.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = whCode.Trim();
        area.ExecuteNonQuery();

        using var cmd = new SqlCommand("DELETE FROM dbo.WH_WarehouseMaster WHERE WhCode = @WhCode;", conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = whCode.Trim();
        cmd.ExecuteNonQuery();
    }

    public List<WarehouseAreaRow> ListWarehouseAreas(string? search = null, bool includeInactive = false, string? whCode = null)
    {
        EnsureWarehouseAreaTable();
        var like = Like(search);
        return Query("""
            SELECT
                A.AreaCode AS AREACD,
                A.AreaName AS AREANM,
                A.WhCode AS WHCD,
                COALESCE(NULLIF(W.WhName, ''), A.WhCode) AS WHNM,
                CAST(COALESCE(A.ActiveFlag, 1) AS bit) AS USE_YN,
                COUNT(DISTINCT L.LocationID) AS LOCATION_COUNT,
                COALESCE(SUM(S.OnHandQty), 0) AS TOTAL_QTY
            FROM dbo.WH_AreaMaster A
            LEFT JOIN dbo.WH_WarehouseMaster W
                   ON W.WhCode = A.WhCode
            LEFT JOIN dbo.MD_Location L
                   ON COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = A.AreaCode
                  AND (@WhCode IS NULL OR L.PlantCode = @WhCode)
                  AND COALESCE(L.ActiveFlag, 1) = 1
            LEFT JOIN dbo.WH_Inventory S
                   ON S.LocationID = L.LocationID
                  AND COALESCE(S.OnHandQty, 0) <> 0
                  AND UPPER(COALESCE(S.Status, 'RECEIVED')) NOT IN ('CANCELED')
            WHERE (@IncludeInactive = 1 OR COALESCE(A.ActiveFlag, 1) = 1)
              AND (@WhCode IS NULL OR A.WhCode = @WhCode)
              AND (@Search IS NULL
                   OR A.AreaCode LIKE @Search
                   OR A.AreaName LIKE @Search)
            GROUP BY A.AreaCode, A.AreaName, A.WhCode, W.WhName, A.ActiveFlag
            ORDER BY A.AreaCode;
            """, r => new WarehouseAreaRow(
                GetString(r, "AREACD") ?? "",
                GetString(r, "AREANM"),
                GetBool(r, "USE_YN"),
                GetInt(r, "LOCATION_COUNT"),
                GetDecimal(r, "TOTAL_QTY"),
                GetString(r, "WHCD"),
                GetString(r, "WHNM")),
            ("@Search", like),
            ("@IncludeInactive", includeInactive),
            ("@WhCode", NullIfBlank(whCode)));
    }

    public List<WarehouseSectionRow> ListWarehouseSections(string? areaCode = null, string? search = null, bool includeInactive = false, string? whCode = null)
    {
        EnsureWarehouseSectionTable();
        var like = Like(search);
        return Query("""
            SELECT
                S.AreaCode AS AREACD,
                S.SectionCode AS SECTIONCD,
                S.SectionName AS SECTIONNM,
                S.WhCode AS WHCD,
                CAST(COALESCE(S.ActiveFlag, 1) AS bit) AS USE_YN,
                COUNT(DISTINCT L.LocationID) AS LOCATION_COUNT,
                COALESCE(SUM(I.OnHandQty), 0) AS TOTAL_QTY
            FROM dbo.WH_AreaSection S
            LEFT JOIN dbo.MD_Location L
                   ON COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = S.AreaCode
                  AND COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') = S.SectionCode
                  AND (@WhCode IS NULL OR L.PlantCode = @WhCode)
                  AND COALESCE(L.ActiveFlag, 1) = 1
            LEFT JOIN dbo.WH_Inventory I
                   ON I.LocationID = L.LocationID
                  AND COALESCE(I.OnHandQty, 0) <> 0
                  AND UPPER(COALESCE(I.Status, 'RECEIVED')) NOT IN ('CANCELED')
            WHERE (@AreaCode IS NULL OR S.AreaCode = @AreaCode)
              AND (@WhCode IS NULL OR S.WhCode = @WhCode)
              AND (@IncludeInactive = 1 OR COALESCE(S.ActiveFlag, 1) = 1)
              AND (@Search IS NULL
                   OR S.SectionCode LIKE @Search
                   OR S.SectionName LIKE @Search)
            GROUP BY S.AreaCode, S.SectionCode, S.SectionName, S.WhCode, S.ActiveFlag
            ORDER BY S.AreaCode, S.SectionCode;
            """, r => new WarehouseSectionRow(
                GetString(r, "AREACD") ?? "",
                GetString(r, "SECTIONCD") ?? "",
                GetString(r, "SECTIONNM"),
                GetBool(r, "USE_YN"),
                GetInt(r, "LOCATION_COUNT"),
                GetDecimal(r, "TOTAL_QTY"),
                GetString(r, "WHCD")),
            ("@AreaCode", NullIfBlank(areaCode)),
            ("@Search", like),
            ("@IncludeInactive", includeInactive),
            ("@WhCode", NullIfBlank(whCode)));
    }

    public bool WarehouseSectionExists(string areaCode, string sectionCode, string? whCode = null)
    {
        EnsureWarehouseSectionTable();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT 1
            FROM dbo.WH_AreaSection
            WHERE AreaCode = @AreaCode
              AND SectionCode = @SectionCode
              AND (@WhCode IS NULL OR WhCode = @WhCode);
            """, conn);
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        cmd.Parameters.Add("@SectionCode", SqlDbType.VarChar, 20).Value = sectionCode.Trim();
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveWarehouseSection(string areaCode, string sectionCode, string? sectionName, bool useYn, string? whCode = null)
    {
        if (string.IsNullOrWhiteSpace(areaCode))
            throw new ArgumentException("Area code is required.", nameof(areaCode));
        if (string.IsNullOrWhiteSpace(sectionCode))
            throw new ArgumentException("Section code is required.", nameof(sectionCode));

        EnsureWarehouseSectionTable();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            MERGE dbo.WH_AreaSection AS tgt
            USING (SELECT @WhCode AS WhCode, @AreaCode AS AreaCode, @SectionCode AS SectionCode) AS src
               ON tgt.AreaCode = src.AreaCode
              AND tgt.SectionCode = src.SectionCode
              AND ISNULL(tgt.WhCode, '') = ISNULL(src.WhCode, '')
            WHEN MATCHED THEN UPDATE SET
                SectionName = @SectionName,
                ActiveFlag = @UseYn,
                ModifiedBy = 'web',
                ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (WhCode, AreaCode, SectionCode, SectionName, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (@WhCode, @AreaCode, @SectionCode, @SectionName, @UseYn, 'web', SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(Truncate(whCode?.Trim() ?? "", 20)) ?? DBNull.Value;
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = Truncate(areaCode.Trim(), 20);
        cmd.Parameters.Add("@SectionCode", SqlDbType.VarChar, 20).Value = Truncate(sectionCode.Trim(), 20);
        AddNullable(cmd, "@SectionName", SqlDbType.NVarChar, 120, sectionName);
        cmd.Parameters.Add("@UseYn", SqlDbType.Bit).Value = useYn;
        cmd.ExecuteNonQuery();
    }

    public void DeleteWarehouseSection(string areaCode, string sectionCode, string? whCode = null)
    {
        if (string.IsNullOrWhiteSpace(areaCode) || string.IsNullOrWhiteSpace(sectionCode))
            return;

        EnsureWarehouseSectionTable();
        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.MD_Location
            WHERE COALESCE(NULLIF(ZoneCode, ''), PlantCode) = @AreaCode
              AND COALESCE(NULLIF(LocationType, ''), 'DEFAULT') = @SectionCode
              AND (@WhCode IS NULL OR PlantCode = @WhCode);
            """, conn);
        check.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        check.Parameters.Add("@SectionCode", SqlDbType.VarChar, 20).Value = sectionCode.Trim();
        check.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Section has locations and cannot be deleted.");

        using var cmd = new SqlCommand("""
            DELETE FROM dbo.WH_AreaSection
            WHERE AreaCode = @AreaCode
              AND SectionCode = @SectionCode
              AND (@WhCode IS NULL OR WhCode = @WhCode);
            """, conn);
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        cmd.Parameters.Add("@SectionCode", SqlDbType.VarChar, 20).Value = sectionCode.Trim();
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    public bool WarehouseAreaExists(string areaCode, string? whCode = null)
    {
        EnsureWarehouseAreaTable();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT 1
            FROM dbo.WH_AreaMaster
            WHERE AreaCode = @AreaCode
              AND (@WhCode IS NULL OR WhCode = @WhCode);
            """, conn);
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode;
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveWarehouseArea(string areaCode, string? areaName, bool useYn, string? whCode = null)
    {
        if (string.IsNullOrWhiteSpace(areaCode))
            throw new ArgumentException("Area code is required.", nameof(areaCode));

        EnsureWarehouseAreaTable();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            MERGE dbo.WH_AreaMaster AS tgt
            USING (SELECT @WhCode AS WhCode, @AreaCode AS AreaCode) AS src
               ON tgt.AreaCode = src.AreaCode
              AND ISNULL(tgt.WhCode, '') = ISNULL(src.WhCode, '')
            WHEN MATCHED THEN UPDATE SET
                WhCode = @WhCode,
                AreaName = @AreaName,
                ActiveFlag = @UseYn,
                ModifiedBy = 'web',
                ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (WhCode, AreaCode, AreaName, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (@WhCode, @AreaCode, @AreaName, @UseYn, 'web', SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(Truncate(whCode?.Trim() ?? "", 20)) ?? DBNull.Value;
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = Truncate(areaCode.Trim(), 20);
        AddNullable(cmd, "@AreaName", SqlDbType.NVarChar, 120, areaName);
        cmd.Parameters.Add("@UseYn", SqlDbType.Bit).Value = useYn;
        cmd.ExecuteNonQuery();
    }

    public void DeleteWarehouseArea(string areaCode, string? whCode = null)
    {
        if (string.IsNullOrWhiteSpace(areaCode))
            return;

        EnsureWarehouseAreaTable();
        EnsureAreaLayoutTable();

        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.MD_Location
            WHERE COALESCE(NULLIF(ZoneCode, ''), PlantCode) = @AreaCode
              AND (@WhCode IS NULL OR PlantCode = @WhCode);
            """, conn);
        check.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        check.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Area has locations and cannot be deleted.");

        using var layout = new SqlCommand("""
            DELETE FROM dbo.WH_AreaLayout
            WHERE AREACD = @OldLayoutKey
               OR AREACD = @LayoutKey
               OR (@WhCode IS NULL AND AREACD LIKE @AnyWhLayoutKey);
            """, conn);
        layout.Parameters.Add("@OldLayoutKey", SqlDbType.NVarChar, 80).Value = OldAreaLayoutKey(areaCode);
        layout.Parameters.Add("@LayoutKey", SqlDbType.NVarChar, 80).Value = AreaLayoutKey(whCode ?? "", areaCode);
        layout.Parameters.Add("@AnyWhLayoutKey", SqlDbType.NVarChar, 80).Value = $"AREA|%|{areaCode.Trim()}";
        layout.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        layout.ExecuteNonQuery();

        using var cmd = new SqlCommand("DELETE FROM dbo.WH_AreaMaster WHERE AreaCode = @AreaCode AND (@WhCode IS NULL OR WhCode = @WhCode);", conn);
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        cmd.ExecuteNonQuery();
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

    public void UpdateOrInsertLocation(
        string locationNo,
        string? locationName,
        string? whCode,
        string areaCode,
        string sectionCode,
        string? rackX,
        string? rackY,
        string? rackZ,
        bool useYn)
    {
        if (string.IsNullOrWhiteSpace(locationNo))
            throw new ArgumentException("Location code is required.", nameof(locationNo));
        if (string.IsNullOrWhiteSpace(areaCode))
            throw new ArgumentException("Area code is required.", nameof(areaCode));
        if (string.IsNullOrWhiteSpace(sectionCode))
            throw new ArgumentException("Section code is required.", nameof(sectionCode));

        var normalizedWh = FirstNonBlank(whCode, "WH01");
        var normalizedArea = areaCode.Trim();
        var normalizedSection = sectionCode.Trim();

        if (LocationExists(locationNo))
        {
            UpdateLocation(locationNo, locationName, normalizedWh, normalizedWh,
                normalizedArea, null, normalizedSection, normalizedSection,
                rackX, rackY, rackZ, useYn);
        }
        else
        {
            InsertLocation(locationNo, locationName, normalizedWh, normalizedWh,
                normalizedArea, null, normalizedSection, normalizedSection,
                rackX, rackY, rackZ, useYn);
        }
    }

    public List<PickingOrderRow> ListPickingOrders(string? search = null, bool includeClosed = true)
    {
        EnsureReleaseSchedulePickingSlipColumns();
        var headers = ListPickingSlipHeaders(search, includeClosed);
        var rows = new List<PickingOrderRow>();
        foreach (var h in headers)
        {
            rows.AddRange(ListPickingSlipLines(h.PickSlipNo).Select(l => new PickingOrderRow(
                l.PickSlipNo,
                h.ReqDate,
                h.ReqLocation,
                l.SeqNo,
                l.PartNo,
                l.PartName,
                l.ReqBoxQty,
                l.ReqUserId,
                h.ReqTime,
                h.PrintDate,
                h.Status == "Closed" ? "Y" : "N",
                null,
                l.Status,
                l.PickedQty,
                l.Loc01,
                l.Loc02,
                l.Loc03)));
        }

        return rows;
    }

    public List<PickingSlipHeaderRow> ListPickingSlipHeaders(string? search = null, bool includeClosed = true)
    {
        EnsureReleaseSchedulePickingSlipColumns();
        var like = Like(search);
        return Query("""
            ;WITH Lines AS
            (
                SELECT
                    COALESCE(NULLIF(O.PickSlipNo, N''), CONCAT(N'RS-', O.ReleaseScheduleID)) AS PICK_SLIPNO,
                    CONVERT(varchar(10), O.RequiredAt, 23) AS REQ_DATE,
                    COALESCE(NULLIF(O.ReqLocation, N''), N'-') AS REQ_LOCATION,
                    COALESCE(NULLIF(O.ReqUserId, N''), O.CreatedBy) AS REQ_USERID,
                    CONVERT(varchar(8), CONVERT(time(0), COALESCE(O.CreatedTS, O.RequiredAt))) AS REQ_TIME,
                    O.PrintDate,
                    O.ReleaseScheduleID,
                    O.ItemNo,
                    I.ItemName,
                    COALESCE(O.DemandQty, 0) AS REQ_BOX_QTY,
                    COALESCE(O.PickedQty, 0) AS PICKED_QTY,
                    CASE
                        WHEN UPPER(COALESCE(O.Status, 'OPEN')) IN ('CLOSED', 'CANCELED') THEN N'Closed'
                        WHEN COALESCE(O.PickedQty, 0) >= COALESCE(O.DemandQty, 0) AND COALESCE(O.DemandQty, 0) > 0 THEN N'Picked'
                        WHEN COALESCE(O.PickedQty, 0) > 0 THEN N'Partial'
                        ELSE N'Open'
                    END AS LINE_STATUS
                FROM dbo.WH_ReleaseSchedule O
                LEFT JOIN dbo.MD_Item I
                       ON I.ItemNo = O.ItemNo
            ),
            Grouped AS
            (
                SELECT
                    PICK_SLIPNO,
                    MAX(REQ_DATE) AS REQ_DATE,
                    MAX(REQ_USERID) AS REQ_USERID,
                    MAX(REQ_TIME) AS REQ_TIME,
                    MAX(PrintDate) AS PRINT_DATE,
                    COUNT(*) AS LINE_COUNT,
                    SUM(REQ_BOX_QTY) AS REQ_BOX_QTY,
                    SUM(PICKED_QTY) AS PICKED_QTY,
                    SUM(CASE WHEN LINE_STATUS = N'Closed' THEN 1 ELSE 0 END) AS CLOSED_LINES,
                    SUM(CASE WHEN LINE_STATUS = N'Picked' THEN 1 ELSE 0 END) AS PICKED_LINES,
                    SUM(CASE WHEN LINE_STATUS = N'Partial' THEN 1 ELSE 0 END) AS PARTIAL_LINES,
                    MIN(ReleaseScheduleID) AS FIRST_ID
                FROM Lines
                GROUP BY PICK_SLIPNO
            ),
            LocationCounts AS
            (
                SELECT
                    PICK_SLIPNO,
                    REQ_LOCATION,
                    COUNT(*) AS LINE_COUNT,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY PICK_SLIPNO
                        ORDER BY COUNT(*) DESC, REQ_LOCATION
                    ) AS RN
                FROM Lines
                GROUP BY PICK_SLIPNO, REQ_LOCATION
            )
            SELECT
                G.PICK_SLIPNO,
                G.REQ_DATE,
                R.REQ_LOCATION,
                G.REQ_USERID,
                G.REQ_TIME,
                G.PRINT_DATE,
                G.LINE_COUNT,
                G.REQ_BOX_QTY,
                G.PICKED_QTY,
                L.ItemNo AS FIRST_PARTNO,
                L.ItemName AS FIRST_PARTNM,
                CASE
                    WHEN G.CLOSED_LINES = G.LINE_COUNT THEN N'Closed'
                    WHEN G.PICKED_LINES = G.LINE_COUNT THEN N'Picked'
                    WHEN G.PARTIAL_LINES > 0 OR G.PICKED_LINES > 0 THEN N'Partial'
                    ELSE N'Open'
                END AS STATUS
            FROM Grouped G
            INNER JOIN LocationCounts R
                    ON R.PICK_SLIPNO = G.PICK_SLIPNO
                   AND R.RN = 1
            LEFT JOIN Lines L
                   ON L.PICK_SLIPNO = G.PICK_SLIPNO
                  AND L.ReleaseScheduleID = G.FIRST_ID
            WHERE (@IncludeClosed = 1 OR G.CLOSED_LINES <> G.LINE_COUNT)
              AND (@Search IS NULL
                   OR G.PICK_SLIPNO LIKE @Search
                   OR R.REQ_LOCATION LIKE @Search
                   OR G.REQ_USERID LIKE @Search
                   OR L.ItemNo LIKE @Search
                   OR L.ItemName LIKE @Search)
            ORDER BY G.REQ_DATE DESC, G.PICK_SLIPNO DESC;
            """, r => new PickingSlipHeaderRow(
                GetString(r, "PICK_SLIPNO") ?? "",
                GetString(r, "REQ_DATE"),
                GetString(r, "REQ_LOCATION"),
                GetString(r, "REQ_USERID"),
                GetString(r, "REQ_TIME"),
                GetDateTime(r, "PRINT_DATE"),
                GetString(r, "STATUS") ?? "Open",
                GetInt(r, "LINE_COUNT"),
                GetDecimal(r, "REQ_BOX_QTY"),
                GetDecimal(r, "PICKED_QTY"),
                GetString(r, "FIRST_PARTNO"),
                GetString(r, "FIRST_PARTNM")),
            ("@Search", like),
            ("@IncludeClosed", includeClosed));
    }

    public List<PickingSlipLineRow> ListPickingSlipLines(string pickSlipNo)
    {
        EnsureReleaseSchedulePickingSlipColumns();
        return Query("""
            ;WITH Base AS
            (
                SELECT
                    COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) AS PICK_SLIPNO,
                    COALESCE(RS.ReqSeqNo, ROW_NUMBER() OVER (PARTITION BY COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) ORDER BY RS.ReleaseScheduleID)) AS SEQNO,
                    RS.ReleaseScheduleID,
                    RS.ItemNo,
                    COALESCE(I.ItemName, RS.ItemNo) AS ItemName,
                    COALESCE(RS.DemandQty, 0) AS DemandQty,
                    COALESCE(RS.PickedQty, 0) AS PickedQty,
                    COALESCE(NULLIF(RS.ReqUserId, N''), RS.CreatedBy) AS RequestUserId,
                    NULLIF(RS.ReqLocation, N'') AS LineCode,
                    COALESCE(ML.LineName, NULLIF(RS.ReqLocation, N'')) AS LineName,
                    RS.Status
                FROM dbo.WH_ReleaseSchedule RS
                LEFT JOIN dbo.MD_Item I
                       ON I.ItemNo = RS.ItemNo
                LEFT JOIN dbo.MD_Line ML
                       ON ML.LineID = RS.ReqLocation
                WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.ReleaseScheduleID)) = @PickSlipNo
            ),
            PickedPhysical AS
            (
                SELECT P.ReleaseScheduleID, SUM(COALESCE(P.PickedQty, 0)) AS PickedQty
                FROM dbo.WH_ReleasePicking P
                INNER JOIN Base B
                        ON B.ReleaseScheduleID = P.ReleaseScheduleID
                GROUP BY P.ReleaseScheduleID
            ),
            AllocationLocations AS
            (
                SELECT
                    A.ReleaseScheduleID,
                    CONCAT(COALESCE(NULLIF(ML.ZoneCode, N''), N'-'), N'|', A.LocationID) AS LocationLabel,
                    SUM(A.AllocatedQty) AS AllocatedQty,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY A.ReleaseScheduleID
                        ORDER BY MIN(COALESCE(A.ProductionDate, CONVERT(datetime2, '9999-12-31'))),
                                 A.LocationID
                    ) AS RN
                FROM dbo.WH_ReleasePickAllocation A
                LEFT JOIN dbo.MD_Location ML
                       ON ML.LocationID = A.LocationID
                INNER JOIN Base B
                        ON B.ReleaseScheduleID = A.ReleaseScheduleID
                GROUP BY A.ReleaseScheduleID, A.LocationID, ML.ZoneCode
            ),
            Locations AS
            (
                SELECT
                    ReleaseScheduleID,
                    MAX(CASE WHEN RN = 1 THEN LocationLabel END) AS LOC_01,
                    MAX(CASE WHEN RN = 1 THEN AllocatedQty END) AS LOC_01_QTY,
                    MAX(CASE WHEN RN = 2 THEN LocationLabel END) AS LOC_02,
                    MAX(CASE WHEN RN = 2 THEN AllocatedQty END) AS LOC_02_QTY,
                    MAX(CASE WHEN RN = 3 THEN LocationLabel END) AS LOC_03,
                    MAX(CASE WHEN RN = 3 THEN AllocatedQty END) AS LOC_03_QTY
                FROM AllocationLocations
                WHERE RN <= 3
                GROUP BY ReleaseScheduleID
            )
            SELECT
                B.PICK_SLIPNO,
                B.SEQNO,
                B.ItemNo AS PARTNO,
                B.ItemName AS PARTNM,
                B.DemandQty AS REQ_BOX_QTY,
                B.PickedQty AS PICKED_BOX_QTY,
                COALESCE(P.PickedQty, 0) AS PICKED_QTY,
                B.RequestUserId AS REQ_USERID,
                B.LineCode AS LINECD,
                B.LineName AS LINENM,
                L.LOC_01,
                COALESCE(L.LOC_01_QTY, 0) AS LOC_01_QTY,
                L.LOC_02,
                COALESCE(L.LOC_02_QTY, 0) AS LOC_02_QTY,
                L.LOC_03,
                COALESCE(L.LOC_03_QTY, 0) AS LOC_03_QTY,
                CASE
                    WHEN UPPER(COALESCE(B.Status, 'OPEN')) IN ('CLOSED', 'CANCELED') THEN N'Closed'
                    WHEN B.DemandQty > 0 AND B.PickedQty >= B.DemandQty THEN N'Picked'
                    WHEN B.PickedQty > 0 THEN N'Partial'
                    ELSE N'Open'
                END AS STATUS
            FROM Base B
            LEFT JOIN PickedPhysical P
                   ON P.ReleaseScheduleID = B.ReleaseScheduleID
            LEFT JOIN Locations L
                   ON L.ReleaseScheduleID = B.ReleaseScheduleID
            ORDER BY B.SEQNO, B.ReleaseScheduleID;
            """, r => new PickingSlipLineRow(
                GetString(r, "PICK_SLIPNO") ?? pickSlipNo,
                GetInt(r, "SEQNO"),
                GetString(r, "PARTNO"),
                GetString(r, "PARTNM"),
                GetDecimal(r, "REQ_BOX_QTY"),
                GetDecimal(r, "PICKED_BOX_QTY"),
                GetDecimal(r, "PICKED_QTY"),
                GetString(r, "REQ_USERID"),
                GetString(r, "LINECD"),
                GetString(r, "LINENM"),
                GetString(r, "LOC_01"),
                GetDecimal(r, "LOC_01_QTY"),
                GetString(r, "LOC_02"),
                GetDecimal(r, "LOC_02_QTY"),
                GetString(r, "LOC_03"),
                GetDecimal(r, "LOC_03_QTY"),
                GetString(r, "STATUS") ?? "Open"),
            ("@PickSlipNo", pickSlipNo.Trim()));
    }

    public List<PickingSlipCandidateRow> ListPickingSlipCandidates(DateTime reqDate, string? lineCode = null, string? partNo = null)
    {
        var like = Like(partNo);
        return Query("""
            ;WITH ReservationDemand AS
            (
                SELECT
                    CONVERT(date, R.RequiredAt) AS REQ_DATE,
                    COALESCE(NULLIF(W.LineID, N''), N'-') AS LINECD,
                    COALESCE(L.LineName, NULLIF(W.LineID, N''), N'-') AS LINENM,
                    R.ItemNo AS PARTNO,
                    COALESCE(I.ItemName, R.ItemNo) AS PARTNM,
                    I.DefaultUOM AS UNIT,
                    COALESCE(NULLIF(MAX(COALESCE(P.QtyPerInner, 0)), 0), 1) AS UNIT_PACK_QTY,
                    SUM(COALESCE(R.RequiredQty, 0)) AS REQ_QTY
                FROM dbo.PP_MaterialReservation R
                INNER JOIN dbo.PP_WorkOrder W
                        ON W.WoID = R.WoID
                LEFT JOIN dbo.MD_Line L
                       ON L.LineID = W.LineID
                LEFT JOIN dbo.MD_Item I
                       ON I.ItemNo = R.ItemNo
                LEFT JOIN dbo.MD_PackagingSpec P
                       ON P.ItemID = R.ItemNo
                      AND COALESCE(P.ActiveFlag, 1) = 1
                WHERE CONVERT(date, R.RequiredAt) = @ReqDate
                  AND UPPER(COALESCE(R.Status, N'OPEN')) NOT IN (N'CANCELED', N'CLOSED')
                  AND (@LineCode IS NULL OR W.LineID = @LineCode)
                  AND (@PartNo IS NULL
                       OR R.ItemNo LIKE @PartNo
                       OR I.ItemName LIKE @PartNo)
                GROUP BY CONVERT(date, R.RequiredAt), W.LineID, L.LineName, R.ItemNo, I.ItemName, I.DefaultUOM
            ),
            ScheduledDemand AS
            (
                SELECT
                    CONVERT(date, RS.RequiredAt) AS REQ_DATE,
                    COALESCE(NULLIF(RS.ReqLocation, N''), N'-') AS LINECD,
                    COALESCE(ML.LineName, NULLIF(RS.ReqLocation, N''), N'-') AS LINENM,
                    RS.ItemNo AS PARTNO,
                    COALESCE(I.ItemName, RS.ItemNo) AS PARTNM,
                    I.DefaultUOM AS UNIT,
                    CAST(1 AS decimal(14, 3)) AS UNIT_PACK_QTY,
                    SUM(COALESCE(RS.DemandQty, 0)) AS REQ_QTY
                FROM dbo.WH_ReleaseSchedule RS
                LEFT JOIN dbo.MD_Line ML
                       ON ML.LineID = RS.ReqLocation
                LEFT JOIN dbo.MD_Item I
                       ON I.ItemNo = RS.ItemNo
                WHERE CONVERT(date, RS.RequiredAt) = @ReqDate
                  AND UPPER(COALESCE(RS.Status, N'OPEN')) NOT IN (N'CANCELED', N'CLOSED')
                GROUP BY CONVERT(date, RS.RequiredAt), RS.ReqLocation, ML.LineName, RS.ItemNo, I.ItemName, I.DefaultUOM
            ),
            Demand AS
            (
                SELECT * FROM ReservationDemand
                UNION ALL
                SELECT * FROM ScheduledDemand
                WHERE NOT EXISTS (SELECT 1 FROM ReservationDemand)
            )
            SELECT TOP (500)
                REQ_DATE,
                LINECD,
                LINENM,
                PARTNO,
                PARTNM,
                UNIT,
                UNIT_PACK_QTY,
                REQ_QTY,
                CEILING(REQ_QTY / NULLIF(UNIT_PACK_QTY, 0)) AS REQ_BOX_QTY
            FROM Demand
            WHERE REQ_QTY > 0
              AND (@LineCode IS NULL OR LINECD = @LineCode)
              AND (@PartNo IS NULL
                   OR PARTNO LIKE @PartNo
                   OR PARTNM LIKE @PartNo)
            ORDER BY LINECD, PARTNO;
            """, r => new PickingSlipCandidateRow(
                GetDateTime(r, "REQ_DATE") ?? reqDate.Date,
                GetString(r, "LINECD") ?? "-",
                GetString(r, "LINENM"),
                GetString(r, "PARTNO") ?? "",
                GetString(r, "PARTNM"),
                GetString(r, "UNIT"),
                GetDecimal(r, "UNIT_PACK_QTY"),
                GetDecimal(r, "REQ_QTY"),
                GetDecimal(r, "REQ_BOX_QTY")),
            ("@ReqDate", reqDate.Date),
            ("@LineCode", NullIfBlank(lineCode)),
            ("@PartNo", like));
    }

    public List<PickingSlipLineOption> ListPickingSlipLineOptions()
    {
        return Query("""
            SELECT LineID AS LINECD, COALESCE(NULLIF(LineName, N''), LineID) AS LINENM
            FROM dbo.MD_Line
            WHERE UPPER(COALESCE(Status, N'ACTIVE')) NOT IN (N'INACTIVE', N'CLOSED')
            ORDER BY LineID;
            """, r => new PickingSlipLineOption(
                GetString(r, "LINECD") ?? "",
                GetString(r, "LINENM") ?? GetString(r, "LINECD") ?? ""));
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
        var requiredAt = DateTime.TryParse(reqDate, out var parsedDate) ? parsedDate.Date : DateTime.Today;
        var slip = CreatePickingSlip(requiredAt, reqUserId, new[] { new CreatePickingSlipLine(partNo, reqLocation, reqBoxQty) }, pickSlipNo);
        return slip;
    }

    public string CreatePickingSlip(
        DateTime reqDate,
        string reqUserId,
        IEnumerable<CreatePickingSlipLine> lines,
        string? requestedPickSlipNo = null)
    {
        EnsureReleaseSchedulePickingSlipColumns();

        var cleanLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.PartNo) && !string.IsNullOrWhiteSpace(l.LineCode) && l.ReqBoxQty > 0)
            .Select(l => new CreatePickingSlipLine(
                Truncate(l.PartNo.Trim().ToUpperInvariant(), 20),
                Truncate(l.LineCode.Trim().ToUpperInvariant(), 40),
                l.ReqBoxQty))
            .ToList();
        if (cleanLines.Count == 0)
            throw new InvalidOperationException("At least one requested line is required.");

        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var pickSlipNo = string.IsNullOrWhiteSpace(requestedPickSlipNo)
                ? GeneratePickSlipNo(conn, tx)
                : Truncate(requestedPickSlipNo.Trim().ToUpperInvariant(), 40);

            if (PickSlipExists(conn, tx, pickSlipNo))
                throw new InvalidOperationException($"Pick Slip No {pickSlipNo} already exists.");

            var seq = 1;
            foreach (var line in cleanLines)
            {
                using var cmd = new SqlCommand("""
                    INSERT INTO dbo.WH_ReleaseSchedule
                        (PickSlipNo, ReqLocation, ReqSeqNo, ReqUserId,
                         ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status,
                         CreatedBy, CreatedTS)
                    VALUES
                        (@PickSlipNo, @ReqLocation, @ReqSeqNo, @ReqUserId,
                         @PartNo, @ReqBoxQty, 0, @RequiredAt, @Priority, 'Open',
                         @ReqUserId, SYSDATETIME());
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """, conn, tx);
                cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo;
                cmd.Parameters.Add("@ReqLocation", SqlDbType.NVarChar, 40).Value = line.LineCode;
                cmd.Parameters.Add("@ReqSeqNo", SqlDbType.Int).Value = seq;
                cmd.Parameters.Add("@ReqUserId", SqlDbType.NVarChar, 80).Value = Truncate(reqUserId, 80);
                cmd.Parameters.Add("@PartNo", SqlDbType.VarChar, 20).Value = line.PartNo;
                AddQtyDecimal(cmd, "@ReqBoxQty", line.ReqBoxQty);
                cmd.Parameters.Add("@RequiredAt", SqlDbType.DateTime2).Value = reqDate.Date;
                cmd.Parameters.Add("@Priority", SqlDbType.TinyInt).Value = Math.Clamp(seq, 1, 9);
                var releaseScheduleId = Convert.ToInt32(cmd.ExecuteScalar());
                BuildPickAllocations(conn, tx, pickSlipNo, releaseScheduleId, line.PartNo, line.ReqBoxQty, reqUserId);
                seq++;
            }

            tx.Commit();
            TryWriteWebOperationLog(
                "PICK_SLIP_CREATE",
                "WH-002",
                reqUserId,
                reqUserId,
                $"Created picking slip {pickSlipNo} with {cleanLines.Count} line(s).",
                "PICK_SLIP",
                pickSlipNo,
                string.Join(", ", cleanLines.Select(l => l.LineCode).Distinct(StringComparer.OrdinalIgnoreCase)),
                "SUCCESS");
            return pickSlipNo;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void BuildPickAllocations(
        SqlConnection conn,
        SqlTransaction tx,
        string pickSlipNo,
        int releaseScheduleId,
        string itemNo,
        decimal requestedBoxQty,
        string createdBy)
    {
        decimal remainingBoxes = requestedBoxQty;
        var allocationSeq = 1;

        using var source = new SqlCommand("""
            SELECT
                W.LotID,
                W.LocationID,
                L.LotCode,
                L.ProducedAt,
                COALESCE(W.OnHandQty, 0) AS OnHandQty
            FROM dbo.WH_Inventory W
            INNER JOIN dbo.tbl_Lot L
                    ON L.LotID = W.LotID
            WHERE W.ItemNo = @ItemNo
              AND COALESCE(W.OnHandQty, 0) > 0
              AND UPPER(COALESCE(W.Status, 'RECEIVED')) NOT IN ('CANCELED', 'RELEASED', 'PICKED')
            ORDER BY COALESCE(L.ProducedAt, CONVERT(datetime2, '9999-12-31')),
                     W.LocationID,
                     L.LotCode;
            """, conn, tx);
        source.Parameters.Add("@ItemNo", SqlDbType.VarChar, 20).Value = itemNo;

        using var reader = source.ExecuteReader();
        var allocations = new List<(int LotId, string LocationId, string LotNo, DateTime? ProductionDate, decimal Qty)>();
        while (reader.Read() && remainingBoxes > 0)
        {
            var available = reader.GetDecimal(reader.GetOrdinal("OnHandQty"));
            if (available <= 0) continue;

            allocations.Add((
                reader.GetInt32(reader.GetOrdinal("LotID")),
                reader.GetString(reader.GetOrdinal("LocationID")),
                reader.GetString(reader.GetOrdinal("LotCode")),
                reader.IsDBNull(reader.GetOrdinal("ProducedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ProducedAt")),
                available));
            remainingBoxes -= 1;
        }
        reader.Close();

        foreach (var allocation in allocations)
        {
            using var insert = new SqlCommand("""
                INSERT INTO dbo.WH_ReleasePickAllocation
                    (PickSlipNo, ReleaseScheduleID, ItemNo, AllocationSeq, LocationID, LotID, LotNo,
                     ProductionDate, AllocatedQty, AllocatedBoxQty, PickedQty, PickedBoxQty, Status, CreatedBy)
                VALUES
                    (@PickSlipNo, @ReleaseScheduleID, @ItemNo, @AllocationSeq, @LocationID, @LotID, @LotNo,
                     @ProductionDate, @AllocatedQty, 1, 0, 0, 'Open', @CreatedBy);
                """, conn, tx);
            insert.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo;
            insert.Parameters.Add("@ReleaseScheduleID", SqlDbType.Int).Value = releaseScheduleId;
            insert.Parameters.Add("@ItemNo", SqlDbType.VarChar, 20).Value = itemNo;
            insert.Parameters.Add("@AllocationSeq", SqlDbType.Int).Value = allocationSeq++;
            insert.Parameters.Add("@LocationID", SqlDbType.VarChar, 20).Value = allocation.LocationId;
            insert.Parameters.Add("@LotID", SqlDbType.Int).Value = allocation.LotId;
            insert.Parameters.Add("@LotNo", SqlDbType.VarChar, 50).Value = allocation.LotNo;
            insert.Parameters.Add("@ProductionDate", SqlDbType.DateTime2).Value = (object?)allocation.ProductionDate ?? DBNull.Value;
            AddQtyDecimal(insert, "@AllocatedQty", allocation.Qty);
            insert.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 80).Value = Truncate(createdBy, 80);
            insert.ExecuteNonQuery();
        }

        // A pick slip is a request, not a physical-pick completion.  Keep the
        // FIFO allocations that exist and let the printout show the remaining
        // locations as Shortage instead of cancelling the whole request.
    }

    public void MarkPickingSlipPrinted(string pickSlipNo, string printedBy)
    {
        EnsureReleaseSchedulePickingSlipColumns();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.WH_ReleaseSchedule
               SET PrintDate = SYSDATETIME(),
                   ModifiedBy = @PrintedBy,
                   ModifiedTS = SYSDATETIME()
             WHERE COALESCE(NULLIF(PickSlipNo, N''), CONCAT(N'RS-', ReleaseScheduleID)) = @PickSlipNo;
            """, conn);
        cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo.Trim();
        cmd.Parameters.Add("@PrintedBy", SqlDbType.NVarChar, 80).Value = Truncate(printedBy, 80);
        var changed = cmd.ExecuteNonQuery();
        if (changed == 0)
            throw new InvalidOperationException("Pick Slip was not found.");

        TryWriteWebOperationLog(
            "PICK_SLIP_PRINT",
            "WH-002",
            printedBy,
            printedBy,
            $"Printed picking slip {pickSlipNo}.",
            "PICK_SLIP",
            pickSlipNo,
            null,
            "SUCCESS");
    }

    public void ClosePickingSlip(string pickSlipNo, string closedBy)
    {
        EnsureReleaseSchedulePickingSlipColumns();
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.WH_ReleaseSchedule
               SET Status = 'Closed',
                   CloseDate = SYSDATETIME(),
                   CloseUserId = @ClosedBy,
                   ModifiedBy = @ClosedBy,
                   ModifiedTS = SYSDATETIME()
             WHERE COALESCE(NULLIF(PickSlipNo, N''), CONCAT(N'RS-', ReleaseScheduleID)) = @PickSlipNo
               AND UPPER(COALESCE(Status, 'OPEN')) <> 'CLOSED';
            """, conn);
        cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo.Trim();
        cmd.Parameters.Add("@ClosedBy", SqlDbType.NVarChar, 80).Value = Truncate(closedBy, 80);
        var changed = cmd.ExecuteNonQuery();
        if (changed == 0)
            throw new InvalidOperationException("Pick Slip was not found or is already closed.");

        TryWriteWebOperationLog(
            "PICK_SLIP_CLOSE",
            "WH-002",
            closedBy,
            closedBy,
            $"Closed picking slip {pickSlipNo}.",
            "PICK_SLIP",
            pickSlipNo,
            null,
            "SUCCESS");
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

    public List<LocationMapRow> ListLocationMap(string? areaCode = null, string? rackZ = null, string? zoneCode = null, string? whCode = null)
    {
        EnsureWarehouseAreaTable();
        return Query("""
            SELECT
                L.LocationID AS LOCATION_NO,
                L.LocationName AS LOCATION_NM,
                COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) AS AREACD,
                COALESCE(NULLIF(A.AreaName, ''), COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode)) AS AREANM,
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
            LEFT JOIN dbo.WH_AreaMaster A
                   ON A.AreaCode = COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode)
                  AND COALESCE(A.WhCode, L.PlantCode) = L.PlantCode
            LEFT JOIN dbo.WH_Inventory S
                   ON S.LocationID = L.LocationID
                  AND COALESCE(S.OnHandQty, 0) <> 0
                  AND UPPER(COALESCE(S.Status, 'RECEIVED')) NOT IN ('CANCELED')
            WHERE COALESCE(L.ActiveFlag, 1) = 1
              AND (@WhCode IS NULL OR L.PlantCode = @WhCode)
              AND (@AreaCode IS NULL OR COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = @AreaCode)
              AND (@ZoneCode IS NULL OR COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') = @ZoneCode)
              AND (@RackZ IS NULL OR L.Slot = @RackZ)
            GROUP BY L.LocationID, L.LocationName, L.PlantCode, L.ZoneCode, A.AreaName,
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
            ("@ZoneCode", NullIfBlank(zoneCode)),
            ("@WhCode", NullIfBlank(whCode)),
            ("@RackZ", NullIfBlank(rackZ)));
    }

    public List<LocationInventoryRow> ListLocationInventory(
        string? whCode = null,
        string? areaCode = null,
        string? zoneCode = null,
        string? rackZ = null,
        string? search = null)
    {
        EnsureWarehouseAreaTable();
        var like = Like(search);

        return Query("""
            SELECT
                S.ItemNo AS PART_NO,
                COALESCE(NULLIF(I.ItemName, N''), S.ItemNo) AS PART_NAME,
                I.DefaultUOM AS UOM,
                S.LocationID AS LOCATION_NO,
                L.PlantCode AS WHCD,
                COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) AS AREACD,
                COALESCE(NULLIF(A.AreaName, ''), COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode)) AS AREANM,
                COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') AS ZONECD,
                COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') AS ZONENM,
                L.Aisle AS RACK_X,
                L.Bay AS RACK_Y,
                L.Slot AS RACK_Z,
                COALESCE(SUM(S.OnHandQty), 0) AS QTY
            FROM dbo.WH_Inventory S
            INNER JOIN dbo.MD_Location L ON L.LocationID = S.LocationID
            LEFT JOIN dbo.MD_Item I ON I.ItemNo = S.ItemNo
            LEFT JOIN dbo.WH_AreaMaster A
                   ON A.AreaCode = COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode)
                  AND COALESCE(A.WhCode, L.PlantCode) = L.PlantCode
            WHERE COALESCE(S.OnHandQty, 0) <> 0
              AND UPPER(COALESCE(S.Status, 'RECEIVED')) NOT IN ('CANCELED')
              AND COALESCE(L.ActiveFlag, 1) = 1
              AND (@WhCode IS NULL OR L.PlantCode = @WhCode)
              AND (@AreaCode IS NULL OR COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = @AreaCode)
              AND (@ZoneCode IS NULL OR COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') = @ZoneCode)
              AND (@RackZ IS NULL OR L.Slot = @RackZ)
              AND (@Search IS NULL
                   OR S.ItemNo LIKE @Search
                   OR I.ItemName LIKE @Search
                   OR S.LocationID LIKE @Search)
            GROUP BY S.ItemNo, I.ItemName, I.DefaultUOM, S.LocationID,
                     L.PlantCode, L.ZoneCode, A.AreaName, L.LocationType,
                     L.Aisle, L.Bay, L.Slot
            ORDER BY S.ItemNo, L.PlantCode,
                     COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode),
                     COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT'),
                     TRY_CONVERT(int, L.Slot), L.Slot,
                     TRY_CONVERT(int, L.Bay), L.Bay,
                     TRY_CONVERT(int, L.Aisle), L.Aisle,
                     S.LocationID;
            """, r => new LocationInventoryRow(
                GetString(r, "PART_NO") ?? "",
                GetString(r, "PART_NAME"),
                GetString(r, "UOM"),
                GetString(r, "LOCATION_NO") ?? "",
                GetString(r, "WHCD"),
                GetString(r, "AREACD"),
                GetString(r, "AREANM"),
                GetString(r, "ZONECD"),
                GetString(r, "ZONENM"),
                GetString(r, "RACK_X"),
                GetString(r, "RACK_Y"),
                GetString(r, "RACK_Z"),
                GetDecimal(r, "QTY")),
            ("@WhCode", NullIfBlank(whCode)),
            ("@AreaCode", NullIfBlank(areaCode)),
            ("@ZoneCode", NullIfBlank(zoneCode)),
            ("@RackZ", NullIfBlank(rackZ)),
            ("@Search", like));
    }

    public List<LocationAreaLayoutRow> ListWarehouseMapPlacements()
    {
        EnsureAreaLayoutTable();
        EnsureWarehouseMasterTable();

        return Query("""
            SELECT
                W.WhCode AS AREACD,
                COALESCE(NULLIF(W.WhName, ''), W.WhCode) AS AREANM,
                M.X_PCT,
                M.Y_PCT,
                M.W_PCT,
                M.H_PCT
            FROM dbo.WH_AreaLayout M
            INNER JOIN dbo.WH_WarehouseMaster W
                    ON M.AREACD = CONCAT(N'WH|', W.WhCode)
            WHERE COALESCE(W.ActiveFlag, 1) = 1
            ORDER BY M.MODIFIED_TS, W.WhCode;
            """, r => new LocationAreaLayoutRow(
                GetString(r, "AREACD") ?? "",
                GetString(r, "AREANM"),
                GetDecimal(r, "X_PCT"),
                GetDecimal(r, "Y_PCT"),
                GetDecimal(r, "W_PCT"),
                GetDecimal(r, "H_PCT")));
    }

    public List<LocationAreaLayoutRow> ListLocationAreaLayouts(string? whCode = null)
    {
        EnsureAreaLayoutTable();
        EnsureWarehouseAreaTable();

        return Query("""
            SELECT
                A.AreaCode AS AREACD,
                COALESCE(NULLIF(A.AreaName, ''), A.AreaCode) AS AREANM,
                M.X_PCT,
                M.Y_PCT,
                M.W_PCT,
                M.H_PCT
            FROM dbo.WH_AreaLayout M
            INNER JOIN dbo.WH_AreaMaster A
                    ON M.AREACD = CONCAT(N'AREA|', A.WhCode, N'|', A.AreaCode)
            WHERE COALESCE(A.ActiveFlag, 1) = 1
              AND (@WhCode IS NULL OR A.WhCode = @WhCode)
            ORDER BY M.MODIFIED_TS, A.AreaCode;
            """, r => new LocationAreaLayoutRow(
                GetString(r, "AREACD") ?? "",
                GetString(r, "AREANM"),
                GetDecimal(r, "X_PCT"),
                GetDecimal(r, "Y_PCT"),
                GetDecimal(r, "W_PCT"),
                GetDecimal(r, "H_PCT")),
            ("@WhCode", NullIfBlank(whCode)));
    }

    public List<LocationAreaLayoutRow> ListZoneMapPlacements(string? areaCode = null, string? whCode = null)
    {
        EnsureAreaLayoutTable();
        EnsureWarehouseSectionTable();

        return Query("""
            SELECT
                Z.SectionCode AS AREACD,
                COALESCE(NULLIF(Z.SectionName, ''), Z.SectionCode) AS AREANM,
                M.X_PCT,
                M.Y_PCT,
                M.W_PCT,
                M.H_PCT
            FROM dbo.WH_AreaLayout M
            INNER JOIN dbo.WH_AreaSection Z
                    ON M.AREACD = CONCAT(N'ZONE|', Z.WhCode, N'|', Z.AreaCode, N'|', Z.SectionCode)
            WHERE COALESCE(Z.ActiveFlag, 1) = 1
              AND (@AreaCode IS NULL OR Z.AreaCode = @AreaCode)
              AND (@WhCode IS NULL OR Z.WhCode = @WhCode)
            ORDER BY M.MODIFIED_TS, Z.AreaCode, Z.SectionCode;
            """, r => new LocationAreaLayoutRow(
                GetString(r, "AREACD") ?? "",
                GetString(r, "AREANM"),
                GetDecimal(r, "X_PCT"),
                GetDecimal(r, "Y_PCT"),
                GetDecimal(r, "W_PCT"),
                GetDecimal(r, "H_PCT")),
            ("@AreaCode", NullIfBlank(areaCode)),
            ("@WhCode", NullIfBlank(whCode)));
    }

    public List<LocationAreaLayoutRow> ListLocationMapPlacements(string? areaCode = null, string? zoneCode = null, string? whCode = null)
    {
        EnsureAreaLayoutTable();

        return Query("""
            SELECT
                L.LocationID AS AREACD,
                COALESCE(NULLIF(L.LocationName, ''), L.LocationID) AS AREANM,
                M.X_PCT,
                M.Y_PCT,
                M.W_PCT,
                M.H_PCT
            FROM dbo.WH_AreaLayout M
            INNER JOIN dbo.MD_Location L
                    ON M.AREACD = CONCAT(N'LOC|', L.LocationID)
            WHERE COALESCE(L.ActiveFlag, 1) = 1
              AND (@WhCode IS NULL OR L.PlantCode = @WhCode)
              AND (@AreaCode IS NULL OR COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = @AreaCode)
              AND (@ZoneCode IS NULL OR COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') = @ZoneCode)
            ORDER BY M.MODIFIED_TS, M.AREACD;
            """, r => new LocationAreaLayoutRow(
                GetString(r, "AREACD") ?? "",
                GetString(r, "AREANM"),
                GetDecimal(r, "X_PCT"),
                GetDecimal(r, "Y_PCT"),
                GetDecimal(r, "W_PCT"),
                GetDecimal(r, "H_PCT")),
            ("@AreaCode", NullIfBlank(areaCode)),
            ("@ZoneCode", NullIfBlank(zoneCode)),
            ("@WhCode", NullIfBlank(whCode)));
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
            throw new ArgumentException("Layout key is required.", nameof(areaCode));

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
        cmd.Parameters.Add("@AreaCode", SqlDbType.NVarChar, 80).Value = areaCode.Trim();
        AddDecimal(cmd, "@XPct", xPct);
        AddDecimal(cmd, "@YPct", yPct);
        AddDecimal(cmd, "@WPct", wPct);
        AddDecimal(cmd, "@HPct", hPct);
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.NVarChar, 80).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void SaveWarehouseMapPlacement(string whCode, decimal xPct, decimal yPct, decimal wPct, decimal hPct) =>
        SaveLocationAreaLayout(WarehouseLayoutKey(whCode), xPct, yPct, wPct, hPct);

    public void SaveFactoryAreaLayout(string whCode, string areaCode, decimal xPct, decimal yPct, decimal wPct, decimal hPct) =>
        SaveLocationAreaLayout(AreaLayoutKey(whCode, areaCode), xPct, yPct, wPct, hPct);

    public void SaveZoneMapPlacement(string whCode, string areaCode, string zoneCode, decimal xPct, decimal yPct, decimal wPct, decimal hPct) =>
        SaveLocationAreaLayout(ZoneLayoutKey(whCode, areaCode, zoneCode), xPct, yPct, wPct, hPct);

    public void SaveLocationMapPlacement(string locationNo, decimal xPct, decimal yPct, decimal wPct, decimal hPct) =>
        SaveLocationAreaLayout(LocationLayoutKey(locationNo), xPct, yPct, wPct, hPct);

    public void DeleteWarehouseMapPlacement(string whCode)
    {
        if (string.IsNullOrWhiteSpace(whCode))
            return;

        EnsureAreaLayoutTable();

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            DELETE FROM dbo.WH_AreaLayout
             WHERE AREACD = @LayoutKey;
            """, conn);
        cmd.Parameters.Add("@LayoutKey", SqlDbType.NVarChar, 80).Value = WarehouseLayoutKey(whCode);
        cmd.ExecuteNonQuery();
    }

    public void DeleteFactoryAreaPlacement(string whCode, string areaCode)
    {
        if (string.IsNullOrWhiteSpace(whCode) || string.IsNullOrWhiteSpace(areaCode))
            return;

        EnsureAreaLayoutTable();

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            DELETE FROM dbo.WH_AreaLayout
             WHERE AREACD = @LayoutKey;
            """, conn);
        cmd.Parameters.Add("@LayoutKey", SqlDbType.NVarChar, 80).Value = AreaLayoutKey(whCode, areaCode);
        cmd.ExecuteNonQuery();
    }

    public void DeleteZoneMapPlacement(string whCode, string areaCode, string zoneCode)
    {
        if (string.IsNullOrWhiteSpace(whCode) || string.IsNullOrWhiteSpace(areaCode) || string.IsNullOrWhiteSpace(zoneCode))
            return;

        EnsureAreaLayoutTable();

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            DELETE FROM dbo.WH_AreaLayout
             WHERE AREACD = @LayoutKey;
            """, conn);
        cmd.Parameters.Add("@LayoutKey", SqlDbType.NVarChar, 80).Value = ZoneLayoutKey(whCode, areaCode, zoneCode);
        cmd.ExecuteNonQuery();
    }

    public void DeleteLocationMapPlacement(string locationNo)
    {
        if (string.IsNullOrWhiteSpace(locationNo))
            return;

        EnsureAreaLayoutTable();

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            DELETE FROM dbo.WH_AreaLayout
             WHERE AREACD = @LayoutKey;
            """, conn);
        cmd.Parameters.Add("@LayoutKey", SqlDbType.NVarChar, 80).Value = LocationLayoutKey(locationNo);
        cmd.ExecuteNonQuery();
    }

    public List<OperationLogRow> ListOperationLogs(
        string? search = null,
        string? eventType = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        using var conn = _factory.OpenConnection();
        EnsureOperationLogTable(conn);
        using var cmd = new SqlCommand("""
            ;WITH InventoryActivity AS
            (
                SELECT
                    OperationLogID, EventTime, EventType,
                    CASE
                        WHEN EventType IN ('RECEIVE', 'CANCEL_RECEIPT') THEN 'INBOUND'
                        WHEN EventType = 'RELEASE_PICK' THEN 'OUTBOUND'
                        WHEN EventType = 'MOVE_LOCATION' THEN 'RELOCATION'
                        WHEN EventType = 'ADJUST_SAVE' THEN 'ADJUSTMENT'
                    END AS OperationType,
                    ScreenCode, EmployeeNo, EmployeeName, WorkerID, TerminalID,
                    LineID, ShiftCode, ScanType, ScanValue, Result, Message,
                    ClientIP, RefDocType, RefDocNo, LotNo, PartNo, LocationID, Qty
                FROM dbo.WH_OperationLog
                WHERE Result = 'SUCCESS'
                  AND EventType IN ('RECEIVE', 'CANCEL_RECEIPT', 'RELEASE_PICK', 'MOVE_LOCATION', 'ADJUST_SAVE')

                UNION ALL

                SELECT
                    -H.LotStatusHistoryID, H.ChangedAt, 'STATUS_CHANGE', 'STATUS',
                    NULL, H.ChangedBy, NULL, H.ChangedBy, NULL,
                    NULL, NULL, NULL, H.LotNo, 'SUCCESS',
                    CONCAT(COALESCE(H.BeforeStatus, 'CREATED'), ' -> ', H.AfterStatus),
                    NULL, H.ReasonCode, H.ReferenceNo, H.LotNo, L.ItemNo, NULL, NULL
                FROM dbo.WH_LotStatusHistory H
                LEFT JOIN dbo.tbl_Lot L ON L.LotID = H.LotID
            )
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
            FROM InventoryActivity
            WHERE (@OperationType IS NULL OR OperationType = @OperationType)
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
            """, conn)
        {
            CommandTimeout = 15
        };
        var searchText = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        cmd.Parameters.Add("@Like", SqlDbType.NVarChar, 130).Value =
            searchText is null ? DBNull.Value : $"%{searchText}%";
        cmd.Parameters.Add("@OperationType", SqlDbType.VarChar, 40).Value =
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

    public long WriteWebOperationLog(
        string eventType,
        string? screenCode,
        string? workerId,
        string? workerName,
        string? message,
        string? refDocType = null,
        string? refDocNo = null,
        string? locationId = null,
        string result = "SUCCESS")
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event Type is required.", nameof(eventType));

        using var conn = _factory.OpenConnection();
        EnsureOperationLogTable(conn);

        using var insert = new SqlCommand("""
            INSERT INTO dbo.WH_OperationLog
            (
                EventTime, EventType, ScreenCode, EmployeeNo, EmployeeName, WorkerID,
                TerminalID, ScanType, ScanValue, Result, Message,
                RefDocType, RefDocNo, LocationID, CreatedBy, CreatedTS
            )
            OUTPUT INSERTED.OperationLogID
            VALUES
            (
                SYSDATETIME(), @EventType, @ScreenCode, @EmployeeNo, @EmployeeName, @WorkerID,
                @TerminalID, @ScanType, @ScanValue, @Result, @Message,
                @RefDocType, @RefDocNo, @LocationID, @CreatedBy, SYSDATETIME()
            );
            """, conn)
        {
            CommandTimeout = 15
        };
        AddOperationLogParameters(insert, eventType, screenCode, workerId, workerName, message, refDocType, refDocNo, locationId, result);
        return Convert.ToInt64(insert.ExecuteScalar());
    }

    public bool TryWriteWebOperationLog(
        string eventType,
        string? screenCode,
        string? workerId,
        string? workerName,
        string? message,
        string? refDocType = null,
        string? refDocNo = null,
        string? locationId = null,
        string result = "SUCCESS")
    {
        try
        {
            WriteWebOperationLog(eventType, screenCode, workerId, workerName, message, refDocType, refDocNo, locationId, result);
            return true;
        }
        catch
        {
            return false;
        }
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

    private void EnsureReleaseSchedulePickingSlipColumns()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WH_ReleaseSchedule
                (
                    ReleaseScheduleID int IDENTITY(1,1) NOT NULL,
                    WoID int NULL,
                    ItemNo varchar(20) NULL,
                    DemandQty decimal(14,3) NULL,
                    PickedQty decimal(14,3) NULL,
                    RequiredAt datetime2 NULL,
                    Priority tinyint NULL,
                    Status varchar(20) NULL,
                    CreatedBy varchar(50) NOT NULL,
                    CreatedTS datetime2 NULL CONSTRAINT DF_WH_ReleaseSchedule_CreatedTS DEFAULT SYSDATETIME(),
                    ModifiedBy nvarchar(450) NULL,
                    ModifiedTS datetime2 NULL,
                    CONSTRAINT PK_WH_ReleaseSchedule PRIMARY KEY CLUSTERED (ReleaseScheduleID)
                );
            END;

            IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PickSlipNo') IS NULL
                ALTER TABLE dbo.WH_ReleaseSchedule ADD PickSlipNo nvarchar(40) NULL;

            IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqLocation') IS NULL
                ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqLocation nvarchar(40) NULL;

            IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqSeqNo') IS NULL
                ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqSeqNo int NULL;

            IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'ReqUserId') IS NULL
                ALTER TABLE dbo.WH_ReleaseSchedule ADD ReqUserId nvarchar(80) NULL;

            IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'PrintDate') IS NULL
                ALTER TABLE dbo.WH_ReleaseSchedule ADD PrintDate datetime2 NULL;

            IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'CloseDate') IS NULL
                ALTER TABLE dbo.WH_ReleaseSchedule ADD CloseDate datetime2 NULL;

            IF COL_LENGTH(N'dbo.WH_ReleaseSchedule', N'CloseUserId') IS NULL
                ALTER TABLE dbo.WH_ReleaseSchedule ADD CloseUserId nvarchar(80) NULL;

            UPDATE dbo.WH_ReleaseSchedule
               SET PickSlipNo = CONCAT(N'RS-', ReleaseScheduleID)
             WHERE NULLIF(PickSlipNo, N'') IS NULL;

            UPDATE dbo.WH_ReleaseSchedule
               SET ReqSeqNo = 1
             WHERE ReqSeqNo IS NULL;

            UPDATE dbo.WH_ReleaseSchedule
               SET ReqUserId = CreatedBy
             WHERE NULLIF(ReqUserId, N'') IS NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WH_ReleaseSchedule') AND name = N'IX_WH_ReleaseSchedule_PickSlipNo')
                CREATE INDEX IX_WH_ReleaseSchedule_PickSlipNo ON dbo.WH_ReleaseSchedule (PickSlipNo, ReqSeqNo, ReleaseScheduleID);

            IF OBJECT_ID(N'dbo.WH_ReleasePickAllocation', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WH_ReleasePickAllocation
                (
                    PickAllocationID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    PickSlipNo nvarchar(40) NOT NULL,
                    ReleaseScheduleID int NOT NULL,
                    ItemNo varchar(20) NOT NULL,
                    AllocationSeq int NOT NULL,
                    LocationID varchar(20) NOT NULL,
                    LotID int NOT NULL,
                    LotNo varchar(50) NOT NULL,
                    ProductionDate datetime2 NULL,
                    AllocatedQty decimal(14,3) NOT NULL,
                    AllocatedBoxQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_AllocatedBoxQty DEFAULT (1),
                    PickedQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_PickedQty DEFAULT (0),
                    PickedBoxQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_PickedBoxQty DEFAULT (0),
                    Status varchar(20) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_Status DEFAULT ('Open'),
                    CreatedBy nvarchar(80) NULL,
                    CreatedTS datetime2 NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_CreatedTS DEFAULT (SYSDATETIME()),
                    ModifiedBy nvarchar(80) NULL,
                    ModifiedTS datetime2 NULL,
                    CONSTRAINT UQ_WH_ReleasePickAllocation_Lot UNIQUE (ReleaseScheduleID, LotID)
                );

                CREATE INDEX IX_WH_ReleasePickAllocation_Slip
                    ON dbo.WH_ReleasePickAllocation (PickSlipNo, ReleaseScheduleID, AllocationSeq);
            END;

            IF COL_LENGTH(N'dbo.WH_ReleasePickAllocation', N'AllocatedBoxQty') IS NULL
                ALTER TABLE dbo.WH_ReleasePickAllocation
                    ADD AllocatedBoxQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_AllocatedBoxQty_Existing DEFAULT (1);

            IF COL_LENGTH(N'dbo.WH_ReleasePickAllocation', N'PickedBoxQty') IS NULL
                ALTER TABLE dbo.WH_ReleasePickAllocation
                    ADD PickedBoxQty decimal(14,3) NOT NULL CONSTRAINT DF_WH_ReleasePickAllocation_PickedBoxQty_Existing DEFAULT (0);

            """, conn)
        {
            CommandTimeout = 15
        };
        cmd.ExecuteNonQuery();

        using var backfill = new SqlCommand("""
            UPDATE dbo.WH_ReleasePickAllocation
               SET AllocatedBoxQty = 1
             WHERE COALESCE(AllocatedBoxQty, 0) = 0;
            """, conn)
        {
            CommandTimeout = 15
        };
        backfill.ExecuteNonQuery();
    }

    private static bool PickSlipExists(SqlConnection conn, SqlTransaction tx, string pickSlipNo)
    {
        using var cmd = new SqlCommand("""
            SELECT 1
            FROM dbo.WH_ReleaseSchedule
            WHERE COALESCE(NULLIF(PickSlipNo, N''), CONCAT(N'RS-', ReleaseScheduleID)) = @PickSlipNo;
            """, conn, tx);
        cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo;
        return cmd.ExecuteScalar() is not null;
    }

    private static string GeneratePickSlipNo(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            DECLARE @Prefix char(8) = CONVERT(char(8), SYSDATETIME(), 112);
            DECLARE @Seq int;

            SELECT @Seq = COALESCE(MAX(TRY_CONVERT(int, RIGHT(PickSlipNo, 2))), 0) + 1
            FROM dbo.WH_ReleaseSchedule WITH (UPDLOCK, HOLDLOCK)
            WHERE PickSlipNo LIKE @Prefix + N'[0-9][0-9]';

            SELECT @Prefix + RIGHT(N'00' + CONVERT(nvarchar(10), COALESCE(@Seq, 1)), 2);
            """, conn, tx);
        return Convert.ToString(cmd.ExecuteScalar()) ?? DateTime.Now.ToString("yyyyMMdd") + "01";
    }

    private void EnsureAreaLayoutTable()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WH_AreaLayout (
                    AREACD NVARCHAR(20) NOT NULL CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY,
                    X_PCT DECIMAL(8,2) NOT NULL,
                    Y_PCT DECIMAL(8,2) NOT NULL,
                    W_PCT DECIMAL(8,2) NOT NULL,
                    H_PCT DECIMAL(8,2) NOT NULL,
                    MODIFIED_BY NVARCHAR(80) NULL,
                    MODIFIED_TS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_LAYOUT_MODIFIED_TS DEFAULT SYSDATETIME()
                );
            END;

            IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.WH_AreaLayout', N'AREACD') < 160
            BEGIN
                DECLARE @pkName sysname;
                SELECT @pkName = kc.name
                FROM sys.key_constraints kc
                WHERE kc.parent_object_id = OBJECT_ID(N'dbo.WH_AreaLayout')
                  AND kc.[type] = 'PK';

                IF @pkName IS NOT NULL
                BEGIN
                    DECLARE @dropSql nvarchar(max) = N'ALTER TABLE dbo.WH_AreaLayout DROP CONSTRAINT ' + QUOTENAME(@pkName);
                    EXEC sys.sp_executesql @dropSql;
                END;

                ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN AREACD NVARCHAR(80) NOT NULL;

                ALTER TABLE dbo.WH_AreaLayout
                    ADD CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY (AREACD);
            END;

            IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.WH_AreaLayout')
                     AND name IN (N'X_PCT', N'Y_PCT', N'W_PCT', N'H_PCT')
                     AND precision < 8
               )
            BEGIN
                ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN X_PCT DECIMAL(8,2) NOT NULL;
                ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN Y_PCT DECIMAL(8,2) NOT NULL;
                ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN W_PCT DECIMAL(8,2) NOT NULL;
                ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN H_PCT DECIMAL(8,2) NOT NULL;
            END;
        """, conn);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureOperationLogTable(SqlConnection conn)
    {
        using var cmd = new SqlCommand("""
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

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'EventTime') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD EventTime datetime2 NOT NULL DEFAULT SYSDATETIME();

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'EventType') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD EventType varchar(40) NOT NULL DEFAULT 'INFO';

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'ScreenCode') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD ScreenCode varchar(20) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'EmployeeNo') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD EmployeeNo nvarchar(40) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'EmployeeName') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD EmployeeName nvarchar(120) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'WorkerID') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD WorkerID nvarchar(450) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'TerminalID') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD TerminalID nvarchar(80) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'LineID') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD LineID nvarchar(40) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'ShiftCode') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD ShiftCode nvarchar(20) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'ScanType') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD ScanType varchar(30) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'ScanValue') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD ScanValue nvarchar(120) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'Result') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD Result varchar(20) NOT NULL DEFAULT 'INFO';

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'Message') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD Message nvarchar(500) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'ClientIP') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD ClientIP nvarchar(64) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'UserAgent') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD UserAgent nvarchar(300) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'RefDocType') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD RefDocType varchar(30) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'RefDocNo') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD RefDocNo nvarchar(80) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'LotNo') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD LotNo nvarchar(80) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'PartNo') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD PartNo nvarchar(80) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'LocationID') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD LocationID nvarchar(80) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'Qty') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD Qty decimal(14,3) NULL;

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'CreatedBy') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD CreatedBy varchar(50) NOT NULL CONSTRAINT DF_WH_OperationLog_CreatedBy DEFAULT 'system';

            IF COL_LENGTH(N'dbo.WH_OperationLog', N'CreatedTS') IS NULL
                ALTER TABLE dbo.WH_OperationLog ADD CreatedTS datetime2 NOT NULL CONSTRAINT DF_WH_OperationLog_CreatedTS DEFAULT SYSDATETIME();

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_OperationLog_Time' AND object_id = OBJECT_ID(N'dbo.WH_OperationLog'))
                CREATE INDEX IX_WH_OperationLog_Time ON dbo.WH_OperationLog (EventTime DESC, OperationLogID DESC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_OperationLog_Search' AND object_id = OBJECT_ID(N'dbo.WH_OperationLog'))
                CREATE INDEX IX_WH_OperationLog_Search ON dbo.WH_OperationLog (EventType, EmployeeNo, WorkerID, ScanValue);
            """, conn)
        {
            CommandTimeout = 15
        };
        cmd.ExecuteNonQuery();
    }

    private void EnsureWarehouseMasterTable()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.WH_WarehouseMaster', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WH_WarehouseMaster (
                    WhCode VARCHAR(20) NOT NULL CONSTRAINT PK_WH_WAREHOUSE_MASTER PRIMARY KEY,
                    WhName NVARCHAR(120) NULL,
                    ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_WAREHOUSE_MASTER_ACTIVE DEFAULT 1,
                    CreatedBy NVARCHAR(80) NULL,
                    CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_WAREHOUSE_MASTER_CREATED_TS DEFAULT SYSDATETIME(),
                    ModifiedBy NVARCHAR(80) NULL,
                    ModifiedTS DATETIME2 NULL
                );
            END;

            MERGE dbo.WH_WarehouseMaster AS tgt
            USING (
                SELECT DISTINCT
                    CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20)) AS WhCode
                FROM dbo.MD_Location
                WHERE COALESCE(NULLIF(PlantCode, ''), 'WH') IS NOT NULL
            ) AS src ON tgt.WhCode = src.WhCode
            WHEN NOT MATCHED THEN INSERT
                (WhCode, WhName, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (src.WhCode, src.WhCode, 1, 'system', SYSDATETIME());
        """, conn);
        cmd.ExecuteNonQuery();
    }

    private void EnsureWarehouseAreaTable()
    {
        EnsureWarehouseMasterTable();

        using var conn = _factory.OpenConnection();
        using (var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.WH_AreaMaster', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WH_AreaMaster (
                    WhCode VARCHAR(20) NULL,
                    AreaCode VARCHAR(20) NOT NULL CONSTRAINT PK_WH_AREA_MASTER PRIMARY KEY,
                    AreaName NVARCHAR(120) NULL,
                    ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_AREA_MASTER_ACTIVE DEFAULT 1,
                    CreatedBy NVARCHAR(80) NULL,
                    CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_MASTER_CREATED_TS DEFAULT SYSDATETIME(),
                    ModifiedBy NVARCHAR(80) NULL,
                    ModifiedTS DATETIME2 NULL
                );
            END;

            IF COL_LENGTH(N'dbo.WH_AreaMaster', N'WhCode') IS NULL
            BEGIN
                ALTER TABLE dbo.WH_AreaMaster ADD WhCode VARCHAR(20) NULL;
            END;
        """, conn))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqlCommand("""
            UPDATE A
               SET WhCode = COALESCE(NULLIF(A.WhCode, ''), X.WhCode, A.AreaCode)
            FROM dbo.WH_AreaMaster A
            OUTER APPLY (
                SELECT TOP (1) L.PlantCode AS WhCode
                FROM dbo.MD_Location L
                WHERE COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = A.AreaCode
                  AND NULLIF(L.PlantCode, '') IS NOT NULL
                ORDER BY L.PlantCode
            ) X
            WHERE NULLIF(A.WhCode, '') IS NULL;
        """, conn))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqlCommand("""
            MERGE dbo.WH_AreaMaster AS tgt
            USING (
                SELECT DISTINCT
                    CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20)) AS WhCode,
                    CAST(COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') AS varchar(20)) AS AreaCode
                FROM dbo.MD_Location
                WHERE COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') IS NOT NULL
            ) AS src ON tgt.AreaCode = src.AreaCode
            WHEN NOT MATCHED THEN INSERT
                (WhCode, AreaCode, AreaName, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (src.WhCode, src.AreaCode, src.AreaCode, 1, 'system', SYSDATETIME());
        """, conn))
        {
            cmd.ExecuteNonQuery();
        }
    }

    private void EnsureWarehouseSectionTable()
    {
        EnsureWarehouseAreaTable();

        using var conn = _factory.OpenConnection();
        using (var cmd = new SqlCommand("""
            IF OBJECT_ID(N'dbo.WH_AreaSection', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WH_AreaSection (
                    WhCode VARCHAR(20) NULL,
                    AreaCode VARCHAR(20) NOT NULL,
                    SectionCode VARCHAR(20) NOT NULL,
                    SectionName NVARCHAR(120) NULL,
                    ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_AREA_SECTION_ACTIVE DEFAULT 1,
                    CreatedBy NVARCHAR(80) NULL,
                    CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_SECTION_CREATED_TS DEFAULT SYSDATETIME(),
                    ModifiedBy NVARCHAR(80) NULL,
                    ModifiedTS DATETIME2 NULL,
                    CONSTRAINT PK_WH_AREA_SECTION PRIMARY KEY (AreaCode, SectionCode)
                );
            END;

            IF COL_LENGTH(N'dbo.WH_AreaSection', N'WhCode') IS NULL
            BEGIN
                ALTER TABLE dbo.WH_AreaSection ADD WhCode VARCHAR(20) NULL;
            END;
        """, conn))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqlCommand("""
            UPDATE S
               SET WhCode = COALESCE(NULLIF(S.WhCode, ''), A.WhCode, X.WhCode)
            FROM dbo.WH_AreaSection S
            LEFT JOIN dbo.WH_AreaMaster A
                   ON A.AreaCode = S.AreaCode
            OUTER APPLY (
                SELECT TOP (1) L.PlantCode AS WhCode
                FROM dbo.MD_Location L
                WHERE COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = S.AreaCode
                  AND COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') = S.SectionCode
                  AND NULLIF(L.PlantCode, '') IS NOT NULL
                ORDER BY L.PlantCode
            ) X
            WHERE NULLIF(S.WhCode, '') IS NULL;
        """, conn))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqlCommand("""
            MERGE dbo.WH_AreaSection AS tgt
            USING (
                SELECT DISTINCT
                    CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20)) AS WhCode,
                    CAST(COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') AS varchar(20)) AS AreaCode,
                    CAST(COALESCE(NULLIF(LocationType, ''), 'DEFAULT') AS varchar(20)) AS SectionCode
                FROM dbo.MD_Location
                WHERE COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') IS NOT NULL
            ) AS src
               ON tgt.AreaCode = src.AreaCode
              AND tgt.SectionCode = src.SectionCode
            WHEN NOT MATCHED THEN INSERT
                (WhCode, AreaCode, SectionCode, SectionName, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (src.WhCode, src.AreaCode, src.SectionCode, src.SectionCode, 1, 'system', SYSDATETIME());
        """, conn))
        {
            cmd.ExecuteNonQuery();
        }
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
        AddNullable(cmd, "@ZoneCode", SqlDbType.VarChar, 10, FirstNonBlank(areaCode, zoneCode));
        AddNullable(cmd, "@LocationType", SqlDbType.VarChar, 20, FirstNonBlank(zoneCode, zoneName, areaName));
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

    private static string WarehouseLayoutKey(string whCode) =>
        $"WH|{Truncate(whCode.Trim(), 76)}";

    private static string OldAreaLayoutKey(string areaCode) =>
        $"AREA|{Truncate(areaCode.Trim(), 74)}";

    private static string AreaLayoutKey(string whCode, string areaCode) =>
        $"AREA|{Truncate(whCode.Trim(), 20)}|{Truncate(areaCode.Trim(), 52)}";

    private static string ZoneLayoutKey(string whCode, string areaCode, string zoneCode) =>
        $"ZONE|{Truncate(whCode.Trim(), 18)}|{Truncate(areaCode.Trim(), 26)}|{Truncate(zoneCode.Trim(), 26)}";

    private static string LocationLayoutKey(string locationNo) =>
        $"LOC|{Truncate(locationNo.Trim(), 75)}";

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

    private static void AddOperationLogParameters(
        SqlCommand cmd,
        string eventType,
        string? screenCode,
        string? workerId,
        string? workerName,
        string? message,
        string? refDocType,
        string? refDocNo,
        string? locationId,
        string result)
    {
        AddNullable(cmd, "@EventType", SqlDbType.VarChar, 40, eventType.ToUpperInvariant());
        AddNullable(cmd, "@ScreenCode", SqlDbType.VarChar, 20, TruncateOrNull(screenCode, 20));
        AddNullable(cmd, "@EmployeeNo", SqlDbType.NVarChar, 40, TruncateOrNull(workerId, 40));
        AddNullable(cmd, "@EmployeeName", SqlDbType.NVarChar, 120, TruncateOrNull(workerName, 120));
        AddNullable(cmd, "@WorkerID", SqlDbType.NVarChar, 450, TruncateOrNull(workerId, 450));
        AddNullable(cmd, "@TerminalID", SqlDbType.NVarChar, 80, "WEB");
        AddNullable(cmd, "@ScanType", SqlDbType.VarChar, 30, "MASTER");
        AddNullable(cmd, "@ScanValue", SqlDbType.NVarChar, 120, TruncateOrNull(refDocNo ?? locationId, 120));
        AddNullable(cmd, "@Result", SqlDbType.VarChar, 20, string.IsNullOrWhiteSpace(result) ? "SUCCESS" : result.ToUpperInvariant());
        AddNullable(cmd, "@Message", SqlDbType.NVarChar, 500, TruncateOrNull(message, 500));
        AddNullable(cmd, "@ClientIP", SqlDbType.NVarChar, 64, null);
        AddNullable(cmd, "@UserAgent", SqlDbType.NVarChar, 300, null);
        AddNullable(cmd, "@RefDocType", SqlDbType.VarChar, 30, TruncateOrNull(refDocType, 30));
        AddNullable(cmd, "@RefDocNo", SqlDbType.NVarChar, 80, TruncateOrNull(refDocNo, 80));
        AddNullable(cmd, "@LotNo", SqlDbType.NVarChar, 80, null);
        AddNullable(cmd, "@PartNo", SqlDbType.NVarChar, 80, null);
        AddNullable(cmd, "@LocationID", SqlDbType.NVarChar, 80, TruncateOrNull(locationId, 80));
        if (!cmd.Parameters.Contains("@Qty"))
        {
            var qty = cmd.Parameters.Add("@Qty", SqlDbType.Decimal);
            qty.Precision = 14;
            qty.Scale = 3;
            qty.Value = DBNull.Value;
        }
        AddNullable(cmd, "@CreatedBy", SqlDbType.VarChar, 50, TruncateOrNull(workerId, 50) ?? "web");
    }

    private static string? TruncateOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Truncate(value, maxLength);
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
