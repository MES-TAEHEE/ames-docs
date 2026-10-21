using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmDeliveryCancellationTests
{
    static ScmRepository Repository()=>new(new AmesConnectionFactory("Server=unused;Database=unused;Integrated Security=True"));
    static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string file="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../..",path)));

    [Fact]
    public void Invalid_requests_fail_before_database_access()
    {
        var version=new Dictionary<string,string>{{"DN-1","0000000000000001"}};
        Assert.Throws<ArgumentException>(()=>Repository().CancelSupplierDeliveryNote("",version,"user","actor"));
        Assert.Throws<ArgumentException>(()=>Repository().CancelSupplierDeliveryNote("DLN-1",new Dictionary<string,string>(),"user","actor"));
        Assert.Throws<ArgumentException>(()=>Repository().CancelSupplierDeliveryNote("DLN-1",new Dictionary<string,string>{{"DN-1","STALE"}},"user","actor"));
        Assert.Throws<ArgumentException>(()=>Repository().CancelSupplierDeliveryNote("DLN-1",version,"", "actor"));
        Assert.Throws<ArgumentException>(()=>Repository().CancelSupplierDeliveryNote("DLN-1",version,"user",new string('a',21)));
        Assert.Throws<ArgumentException>(()=>Repository().CancelSupplierDeliveryNote("DLN-1",new Dictionary<string,string>{{"DN-1","0000000000000001"},{"dn-1","0000000000000002"}},"user","actor"));
    }

    [Fact]
    public void Cancellation_guards_all_note_lines_and_history_before_voiding_labels_without_deleting_history()
    {
        var sql=ScmRepository.CancelSupplierDeliveryNoteSql;
        Assert.Contains("d.Status NOT IN('Registered','Shipped')",sql);
        Assert.Contains("j.[key] COLLATE DATABASE_DEFAULT=d.DeliveryNumber",sql);
        Assert.Contains("j.value COLLATE DATABASE_DEFAULT=CONVERT(varchar(16),CONVERT(binary(8),d.Version),2)",sql);
        Assert.DoesNotContain("CONVERT(varchar(16),d.Version,2)",sql);
        Assert.Contains("This delivery note is already cancelled.",sql);
        Assert.Contains("including partial receipts",sql);
        Assert.True(sql.IndexOf("including partial receipts",StringComparison.Ordinal)<sql.IndexOf("The delivery note changed.",StringComparison.Ordinal));
        Assert.Contains("m.UserID=@U AND m.VendorID=t.VendorID",sql);
        Assert.Contains("l.ReceivedQty>0",sql);
        Assert.Contains("dbo.WH_Inventory w WITH(HOLDLOCK)",sql);
        Assert.Contains("h.SourceType='SCM_DeliveryLine'",sql);
        Assert.Contains("WHERE h.TransactionType='IN'",sql);
        Assert.True(sql.IndexOf("WHERE h.TransactionType='IN'",StringComparison.Ordinal)<sql.IndexOf("UPDATE b SET ActiveFlag=0",StringComparison.Ordinal));
        Assert.Contains("otherTarget.DeliveryID=otherLine.DeliveryID",sql);
        Assert.Contains("Status='Cancelled',ModifiedBy=@Actor,ModifiedUserID=@U",sql);
        Assert.DoesNotMatch(@"(?i)\b(?:DELETE|DROP|TRUNCATE)\b",sql);
        var repository=Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryCancellation.cs");
        Assert.True(repository.IndexOf("LockCaseOrders(c,tx,orders)",StringComparison.Ordinal)<repository.IndexOf("cmd.ExecuteNonQuery();tx.Commit()",StringComparison.Ordinal));
        Assert.Contains("n.NoteNumber=@N OR (x.NoteID IS NULL AND d.DeliveryNumber=@N)",sql);
    }

    [Fact]
    public void Cancelled_notes_remain_grouped_but_label_and_inbound_paths_reject_old_barcodes()
    {
        var batches=Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryNoteBatches.cs");
        Assert.Contains("'Registered','Shipped','Received','Cancelled'",batches);
        Assert.Contains("linked.Status='Cancelled'",batches);
        Assert.Contains("d.Status<>'Cancelled' AND b.ActiveFlag=1",Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryBoxes.cs"));
        Assert.Contains("JOIN dbo.SCM_DeliveryBox b ON b.CaseNo=k.CaseNo AND b.ActiveFlag=1",Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryCases.cs"));
        Assert.Contains("AND D.Status IN ('Shipped', 'Received')",PdaWarehouseSql.InboundReceive);
        Assert.Contains("AND B.ActiveFlag = 1",PdaWarehouseSql.InboundReceive);
        var page=Read("src/06_Web/AMES.Web/Components/Shared/ScmPreview.razor");
        Assert.Contains("Dialog.Confirm(L[\"SCM.Preview.Cancellation.Confirm\"]",page);
        Assert.Contains("Scm.CancelSupplierDeliveryNote(note.Number,versions,_portalUserId!,_actor)",page);
        Assert.Contains("Click=\"@(()=>CancelNote(noteDetail))\"",page);
        Assert.DoesNotContain("@if(CanCancelNote(noteDetail))",page);
        Assert.Contains("Disabled=\"@(_cancelNoteBusy || !CanCancelNote(noteDetail))\"",page);
        Assert.Contains("@CancellationBlockedReason(noteDetail)",page);
        Assert.Contains("SCM.Preview.Cancellation.PartiallyReceived",page);
        Assert.Contains("SCM.Preview.Cancellation.AlreadyCancelled",page);
        Assert.DoesNotContain("SCM.Preview.Cancellation.Recreate",page);
        Assert.DoesNotContain("_creationOrder=note.First.OrderNumber",page);
        Assert.Contains("Store.Deliveries.Where(d=>note.Numbers.Contains(d.Number)).All(d=>d.Received==0)",page);
    }

    [SkippableFact]
    public void Sql_timestamp_hex_matches_the_client_version_without_database_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for a read-only timestamp conversion check.");
        using var c=new AmesConnectionFactory(connection!).OpenConnection();
        using var cmd=new Microsoft.Data.SqlClient.SqlCommand("SELECT TOP(100) Version,CONVERT(varchar(16),CONVERT(binary(8),Version),2) FROM dbo.SCM_Delivery ORDER BY DeliveryID DESC",c);
        using var r=cmd.ExecuteReader();
        while(r.Read())Assert.Equal(Convert.ToHexString((byte[])r[0]),r.GetString(1));
    }
}
