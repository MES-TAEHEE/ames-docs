using System.Data;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

[Collection(BackflushDbCollection.Name)]
public sealed class PickingRequestTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0001")]
    [InlineData("100000000000")]
    public void Invalid_quantities_are_rejected_before_opening_a_connection(string value)
    {
        var input=new WarehouseRepository.PickingRequestInput("LOCATION","PART",decimal.Parse(value,System.Globalization.CultureInfo.InvariantCulture),DateTime.Today);
        Assert.Throws<ArgumentException>(()=>WarehouseRepository.ValidatePickingRequest(input,"TEST"));
    }

    [Fact]
    public void Request_actor_and_identifiers_are_never_silently_truncated()
    {
        var input=new WarehouseRepository.PickingRequestInput("LOCATION","PART",.75m,DateTime.Today);
        Assert.Equal(.75m,WarehouseRepository.ValidatePickingRequest(input,"TEST").Qty);
        Assert.Throws<ArgumentException>(()=>WarehouseRepository.ValidatePickingRequest(input,new string('X',21)));
        Assert.Throws<ArgumentException>(()=>WarehouseRepository.ValidatePickingRequest(input with{PartNo=new string('X',21)},"TEST"));
        Assert.Throws<ArgumentException>(()=>WarehouseRepository.ValidatePickingRequest(input with{Destination=""},"TEST"));
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Local_clone_request_release_and_delivery_preserve_decimal_quantity_and_close_the_order(bool warehouseTarget)
    {
        var cs=Environment.GetEnvironmentVariable("AMES_LOCATION_PARTS_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(cs),"Local clone flow connection is not configured.");
        var settings=new SqlConnectionStringBuilder(cs);
        Assert.Equal(@"localhost\SQLEXPRESS",settings.DataSource,StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith("AMES_TEST_",settings.InitialCatalog);
        using var connection=new AmesConnectionFactory(cs!).OpenConnection();
        using var transaction=connection.BeginTransaction();
        var id="VPR"+Guid.NewGuid().ToString("N")[..10];
        try
        {
            WarehouseRepository.LockLinePicking(connection,transaction);
            Exec("""
                INSERT dbo.MD_Item(ItemNo,ItemName,ItemType,DefaultUOM,CreatedBy) VALUES(@Id,'Isolated request material','MATERIAL','G','TEST');
                DECLARE @Prefix char(2)=(SELECT TOP(1) P FROM(VALUES('Z0'),('Z1'),('Z2'),('Z3'),('Z4'),('Z5'),('Z6'),('Z7'),('Z8'),('Z9')) C(P)
                    WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_Line M WHERE M.LotPrefix=C.P));
                IF @Prefix IS NULL THROW 51833,'No isolated line prefix available.',1;
                INSERT dbo.MD_Line(LineID,LineNameEn,WCID,Status,LotPrefix,CreatedBy)
                    SELECT @Id,'Isolated request line',WCID,'ACTIVE',@Prefix,'TEST' FROM dbo.MD_WorkCenter WHERE WCID=(SELECT TOP(1) WCID FROM dbo.MD_WorkCenter);
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LineID,LocationType,LocationLevel,ActiveFlag,CreatedBy)
                    VALUES(@Id,'Request line destination','EOS','TEST_REQUEST',@Id,'LINE',2,1,'TEST'),
                          (@Id+'W','Request warehouse destination','EOS','MAT_AREA',NULL,'WH',2,1,'TEST'),
                          (@Id+'S','Request warehouse source','EOS','MAT_AREA',NULL,'WH',2,1,'TEST');
                INSERT dbo.WH_Inventory(LotNo,PartNo,UnitType,LocationNo,Qty,ReceivedAt,CreatedAt)
                    VALUES(@Id,@Id,'PART',@Id+'S',100.125,SYSDATETIME(),SYSDATETIME());
                """);
            var destination=warehouseTarget?id+"W":id;
            var input=new WarehouseRepository.PickingRequestInput(destination,id,100.125m,DateTime.Today);
            if(warehouseTarget)
                Assert.Throws<InvalidOperationException>(()=>WarehouseRepository.CreatePickingRequest(connection,transaction,input with{LineLocationOnly=true},"TEST"));
            else input=input with{LineLocationOnly=true};
            var slip=WarehouseRepository.CreatePickingRequest(connection,transaction,input,"TEST");
            Assert.Equal("WH-REQUEST",Convert.ToString(Scalar("SELECT CreatedBy FROM dbo.WH_PickSlip WHERE PickSlipNo=@Slip",slip)));
            Assert.Equal("TEST",Convert.ToString(Scalar("SELECT ReqUserId FROM dbo.WH_PickSlip WHERE PickSlipNo=@Slip",slip)));
            Assert.Equal(100.125m,Convert.ToDecimal(Scalar("SELECT DemandQty FROM dbo.WH_PickSlip WHERE PickSlipNo=@Slip",slip)));
            Assert.Equal(1,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.SYS_AuditLog WHERE TargetID=@Slip AND ActorUserID='TEST' AND ActionType='CREATE'",slip)));
            var duplicate=Assert.Throws<AMES.Contracts.Exceptions.PickingRequestExistsException>(()=>WarehouseRepository.CreatePickingRequest(connection,transaction,input,"TEST"));
            Assert.Equal(slip,Assert.Single(duplicate.ExistingOrders).PickSlipNo);
            Assert.Equal(100.125m,duplicate.ExistingOrders[0].RemainingQty);
            Assert.Empty(WarehouseRepository.ListExistingPickingRequests(connection,transaction,destination,"OTHER-PART"));
            Assert.Empty(WarehouseRepository.ListExistingPickingRequests(connection,transaction,id+"S",id));
            var additional=WarehouseRepository.CreatePickingRequest(connection,transaction,input with{ConfirmAdditional=true},"TEST");
            Assert.NotEqual(slip,additional);
            WarehouseRepository.CancelPickingSlip(connection,transaction,additional,"TEST");
            Assert.Single(WarehouseRepository.ListExistingPickingRequests(connection,transaction,destination,id));
            var api=Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
            var candidateStart=api.IndexOf("using var lotCmd = new SqlCommand($\"\"\"",api.IndexOf("var lots = new List<ReleaseBatchLot>",StringComparison.Ordinal),StringComparison.Ordinal);
            candidateStart=api.IndexOf("\"\"\"",candidateStart,StringComparison.Ordinal)+3;
            var candidateSql=api[candidateStart..api.IndexOf("\"\"\"",candidateStart,StringComparison.Ordinal)]
                .Replace("{WarehouseRepository.LocationPartAssignmentsSql(conn,tx)}",WarehouseRepository.LocationPartAssignmentsSql(connection,transaction))
                .Replace("{WarehouseRepository.WarehouseBoxPredicate}",WarehouseRepository.WarehouseBoxPredicate);
            using(var candidate=new SqlCommand(candidateSql,connection,transaction))
            {
                candidate.Parameters.AddWithValue("@Slip",slip);candidate.Parameters.AddWithValue("@LotNo",id);
                candidate.Parameters.AddWithValue("@BatchAllocations","[]");
                using var reader=candidate.ExecuteReader();Assert.True(reader.Read());
                Assert.Equal(destination,reader["LineDestination"]);
                Assert.True((bool)reader["ValidLineDestination"]);
                Assert.True((bool)reader["EligibleBox"]);
                Assert.False((bool)reader["AlreadyAtDestination"]);
            }
            using(var release=new SqlCommand(PdaWarehouseSql.ReleasePickedLot,connection,transaction))
            {
                release.Parameters.AddWithValue("@LineDestination",destination);release.Parameters.AddWithValue("@LotNo",id);
                release.Parameters.AddWithValue("@Qty",100.125m);release.Parameters.AddWithValue("@ItemNo",id);
                release.Parameters.AddWithValue("@LocationID",id+"S");release.Parameters.AddWithValue("@ReasonCode","RELEASE_PICK");
                release.Parameters.AddWithValue("@ScheduleID",Scalar("SELECT PickSlipID FROM dbo.WH_PickSlip WHERE PickSlipNo=@Slip",slip)!);
                release.Parameters.AddWithValue("@User","TEST");release.Parameters.AddWithValue("@Slip",slip);release.ExecuteNonQuery();
            }
            var start=api.IndexOf(";WITH PickCounts AS",StringComparison.Ordinal);
            using(var finish=new SqlCommand(api[start..api.IndexOf("\"\"\"",start,StringComparison.Ordinal)],connection,transaction))
            {
                finish.Parameters.AddWithValue("@Slip",slip);finish.Parameters.AddWithValue("@User","TEST");finish.ExecuteNonQuery();
            }
            Assert.Equal("Closed",Convert.ToString(Scalar("SELECT Status FROM dbo.WH_PickSlip WHERE PickSlipNo=@Slip",slip)));
            Assert.Equal(100.125m,Convert.ToDecimal(Scalar("SELECT PickedQty FROM dbo.WH_PickSlip WHERE PickSlipNo=@Slip",slip)));
            Assert.Equal(100.125m,Convert.ToDecimal(Scalar($"SELECT SUM(Qty) FROM ({WarehouseRepository.LineTransfersInTransitSql}) T WHERE T.LotNo=@Id")));
            using(var move=new SqlCommand(PdaWarehouseSql.InboundMove,connection,transaction))
            {
                move.Parameters.AddWithValue("@ReceiveMode","LOCAL");move.Parameters.AddWithValue("@LotBarcode",id);
                move.Parameters.AddWithValue("@LocationId",destination);move.Parameters.AddWithValue("@UserId","TEST");
                using var reader=move.ExecuteReader();do{while(reader.Read()){}}while(reader.NextResult());
            }
            Assert.Equal(destination,Convert.ToString(Scalar("SELECT LocationNo FROM dbo.WH_Inventory WHERE LotNo=@Id")));
            Assert.Equal(100.125m,Convert.ToDecimal(Scalar("SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=@Id")));
            Assert.Equal(0,Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM ({WarehouseRepository.LineTransfersInTransitSql}) T WHERE T.LotNo=@Id")));
            var cancelled=WarehouseRepository.CreatePickingRequest(connection,transaction,input,"TEST");
            Assert.NotEqual(slip,cancelled);
            WarehouseRepository.CancelPickingSlip(connection,transaction,cancelled,"TEST");
            Assert.Equal("Cancelled",Convert.ToString(Scalar("SELECT Status FROM dbo.WH_PickSlip WHERE PickSlipNo=@Slip",cancelled)));
            Assert.Equal(100.125m,Convert.ToDecimal(Scalar("SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=@Id")));
        }
        finally{if(transaction.Connection is not null)transaction.Rollback();}
        object? Scalar(string sql,string? slip=null)
        {
            using var command=new SqlCommand(sql,connection,transaction);
            command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@Slip",(object?)slip??DBNull.Value);
            return command.ExecuteScalar();
        }
        void Exec(string sql)=>Scalar(sql);
    }

    [Fact]
    public void New_request_ui_rechecks_edit_and_order_list_labels_the_source()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingRequests.razor");
        Assert.Contains("/wh/picking-requests",page);
        Assert.Contains("Permissions.RecheckEditAsync(Auth,Href)",page);
        Assert.Contains("WH.CreatePickingRequest(input,actor)",page);
        Assert.Contains("if(_saving || !_requestOpen)return;",page);
        Assert.Contains("\"REQUEST\" => L[\"WH.Picking.Source.Request\"]",Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor"));
        Assert.DoesNotContain("CREATE TABLE",Read("dist/migrate_wh_picking_requests.sql"));
    }

    [SkippableFact]
    public void Request_options_and_source_headers_load_from_the_local_clone_without_writes()
    {
        var cs=Environment.GetEnvironmentVariable("AMES_LINE_PICKING_READONLY_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(cs),"Local clone read-only connection is not configured.");
        var repository=new WarehouseRepository(new AmesConnectionFactory(cs!));
        Assert.NotEmpty(repository.ListPickingRequestDestinations());
        Assert.NotEmpty(repository.ListPickingRequestParts());
        Assert.NotNull(repository.ListPickingSlipHeaders());
    }
    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string file="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../..",path)));
}
