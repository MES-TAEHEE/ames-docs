using System.Data;
using System.Text.Json;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    // Percentages of the original image, independent of zoom and viewport size.
    public sealed record FactoryMapZone(string Warehouse, string Area, string? Parent,
        decimal X, decimal Y, decimal Width, decimal Height, string? Code = null)
    {
        public void Validate()
        {
            if (Code is not null) NormalizeFactoryMapCode(Code);
            if (string.IsNullOrWhiteSpace(Warehouse) || Warehouse.Length > 20
                || string.IsNullOrWhiteSpace(Area) || Area.Length > 20 || Parent?.Length > 20
                || X < 0 || Y < 0 || Width < .25m || Height < .25m
                || X + Width > 100 || Y + Height > 100)
                throw new ArgumentException("Select a location group and draw a rectangle inside the map.");
        }
    }

    public sealed record FactoryMapRegion(int Id, string Name, FactoryMapZone Zone, string StoredValue);

    public static string NormalizeFactoryMapCode(string? code)
    {
        var value = code?.Trim().ToUpperInvariant() ?? "";
        if (value.Length > 3 || value.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Use up to three letters for the region code.");
        return value;
    }

    public List<FactoryMapRegion> ListFactoryMapRegions()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT ConfigID, CodeName, ConfigValue FROM dbo.SYS_Config
            WHERE ConfigType='FG_MAP' AND Category='FG_LAYOUT' AND IsActive=1
            ORDER BY ConfigID;
            """, conn);
        using var reader = cmd.ExecuteReader();
        var rows = new List<FactoryMapRegion>();
        while (reader.Read())
        {
            var raw = reader.GetString(2);
            var zone = JsonSerializer.Deserialize<FactoryMapZone>(raw)
                ?? throw new InvalidOperationException("Invalid factory map region.");
            zone.Validate();
            rows.Add(new(reader.GetInt32(0), reader.GetString(1), zone, raw));
        }
        return rows;
    }

    public int SaveFactoryMapRegion(string name, FactoryMapZone zone, string actor, FactoryMapRegion? original = null)
    {
        if (zone.Code is not null) zone = zone with { Code = NormalizeFactoryMapCode(zone.Code) };
        zone.Validate();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80
            || string.IsNullOrWhiteSpace(actor) || actor.Length > 20)
            throw new ArgumentException("Enter a region name (up to 80 characters).");
        var value = JsonSerializer.Serialize(zone);
        if (value.Length > 500) throw new ArgumentException("The region settings are too long.");
        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        var code = zone.Code ?? (name.Trim().Length is > 0 and <= 3 && name.Trim().All(char.IsAsciiLetter) ? name.Trim().ToUpperInvariant() : "");
        if (code.Length > 0)
        {
            using var unique = new SqlCommand(FactoryMapUniqueCodeSql, conn, tx);
            unique.Parameters.Add("@Code", SqlDbType.NVarChar, 3).Value = code;
            ColumnParams.Add(unique, "@Id", "SYS_Config.ConfigID", original?.Id ?? 0);
            if (unique.ExecuteScalar() is not null) throw new DuplicateNameException("This region letter is already in use.");
        }
        using (var target = new SqlCommand(FactoryMapTargetSql, conn, tx))
        {
            ColumnParams.Add(target, "@Wh", "MD_Location.WhCode", zone.Warehouse);
            ColumnParams.Add(target, "@Area", "MD_Location.AreaCode", zone.Area);
            ColumnParams.Add(target, "@Parent", "MD_Location.ParentLocationID", (object?)zone.Parent ?? DBNull.Value);
            if (target.ExecuteScalar() is null)
                throw new InvalidOperationException("The FG location group changed. Refresh the map.");
        }
        using var cmd = new SqlCommand(original is null ? FactoryMapInsertSql : FactoryMapUpdateSql, conn, tx);
        ColumnParams.Add(cmd, "@Name", "SYS_Config.CodeName", name.Trim());
        ColumnParams.Add(cmd, "@Value", "SYS_Config.ConfigValue", value);
        ColumnParams.Add(cmd, "@Actor", "SYS_Config.CreatedBy", actor);
        if (original is null)
            ColumnParams.Add(cmd, "@Key", "SYS_Config.ConfigKey", "FG_MAP_" + Guid.NewGuid().ToString("N"));
        else AddFactoryMapOriginal(cmd, original);
        var result = cmd.ExecuteScalar();
        if (result is null) throw new DBConcurrencyException("The map region changed. Refresh and retry.");
        tx.Commit();
        return (int)result;
    }

    internal const string FactoryMapTargetSql = """
            SELECT TOP(1) 1 FROM dbo.MD_Location L WITH(HOLDLOCK)
            WHERE L.ActiveFlag=1 AND L.LocationLevel=2 AND L.LocationType='FG'
              AND L.WhCode=@Wh AND L.AreaCode=@Area
              AND ((@Parent IS NULL AND L.ParentLocationID IS NULL) OR L.ParentLocationID=@Parent)
              AND (@Parent IS NULL OR EXISTS(SELECT 1 FROM dbo.MD_Location P WITH(HOLDLOCK)
                  WHERE P.LocationID=@Parent AND P.ActiveFlag=1 AND P.LocationLevel=1
                    AND P.WhCode=@Wh AND P.AreaCode=@Area));
            """;

    internal const string FactoryMapUniqueCodeSql = """
        SELECT TOP(1) ConfigID FROM dbo.SYS_Config WITH(UPDLOCK,HOLDLOCK)
        WHERE ConfigType='FG_MAP' AND Category='FG_LAYOUT' AND IsActive=1 AND ConfigID<>@Id
          AND UPPER(LTRIM(RTRIM(COALESCE(JSON_VALUE(CASE WHEN ISJSON(ConfigValue)=1 THEN ConfigValue ELSE N'{}' END,'$.Code'),
              CASE WHEN LEN(LTRIM(RTRIM(CodeName))) BETWEEN 1 AND 3
                AND UPPER(LTRIM(RTRIM(CodeName))) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Z]%'
                THEN CodeName ELSE N'' END))))=@Code;
        """;

    internal const string FactoryMapInsertSql = """
            SET QUOTED_IDENTIFIER ON; SET XACT_ABORT ON;
            INSERT dbo.SYS_Config(ConfigKey,ConfigType,Category,CodeName,ConfigValue,IsActive,CreatedBy)
            OUTPUT INSERTED.ConfigID
            VALUES(@Key,'FG_MAP','FG_LAYOUT',@Name,@Value,1,@Actor);
            """;
    internal const string FactoryMapUpdateSql = """
            SET QUOTED_IDENTIFIER ON; SET XACT_ABORT ON;
            UPDATE dbo.SYS_Config SET CodeName=@Name,ConfigValue=@Value,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            OUTPUT INSERTED.ConfigID
            WHERE ConfigID=@Id AND ConfigType='FG_MAP' AND Category='FG_LAYOUT' AND IsActive=1
              AND ConfigValue=@Original AND CodeName=@OriginalName;
            """;

    public void DeleteFactoryMapRegion(FactoryMapRegion original)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(FactoryMapDeleteSql, conn);
        AddFactoryMapOriginal(cmd, original);
        if (cmd.ExecuteScalar() is null) throw new DBConcurrencyException("The map region changed. Refresh and retry.");
    }

    internal const string FactoryMapDeleteSql = """
            SET QUOTED_IDENTIFIER ON;
            DELETE dbo.SYS_Config OUTPUT DELETED.ConfigID
            WHERE ConfigID=@Id AND ConfigType='FG_MAP' AND Category='FG_LAYOUT' AND IsActive=1
              AND ConfigValue=@Original AND CodeName=@OriginalName;
            """;

    private static void AddFactoryMapOriginal(SqlCommand cmd, FactoryMapRegion original)
    {
        ColumnParams.Add(cmd, "@Id", "SYS_Config.ConfigID", original.Id);
        ColumnParams.Add(cmd, "@Original", "SYS_Config.ConfigValue", original.StoredValue);
        ColumnParams.Add(cmd, "@OriginalName", "SYS_Config.CodeName", original.Name);
    }
}
