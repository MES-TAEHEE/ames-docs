using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public class PopSessionDatabaseTimeTests
{
    [SqlServerFact]
    public void Session_timestamps_are_returned_from_database_and_share_one_instant()
    {
        // Isolated local test database; never reads AMES_DEV configuration.
        var database = "AMES_SESSION_TEST_" + Guid.NewGuid().ToString("N");
        var settings = SqlServerFactAttribute.Settings();
        using var master = new SqlConnection(settings.ConnectionString);
        master.Open();
        using (var create = new SqlCommand($"CREATE DATABASE [{database}]", master)) create.ExecuteNonQuery();
        try
        {
            settings.InitialCatalog = database;
            var factory = new AmesConnectionFactory(settings.ConnectionString);
            using var conn = factory.OpenConnection();
            using (var schema = new SqlCommand("""
                CREATE TABLE PR_PopSession(SessionID int IDENTITY PRIMARY KEY,OperatorID nvarchar(450),TerminalID varchar(20),
                    LineID varchar(20),ShiftCode varchar(10),AuthMethod varchar(20),StartedAt datetime2,ExpiresAt datetime2,
                    CreatedBy varchar(20),CreatedTS datetime2);
                CREATE TABLE AspNetRoles(Id nvarchar(450),Name nvarchar(256));
                CREATE TABLE AspNetUserRoles(UserId nvarchar(450),RoleId nvarchar(450));
                """, conn)) schema.ExecuteNonQuery();
            var before = DbClock.ReadNow(factory);
            var session = new PopSessionRepository(factory).CreateSession(new EmployeeProfileDto
            {
                UserId = "test", UserName = "test", EmployeeNo = "TEST", EmployeeName = "Test", PasswordHash = ""
            }, "PDA", "01", "DAY", AuthMethod.Badge);
            var after = DbClock.ReadNow(factory);
            using var read = new SqlCommand("SELECT StartedAt,ExpiresAt,CreatedTS FROM PR_PopSession", conn);
            using var rows = read.ExecuteReader();
            Assert.True(rows.Read());
            Assert.Equal(rows.GetDateTime(0), session.StartedAt);
            Assert.Equal(rows.GetDateTime(1), session.ExpiresAt);
            Assert.Equal(rows.GetDateTime(2), session.StartedAt);
            Assert.Equal(PopSessionRepository.DefaultLifetime, session.ExpiresAt - session.StartedAt);
            Assert.InRange(session.StartedAt, before, after);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", master);
            drop.ExecuteNonQuery();
        }
    }
}
