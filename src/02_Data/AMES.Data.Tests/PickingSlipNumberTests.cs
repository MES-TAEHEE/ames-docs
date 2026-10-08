using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public class PickingSlipNumberTests
{
    [SqlServerFact]
    public void Numbers_are_sequential_per_line_and_day_beyond_99()
    {
        var settings = SqlServerFactAttribute.Settings();
        var database = "AMES_PICK_TEST_" + Guid.NewGuid().ToString("N");
        using var master = new SqlConnection(settings.ConnectionString);
        master.Open();
        using (var create = new SqlCommand($"CREATE DATABASE [{database}]", master)) create.ExecuteNonQuery();
        try
        {
            settings.InitialCatalog = database;
            using var conn = new SqlConnection(settings.ConnectionString);
            conn.Open();
            using (var setup = new SqlCommand("""
                CREATE TABLE dbo.MD_Line(LineID varchar(20) NOT NULL, LotPrefix char(2) NULL);
                INSERT dbo.MD_Line VALUES ('LINE-1','01'),('LINE-2','02');
                CREATE TABLE dbo.WH_PickSlip(
                    PickSlipID int IDENTITY PRIMARY KEY, PickSlipNo nvarchar(40), ReqLocation nvarchar(40),
                    ReqSeqNo int, ReqUserId nvarchar(80), ItemNo varchar(20), DemandQty decimal(14,3),
                    PickedQty decimal(14,3), RequiredAt datetime2, Priority tinyint, Status varchar(20),
                    CreatedBy varchar(20), CreatedTS datetime2);
                DECLARE @Prefix nvarchar(10) = CONCAT(N'PK01',CONVERT(char(6),SYSDATETIME(),12));
                INSERT dbo.WH_PickSlip(PickSlipNo,CreatedBy)
                VALUES (@Prefix + N'099','TEST'), (@Prefix + N'BAD','TEST');
                """, conn)) setup.ExecuteNonQuery();

            string todayPrefix;
            using (var date = new SqlCommand("SELECT CONVERT(char(6),SYSDATETIME(),12)", conn))
                todayPrefix = (string)date.ExecuteScalar()!;

            var repo = new WarehouseRepository(new AmesConnectionFactory(settings.ConnectionString));
            string Create(string line) => repo.CreatePickingSlip(DateTime.Today, "TEST",
                new[] { new WarehouseRepository.CreatePickingSlipLine("PART-1", line, 1) });

            Assert.Equal("PK01" + todayPrefix + "100", Create("LINE-1"));
            Assert.Equal("PK01" + todayPrefix + "101", Create("LINE-1"));
            Assert.Equal("PK02" + todayPrefix + "001", Create("LINE-2"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", master);
            drop.ExecuteNonQuery();
        }
    }
}
