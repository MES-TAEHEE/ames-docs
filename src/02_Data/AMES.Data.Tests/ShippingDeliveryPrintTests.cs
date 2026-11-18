using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShippingDeliveryPrintTests
{
    [SqlServerFact]
    public void Print_header_does_not_require_inventory_or_delivery_load_schema()
    {
        var database="AMES_PRINT_TEST_"+Guid.NewGuid().ToString("N");
        var settings=SqlServerFactAttribute.Settings();
        using var master=new SqlConnection(settings.ConnectionString);master.Open();
        using(var create=new SqlCommand($"CREATE DATABASE [{database}]",master))create.ExecuteNonQuery();
        try
        {
            settings.InitialCatalog=database;var factory=new AmesConnectionFactory(settings.ConnectionString);
            using(var conn=factory.OpenConnection())
            using(var sql=new SqlCommand("""
                CREATE TABLE FG_Delivery(DeliveryID int,DeliveryNo varchar(30),CustomerID varchar(20),ShipDate date,TrailerNo nvarchar(50),Status varchar(20));
                CREATE TABLE FG_ShippingOrder(OrderID int,OrderNo varchar(30),DeliveryID int);
                INSERT FG_Delivery VALUES(1,'DCR-1','C1','20261118','Trailer 1','Open');
                INSERT FG_ShippingOrder VALUES(1,'ORD-1',1);
                """,conn))sql.ExecuteNonQuery();
            var repo=new FinishedGoodsRepository(factory);
            foreach(var barcode in new[]{"DCR-1","ORD-1"})
            {
                var document=repo.GetShippingDeliveryForPrint(barcode);
                Assert.Equal("DCR-1",document.DeliveryNo);
                Assert.Equal("C1",document.CustomerId);
                Assert.Equal(new DateTime(2026,11,18),document.ShipDate);
                Assert.Equal("Trailer 1",document.TrailerNo);
                Assert.Empty(document.Loads);
                Assert.False(document.SupportsPreparation);
            }
            Assert.Throws<InvalidOperationException>(()=>repo.GetShippingDeliveryForPrint("MISSING"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop=new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]",master);drop.ExecuteNonQuery();
        }
    }
}
