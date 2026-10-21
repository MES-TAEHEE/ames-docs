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
        Assert.DoesNotMatch(@"(?i)\b(?:INSERT|UPDATE|DELETE|MERGE)\s+dbo\.",query);
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
