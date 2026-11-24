using System.Data;
using System.Text.Json;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Repositories.WarehouseRepository;

namespace AMES.Data.Tests;

public sealed class FactoryMapTests
{
    [Theory]
    [InlineData(0, 0, 100, 100)]
    [InlineData(99.75, 99.75, .25, .25)]
    [InlineData(12.1234, 34.5678, 10.1, 20.2)]
    public void Rectangle_coordinates_roundtrip_without_zoom_or_culture_loss(decimal x, decimal y, decimal w, decimal h)
    {
        var zone = new FactoryMapZone("EOS", "FG_AREA", "FG1", x, y, w, h);
        zone.Validate();
        var json = JsonSerializer.Serialize(zone);
        Assert.True(json.Length <= 500);
        Assert.Equal(zone, JsonSerializer.Deserialize<FactoryMapZone>(json));
    }

    [Theory]
    [InlineData(-1, 0, 10, 10)]
    [InlineData(0, -1, 10, 10)]
    [InlineData(99, 0, 2, 1)]
    [InlineData(0, 99, 1, 2)]
    [InlineData(0, 0, 0, 10)]
    [InlineData(0, 0, 10, .24)]
    public void Out_of_image_and_accidental_click_rectangles_are_rejected(decimal x, decimal y, decimal w, decimal h)
        => Assert.Throws<ArgumentException>(() => new FactoryMapZone("EOS", "FG", null, x, y, w, h).Validate());

    [Theory]
    [InlineData("", "actor")]
    [InlineData("Zone", "")]
    [InlineData("Zone", "actor-is-over-twenty-characters")]
    public void Invalid_metadata_is_rejected_before_opening_a_connection(string name, string actor)
    {
        var repo = new WarehouseRepository(new AmesConnectionFactory("Server=unused;Database=unused"));
        Assert.Throws<ArgumentException>(() => repo.SaveFactoryMapRegion(name, new("EOS", "FG", null, 0, 0, 10, 10), actor));
    }

    [SkippableFact]
    public void Saved_regions_support_unicode_and_reject_stale_edits_and_unrelated_config_deletion()
    {
        Skip.If(string.IsNullOrWhiteSpace(AmesDevDb.Conn), "Set AMES_TEST_CONN for temporary-table SQL checks.");
        using var conn = new SqlConnection(AmesDevDb.Conn);
        conn.Open();
        using (var setup = new SqlCommand("""
            CREATE TABLE #Config(ConfigID int IDENTITY PRIMARY KEY,ConfigKey varchar(60),ConfigType varchar(15),
                Category varchar(30),CodeName nvarchar(80),ConfigValue nvarchar(500),IsActive bit,
                CreatedBy varchar(20),CreatedTS datetime2 DEFAULT SYSDATETIME(),ModifiedBy varchar(20),ModifiedTS datetime2);
            INSERT #Config(ConfigKey,ConfigType,Category,CodeName,ConfigValue,IsActive) VALUES('other','STRING','SYSTEM',N'Other','old',1);
            """, conn)) setup.ExecuteNonQuery();
        var insert = Command(conn, FactoryMapInsertSql);
        using (insert)
        {
            insert.Parameters.Add("@Key", SqlDbType.VarChar, 60).Value = "FG_MAP_TEST";
            AddValues(insert, "완제품 구역", "{\"X\":10}");
            Assert.Equal(2, insert.ExecuteScalar());
        }
        using (var read = new SqlCommand("SELECT CodeName FROM #Config WHERE ConfigID=2", conn))
            Assert.Equal("완제품 구역", read.ExecuteScalar());
        using (var update = Command(conn, FactoryMapUpdateSql))
        {
            AddValues(update, "Renamed", "{\"X\":20}");
            AddOriginal(update, 2, "완제품 구역", "{\"X\":10}");
            Assert.Equal(2, update.ExecuteScalar());
            Assert.Null(update.ExecuteScalar()); // A second user's stale snapshot cannot overwrite the change.
        }
        using (var staleDelete = Command(conn, FactoryMapDeleteSql))
        {
            AddOriginal(staleDelete, 2, "완제품 구역", "{\"X\":10}");
            Assert.Null(staleDelete.ExecuteScalar());
        }
        using (var unrelated = Command(conn, FactoryMapDeleteSql))
        {
            AddOriginal(unrelated, 1, "Other", "old");
            Assert.Null(unrelated.ExecuteScalar());
        }
        using (var delete = Command(conn, FactoryMapDeleteSql))
        {
            AddOriginal(delete, 2, "Renamed", "{\"X\":20}");
            Assert.Equal(2, delete.ExecuteScalar());
        }
        using var count = new SqlCommand("SELECT COUNT(*) FROM #Config", conn);
        Assert.Equal(1, count.ExecuteScalar());
    }

    [SkippableTheory]
    [InlineData("EOS", "FG", "P1", true)]
    [InlineData("EOS", "FG", null, true)]
    [InlineData("OTHER", "FG", "P1", false)]
    [InlineData("EOS", "FG", "INACTIVE", false)]
    [InlineData("EOS", "MATERIAL", null, false)]
    [InlineData("EOS", "ONLY_PARENTED", null, false)]
    public void Only_active_matching_FG_groups_can_be_linked(string wh, string area, string? parent, bool allowed)
    {
        Skip.If(string.IsNullOrWhiteSpace(AmesDevDb.Conn), "Set AMES_TEST_CONN for temporary-table SQL checks.");
        using var conn = new SqlConnection(AmesDevDb.Conn);
        conn.Open();
        using (var setup = new SqlCommand("""
            CREATE TABLE #Locations(LocationID varchar(20),WhCode varchar(20),AreaCode varchar(20),ParentLocationID varchar(20),
                LocationType varchar(20),ActiveFlag bit,LocationLevel tinyint);
            INSERT #Locations VALUES('P1','EOS','FG',NULL,'FG',1,1),('C1','EOS','FG','P1','FG',1,2),
                ('U1','EOS','FG',NULL,'FG',1,2),('INACTIVE','EOS','FG',NULL,'FG',0,1),
                ('C2','EOS','FG','INACTIVE','FG',1,2),('M1','EOS','MATERIAL',NULL,'RACK',1,2),
                ('C3','EOS','ONLY_PARENTED','P1','FG',1,2);
            """, conn)) setup.ExecuteNonQuery();
        using var cmd = new SqlCommand(FactoryMapTargetSql.Replace("dbo.MD_Location", "#Locations"), conn);
        cmd.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = wh;
        cmd.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = area;
        cmd.Parameters.Add("@Parent", SqlDbType.VarChar, 20).Value = (object?)parent ?? DBNull.Value;
        Assert.Equal(allowed, cmd.ExecuteScalar() is not null);
    }

    private static SqlCommand Command(SqlConnection conn, string sql) => new(sql.Replace("dbo.SYS_Config", "#Config"), conn);
    private static void AddValues(SqlCommand cmd, string name, string json)
    {
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 80).Value = name;
        cmd.Parameters.Add("@Value", SqlDbType.NVarChar, 500).Value = json;
        cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = "itest";
    }
    private static void AddOriginal(SqlCommand cmd, int id, string name, string json)
    {
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        cmd.Parameters.Add("@Original", SqlDbType.NVarChar, 500).Value = json;
        cmd.Parameters.Add("@OriginalName", SqlDbType.NVarChar, 80).Value = name;
    }
}
