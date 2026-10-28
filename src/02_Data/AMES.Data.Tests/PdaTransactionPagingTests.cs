using Xunit;

namespace AMES.Data.Tests;

public sealed class PdaTransactionPagingTests
{
    [Fact]
    public void Transactions_use_server_pages_and_lazy_DCR_part_LOT_details()
    {
        var page=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh08TransactionHistory.razor");
        var paging=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh08TransactionHistory.Paging.cs");
        Assert.DoesNotContain("<PdaStockFilter",page);
        Assert.Contains("id=\"wh08-next\"",page);
        Assert.Contains("row.DeliveryId",page);
        Assert.Contains("OpenDcrPart(part)",page);
        Assert.Contains("TransactionPartsAsync(id)",paging);
        Assert.Contains("deliveryId:id,partNo:part.PartNo",paging);
        Assert.Contains("generation!=_pageGeneration",paging);
        Assert.Contains("generation!=_dcrGeneration",paging);
        Assert.Contains("known.Add(x.Id)",paging);
        Assert.Contains("query.Search,query.From,query.To",paging);
        var js=Read("src/05_Pda/AMES.Pda/wwwroot/js/pda-paging.js");
        Assert.Contains("IntersectionObserver",js);
        Assert.Contains("watch.pending",js);
        Assert.Contains("watch.observer.unobserve(element)",js);
        Assert.Contains("observer.disconnect()",js);
    }

    [Fact]
    public void Assy_Ready_shows_pallets_and_only_their_unlocated_contents()
    {
        var page=Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/Fg01QcComplete.razor");
        Assert.Contains("Api.AssyReadyPalletsAsync()",page);
        Assert.Contains("row.PalletNo",page);
        Assert.Contains("foreach(var part in row.Parts)",page);
        Assert.Contains(".SelectMany(row=>row.PalletNo is not null",page);
        Assert.Contains("row.PalletNo ?? row.Lots![0].LotNo",page);
        Assert.DoesNotContain("UNPALLETIZED ASSY",page);
        var sql=Read("src/02_Data/AMES.Data/Repositories/AssyReadyRepository.cs");
        Assert.Contains("PalletContents",sql);
        Assert.Contains("W.Qty>0 AND NULLIF(LTRIM(RTRIM(W.LocationNo)),'') IS NULL",sql);
        Assert.Contains("I.ItemType='ASSY'",sql);
        Assert.DoesNotContain("dbo.PR_ProductionResult",sql);
        Assert.DoesNotContain("dbo.tbl_Lot",sql);
        Assert.Contains("R.Status IN('Open','Inspecting')",sql);
        Assert.Contains("NOT IN('FG_OUTBOUND','FG_DEFECT','INV_DEFECT')",sql);
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../..",path)));
}
