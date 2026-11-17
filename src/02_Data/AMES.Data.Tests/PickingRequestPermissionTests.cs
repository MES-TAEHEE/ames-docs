using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class PickingRequestPermissionTests
{
    [SqlServerFact]
    public void System_admin_has_access_without_screen_grants_and_ordinary_roles_keep_matrix_permissions()
    {
        var database="AMES_PICK_ACCESS_TEST_"+Guid.NewGuid().ToString("N");
        var settings=SqlServerFactAttribute.Settings();
        using var master=new SqlConnection(settings.ConnectionString);master.Open();
        using(var create=new SqlCommand($"CREATE DATABASE [{database}]",master))create.ExecuteNonQuery();
        try
        {
            settings.InitialCatalog=database;
            var factory=new AmesConnectionFactory(settings.ConnectionString);
            using var connection=factory.OpenConnection();
            void Sql(string sql){using var command=new SqlCommand(sql,connection);command.ExecuteNonQuery();}
            Sql("""
                CREATE TABLE SYS_UserProfile(UserID varchar(40),EmployeeNo varchar(20),AccountStatus varchar(20));
                CREATE TABLE AspNetUsers(Id varchar(40),LockoutEnd datetimeoffset NULL);
                CREATE TABLE AspNetRoles(Id varchar(40),Name nvarchar(100));
                CREATE TABLE AspNetUserRoles(UserId varchar(40),RoleId varchar(40));
                CREATE TABLE SYS_RolePermission(RoleID varchar(40),RoleName nvarchar(100),ScreenCode varchar(20),ModuleCode varchar(20),PermissionLevel varchar(10));
                CREATE TABLE SYS_Screen(ScreenCode varchar(20),ModuleCode varchar(20),HRef varchar(100),IsVisible bit);
                INSERT AspNetRoles VALUES('ROLE-SYSADMIN','System Administrator'),('VIEW','Viewer'),('EDIT','Editor'),('FAKE','System Administrator');
                INSERT SYS_UserProfile VALUES('ADMIN','ADMIN','Active'),('VIEW','VIEW','ACTIVE'),('EDIT','EDIT','ACTIVE'),
                    ('NONE','NONE','ACTIVE'),('INACTIVE','INACTIVE','INACTIVE'),('LOCKED','LOCKED','ACTIVE');
                INSERT AspNetUsers VALUES('ADMIN',NULL),('VIEW',NULL),('EDIT',NULL),('NONE',NULL),('INACTIVE',NULL),('LOCKED',DATEADD(hour,1,SYSUTCDATETIME()));
                INSERT AspNetUserRoles VALUES('ADMIN','ROLE-SYSADMIN'),('VIEW','VIEW'),('EDIT','EDIT'),('NONE','FAKE'),('INACTIVE','ROLE-SYSADMIN'),('LOCKED','ROLE-SYSADMIN');
                INSERT SYS_Screen VALUES('WH-05','WEB','wh/picking-requests',0);
                INSERT SYS_RolePermission VALUES('VIEW','Viewer','WH-05','WEB','RXX'),('EDIT','Editor','WH-05','WEB','REX');
                """);
            var repository=new WarehouseRepository(factory);
            Assert.True(repository.HasPickingRequestAccess("ADMIN",'R'));
            Assert.True(repository.HasPickingRequestAccess("ADMIN",'E'));
            Assert.True(repository.HasPickingRequestAccess("VIEW",'R'));
            Assert.False(repository.HasPickingRequestAccess("VIEW",'E'));
            Assert.True(repository.HasPickingRequestAccess("EDIT",'R'));
            Assert.True(repository.HasPickingRequestAccess("EDIT",'E'));
            foreach(var employee in new[]{"NONE","INACTIVE","LOCKED","MISSING"})
            {
                Assert.False(repository.HasPickingRequestAccess(employee,'R'));
                Assert.False(repository.HasPickingRequestAccess(employee,'E'));
            }
            Sql("DELETE SYS_RolePermission;");
            Assert.True(repository.HasPickingRequestAccess("ADMIN",'E'));
            Assert.False(repository.HasPickingRequestAccess("EDIT",'R'));
            Sql("DELETE AspNetUserRoles WHERE UserId='ADMIN';");
            Assert.False(repository.HasPickingRequestAccess("ADMIN",'R'));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop=new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]",master);
            drop.ExecuteNonQuery();
        }
    }
}
