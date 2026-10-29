using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShippingDueRiskQueryTests
{
    [SkippableFact]
    public void Read_query_includes_unplanned_old_and_unknown_SOs_but_not_closed_or_fully_shipped()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection), "Table-variable fixture; set connection explicitly.");
        var sql = FinishedGoodsRepository.DueRiskSql.Replace("dbo.PP_CustomerOrder", "@Orders").Replace("dbo.MD_Item", "@Items").Replace("dbo.MD_Customer", "@Customers");
        Assert.DoesNotContain("dbo.", sql);
        using var conn = new SqlConnection(connection); conn.Open();
        using var cmd = new SqlCommand("""
            DECLARE @Orders TABLE(SoID int,SoNumber varchar(20),CustomerID varchar(20),ItemNo varchar(20),
                RequestedDeliveryDate date,OrderQty decimal(18,3),ShippedQty decimal(18,3),Status varchar(20));
            DECLARE @Items TABLE(ItemNo varchar(20),ItemName nvarchar(200));
            DECLARE @Customers TABLE(CustomerID varchar(20)); INSERT @Customers VALUES('C1');
            INSERT @Items VALUES('PART','Part');
            INSERT @Orders VALUES(1,'SO-OLD','C1','PART','20200101',100,0,'Open'),
                (2,'SO-PARTIAL','C1','PART','20261029',100,20,'Loading'),
                (3,'SO-NO-DUE','C1','PART',NULL,100,0,NULL),
                (4,'SO-NO-QTY','C1','PART','20261029',NULL,0,'Open'),
                (5,'SO-CANCEL','C1','PART','20261029',100,0,' Cancelled '),
                (6,'SO-CLOSED','C1','PART','20261029',100,0,'Closed'),
                (7,'SO-FULL','C1','PART','20261029',100,100,'Open'),
                (8,'SO-SHIPPED','C1','PART','20261029',100,0,'Shipped'),
                (9,'SO-NO-ITEM','C1','MISSING','20261029',100,0,'Open');
            """ + sql, conn);
        using var r = cmd.ExecuteReader();
        var ids = new List<int>(); while (r.Read())
        {
            ids.Add((int)r["SoID"]);
            Assert.Equal((int)r["SoID"] != 9, (bool)r["Identified"]);
        }
        Assert.Equal(new[] { 1, 2, 3, 4, 9 }, ids.Order());
    }
}
