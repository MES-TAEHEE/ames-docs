using System.Data;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmPurchaseOrderSaveTests
{
    [SqlServerFact]
    public void Save_issues_all_lines_atomically_and_keeps_validation_and_version_guards()
    {
        var settings=SqlServerFactAttribute.Settings();
        var database="AMES_PO_SAVE_TEST_"+Guid.NewGuid().ToString("N");
        using var master=new SqlConnection(settings.ConnectionString);master.Open();
        using(var create=new SqlCommand($"CREATE DATABASE [{database}]",master))create.ExecuteNonQuery();
        try
        {
            settings.InitialCatalog=database;
            var factory=new AmesConnectionFactory(settings.ConnectionString);
            void Sql(string sql){using var c=factory.OpenConnection();using var command=new SqlCommand(sql,c);command.ExecuteNonQuery();}
            int Count(string sql){using var c=factory.OpenConnection();using var command=new SqlCommand(sql,c);return Convert.ToInt32(command.ExecuteScalar());}
            Dictionary<int,string> Versions(string number)
            {
                using var c=factory.OpenConnection();using var command=new SqlCommand("SELECT PoID,ScmRowVersion FROM WH_PurchaseOrder WHERE PoNumber=@N",c);
                command.Parameters.Add("@N",SqlDbType.VarChar,30).Value=number;
                using var r=command.ExecuteReader();var versions=new Dictionary<int,string>();
                while(r.Read())versions.Add(r.GetInt32(0),Convert.ToHexString((byte[])r[1]));return versions;
            }
            Sql("""
                CREATE TABLE SCM_PurchaseOrderSequence(NumberDate date PRIMARY KEY,LastNumber int);
                CREATE TABLE MD_Item(ItemNo varchar(20) PRIMARY KEY,ItemName nvarchar(200),ItemType varchar(20),DefaultUOM varchar(20),ActiveFlag bit);
                CREATE TABLE MD_Vendor(VendorID varchar(20) PRIMARY KEY,ActiveFlag bit);
                CREATE TABLE SCM_ItemVendor(VendorID varchar(20),ItemNo varchar(20),ActiveFlag bit);
                CREATE TABLE WH_PurchaseOrder(PoID int IDENTITY PRIMARY KEY,PoNumber varchar(30),PoLineNo int,VendorID varchar(20),ItemNo varchar(20),OrderQty decimal(18,3),ReceivedQty decimal(18,3),
                    UnitCode varchar(20),UnitPrice decimal(18,4),Currency varchar(3),OrderDate date,DueDate date,Status varchar(20),DeliveryDestination nvarchar(200),CreatedBy varchar(20),CreatedTS datetime2,
                    ModifiedBy varchar(20),ModifiedTS datetime2,ScmRowVersion rowversion);
                CREATE TABLE WH_Inventory(LotNo varchar(64),InvoiceNo varchar(30));
                CREATE TABLE SCM_DeliveryBox(BoxNumber varchar(64),DeliveryLineID int);
                CREATE TABLE SCM_DeliveryLine(DeliveryLineID int,PoID int);
                CREATE TABLE SCM_Delivery(PoNumber varchar(30),Status varchar(20));
                INSERT MD_Item VALUES('PART-1','Part one','MATERIAL','EA',1),('PART-2','Part two','MATERIAL','EA',1);
                INSERT MD_Vendor VALUES('V1',1);
                INSERT SCM_ItemVendor VALUES('V1','PART-1',1),('V1','PART-2',1);
                """);
            var repository=new ScmRepository(factory);
            var order=new ScmRepository.PurchaseOrder("","V1",DateTime.Today,DateTime.Today.AddDays(1),"EOS","Draft","USD",
                [new(1,"PART-1","Part one","EA",10,2,0),new(2,"PART-2","Part two","EA",20,3,0)]);
            var number=repository.SavePurchaseOrder(order,new Dictionary<int,string>(),"tester");
            Assert.Equal(2,Count($"SELECT COUNT(*) FROM WH_PurchaseOrder WHERE PoNumber='{number}' AND Status='Open'"));
            var saved=order with {Number=number};
            Assert.Throws<InvalidOperationException>(()=>repository.SavePurchaseOrder(saved,Versions(number),"tester"));
            Sql($"UPDATE WH_PurchaseOrder SET Status='Draft' WHERE PoNumber='{number}';");
            var stale=Versions(number);
            Sql($"UPDATE WH_PurchaseOrder SET ModifiedBy='other' WHERE PoNumber='{number}';");
            Assert.Throws<InvalidOperationException>(()=>repository.SavePurchaseOrder(saved,stale,"tester"));
            var invalid=saved with {Lines=[saved.Lines[0] with {Quantity=30},saved.Lines[1] with {Item="UNKNOWN"}]};
            Assert.Throws<InvalidOperationException>(()=>repository.SavePurchaseOrder(invalid,Versions(number),"tester"));
            Assert.Equal(2,Count($"SELECT COUNT(*) FROM WH_PurchaseOrder WHERE PoNumber='{number}' AND Status='Draft'"));
            Assert.Equal(10,Count($"SELECT OrderQty FROM WH_PurchaseOrder WHERE PoNumber='{number}' AND PoLineNo=1"));
            repository.SavePurchaseOrder(saved,Versions(number),"tester");
            Assert.Equal(2,Count($"SELECT COUNT(*) FROM WH_PurchaseOrder WHERE PoNumber='{number}' AND Status='Open'"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop=new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];",master);drop.ExecuteNonQuery();
        }
    }
}
