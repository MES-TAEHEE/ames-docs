using System.Data;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

[Collection(BackflushDbCollection.Name)]
public sealed class LinePickingFlowTests
{
    [Fact]
    public void Release_keeps_line_supply_in_transit_until_set_location()
    {
        Assert.Contains("LocationNo=CASE WHEN @LineDestination IS NULL THEN LocationNo ELSE NULL END",PdaWarehouseSql.ReleasePickedLot);
        Assert.DoesNotContain("'IN'",PdaWarehouseSql.ReleasePickedLot);
        Assert.Contains("@ExpectedLineLocation<>@Location",PdaWarehouseSql.InboundMove);
        Assert.Contains("'MOVE'",PdaWarehouseSql.InboundMove);
        Assert.DoesNotContain("CloseDate",WarehouseRepository.LineTransfersInTransitSql);
        Assert.Contains("T.TransactionType='OUT' AND T.SourceType='PICK_SLIP'",WarehouseRepository.LineTransfersInTransitSql);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Local_clone_work_order_release_delivery_consumption_and_minimum_replenishment_are_idempotent(bool wrongDestination)
    {
        var value=Environment.GetEnvironmentVariable("AMES_LOCATION_PARTS_TEST_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(value),"Local clone flow connection is not configured.");
        var settings=new SqlConnectionStringBuilder(value);
        Assert.StartsWith("AMES_TEST_",settings.InitialCatalog);
        Assert.Contains(settings.DataSource.ToLowerInvariant(),new[]{"localhost\\sqlexpress",".\\sqlexpress"});
        using var connection=new AmesConnectionFactory(value!).OpenConnection();
        using var transaction=connection.BeginTransaction();
        var id="VLP"+Guid.NewGuid().ToString("N")[..10];
        var part=id+"M";
        var product=id+"P";
        try
        {
            WarehouseRepository.LockLinePicking(connection,transaction);
            using(var setup=new SqlCommand("""
                DECLARE @Prefix char(2)=(SELECT TOP(1) P FROM (VALUES('Z0'),('Z1'),('Z2'),('Z3'),('Z4'),('Z5'),('Z6'),('Z7'),('Z8'),('Z9')) C(P)
                    WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_Line L WHERE L.LotPrefix=C.P));
                IF @Prefix IS NULL THROW 51000,'No isolated test line prefix available.',1;
                INSERT dbo.MD_Item(ItemNo,ItemName,ItemType,DefaultUOM,CreatedBy)
                    VALUES(@Part,'Test line material','MATERIAL','G','TEST'),(@Product,'Test line product','ASSY','EA','TEST');
                INSERT dbo.MD_Line(LineID,LineNameEn,WCID,Status,LotPrefix,CreatedBy)
                    SELECT @Id,'Test picking line',WCID,'ACTIVE',@Prefix,'TEST' FROM dbo.MD_WorkCenter WHERE WCID=(SELECT TOP(1) WCID FROM dbo.MD_WorkCenter);
                INSERT dbo.MD_Station(StationCode,LineID,OrderSeq,Status,CreatedBy) VALUES(@Id,@Id,1,'ACTIVE','TEST');
                INSERT dbo.MD_Bop(BOPID,ItemNo,RoutingType,StepSeq,StationCode,CreatedBy) VALUES(@Id,@Product,'A',10,@Id,'TEST');
                INSERT dbo.MD_BomVersion(VersionID,RootItemNo,Status,EffFrom,CreatedBy) VALUES(@Id,@Product,'APPROVED',CAST(SYSDATETIME() AS date),'TEST');
                INSERT dbo.MD_Bom(BOMID,ParentItemNo,CompItemNo,QtyPer,ScrapPct,UOM,VersionID,ActiveFlag,CreatedBy)
                    VALUES(@Id,@Product,@Part,2,0,'G',@Id,1,'TEST');
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LineID,LocationType,LocationLevel,ActiveFlag,CreatedBy)
                    VALUES(@Id,'Test line location','EOS',@Prefix,@Id,'LINE',2,1,'TEST'),
                          (@Id+'Y','Other position on the same line','EOS',@Prefix,@Id,'LINE',2,1,'TEST');
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationType,LocationLevel,ActiveFlag,CreatedBy)
                    VALUES(@Id+'X','Wrong destination','EOS','MAT_AREA','WH',2,1,'TEST');
                INSERT dbo.WH_Inventory(LotNo,PartNo,UnitType,LocationNo,Qty,ReceivedAt,CreatedAt)
                    VALUES(@Id+'S',@Part,'BOX',@Id,5,SYSDATETIME(),SYSDATETIME()),(@Id+'B',@Part,'BOX',NULL,5,SYSDATETIME(),SYSDATETIME()),
                          (@Id+'C',@Part,'PART',NULL,10,SYSDATETIME(),SYSDATETIME());
                INSERT dbo.PP_WorkOrder(WoNumber,ItemNo,OrderQty,CompletedQty,Status,LineID,CreatedBy)
                    VALUES(@Id,@Product,10,0,'Released',@Id,'TEST');
                DECLARE @Wo int=SCOPE_IDENTITY();
                INSERT dbo.PP_WorkOrderRouting(WoID,StepSeq,ProcessCode,LineID,ItemNo,Status,CompletedQty,CreatedBy)
                    VALUES(@Wo,10,'INJ',@Id,@Product,'Released',0,'TEST');
                """,connection,transaction))
            {
                setup.Parameters.AddWithValue("@Id",id);setup.Parameters.AddWithValue("@Part",part);setup.Parameters.AddWithValue("@Product",product);
                setup.ExecuteNonQuery();
            }
            WarehouseRepository.SaveLocationParts(connection,transaction,id,[new(part,10)],"TEST");
            Assert.Contains(WarehouseRepository.ReadLineStock(connection,transaction,true),r=>r.LineCode==id&&r.PartNo==part&&r.Qty==5);
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(15,Requested());
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(15,Requested());
            var inventoryBeforeCancel=Convert.ToString(Scalar("SELECT LotNo,Qty,LocationNo FROM dbo.WH_Inventory WHERE PartNo=@Part ORDER BY LotNo FOR JSON PATH,INCLUDE_NULL_VALUES"));
            Execute("""
                INSERT dbo.WH_PickSlip(PickSlipNo,ItemNo,DemandQty,PickedQty,Status,CloseDate,CreatedBy,CreatedTS)
                VALUES(@Id+'CAN',@Part,10,0,'Open',NULL,'TEST',SYSDATETIME()),
                      (@Id+'CAN',@Part,10,3,'Partial',NULL,'TEST',SYSDATETIME()),
                      (@Id+'CAN',@Part,5,5,'Closed',SYSDATETIME(),'TEST',SYSDATETIME());
                """);
            WarehouseRepository.CancelPickingSlip(connection,transaction,id+"CAN","TEST");
            Assert.Equal(2,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.WH_PickSlip WHERE PickSlipNo=@Id+'CAN' AND Status='Cancelled' AND CloseUserId='TEST'")));
            Assert.Equal(1,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.WH_PickSlip WHERE PickSlipNo=@Id+'CAN' AND Status='Closed' AND PickedQty=5")));
            Assert.Equal(1,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.WH_PickSlip WHERE PickSlipNo=@Id+'CAN' AND Status='Cancelled' AND PickedQty=3 AND DemandQty=10")));
            Assert.Equal(1,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.SYS_AuditLog WHERE TargetEntity='WH_PickSlip' AND TargetID=@Id+'CAN' AND ActionType='CANCEL'")));
            Assert.Throws<InvalidOperationException>(()=>WarehouseRepository.CancelPickingSlip(connection,transaction,id+"CAN","TEST"));
            Assert.Throws<InvalidOperationException>(()=>WarehouseRepository.CancelPickingSlip(connection,transaction,id+"MISSING","TEST"));
            Execute("INSERT dbo.WH_PickSlip(PickSlipNo,ItemNo,DemandQty,PickedQty,Status,CloseDate,CreatedBy,CreatedTS) VALUES(@Id+'DONE',@Part,5,5,'Closed',SYSDATETIME(),'TEST',SYSDATETIME());");
            Assert.Throws<InvalidOperationException>(()=>WarehouseRepository.CancelPickingSlip(connection,transaction,id+"DONE","TEST"));
            Assert.Equal(inventoryBeforeCancel,Convert.ToString(Scalar("SELECT LotNo,Qty,LocationNo FROM dbo.WH_Inventory WHERE PartNo=@Part ORDER BY LotNo FOR JSON PATH,INCLUDE_NULL_VALUES")));
            Release("B",5);
            Assert.Equal(1,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND Status='Closed'")));
            Assert.Equal(1,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND CloseDate IS NULL")));
            transaction.Save("CancelPartial");
            var partialSlip=Convert.ToString(Scalar("SELECT TOP(1) PickSlipNo FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND CloseDate IS NULL"))!;
            WarehouseRepository.CancelPickingSlip(connection,transaction,partialSlip,"TEST");
            Assert.Equal(1,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND Status='Closed' AND PickedQty=5")));
            Assert.Equal(5m,Convert.ToDecimal(Scalar($"SELECT SUM(Qty) FROM ({WarehouseRepository.LineTransfersInTransitSql}) T WHERE ReqLocation=@Id AND PartNo=@Part")));
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(10m,Convert.ToDecimal(Scalar("SELECT SUM(DemandQty-PickedQty) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND CloseDate IS NULL")));
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(25,Requested());
            transaction.Rollback("CancelPartial");
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(15,Requested());
            Release("C",10);
            Assert.Equal(0,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND CloseDate IS NULL")));
            Assert.Equal(0,Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM dbo.WH_Inventory W WHERE W.LotNo=@Id+'B' AND ({WarehouseRepository.WarehouseBoxPredicate})")));
            Assert.Equal(5,LineStock());
            Assert.Equal(15m,Convert.ToDecimal(Scalar($"SELECT SUM(Qty) FROM ({WarehouseRepository.LineTransfersInTransitSql}) T WHERE ReqLocation=@Id AND PartNo=@Part")));
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(15,Requested());
            foreach(var suffix in new[]{"B","C"})
            {
                using var move=new SqlCommand(PdaWarehouseSql.InboundMove,connection,transaction);
                move.Parameters.AddWithValue("@ReceiveMode","LOCAL");move.Parameters.AddWithValue("@LotBarcode",id+suffix);
                move.Parameters.AddWithValue("@LocationId",wrongDestination?id+"X":suffix=="B"?id:id+"Y");move.Parameters.AddWithValue("@UserId","TEST");
                if(wrongDestination)
                {
                    Assert.Equal(51430,Assert.Throws<SqlException>(()=>move.ExecuteReader()).Number);
                    return;
                }
                using var reader=move.ExecuteReader();while(reader.Read()){}while(reader.NextResult())while(reader.Read()){}
            }
            Assert.Equal(20,LineStock());
            Assert.Equal(0,Convert.ToInt32(Scalar($"SELECT COUNT(*) FROM ({WarehouseRepository.LineTransfersInTransitSql}) T WHERE ReqLocation=@Id")));
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(15,Requested());
            for(var n=1;n<=10;n++)
            {
                using var lot=new SqlCommand("""
                    INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,CreatedBy)
                    OUTPUT INSERTED.LotID VALUES(@Code,@Product,@Id,'INJ',1,1,SYSDATETIME(),'CONFIRMED','TEST');
                    """,connection,transaction);
                lot.Parameters.AddWithValue("@Code",id+"P"+n);lot.Parameters.AddWithValue("@Product",product);lot.Parameters.AddWithValue("@Id",id);
                var lotId=Convert.ToInt32(lot.ExecuteScalar());
                var result=BackflushRepository.Apply(connection,transaction,lotId,id+"P"+n,product,id,"TEST","TEST");
                Assert.Null(result.Error);Assert.Empty(result.Shortages);
                Assert.True(BackflushRepository.Apply(connection,transaction,lotId,id+"P"+n,product,id,"TEST","TEST").Skipped);
                Execute($"UPDATE dbo.PP_WorkOrderRouting SET CompletedQty={n},Status='{(n==10?"Closed":"In Progress")}' WHERE LineID=@Id; UPDATE dbo.PP_WorkOrder SET CompletedQty={n},Status='{(n==10?"Closed":"In Progress")}' WHERE WoNumber=@Id;");
                Assert.Equal(20-2*n,LineStock());
            }
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(25,Requested()); // ten new grams for the standalone minimum, not the completed WO.
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(25,Requested());
            var cancelledSlip=Convert.ToString(Scalar("SELECT TOP(1) PickSlipNo FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND CloseDate IS NULL"))!;
            WarehouseRepository.CancelPickingSlip(connection,transaction,cancelledSlip,"TEST");
            Assert.Equal(0,Convert.ToInt32(Scalar("SELECT COUNT(*) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND CloseDate IS NULL")));
            Assert.Equal(0,LineStock());
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(35,Requested());
            Assert.Equal(10m,Convert.ToDecimal(Scalar("SELECT SUM(DemandQty-PickedQty) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND CloseDate IS NULL")));
            Assert.NotEqual(cancelledSlip,Convert.ToString(Scalar("SELECT TOP(1) PickSlipNo FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND CloseDate IS NULL")));
            WarehouseRepository.GenerateWorkOrderPicking(connection,transaction);
            Assert.Equal(35,Requested());
            Assert.Equal(20m,Convert.ToDecimal(Scalar("SELECT -SUM(QtyChange) FROM dbo.WH_InventoryTransaction WHERE PartNo=@Part AND SourceType='BACKFLUSH'")));
        }
        finally { if(transaction.Connection is not null) transaction.Rollback(); }

        object? Scalar(string sql)
        {
            using var cmd=new SqlCommand(sql,connection,transaction);
            cmd.Parameters.AddWithValue("@Id",id);cmd.Parameters.AddWithValue("@Part",part);
            return cmd.ExecuteScalar();
        }
        void Execute(string sql) => Scalar(sql);
        decimal Requested()=>Convert.ToDecimal(Scalar("SELECT COALESCE(SUM(DemandQty),0) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND ItemNo=@Part"));
        decimal LineStock()=>Convert.ToDecimal(Scalar("SELECT COALESCE(SUM(W.Qty),0) FROM dbo.WH_Inventory W JOIN dbo.MD_Location L ON L.LocationID=W.LocationNo WHERE L.LineID=@Id AND W.PartNo=@Part"));
        void Release(string suffix,decimal qty)
        {
            var pickId=Convert.ToInt32(Scalar("SELECT MIN(PickSlipID) FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND ItemNo=@Part AND CloseDate IS NULL"));
            var slip=Convert.ToString(Scalar("SELECT TOP(1) PickSlipNo FROM dbo.WH_PickSlip WHERE ReqLocation=@Id AND ItemNo=@Part"))!;
            using(var release=new SqlCommand(PdaWarehouseSql.ReleasePickedLot,connection,transaction))
            {
                release.Parameters.AddWithValue("@LineDestination",id);release.Parameters.AddWithValue("@LotNo",id+suffix);
                release.Parameters.AddWithValue("@Qty",qty);release.Parameters.AddWithValue("@ItemNo",part);
                release.Parameters.Add("@LocationID",SqlDbType.VarChar,50).Value=DBNull.Value;
                release.Parameters.AddWithValue("@ReasonCode","RELEASE_PICK");release.Parameters.AddWithValue("@ScheduleID",pickId);
                release.Parameters.AddWithValue("@User","TEST");release.Parameters.AddWithValue("@Slip",slip);
                release.ExecuteNonQuery();
            }
            var source=ReadApi();
            var start=source.IndexOf(";WITH PickCounts AS",StringComparison.Ordinal);
            var sql=source[start..source.IndexOf("\"\"\"",start,StringComparison.Ordinal)];
            using var finish=new SqlCommand(sql,connection,transaction);
            finish.Parameters.AddWithValue("@Slip",slip);finish.Parameters.AddWithValue("@User","TEST");finish.ExecuteNonQuery();
        }
    }

    private static string ReadApi([System.Runtime.CompilerServices.CallerFilePath]string current="")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!,"../../04_Api/AMES.Api/Endpoints/WhEndpoints.cs")));
}
