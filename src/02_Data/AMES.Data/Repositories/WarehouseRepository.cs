using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;
using AMES.Data.Services;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
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
        string? WhName,
        string? LocationPrefix);

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
        string? FirstPartName,
        string QuantityUnit = "BOX");

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
        string Status,
        string QuantityUnit = "BOX");

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

    public record UnifiedInventoryRow(
        string LocationNo,
        string PartNo,
        string? PartName,
        string LotNo,
        decimal Qty,
        string Unit,
        string WarehouseCode,
        string WarehouseName,
        string AreaCode,
        string AreaName);

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
        string? SourceType,
        int? SourceId,
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
        var like = Like(search);
        return Query("""
            WITH Stock AS (
                SELECT LocationNo,
                       COUNT(DISTINCT LotNo) AS LotCount,
                       COUNT(DISTINCT PartNo) AS PartCount,
                       SUM(Qty) AS TotalQty
                FROM dbo.WH_Inventory
                WHERE Qty <> 0
                GROUP BY LocationNo
            )
            SELECT
                L.LocationID AS LOCATION_NO,
                L.LocationName AS LOCATION_NM,
                L.WhCode AS WHCD,
                COALESCE(NULLIF(W.CodeName, ''), L.WhCode) AS WHNM,
                L.AreaCode AS AREACD,
                COALESCE(NULLIF(A.CodeName, ''), L.AreaCode) AS AREANM,
                L.ZoneCode AS ZONECD,
                L.ZoneCode AS ZONENM,
                L.Aisle AS RACK_X,
                L.Bay AS RACK_Y,
                L.Slot AS RACK_Z,
                CAST(COALESCE(L.ActiveFlag, 1) AS bit) AS USE_YN,
                COALESCE(S.LotCount, 0) AS LOT_COUNT,
                COALESCE(S.PartCount, 0) AS PART_COUNT,
                COALESCE(S.TotalQty, 0) AS TOTAL_QTY
            FROM dbo.MD_Location L
            OUTER APPLY (
                SELECT TOP (1) CodeID, CodeName
                FROM dbo.MD_CodeItem
                WHERE GroupCode = 'WH_CODE' AND CodeValue = L.WhCode
                ORDER BY CodeID
            ) W
            OUTER APPLY (
                SELECT TOP (1) CodeName
                FROM dbo.MD_CodeItem
                WHERE GroupCode = 'WH_AREA' AND CodeValue = L.AreaCode
                  AND (ParentCodeID IS NULL OR ParentCodeID = W.CodeID)
                ORDER BY CASE WHEN ParentCodeID = W.CodeID THEN 0 ELSE 1 END, CodeID
            ) A
            LEFT JOIN Stock S ON S.LocationNo = L.LocationID
            WHERE (@IncludeInactive = 1 OR COALESCE(L.ActiveFlag, 1) = 1)
              AND (@WhCode IS NULL OR L.WhCode = @WhCode)
              AND (@AreaCode IS NULL OR L.AreaCode = @AreaCode)
              AND (@SectionCode IS NULL OR COALESCE(NULLIF(L.ZoneCode, ''), 'DEFAULT') = @SectionCode)
              AND (@Search IS NULL
                   OR L.LocationID LIKE @Search
                   OR L.LocationName LIKE @Search
                   OR L.ZoneCode LIKE @Search
                   OR L.AreaCode LIKE @Search
                   OR L.WhCode LIKE @Search)
            ORDER BY L.WhCode, L.AreaCode, L.ZoneCode,
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

    public List<UnifiedInventoryRow> ListUnifiedInventory(bool finishedGoods)
    {
        return Query("""
            SELECT
                I.LocationNo AS LOCATION_NO,
                I.PartNo AS PART_NO,
                COALESCE(NULLIF(I.PartName, ''), M.ItemName) AS PART_NAME,
                I.LotNo AS LOT_NO,
                I.Qty AS QTY,
                COALESCE(NULLIF(M.DefaultUOM, ''), 'EA') AS UOM,
                COALESCE(NULLIF(L.WhCode, ''), 'EOS') AS WH_CODE,
                COALESCE(NULLIF(W.CodeName, ''), NULLIF(L.WhCode, ''), 'EOS') AS WH_NAME,
                COALESCE(L.AreaCode, '') AS AREA_CODE,
                COALESCE(NULLIF(A.CodeName, ''), L.AreaCode, '') AS AREA_NAME
            FROM dbo.WH_Inventory I
            LEFT JOIN dbo.MD_Location L
                   ON L.LocationID = I.LocationNo
            LEFT JOIN dbo.MD_CodeItem W ON W.GroupCode='WH_CODE' AND W.CodeValue=L.WhCode
            LEFT JOIN dbo.MD_CodeItem A ON A.GroupCode='WH_AREA' AND A.CodeValue=L.AreaCode
                  AND (A.ParentCodeID IS NULL OR A.ParentCodeID=W.CodeID)
            LEFT JOIN dbo.MD_Item M
                   ON M.ItemNo = I.PartNo
            WHERE I.Qty > 0
              AND I.PartNo IS NOT NULL
              AND ((@FinishedGoods = 1 AND L.AreaCode = 'FG_AREA')
                OR (@FinishedGoods = 0 AND COALESCE(L.AreaCode, '') <> 'FG_AREA'))
            ORDER BY I.ReceivedAt, I.CreatedAt, I.LotNo;
            """, r => new UnifiedInventoryRow(
                GetString(r, "LOCATION_NO") ?? "",
                GetString(r, "PART_NO") ?? "",
                GetString(r, "PART_NAME"),
                GetString(r, "LOT_NO") ?? "",
                GetDecimal(r, "QTY"),
                GetString(r, "UOM") ?? "EA",
                GetString(r, "WH_CODE") ?? "EOS",
                GetString(r, "WH_NAME") ?? "EOS",
                GetString(r, "AREA_CODE") ?? "",
                GetString(r, "AREA_NAME") ?? ""),
            ("@FinishedGoods", finishedGoods));
    }

    public List<WarehouseMasterRow> ListWarehouses(string? search = null, bool includeInactive = false)
    {
        var like = Like(search);
        return Query("""
            SELECT
                W.CodeValue AS WHCD,
                W.CodeName AS WHNM,
                CAST(COALESCE(W.UseFlag, 1) AS bit) AS USE_YN,
                COUNT(DISTINCT A.CodeValue) AS AREA_COUNT,
                COUNT(DISTINCT L.LocationID) AS LOCATION_COUNT,
                COALESCE(SUM(S.Qty), 0) AS TOTAL_QTY
            FROM dbo.MD_CodeItem W
            LEFT JOIN dbo.MD_CodeItem A ON A.GroupCode='WH_AREA' AND A.ParentCodeID=W.CodeID AND COALESCE(A.UseFlag,1)=1
            LEFT JOIN dbo.MD_Location L ON L.WhCode=W.CodeValue AND COALESCE(L.ActiveFlag,1)=1
            LEFT JOIN dbo.WH_Inventory S ON S.LocationNo=L.LocationID AND S.Qty<>0
            WHERE W.GroupCode='WH_CODE'
              AND (@IncludeInactive = 1 OR COALESCE(W.UseFlag, 1) = 1)
              AND (@Search IS NULL
                   OR W.CodeValue LIKE @Search
                   OR W.CodeName LIKE @Search)
            GROUP BY W.CodeValue,W.CodeName,W.UseFlag,W.SortOrder
            ORDER BY W.SortOrder,W.CodeValue;
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
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='WH_CODE' AND CodeValue=@WhCode;", conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = whCode.Trim();
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveWarehouse(string whCode, string? whName, bool useYn)
    {
        if (string.IsNullOrWhiteSpace(whCode))
            throw new ArgumentException("Warehouse code is required.", nameof(whCode));

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            MERGE dbo.MD_CodeItem AS tgt
            USING (SELECT CONCAT('WH_CODE_',@WhCode) AS CodeID) AS src ON tgt.CodeID=src.CodeID
            WHEN MATCHED THEN UPDATE SET
                CodeName=@WhName,UseFlag=@UseYn,
                ModifiedBy = 'web',
                ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (CodeID,GroupCode,CodeValue,CodeName,SortOrder,UseFlag,CreatedBy,CreatedTS)
            VALUES
                (src.CodeID,'WH_CODE',@WhCode,@WhName,100,@UseYn,'web',SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = Truncate(whCode.Trim(), 20);
        AddNullable(cmd, "@WhName", SqlDbType.NVarChar, 60, whName);
        cmd.Parameters.Add("@UseYn", SqlDbType.Bit).Value = useYn;
        cmd.ExecuteNonQuery();
    }

    public void DeleteWarehouse(string whCode)
    {
        if (string.IsNullOrWhiteSpace(whCode))
            return;

        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.MD_Location
            WHERE WhCode = @WhCode;
            """, conn);
        check.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = whCode.Trim();
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Warehouse has locations and cannot be deleted.");

        using var cmd = new SqlCommand("DELETE FROM dbo.MD_CodeItem WHERE GroupCode='WH_CODE' AND CodeValue=@WhCode;", conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = whCode.Trim();
        cmd.ExecuteNonQuery();
    }

    public List<WarehouseAreaRow> ListWarehouseAreas(string? search = null, bool includeInactive = false, string? whCode = null)
    {
        var like = Like(search);
        return Query("""
            SELECT
                A.CodeValue AS AREACD,
                A.CodeName AS AREANM,
                A.Attribute1 AS LOCATION_PREFIX,
                W.CodeValue AS WHCD,
                COALESCE(NULLIF(W.CodeName,''),W.CodeValue) AS WHNM,
                CAST(COALESCE(A.UseFlag, 1) AS bit) AS USE_YN,
                COUNT(DISTINCT L.LocationID) AS LOCATION_COUNT,
                COALESCE(SUM(S.Qty), 0) AS TOTAL_QTY
            FROM dbo.MD_CodeItem A
            LEFT JOIN dbo.MD_CodeItem W ON W.CodeID=A.ParentCodeID AND W.GroupCode='WH_CODE'
            LEFT JOIN dbo.MD_Location L
                   ON L.AreaCode=A.CodeValue
                  AND (@WhCode IS NULL OR L.WhCode = @WhCode)
                  AND COALESCE(L.ActiveFlag, 1) = 1
            LEFT JOIN dbo.WH_Inventory S ON S.LocationNo=L.LocationID AND S.Qty<>0
            WHERE A.GroupCode='WH_AREA'
              AND (@IncludeInactive = 1 OR COALESCE(A.UseFlag, 1) = 1)
              AND (@WhCode IS NULL OR W.CodeValue=@WhCode)
              AND (@Search IS NULL
                   OR A.CodeValue LIKE @Search
                   OR A.CodeName LIKE @Search)
            GROUP BY A.CodeValue,A.CodeName,A.Attribute1,W.CodeValue,W.CodeName,A.UseFlag,A.SortOrder
            ORDER BY A.SortOrder,A.CodeValue;
            """, r => new WarehouseAreaRow(
                GetString(r, "AREACD") ?? "",
                GetString(r, "AREANM"),
                GetBool(r, "USE_YN"),
                GetInt(r, "LOCATION_COUNT"),
                GetDecimal(r, "TOTAL_QTY"),
                GetString(r, "WHCD"),
                GetString(r, "WHNM"),
                GetString(r, "LOCATION_PREFIX")),
            ("@Search", like),
            ("@IncludeInactive", includeInactive),
            ("@WhCode", NullIfBlank(whCode)));
    }

    public List<WarehouseSectionRow> ListWarehouseSections(string? areaCode = null, string? search = null, bool includeInactive = false, string? whCode = null)
    {
        var like = Like(search);
        return Query("""
            WITH ZoneBase AS
            (
                SELECT CodeValue AS SectionCode,CodeName AS SectionName,UseFlag,SortOrder
                FROM dbo.MD_CodeItem WHERE GroupCode='WH_ZONE'
                UNION
                SELECT DISTINCT COALESCE(NULLIF(ZoneCode,''),'DEFAULT'),COALESCE(NULLIF(ZoneCode,''),'DEFAULT'),CAST(1 AS bit),999
                FROM dbo.MD_Location
            )
            SELECT
                @AreaCode AS AREACD,
                S.SectionCode AS SECTIONCD,
                S.SectionName AS SECTIONNM,
                @WhCode AS WHCD,
                CAST(COALESCE(S.UseFlag, 1) AS bit) AS USE_YN,
                COUNT(DISTINCT L.LocationID) AS LOCATION_COUNT,
                COALESCE(SUM(I.Qty), 0) AS TOTAL_QTY
            FROM ZoneBase S
            LEFT JOIN dbo.MD_Location L
                   ON (@AreaCode IS NULL OR L.AreaCode=@AreaCode)
                  AND COALESCE(NULLIF(L.ZoneCode, ''), 'DEFAULT') = S.SectionCode
                  AND (@WhCode IS NULL OR L.WhCode = @WhCode)
                  AND COALESCE(L.ActiveFlag, 1) = 1
            LEFT JOIN dbo.WH_Inventory I ON I.LocationNo=L.LocationID AND I.Qty<>0
            WHERE (@IncludeInactive = 1 OR COALESCE(S.UseFlag, 1) = 1)
              AND (@Search IS NULL
                   OR S.SectionCode LIKE @Search
                   OR S.SectionName LIKE @Search)
            GROUP BY S.SectionCode,S.SectionName,S.UseFlag,S.SortOrder
            HAVING COUNT(L.LocationID)>0 OR EXISTS(SELECT 1 FROM dbo.MD_CodeItem C WHERE C.GroupCode='WH_ZONE' AND C.CodeValue=S.SectionCode)
            ORDER BY S.SortOrder,S.SectionCode;
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
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT 1
            WHERE EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='WH_ZONE' AND CodeValue=@SectionCode)
               OR EXISTS(SELECT 1 FROM dbo.MD_Location WHERE AreaCode=@AreaCode AND ZoneCode=@SectionCode
                         AND (@WhCode IS NULL OR WhCode=@WhCode));
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

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            MERGE dbo.MD_CodeItem AS tgt
            USING(SELECT CONCAT('WH_ZONE_',@SectionCode) CodeID) src ON src.CodeID=tgt.CodeID
            WHEN MATCHED THEN UPDATE SET
                CodeName=@SectionName,UseFlag=@UseYn,ModifiedBy='web',ModifiedTS=SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (CodeID,GroupCode,CodeValue,CodeName,SortOrder,UseFlag,CreatedBy,CreatedTS)
            VALUES
                (src.CodeID,'WH_ZONE',@SectionCode,@SectionName,100,@UseYn,'web',SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(Truncate(whCode?.Trim() ?? "", 20)) ?? DBNull.Value;
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = Truncate(areaCode.Trim(), 20);
        cmd.Parameters.Add("@SectionCode", SqlDbType.VarChar, 20).Value = Truncate(sectionCode.Trim(), 20);
        AddNullable(cmd, "@SectionName", SqlDbType.NVarChar, 60, sectionName);
        cmd.Parameters.Add("@UseYn", SqlDbType.Bit).Value = useYn;
        cmd.ExecuteNonQuery();
    }

    public void DeleteWarehouseSection(string areaCode, string sectionCode, string? whCode = null)
    {
        if (string.IsNullOrWhiteSpace(areaCode) || string.IsNullOrWhiteSpace(sectionCode))
            return;

        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.MD_Location
            WHERE AreaCode = @AreaCode
              AND COALESCE(NULLIF(ZoneCode, ''), 'DEFAULT') = @SectionCode
              AND (@WhCode IS NULL OR WhCode = @WhCode);
            """, conn);
        check.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        check.Parameters.Add("@SectionCode", SqlDbType.VarChar, 20).Value = sectionCode.Trim();
        check.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Section has locations and cannot be deleted.");

        using var cmd = new SqlCommand("""
            DELETE FROM dbo.MD_CodeItem WHERE GroupCode='WH_ZONE' AND CodeValue=@SectionCode;
            """, conn);
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        cmd.Parameters.Add("@SectionCode", SqlDbType.VarChar, 20).Value = sectionCode.Trim();
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    public bool WarehouseAreaExists(string areaCode, string? whCode = null)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT 1
            FROM dbo.MD_CodeItem A LEFT JOIN dbo.MD_CodeItem W ON W.CodeID=A.ParentCodeID
            WHERE A.GroupCode='WH_AREA' AND A.CodeValue=@AreaCode
              AND (@WhCode IS NULL OR W.CodeValue=@WhCode);
            """, conn);
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode;
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveWarehouseArea(string areaCode, string? areaName, bool useYn, string? whCode = null)
    {
        if (string.IsNullOrWhiteSpace(areaCode))
            throw new ArgumentException("Area code is required.", nameof(areaCode));

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            MERGE dbo.MD_CodeItem AS tgt
            USING (SELECT CONCAT('WH_AREA_',@AreaCode) AS CodeID) AS src ON tgt.CodeID=src.CodeID
            WHEN MATCHED THEN UPDATE SET
                ParentCodeID=CASE WHEN @WhCode IS NULL THEN NULL ELSE CONCAT('WH_CODE_',@WhCode) END,
                CodeName=@AreaName,UseFlag=@UseYn,
                ModifiedBy = 'web',
                ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (CodeID,GroupCode,CodeValue,CodeName,ParentCodeID,SortOrder,UseFlag,CreatedBy,CreatedTS)
            VALUES
                (src.CodeID,'WH_AREA',@AreaCode,@AreaName,
                 CASE WHEN @WhCode IS NULL THEN NULL ELSE CONCAT('WH_CODE_',@WhCode) END,
                 100,@UseYn,'web',SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(Truncate(whCode?.Trim() ?? "", 20)) ?? DBNull.Value;
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = Truncate(areaCode.Trim(), 20);
        AddNullable(cmd, "@AreaName", SqlDbType.NVarChar, 60, areaName);
        cmd.Parameters.Add("@UseYn", SqlDbType.Bit).Value = useYn;
        cmd.ExecuteNonQuery();
    }

    public void DeleteWarehouseArea(string areaCode, string? whCode = null)
    {
        if (string.IsNullOrWhiteSpace(areaCode))
            return;

        using var conn = _factory.OpenConnection();
        using var check = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.MD_Location
            WHERE AreaCode = @AreaCode
              AND (@WhCode IS NULL OR WhCode = @WhCode);
            """, conn);
        check.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        check.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Area has locations and cannot be deleted.");

        using var cmd = new SqlCommand("DELETE FROM dbo.MD_CodeItem WHERE GroupCode='WH_AREA' AND CodeValue=@AreaCode;", conn);
        cmd.Parameters.Add("@AreaCode", SqlDbType.VarChar, 20).Value = areaCode.Trim();
        cmd.Parameters.Add("@WhCode", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(whCode) ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    public record LocationMapDeleteImpact(int Locations, int StockRows, decimal StockQty);

    public LocationMapDeleteImpact GetLocationMapDeleteImpact(string whCode, string? areaCode, string? floor = null)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT COUNT(DISTINCT l.LocationID), COUNT(i.LotNo), COALESCE(SUM(ABS(i.Qty)),0)
            FROM dbo.MD_Location l
            LEFT JOIN dbo.WH_Inventory i ON i.LocationNo=l.LocationID
            WHERE l.WhCode=@Wh AND (@Area IS NULL OR l.AreaCode=@Area) AND (@Floor IS NULL OR l.Slot=@Floor);
            """, conn);
        cmd.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
        cmd.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = (object?)areaCode ?? DBNull.Value;
        cmd.Parameters.Add("@Floor", SqlDbType.VarChar, 5).Value = (object?)floor ?? DBNull.Value;
        using var r = cmd.ExecuteReader();
        r.Read();
        return new(r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2));
    }

    public void DeleteLocationMapScope(string whCode, string? areaCode, string? floor = null)
    {
        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        using var cmd = new SqlCommand("""
            SET XACT_ABORT ON;
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SET ANSI_WARNINGS ON;
            SET ANSI_PADDING ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET ARITHABORT ON;
            SET NUMERIC_ROUNDABORT OFF;
            IF @Area IS NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK) WHERE GroupCode='WH_CODE' AND CodeValue=@Wh)
                    THROW 51020,'Warehouse no longer exists.',1;
            END
            ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK)
                                WHERE GroupCode='WH_AREA' AND CodeValue=@Area AND ParentCodeID=CONCAT('WH_CODE_',@Wh))
                THROW 51021,'Area no longer exists in this warehouse.',1;
            IF @Floor IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.MD_Location WITH (UPDLOCK,HOLDLOCK)
                                                  WHERE WhCode=@Wh AND AreaCode=@Area AND Slot=@Floor AND ActiveFlag=1)
                THROW 51029,'Floor no longer exists in this area.',1;
            IF @Area IS NULL AND EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE ParentCodeID=CONCAT('WH_CODE_',@Wh) AND GroupCode<>'WH_AREA')
                THROW 51026,'Warehouse contains other linked master codes.',1;
            IF @Area IS NULL AND EXISTS (SELECT 1 FROM dbo.MD_CodeItem child JOIN dbo.MD_CodeItem area ON area.CodeID=child.ParentCodeID
                                         WHERE area.GroupCode='WH_AREA' AND area.ParentCodeID=CONCAT('WH_CODE_',@Wh))
                THROW 51028,'A warehouse area contains linked master codes.',1;
            IF @Floor IS NULL AND @Area IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE ParentCodeID=CONCAT('WH_AREA_',@Area))
                THROW 51027,'Area contains linked master codes.',1;

            SELECT LocationID INTO #Scope FROM dbo.MD_Location WITH (UPDLOCK,HOLDLOCK)
            WHERE WhCode=@Wh AND (@Area IS NULL OR AreaCode=@Area) AND (@Floor IS NULL OR Slot=@Floor);
            IF EXISTS (SELECT 1 FROM dbo.WH_Inventory i WITH (UPDLOCK,HOLDLOCK)
                       JOIN #Scope s ON s.LocationID=i.LocationNo)
                THROW 51022,'Inventory was found. Move it before deleting this scope.',1;
            IF EXISTS (SELECT 1 FROM dbo.MNT_SparePartItem p JOIN #Scope s ON s.LocationID=p.LocationID)
                THROW 51023,'A spare part is linked to a location. Move it before deleting this scope.',1;
            IF EXISTS (SELECT 1 FROM dbo.WH_Inventory child
                       WHERE child.ParentLotNo IN (SELECT i.LotNo FROM dbo.WH_Inventory i JOIN #Scope s ON s.LocationID=i.LocationNo)
                         AND child.LocationNo NOT IN (SELECT LocationID FROM #Scope))
                THROW 51024,'Inventory contains units linked to locations outside this scope.',1;
            IF EXISTS (SELECT 1 FROM dbo.WH_Inventory child JOIN #Scope s ON s.LocationID=child.LocationNo
                       WHERE child.ParentLotNo IS NOT NULL AND EXISTS (
                           SELECT 1 FROM dbo.WH_Inventory parent WHERE parent.LotNo=child.ParentLotNo
                             AND parent.LocationNo NOT IN (SELECT LocationID FROM #Scope)))
                THROW 51025,'Inventory is linked to a parent outside this scope.',1;

            UPDATE l SET ActiveFlag=0, ModifiedBy='web', ModifiedTS=SYSDATETIME()
            FROM dbo.MD_Location l JOIN #Scope s ON s.LocationID=l.LocationID;
            IF @Floor IS NULL AND @Area IS NULL
            BEGIN
                DELETE FROM dbo.MD_CodeItem WHERE GroupCode='WH_AREA' AND ParentCodeID=CONCAT('WH_CODE_',@Wh);
                DELETE FROM dbo.MD_CodeItem WHERE GroupCode='WH_CODE' AND CodeValue=@Wh;
            END
            IF @Floor IS NULL AND @Area IS NOT NULL
                DELETE FROM dbo.MD_CodeItem WHERE GroupCode='WH_AREA' AND CodeValue=@Area AND ParentCodeID=CONCAT('WH_CODE_',@Wh);
            """, conn, tx);
        cmd.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
        cmd.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = (object?)areaCode ?? DBNull.Value;
        cmd.Parameters.Add("@Floor", SqlDbType.VarChar, 5).Value = (object?)floor ?? DBNull.Value;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public bool LocationExists(string locationNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Location WHERE LocationID = @LocationNo;", conn);
        cmd.Parameters.Add("@LocationNo", SqlDbType.VarChar, 50).Value = locationNo;
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
                (LocationID, LocationName, WhCode, AreaCode, ZoneCode, Aisle, Bay, Slot,
                 LocationType, PlantCode, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
                (@LocationNo, @LocationName, @WhCode, @AreaCode, @ZoneCode, @RackX, @RackY, @RackZ,
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
                WhCode = @WhCode,
                AreaCode = @AreaCode,
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
            "SELECT COUNT(1) FROM dbo.WH_Inventory WHERE LocationNo=@LocationNo AND Qty<>0;", conn);
        check.Parameters.Add("@LocationNo", SqlDbType.VarChar, 50).Value = locationNo;
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Location has inventory and cannot be deleted.");

        using var cmd = new SqlCommand("DELETE FROM dbo.MD_Location WHERE LocationID = @LocationNo;", conn);
        cmd.Parameters.Add("@LocationNo", SqlDbType.VarChar, 50).Value = locationNo;
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

    public void SaveLocationMapAreaPrefix(string whCode, string areaCode, string prefix, string? areaName = null)
    {
        prefix = prefix.Trim().ToUpperInvariant();
        if (!LocationMapNaming.ValidPrefix(prefix))
            throw new ArgumentException("Area prefix must be two letters (A–Z).");
        if (areaCode.Length is < 1 or > 20 || whCode.Length is < 1 or > 20)
            throw new ArgumentException("Invalid warehouse or area code.");
        if (areaName is not null && !areaCode.Equals(prefix, StringComparison.Ordinal))
            throw new ArgumentException("New Area Code must match its two-letter prefix.");

        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        using var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='WH_CODE' AND CodeValue=@Wh AND COALESCE(UseFlag,1)=1)
                THROW 51000, 'Warehouse does not exist.', 1;
            IF EXISTS (SELECT 1 FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK)
                       WHERE GroupCode='WH_AREA' AND (Attribute1=@Prefix OR (LEN(CodeValue)=2 AND CodeValue=@Prefix)) AND CodeValue<>@Area)
                THROW 51001, 'Area prefix is already in use.', 1;
            IF EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='WH_AREA' AND CodeValue=@Area)
            BEGIN
                IF @Name IS NOT NULL THROW 51002, 'Area code already exists.', 1;
                IF EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='WH_AREA' AND CodeValue=@Area
                           AND NULLIF(Attribute1,'') IS NOT NULL AND Attribute1<>@Prefix)
                   AND EXISTS (SELECT 1 FROM dbo.MD_Location WHERE AreaCode=@Area)
                    THROW 51005, 'Area prefix cannot change after locations are created.', 1;
                UPDATE dbo.MD_CodeItem SET Attribute1=@Prefix,ModifiedBy='web',ModifiedTS=SYSDATETIME()
                WHERE GroupCode='WH_AREA' AND CodeValue=@Area AND ParentCodeID=CONCAT('WH_CODE_',@Wh);
                IF @@ROWCOUNT=0 THROW 51003, 'Area is not in this warehouse.', 1;
            END
            ELSE
            BEGIN
                IF @Name IS NULL THROW 51004, 'Area does not exist.', 1;
                INSERT dbo.MD_CodeItem
                    (CodeID,GroupCode,CodeValue,CodeName,ParentCodeID,Attribute1,SortOrder,UseFlag,CreatedBy,CreatedTS)
                VALUES (CONCAT('WH_AREA_',@Area),'WH_AREA',@Area,@Name,CONCAT('WH_CODE_',@Wh),@Prefix,100,1,'web',SYSDATETIME());
            END
            """, conn, tx);
        cmd.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
        cmd.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
        cmd.Parameters.Add("@Prefix", SqlDbType.NVarChar, 200).Value = prefix;
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 60).Value = (object?)areaName ?? DBNull.Value;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public string AddLocationMapAxis(string whCode, string areaCode, string kind, string? floor, string? name = null, decimal? capacity = null)
    {
        if (kind is not ("Floor" or "Row" or "Column"))
            throw new ArgumentException("Invalid map axis.");
        if (name?.Length > 60) throw new ArgumentException("Location name is too long.");
        if (capacity is < 0 or > 9999999.999m)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Load Capacity must be between 0 and 9,999,999.999.");

        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        string prefix;
        using (var area = new SqlCommand("""
            SELECT COALESCE(Attribute1,'') FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK)
            WHERE GroupCode='WH_AREA' AND CodeValue=@Area AND ParentCodeID=CONCAT('WH_CODE_',@Wh) AND COALESCE(UseFlag,1)=1;
            """, conn, tx))
        {
            area.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
            area.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
            prefix = area.ExecuteScalar() as string ?? throw new InvalidOperationException("Area does not exist in this warehouse.");
        }
        if (!LocationMapNaming.ValidPrefix(prefix))
        {
            var used = new List<string?>();
            using (var readPrefixes = new SqlCommand("SELECT CodeValue,Attribute1 FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK) WHERE GroupCode='WH_AREA' AND CodeValue<>@Area;", conn, tx))
            {
                readPrefixes.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
                using var reader = readPrefixes.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.GetString(0).Length == 2) used.Add(reader.GetString(0));
                    if (!reader.IsDBNull(1)) used.Add(reader.GetString(1));
                }
            }
            prefix = LocationMapNaming.AvailablePrefix(areaCode, used);
            using var savePrefix = new SqlCommand("UPDATE dbo.MD_CodeItem SET Attribute1=@Prefix,ModifiedBy='web',ModifiedTS=SYSDATETIME() WHERE GroupCode='WH_AREA' AND CodeValue=@Area AND ParentCodeID=CONCAT('WH_CODE_',@Wh);", conn, tx);
            savePrefix.Parameters.Add("@Prefix", SqlDbType.NVarChar, 200).Value = prefix;
            savePrefix.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
            savePrefix.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
            savePrefix.ExecuteNonQuery();
        }
        var existing = new List<(string Row, string Column, string Floor)>();
        using (var read = new SqlCommand("SELECT Aisle,Bay,Slot FROM dbo.MD_Location WHERE WhCode=@Wh AND AreaCode=@Area;", conn, tx))
        {
            read.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
            read.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
            using var reader = read.ExecuteReader();
            while (reader.Read())
                existing.Add((reader.IsDBNull(0) ? "" : reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
        }

        var plan = LocationMapNaming.PlanAxis(existing, kind, floor);
        var cells = LocationMapNaming.MissingCells(existing, plan);
        if (cells.Count is 0 or > 1000) throw new InvalidOperationException("Add at most 1,000 locations at a time.");
        var planned = plan.Cells.ToHashSet();

        foreach (var cell in cells)
        {
            if (cell.Column.Length is < 2 or > 5 || !int.TryParse(cell.Column, out var x) || x < 1
                || LocationMapNaming.YOrder(cell.Row) == 0
                || cell.Floor.Length is < 2 or > 5 || cell.Floor[0] != 'F' || !int.TryParse(cell.Floor[1..], out var level) || level < 1)
                throw new InvalidOperationException("Generated coordinates must use X=01, Y=A1, Floor=F1 format.");
            var id = LocationMapNaming.LocationNo(prefix, cell.Column, cell.Row, cell.Floor);
            using var insert = new SqlCommand("""
                INSERT INTO dbo.MD_Location
                    (LocationID,LocationName,WhCode,AreaCode,Aisle,Bay,Slot,Capacity,ActiveFlag,CreatedBy)
                VALUES (@Id,@Name,@Wh,@Area,@Row,@Column,@Floor,@Capacity,1,'web');
                """, conn, tx);
            insert.Parameters.Add("@Id", SqlDbType.VarChar, 20).Value = id;
            insert.Parameters.Add("@Name", SqlDbType.NVarChar, 60).Value = planned.Contains(cell) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : id;
            insert.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
            insert.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
            insert.Parameters.Add("@Row", SqlDbType.VarChar, 5).Value = cell.Row;
            insert.Parameters.Add("@Column", SqlDbType.VarChar, 5).Value = cell.Column;
            insert.Parameters.Add("@Floor", SqlDbType.VarChar, 5).Value = cell.Floor;
            var capacityParameter = insert.Parameters.Add("@Capacity", SqlDbType.Decimal);
            capacityParameter.Precision = 10;
            capacityParameter.Scale = 3;
            capacityParameter.Value = planned.Contains(cell) ? (object?)capacity ?? DBNull.Value : DBNull.Value;
            insert.ExecuteNonQuery();
        }
        tx.Commit();
        return plan.Floor;
    }

    public void SetLocationMapGrid(string whCode, string areaCode, string floor, int aisles, int bays)
    {
        if (LocationMapNaming.FloorCode(floor) != floor || aisles < 1 || bays < 1 || (long)aisles * bays > 1000)
            throw new ArgumentException("Grid must contain 1 to 1,000 locations on a valid floor.");

        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        string prefix;
        using (var area = new SqlCommand("SELECT Attribute1 FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK) WHERE GroupCode='WH_AREA' AND CodeValue=@Area AND ParentCodeID=CONCAT('WH_CODE_',@Wh) AND COALESCE(UseFlag,1)=1;", conn, tx))
        {
            area.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
            area.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
            prefix = area.ExecuteScalar() as string ?? throw new InvalidOperationException("Select a registered Area first.");
        }
        if (!LocationMapNaming.ValidPrefix(prefix)) throw new InvalidOperationException("Area needs a two-letter location prefix.");

        var existing = new List<(string Id, string Aisle, string Bay, bool Active)>();
        using (var read = new SqlCommand("SELECT LocationID,Aisle,Bay,ActiveFlag FROM dbo.MD_Location WITH (UPDLOCK,HOLDLOCK) WHERE WhCode=@Wh AND AreaCode=@Area AND Slot=@Floor;", conn, tx))
        {
            read.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
            read.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
            read.Parameters.Add("@Floor", SqlDbType.VarChar, 5).Value = floor;
            using var reader = read.ExecuteReader();
            while (reader.Read()) existing.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2), reader.IsDBNull(3) || reader.GetBoolean(3)));
        }
        var wanted = (from row in Enumerable.Range(1, aisles)
                      from column in Enumerable.Range(1, bays)
                      select (Aisle: LocationMapNaming.YCode(row), Bay: column.ToString("D2"))).ToHashSet();
        var removing = existing.Where(x => x.Active && !wanted.Contains((x.Aisle, x.Bay))).Select(x => x.Id).ToList();
        foreach (var id in removing)
        {
            using var check = new SqlCommand("""
                SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.WH_Inventory WHERE LocationNo=@Id AND Qty<>0)
                    OR EXISTS(SELECT 1 FROM dbo.MNT_SparePartItem WHERE LocationID=@Id)
                    THEN 1 ELSE 0 END;
                """, conn, tx);
            check.Parameters.Add("@Id", SqlDbType.VarChar, 20).Value = id;
            if (Convert.ToInt32(check.ExecuteScalar()) != 0)
                throw new InvalidOperationException($"Cannot reduce the grid: location {id} contains stock or linked parts.");
        }
        foreach (var id in removing)
        {
            using var remove = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WHERE LocationNo=@Id)
                   OR EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction WHERE LocationNo=@Id)
                    UPDATE dbo.MD_Location SET ActiveFlag=0 WHERE LocationID=@Id;
                ELSE DELETE FROM dbo.MD_Location WHERE LocationID=@Id;
                """, conn, tx);
            remove.Parameters.Add("@Id", SqlDbType.VarChar, 20).Value = id;
            remove.ExecuteNonQuery();
        }
        foreach (var row in existing.Where(x => !x.Active && wanted.Contains((x.Aisle, x.Bay))))
        {
            using var reactivate = new SqlCommand("UPDATE dbo.MD_Location SET ActiveFlag=1 WHERE LocationID=@Id;", conn, tx);
            reactivate.Parameters.Add("@Id", SqlDbType.VarChar, 20).Value = row.Id;
            reactivate.ExecuteNonQuery();
        }
        var occupied = existing.Where(x => !removing.Contains(x.Id)).Select(x => (x.Aisle, x.Bay)).ToHashSet();
        foreach (var cell in wanted.Where(x => !occupied.Contains(x)))
        {
            var id = LocationMapNaming.LocationNo(prefix, cell.Bay, cell.Aisle, floor);
            using var insert = new SqlCommand("""
                INSERT dbo.MD_Location (LocationID,LocationName,WhCode,AreaCode,Aisle,Bay,Slot,ActiveFlag,CreatedBy)
                VALUES (@Id,@Id,@Wh,@Area,@Aisle,@Bay,@Floor,1,'web');
                """, conn, tx);
            insert.Parameters.Add("@Id", SqlDbType.VarChar, 20).Value = id;
            insert.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
            insert.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
            insert.Parameters.Add("@Aisle", SqlDbType.VarChar, 5).Value = cell.Aisle;
            insert.Parameters.Add("@Bay", SqlDbType.VarChar, 5).Value = cell.Bay;
            insert.Parameters.Add("@Floor", SqlDbType.VarChar, 5).Value = floor;
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public List<PickingOrderRow> ListPickingOrders(string? search = null, bool includeClosed = true)
    {
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
        var like = Like(search);
        return Query("""
            ;WITH Lines AS
            (
                SELECT
                    COALESCE(NULLIF(O.PickSlipNo, N''), CONCAT(N'RS-', O.PickSlipID)) AS PICK_SLIPNO,
                    CONVERT(varchar(10), O.RequiredAt, 23) AS REQ_DATE,
                    COALESCE(NULLIF(O.ReqLocation, N''), N'-') AS REQ_LOCATION,
                    COALESCE(NULLIF(O.ReqUserId, N''), O.CreatedBy) AS REQ_USERID,
                    CONVERT(varchar(8), CONVERT(time(0), COALESCE(O.CreatedTS, O.RequiredAt))) AS REQ_TIME,
                    O.PrintDate,
                    O.PickSlipID,
                    O.ItemNo,
                    I.ItemName,
                    CASE WHEN O.CreatedBy='WH-AUTO' THEN 'EA' ELSE 'BOX' END AS QUANTITY_UNIT,
                    COALESCE(O.DemandQty, 0) AS REQ_BOX_QTY,
                    COALESCE(O.PickedQty, 0) AS PICKED_QTY,
                    CASE
                        WHEN UPPER(COALESCE(O.Status, 'OPEN')) IN ('CLOSED', 'CANCELED') THEN N'Closed'
                        WHEN COALESCE(O.PickedQty, 0) >= COALESCE(O.DemandQty, 0) AND COALESCE(O.DemandQty, 0) > 0 THEN N'Picked'
                        WHEN COALESCE(O.PickedQty, 0) > 0 THEN N'Partial'
                        ELSE N'Open'
                    END AS LINE_STATUS
                FROM dbo.WH_PickSlip O
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
                    MIN(PickSlipID) AS FIRST_ID
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
                L.QUANTITY_UNIT,
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
                  AND L.PickSlipID = G.FIRST_ID
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
                GetString(r, "FIRST_PARTNM"),
                GetString(r, "QUANTITY_UNIT") ?? "BOX"),
            ("@Search", like),
            ("@IncludeClosed", includeClosed));
    }

    public List<PickingSlipLineRow> ListPickingSlipLines(string pickSlipNo)
    {
        return Query("""
            ;WITH Base AS
            (
                SELECT
                    COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.PickSlipID)) AS PICK_SLIPNO,
                    COALESCE(RS.ReqSeqNo, ROW_NUMBER() OVER (PARTITION BY COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.PickSlipID)) ORDER BY RS.PickSlipID)) AS SEQNO,
                    RS.PickSlipID,
                    RS.ItemNo,
                    COALESCE(I.ItemName, RS.ItemNo) AS ItemName,
                    COALESCE(RS.DemandQty, 0) AS DemandQty,
                    COALESCE(RS.PickedQty, 0) AS PickedQty,
                    COALESCE(NULLIF(RS.ReqUserId, N''), RS.CreatedBy) AS RequestUserId,
                    NULLIF(RS.ReqLocation, N'') AS LineCode,
                    COALESCE(ML.LineName, NULLIF(RS.ReqLocation, N'')) AS LineName,
                    RS.Status,
                    CASE WHEN RS.CreatedBy='WH-AUTO' THEN 'EA' ELSE 'BOX' END AS QUANTITY_UNIT
                FROM dbo.WH_PickSlip RS
                LEFT JOIN dbo.MD_Item I
                       ON I.ItemNo = RS.ItemNo
                LEFT JOIN dbo.MD_Line ML
                       ON ML.LineID = RS.ReqLocation
                WHERE COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.PickSlipID)) = @PickSlipNo
            ),
            PickedPhysical AS
            (
                SELECT T.SourceID AS PickSlipID, SUM(-COALESCE(T.QtyChange,0)) AS PickedQty
                FROM dbo.WH_InventoryTransaction T
                INNER JOIN Base B
                        ON B.PickSlipID=T.SourceID
                WHERE T.TransactionType='OUT' AND T.SourceType='PICK_SLIP'
                GROUP BY T.SourceID
            )
            SELECT
                B.PICK_SLIPNO,
                B.SEQNO,
                B.ItemNo AS PARTNO,
                B.ItemName AS PARTNM,
                B.QUANTITY_UNIT,
                B.DemandQty AS REQ_BOX_QTY,
                B.PickedQty AS PICKED_BOX_QTY,
                COALESCE(P.PickedQty, 0) AS PICKED_QTY,
                B.RequestUserId AS REQ_USERID,
                B.LineCode AS LINECD,
                B.LineName AS LINENM,
                CAST(NULL AS nvarchar(50)) AS LOC_01,
                CAST(0 AS decimal(14,3)) AS LOC_01_QTY,
                CAST(NULL AS nvarchar(50)) AS LOC_02,
                CAST(0 AS decimal(14,3)) AS LOC_02_QTY,
                CAST(NULL AS nvarchar(50)) AS LOC_03,
                CAST(0 AS decimal(14,3)) AS LOC_03_QTY,
                CASE
                    WHEN UPPER(COALESCE(B.Status, 'OPEN')) IN ('CLOSED', 'CANCELED') THEN N'Closed'
                    WHEN B.DemandQty > 0 AND B.PickedQty >= B.DemandQty THEN N'Picked'
                    WHEN B.PickedQty > 0 THEN N'Partial'
                    ELSE N'Open'
                END AS STATUS
            FROM Base B
            LEFT JOIN PickedPhysical P
                   ON P.PickSlipID = B.PickSlipID
            ORDER BY B.SEQNO, B.PickSlipID;
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
                GetString(r, "STATUS") ?? "Open",
                GetString(r, "QUANTITY_UNIT") ?? "BOX"),
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
                    COALESCE(NULLIF(RL.LineID, N''), N'-') AS LINECD,
                    COALESCE(L.LineName, NULLIF(RL.LineID, N''), N'-') AS LINENM,
                    R.ItemNo AS PARTNO,
                    COALESCE(I.ItemName, R.ItemNo) AS PARTNM,
                    I.DefaultUOM AS UNIT,
                    COALESCE(NULLIF(MAX(COALESCE(P.QtyPerInner, 0)), 0), 1) AS UNIT_PACK_QTY,
                    SUM(COALESCE(R.RequiredQty, 0)) AS REQ_QTY
                FROM dbo.PP_MaterialReservation R
                INNER JOIN dbo.PP_WorkOrder W
                        ON W.WoID = R.WoID
                OUTER APPLY (SELECT TOP 1 r.LineID FROM dbo.PP_WorkOrderRouting r
                             WHERE r.WoID = W.WoID AND r.LineID IS NOT NULL
                             ORDER BY r.StepSeq) RL
                LEFT JOIN dbo.MD_Line L
                       ON L.LineID = RL.LineID
                LEFT JOIN dbo.MD_Item I
                       ON I.ItemNo = R.ItemNo
                LEFT JOIN dbo.MD_PackagingSpec P
                       ON P.ItemID = R.ItemNo
                      AND COALESCE(P.ActiveFlag, 1) = 1
                WHERE CONVERT(date, R.RequiredAt) = @ReqDate
                  AND UPPER(COALESCE(R.Status, N'OPEN')) NOT IN (N'CANCELED', N'CLOSED')
                  AND (@LineCode IS NULL OR RL.LineID = @LineCode)
                  AND (@PartNo IS NULL
                       OR R.ItemNo LIKE @PartNo
                       OR I.ItemName LIKE @PartNo)
                GROUP BY CONVERT(date, R.RequiredAt), RL.LineID, L.LineName, R.ItemNo, I.ItemName, I.DefaultUOM
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
                FROM dbo.WH_PickSlip RS
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
        var requiredAt = DateTime.TryParse(reqDate, out var parsedDate) ? parsedDate.Date : DbClock.Today;
        var slip = CreatePickingSlip(requiredAt, reqUserId, new[] { new CreatePickingSlipLine(partNo, reqLocation, reqBoxQty) }, pickSlipNo);
        return slip;
    }

    public string CreatePickingSlip(
        DateTime reqDate,
        string reqUserId,
        IEnumerable<CreatePickingSlipLine> lines,
        string? requestedPickSlipNo = null)
    {
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
                    INSERT INTO dbo.WH_PickSlip
                        (PickSlipNo, ReqLocation, ReqSeqNo, ReqUserId,
                         ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status,
                         CreatedBy, CreatedTS)
                    VALUES
                        (@PickSlipNo, @ReqLocation, @ReqSeqNo, @ReqUserId,
                         @PartNo, @ReqBoxQty, 0, @RequiredAt, @Priority, 'Open',
                         @CreatedBy, SYSDATETIME());
                    """, conn, tx);
                cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo;
                cmd.Parameters.Add("@ReqLocation", SqlDbType.NVarChar, 40).Value = line.LineCode;
                cmd.Parameters.Add("@ReqSeqNo", SqlDbType.Int).Value = seq;
                cmd.Parameters.Add("@ReqUserId", SqlDbType.NVarChar, 80).Value = Truncate(reqUserId, 80);
                cmd.Parameters.Add("@CreatedBy", SqlDbType.VarChar, 20).Value = Truncate(reqUserId, 20);
                cmd.Parameters.Add("@PartNo", SqlDbType.VarChar, 20).Value = line.PartNo;
                AddQtyDecimal(cmd, "@ReqBoxQty", line.ReqBoxQty);
                cmd.Parameters.Add("@RequiredAt", SqlDbType.DateTime2).Value = reqDate.Date;
                cmd.Parameters.Add("@Priority", SqlDbType.TinyInt).Value = Math.Clamp(seq, 1, 9);
                cmd.ExecuteNonQuery();
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

    public void MarkPickingSlipPrinted(string pickSlipNo, string printedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.WH_PickSlip
               SET PrintDate = SYSDATETIME(),
                   ModifiedBy = @PrintedBy,
                   ModifiedTS = SYSDATETIME()
             WHERE COALESCE(NULLIF(PickSlipNo, N''), CONCAT(N'RS-', PickSlipID)) = @PickSlipNo;
            """, conn);
        cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo.Trim();
        cmd.Parameters.Add("@PrintedBy", SqlDbType.VarChar, 20).Value = Truncate(printedBy, 20);
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
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.WH_PickSlip
               SET Status = 'Closed',
                   CloseDate = SYSDATETIME(),
                   CloseUserId = @ClosedBy,
                   ModifiedBy = @ModifiedBy,
                   ModifiedTS = SYSDATETIME()
             WHERE COALESCE(NULLIF(PickSlipNo, N''), CONCAT(N'RS-', PickSlipID)) = @PickSlipNo
               AND UPPER(COALESCE(Status, 'OPEN')) <> 'CLOSED';
            """, conn);
        cmd.Parameters.Add("@PickSlipNo", SqlDbType.NVarChar, 40).Value = pickSlipNo.Trim();
        cmd.Parameters.Add("@ClosedBy", SqlDbType.NVarChar, 80).Value = Truncate(closedBy, 80);
        cmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar, 20).Value = Truncate(closedBy, 20);
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
                SELECT PartNo AS PARTNO FROM dbo.WH_Inventory WHERE PartNo IS NOT NULL AND PartNo <> N''
                UNION
                SELECT ItemNo AS PARTNO FROM dbo.WH_PickSlip WHERE ItemNo IS NOT NULL AND ItemNo <> N''
                UNION
                SELECT ItemNo AS PARTNO FROM dbo.MD_Item WHERE ItemNo IS NOT NULL AND ItemNo <> N''
            ) P
            ORDER BY PARTNO;
            """, r => new PartOptionRow(GetString(r, "PARTNO") ?? ""));
    }

    public List<LocationMapRow> ListLocationMap(string? areaCode = null, string? rackZ = null, string? zoneCode = null, string? whCode = null)
    {
        return Query("""
            SELECT
                L.LocationID AS LOCATION_NO,
                L.LocationName AS LOCATION_NM,
                L.AreaCode AS AREACD,
                COALESCE(NULLIF(A.CodeName, ''), L.AreaCode) AS AREANM,
                L.ZoneCode AS ZONECD,
                L.ZoneCode AS ZONENM,
                L.Aisle AS RACK_X,
                L.Bay AS RACK_Y,
                L.Slot AS RACK_Z,
                COUNT(DISTINCT S.LotNo) AS LOT_COUNT,
                COUNT(DISTINCT S.PartNo) AS PART_COUNT,
                COALESCE(SUM(S.Qty), 0) AS TOTAL_QTY,
                CASE
                    WHEN COALESCE(SUM(S.Qty), 0) = 0 THEN N'Empty'
                    WHEN COUNT(DISTINCT S.PartNo) > 1 THEN N'Mixed'
                    ELSE N'Stocked'
                END AS STATUS
            FROM dbo.MD_Location L
            LEFT JOIN dbo.MD_CodeItem A ON A.GroupCode='WH_AREA' AND A.CodeValue=L.AreaCode
            LEFT JOIN dbo.WH_Inventory S ON S.LocationNo=L.LocationID AND S.Qty<>0
            WHERE COALESCE(L.ActiveFlag, 1) = 1
              AND (@WhCode IS NULL OR L.WhCode = @WhCode)
              AND (@AreaCode IS NULL OR L.AreaCode = @AreaCode)
              AND (@ZoneCode IS NULL OR COALESCE(NULLIF(L.ZoneCode, ''), 'DEFAULT') = @ZoneCode)
              AND (@RackZ IS NULL OR L.Slot = @RackZ)
            GROUP BY L.LocationID, L.LocationName, L.WhCode, L.AreaCode, L.ZoneCode, A.CodeName,
                     L.LocationType, L.Aisle, L.Bay, L.Slot
            ORDER BY L.AreaCode, L.ZoneCode,
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
        var like = Like(search);

        return Query("""
            SELECT
                S.PartNo AS PART_NO,
                COALESCE(NULLIF(S.PartName,N''),NULLIF(I.ItemName,N''),S.PartNo) AS PART_NAME,
                I.DefaultUOM AS UOM,
                S.LocationNo AS LOCATION_NO,
                L.WhCode AS WHCD,
                L.AreaCode AS AREACD,
                COALESCE(NULLIF(A.CodeName, ''), L.AreaCode) AS AREANM,
                COALESCE(NULLIF(L.ZoneCode, ''), 'DEFAULT') AS ZONECD,
                COALESCE(NULLIF(L.ZoneCode, ''), 'DEFAULT') AS ZONENM,
                L.Aisle AS RACK_X,
                L.Bay AS RACK_Y,
                L.Slot AS RACK_Z,
                COALESCE(SUM(S.Qty), 0) AS QTY
            FROM dbo.WH_Inventory S
            INNER JOIN dbo.MD_Location L ON L.LocationID=S.LocationNo
            LEFT JOIN dbo.MD_Item I ON I.ItemNo=S.PartNo
            LEFT JOIN dbo.MD_CodeItem A ON A.GroupCode='WH_AREA' AND A.CodeValue=L.AreaCode
            WHERE S.Qty<>0
              AND COALESCE(L.ActiveFlag, 1) = 1
              AND (@WhCode IS NULL OR L.WhCode = @WhCode)
              AND (@AreaCode IS NULL OR L.AreaCode = @AreaCode)
              AND (@ZoneCode IS NULL OR COALESCE(NULLIF(L.ZoneCode, ''), 'DEFAULT') = @ZoneCode)
              AND (@RackZ IS NULL OR L.Slot = @RackZ)
              AND (@Search IS NULL
                   OR S.PartNo LIKE @Search
                   OR I.ItemName LIKE @Search
                   OR S.LocationNo LIKE @Search)
            GROUP BY S.PartNo,S.PartName,I.ItemName,I.DefaultUOM,S.LocationNo,
                     L.WhCode,L.AreaCode,L.ZoneCode,A.CodeName,
                     L.Aisle, L.Bay, L.Slot
            ORDER BY S.PartNo,L.WhCode,L.AreaCode,L.ZoneCode,
                     TRY_CONVERT(int, L.Slot), L.Slot,
                     TRY_CONVERT(int, L.Bay), L.Bay,
                     TRY_CONVERT(int, L.Aisle), L.Aisle,
                     S.LocationNo;
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

    public List<OperationLogRow> ListOperationLogs(
        string? search = null,
        string? eventType = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT TOP (500)
                T.TransactionID AS OperationLogID,
                T.TransactionTime AS EventTime,
                X.EventType,
                X.ScreenCode,
                T.OperatorID AS EmployeeNo,
                CAST(NULL AS nvarchar(120)) AS EmployeeName,
                T.OperatorID AS WorkerID,
                CAST(NULL AS nvarchar(80)) AS TerminalID,
                CAST(NULL AS nvarchar(40)) AS LineID,
                CAST(NULL AS nvarchar(20)) AS ShiftCode,
                'LOT' AS ScanType,
                T.LotNo AS ScanValue,
                'SUCCESS' AS Result,
                CASE WHEN T.TransactionType = 'MOVE' THEN CONCAT('Location Change to ', T.LocationNo)
                     ELSE CONCAT(COALESCE(NULLIF(T.Note, ''), COALESCE(T.ReasonCode, T.TransactionType)),
                                 ' (', COALESCE(T.QtyBefore, 0), ' -> ', COALESCE(T.QtyAfter, 0), ')')
                END AS Message,
                CAST(NULL AS nvarchar(64)) AS ClientIP,
                T.SourceType,
                T.SourceID,
                T.LotNo,
                T.PartNo,
                T.LocationNo AS LocationID,
                T.QtyChange AS Qty
            FROM dbo.WH_InventoryTransaction T
            CROSS APPLY (SELECT
                CASE UPPER(T.TransactionType)
                    WHEN 'IN' THEN 'RECEIVE'
                    WHEN 'OUT' THEN 'RELEASE_PICK'
                    WHEN 'ADJ' THEN 'ADJUST_SAVE'
                    WHEN 'MOVE' THEN 'MOVE_LOCATION'
                    WHEN 'CANCEL' THEN 'CANCEL_RECEIPT'
                    ELSE UPPER(T.TransactionType)
                END AS EventType,
                CASE UPPER(T.TransactionType)
                    WHEN 'IN' THEN 'INBOUND'
                    WHEN 'OUT' THEN 'OUTBOUND'
                    WHEN 'ADJ' THEN 'ADJUSTMENT'
                    WHEN 'MOVE' THEN 'RELOCATION'
                    WHEN 'CANCEL' THEN 'INBOUND'
                END AS OperationType,
                CASE UPPER(T.TransactionType)
                    WHEN 'OUT' THEN 'WH003'
                    WHEN 'ADJ' THEN 'WH005'
                    ELSE 'WH002'
                END AS ScreenCode) X
            WHERE (@OperationType IS NULL OR X.OperationType = @OperationType)
              AND (@DateFrom IS NULL OR T.TransactionTime >= @DateFrom)
              AND (@DateTo IS NULL OR T.TransactionTime < DATEADD(day, 1, @DateTo))
              AND (@Like IS NULL
                   OR X.EventType LIKE @Like
                   OR X.ScreenCode LIKE @Like
                   OR T.OperatorID LIKE @Like
                   OR T.LotNo LIKE @Like
                   OR T.Note LIKE @Like
                   OR T.ReasonCode LIKE @Like
                   OR T.PartNo LIKE @Like
                   OR T.LocationNo LIKE @Like
                   OR CONVERT(nvarchar(80), T.SourceID) LIKE @Like)
            ORDER BY T.TransactionTime DESC, T.TransactionID DESC;
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
                GetString(rdr, "SourceType"),
                GetNullableInt(rdr, "SourceID"),
                GetString(rdr, "LotNo"),
                GetString(rdr, "PartNo"),
                GetString(rdr, "LocationID"),
                GetNullableDecimal(rdr, "Qty")));
        }

        return list;
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
        return true;
    }

    public List<InventorySettingRow> ListInventorySettings(string? search = null, string? status = null)
    {
        var like = Like(search);
        return Query("""
            WITH Stock AS (
                SELECT
                    W.PartNo AS ItemNo,
                    SUM(COALESCE(W.Qty, 0)) AS CURRENT_QTY,
                    COUNT(DISTINCT W.LocationNo) AS LOCATION_COUNT,
                    COUNT(DISTINCT W.LotNo) AS LOT_COUNT
                FROM dbo.WH_Inventory W
                WHERE W.PartNo IS NOT NULL AND W.Qty<>0
                GROUP BY W.PartNo
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
        itemCmd.Parameters.Add("@ModifiedBy", SqlDbType.VarChar, 20).Value = Truncate(modifiedBy, 20);
        if (itemCmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Item was not found.");
    }

    private static bool PickSlipExists(SqlConnection conn, SqlTransaction tx, string pickSlipNo)
    {
        using var cmd = new SqlCommand("""
            SELECT 1
            FROM dbo.WH_PickSlip
            WHERE COALESCE(NULLIF(PickSlipNo, N''), CONCAT(N'RS-', PickSlipID)) = @PickSlipNo;
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
            FROM dbo.WH_PickSlip WITH (UPDLOCK, HOLDLOCK)
            WHERE PickSlipNo LIKE @Prefix + N'[0-9][0-9]';

            SELECT @Prefix + RIGHT(N'00' + CONVERT(nvarchar(10), COALESCE(@Seq, 1)), 2);
            """, conn, tx);
        return Convert.ToString(cmd.ExecuteScalar()) ?? DbClock.Now.ToString("yyyyMMdd") + "01";
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
        cmd.Parameters.Add("@LocationNo", SqlDbType.VarChar, 50).Value = locationNo;
        AddNullable(cmd, "@LocationName", SqlDbType.NVarChar, 120, locationName);
        AddNullable(cmd, "@WhCode", SqlDbType.VarChar, 20, whCode);
        AddNullable(cmd, "@AreaCode", SqlDbType.VarChar, 20, areaCode);
        AddNullable(cmd, "@PlantCode", SqlDbType.VarChar, 20, whCode);
        AddNullable(cmd, "@ZoneCode", SqlDbType.VarChar, 20, zoneCode);
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
