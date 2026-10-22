using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmSupplierDeliveryProgressTests
{
    static SupplierDeliveryLineDto Line(int id,decimal qty,decimal received,bool shipped=true,string unit="EA")
        => new(id,"DN-1","PO-1","PART-1","Part",unit,qty,received,shipped,[]);
    static SupplierDeliveryProgressDto Document(params SupplierDeliveryLineDto[] lines)
        => new("DLN-1","V1","Supplier",new(2026,10,21),new(2026,10,20),lines.ToList());

    [Theory]
    [InlineData(0,"OnTheWay",false)]
    [InlineData(5,"PartiallyReceived",false)]
    [InlineData(10,"Received",true)]
    public void Receipt_status_uses_actual_delivery_line_receipts(int received,string status,bool complete)
    {
        var document=Document(Line(1,10,received));
        Assert.Equal(status,document.Status);
        Assert.Equal(complete,document.Complete);
        Assert.Equal(10-received,document.Lines[0].Remaining);
    }

    [Fact]
    public void Receiving_one_of_several_parts_does_not_complete_the_note_or_mix_uoms()
    {
        var document=Document(Line(1,700,700,unit:"KG"),Line(2,50,0));
        Assert.Equal("PartiallyReceived",document.Status);
        Assert.False(document.Complete);
        Assert.Equal(2,document.Lines.Select(l=>l.Unit).Distinct().Count());
        Assert.Equal("Registered",Document(Line(1,10,0,false)).Status);
    }

    [Fact]
    public void Unit_receipts_remain_distinct_even_for_the_same_part()
    {
        var line=Line(1,1400,700,unit:"KG");
        line.Items.AddRange([new("UIT-1","SHU-1",700,true),new("UIT-2","SHU-1",700,false)]);
        var document=Document(line);
        Assert.Equal(2,document.UnitItems);
        Assert.Equal(1,document.ReceivedItems);
    }

    [Fact]
    public void Detail_lists_unit_items_directly_and_highlights_their_own_receipt_state()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Scm/DeliveryProgress.razor");
        Assert.Contains("TItem=\"UnitItemRow\" Data=\"@UnitItems(selected)\"",page);
        Assert.Contains(".SelectMany(line=>line.Items.Select(item=>new UnitItemRow(line,item)))",page);
        Assert.DoesNotContain("Data=\"selected.Lines\"",page);
        Assert.DoesNotContain("<Template Context=\"line\">",page);
        Assert.Contains("if(args.Data?.Item.Received==true)args.Attributes[\"class\"]=\"scm-received-row\"",page);
        Assert.Contains("row.Item.Received?\"check_circle\":\"schedule\"",page);
        Assert.Contains("row.Item.Quantity,row.Line.Unit",page);
        Assert.Contains("if(args.Data?.Complete==true)args.Attributes[\"class\"]=\"scm-received-row\"",page);
        var css=Read("src/06_Web/AMES.Web/Components/Pages/Scm/DeliveryProgress.razor.css");
        Assert.Contains("tr.scm-received-row > td",css);
        Assert.Contains("var(--ames-tx-green-300)",css);
        Assert.Contains("background:rgba(34,197,94,.16)",css);
    }

    [Fact]
    public void Invalid_searches_are_rejected_before_database_access()
    {
        var repo=new ScmRepository(new AmesConnectionFactory("Server=unused;Database=unused;Integrated Security=True"));
        Assert.Throws<ArgumentException>(()=>repo.ListSupplierDeliveryProgress(new(2026,10,22),new(2026,10,21)));
        Assert.Throws<ArgumentException>(()=>repo.ListSupplierDeliveryProgress(search:new string('x',121)));
    }

    [SkippableFact]
    public void Existing_delivery_progress_reads_without_database_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only verification.");
        var repo=new ScmRepository(new AmesConnectionFactory(connection!));
        var all=repo.ListSupplierDeliveryProgress(includeReceived:true);
        Assert.Equal(all.Count,all.Select(d=>d.Number).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(all,d=>{
            Assert.NotEmpty(d.Lines);
            Assert.Equal(d.Lines.Count,d.Lines.Select(l=>l.Id).Distinct().Count());
            var labels=d.Lines.SelectMany(l=>l.Items).ToList();
            Assert.Equal(labels.Count,labels.Select(i=>i.Barcode).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
        Assert.All(repo.ListSupplierDeliveryProgress(shippedOnly:true),d=>{
            Assert.False(d.Complete);
            Assert.All(d.Lines,l=>Assert.True(l.Shipped));
        });
        if(all.Count>0)Assert.Contains(repo.ListSupplierDeliveryProgress(search:all[0].Number,includeReceived:true),d=>d.Number==all[0].Number);
    }

    [SkippableFact]
    public void Pda_document_receipt_counts_match_the_delivery_schedule_without_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only document verification.");
        var factory=new AmesConnectionFactory(connection!);
        var documents=new ScmRepository(factory).ListSupplierDeliveryProgress(includeReceived:true,shippedOnly:true);
        using var c=factory.OpenConnection();
        var checkedDocuments=0;
        foreach(var document in documents.Where(d=>d.UnitItems>0))
        {
            using(var known=new SqlCommand("SELECT COUNT(*) FROM dbo.SCM_DeliveryNote WHERE NoteNumber=@N",c))
            {known.Parameters.Add("@N",SqlDbType.VarChar,50).Value=document.Number;if((int)known.ExecuteScalar()! == 0)continue;}
            using var cmd=new SqlCommand(PdaWarehouseSql.InboundDocument,c);
            cmd.Parameters.Add("@ReceiveMode",SqlDbType.NVarChar,10).Value="LOCAL";
            cmd.Parameters.Add("@DocumentBarcode",SqlDbType.NVarChar,50).Value=document.Number;
            using var reader=cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(document.UnitItems,reader.GetInt32(reader.GetOrdinal("TOTAL_BOXES")));
            Assert.Equal(document.ReceivedItems,reader.GetInt32(reader.GetOrdinal("SCANNED_BOXES")));
            checkedDocuments++;
        }
        Assert.True(checkedDocuments>0 || documents.All(d=>d.UnitItems==0));
    }

    static string Read(string relative,[System.Runtime.CompilerServices.CallerFilePath]string file="")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../..",relative)));

    [Fact]
    public void Progress_is_read_only_and_keeps_unit_receipts_after_stock_is_consumed()
    {
        var query=Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryProgress.cs");
        Assert.Contains("l.Quantity,l.ReceivedQty",query);
        Assert.Contains("t.ReasonCode='INBOUND_CANCEL'",query);
        Assert.Contains("ORDER BY t.TransactionID DESC",query);
        Assert.DoesNotContain("W.Qty > 0",query);
        Assert.DoesNotContain("W.Qty > 0",PdaWarehouseSql.InboundDocument);
        Assert.Contains("T.ReasonCode='INBOUND_CANCEL'",PdaWarehouseSql.InboundDocument);
        Assert.DoesNotMatch(@"(?i)\b(?:INSERT|UPDATE|DELETE|MERGE)\s+dbo\.",query);
        var api=Read("src/04_Api/AMES.Api/Endpoints/SupplierDeliveryEndpoints.cs");
        Assert.Contains("context.GetSession() is null",api);
        Assert.Contains("shippedOnly: true",api);
        Assert.DoesNotContain("ReceiveDeliveryCase",api);
    }

    [Fact]
    public void Pda_lists_only_note_titles_and_opens_the_selected_note_in_a_scrollable_dialog()
    {
        var page=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh01DeliverySchedule.razor");
        var list=page[page.IndexOf("<div class=\"supplier-delivery-list\">",StringComparison.Ordinal)..page.IndexOf("@if (!_busy",StringComparison.Ordinal)];
        Assert.Contains("Text=\"@document.Number\"",list);
        Assert.Contains("Click=\"() => OpenDetail(document)\"",list);
        Assert.DoesNotContain("Vendor",list);
        Assert.DoesNotContain(".Lines",list);
        Assert.Contains("@if (_selected is {} selected)",page);
        Assert.Contains("role=\"dialog\" aria-modal=\"true\"",page);
        Assert.Contains("@foreach (var line in selected.Lines)",page);
        Assert.Contains("@foreach (var item in line.Items)",page);
        Assert.Contains("void OpenDetail(SupplierDeliveryProgressDto document) => _selected = document",page);
        Assert.Contains("void CloseDetail() => _selected = null",page);
        Assert.Contains("@onclick:stopPropagation=\"true\"",page);
        Assert.Contains("wh08-detail-rows",page);
        Assert.Contains("OpenInbound(selected.Number)",page);
        Assert.DoesNotContain("Toggle(",page);
    }

    [Fact]
    public void Schedule_opens_document_viewer_but_does_not_receive_on_click()
    {
        var schedule=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh01DeliverySchedule.razor");
        Assert.Contains("SUPPLIER DELIVERIES",schedule);
        Assert.Contains(">Supplier Deliveries</div>",Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/WhHome.razor"));
        Assert.Contains("/wh/02?delivery=",schedule);
        Assert.DoesNotContain("WhReceive",schedule);
        var inbound=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh02PdaInbound.razor");
        Assert.Contains("SupplyParameterFromQuery(Name = \"delivery\")",inbound);
        Assert.Contains("Physical unit scans still control receipt",inbound);
        Assert.Contains("!IsCaseDocument && !IsScheduledDelivery",inbound);
        Assert.Contains("_document is not null && !IsScheduledDelivery",inbound);
        Assert.Contains("This box does not belong to the scanned document.",inbound);
        var labels=Read("src/06_Web/AMES.Web/Components/Shared/BoxLabelSheet.razor");
        Assert.Contains("Code128Barcode Value=\"@b.Number\"",labels);
        Assert.Contains("Code128Barcode Value=\"@b.Item\"",labels);
    }

    [Fact]
    public void Screen_renumber_preserves_routes_and_role_grants()
    {
        var migration=Read("dist/migrate_scm_delivery_progress_screen.sql");
        Assert.Contains("m.TempCode",migration);
        Assert.Contains("dbo.SYS_RolePermission p JOIN @Move",migration);
        Assert.Contains("'scm/delivery-progress','SCM-003'",migration);
        Assert.Contains("'scm/purchase-items','SCM-004'",migration);
        Assert.Contains("'scm/portal-users','SCM-005'",migration);
        Assert.DoesNotContain("DELETE",migration);
        Assert.DoesNotContain("DROP",migration);
        Assert.Contains("migrate_scm_delivery_progress_screen.sql",Read("dist/rebuild_db.sh"));
    }
}
