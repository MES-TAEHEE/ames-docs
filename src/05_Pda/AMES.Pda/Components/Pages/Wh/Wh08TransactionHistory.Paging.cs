using AMES.Contracts.Dto;
using AMES.Pda.Services;
using Microsoft.JSInterop;

namespace AMES.Pda.Components.Pages.Wh;

public partial class Wh08TransactionHistory
{
    private IJSObjectReference? _pagingModule;
    private PdaTransactionPageDto? _feedPage,_dcrLotsPage;
    private sealed record FeedQuery(string Search,DateTime? From,DateTime? To,string Worker,string Reason,string Direction);
    private FeedQuery? _feedQuery;
    private bool _hasMore,_loadingMore,_dcrLoading,_dcrLotsMore,_dcrLotsBusy;
    private string _moreError="",_dcrError="",_dcrLotsError="";
    private int _pageGeneration,_dcrGeneration;
    private PdaApi.WarehouseTransactionRow? _dcrRow;
    private PdaTransactionPartDto? _dcrPart;
    private List<PdaTransactionPartDto> _dcrParts=[];
    private List<PdaTransactionRowDto> _dcrLots=[];

    private async Task WatchTransactionEndAsync()
    {
        if(IsSpareParts||_scannerRef is null)return;
        try
        {
            _pagingModule??=await JS.InvokeAsync<IJSObjectReference>("import","./js/pda-paging.js");
            if(!_loading && _hasMore && _dcrRow is null && string.IsNullOrEmpty(_moreError))
                await _pagingModule.InvokeVoidAsync("observe","wh08-next",_scannerRef,nameof(LoadMoreTransactions));
            if(_dcrLotsMore && !_dcrLoading && string.IsNullOrEmpty(_dcrLotsError))
                await _pagingModule.InvokeVoidAsync("observe","wh08-lots-next",_scannerRef,nameof(LoadMoreDcrLots));
        }
        catch(JSDisconnectedException){}
    }

    [JSInvokable]
    public async Task<bool> LoadMoreTransactions()
    {
        if(_loading||_loadingMore||!_hasMore||_feedPage is null||_feedQuery is null||_dcrRow is not null||_editingFilters||_editingBarcode)return false;
        var generation=_pageGeneration;var page=_feedPage;var query=_feedQuery;
        _loadingMore=true;_moreError="";await InvokeAsync(StateHasChanged);
        try
        {
            var next=await Api.TransactionPageAsync(query.Search,query.From,query.To,query.Worker,query.Reason,query.Direction,page.BeforeAt,page.BeforeId);
            if(generation!=_pageGeneration)return false;
            var known=_rows.Select(x=>x.RowNo).ToHashSet();
            _rows.AddRange(next.Items.Where(x=>known.Add(x.Id)).Select(ToHistoryRow));
            _feedPage=next;_hasMore=next.HasMore;return _hasMore;
        }
        catch(Exception ex){if(generation==_pageGeneration)_moreError=ex.Message;return false;}
        finally{_loadingMore=false;await InvokeAsync(StateHasChanged);}
    }

    private async Task RetryMore(){await LoadMoreTransactions();}
    private async Task OpenTransaction(PdaApi.WarehouseTransactionRow row)
    {
        if(row.DeliveryId is not {} id){OpenSpDetail(row);return;}
        var generation=++_dcrGeneration;
        _dcrRow=row;_dcrPart=null;_dcrParts=[];_dcrLots=[];_dcrError="";_dcrLoading=true;
        try{var parts=await Api.TransactionPartsAsync(id);if(generation==_dcrGeneration)_dcrParts=parts;}
        catch(Exception ex){if(generation==_dcrGeneration)_dcrError=ex.Message;}
        finally{if(generation==_dcrGeneration)_dcrLoading=false;}
    }
    private void CloseDcr(){_dcrGeneration++;_dcrRow=null;_dcrPart=null;_dcrLotsMore=false;_dcrLoading=false;}
    private void BackToDcrParts(){_dcrGeneration++;_dcrPart=null;_dcrLoading=false;_dcrError="";_dcrLotsMore=false;}
    private async Task OpenDcrPart(PdaTransactionPartDto part)
    {
        if(_dcrRow?.DeliveryId is not {} id)return;
        var generation=++_dcrGeneration;
        _dcrPart=part;_dcrLots=[];_dcrError="";_dcrLotsError="";_dcrLotsMore=false;_dcrLoading=true;
        try
        {
            var page=await Api.TransactionPageAsync(null,null,null,
                deliveryId:id,partNo:part.PartNo);
            if(generation!=_dcrGeneration)return;
            _dcrLotsPage=page;_dcrLots=page.Items;_dcrLotsMore=page.HasMore;
        }
        catch(Exception ex){if(generation==_dcrGeneration)_dcrError=ex.Message;}
        finally{if(generation==_dcrGeneration)_dcrLoading=false;}
    }
    [JSInvokable]
    public async Task<bool> LoadMoreDcrLots()
    {
        if(_dcrLoading||_dcrLotsBusy||!_dcrLotsMore||_dcrLotsPage is null||_dcrRow?.DeliveryId is not {} id||_dcrPart is null)return false;
        var generation=_dcrGeneration;var part=_dcrPart;var page=_dcrLotsPage;
        _dcrLotsBusy=true;_dcrLotsError="";
        await InvokeAsync(StateHasChanged);
        try
        {
            var next=await Api.TransactionPageAsync(null,null,null,
                beforeAt:page.BeforeAt,beforeId:page.BeforeId,deliveryId:id,partNo:part.PartNo);
            if(generation!=_dcrGeneration)return false;
            var known=_dcrLots.Select(x=>x.Id).ToHashSet();
            _dcrLots.AddRange(next.Items.Where(x=>known.Add(x.Id)));_dcrLotsPage=next;_dcrLotsMore=next.HasMore;
            return _dcrLotsMore;
        }
        catch(Exception ex){if(generation==_dcrGeneration)_dcrLotsError=ex.Message;return false;}
        finally{_dcrLotsBusy=false;await InvokeAsync(StateHasChanged);}
    }
    private async Task RetryDcrLots(){await LoadMoreDcrLots();}

    private static PdaApi.WarehouseTransactionRow ToHistoryRow(PdaTransactionRowDto row)=>new(row.Id,row.Barcode,row.PartNo,
        row.OccurredAt.ToString("yyyy-MM-dd"),row.OccurredAt.ToString("HH:mm:ss"),row.LocationNo,row.Qty,
        row.Direction switch{"IN"=>"In","OUT"=>"Out","MOVE"=>"Location Change","RETURN"=>"Return",_=>"Adjust"},
        row.Direction,row.WorkerId,row.ReasonCode,row.Note,null,row.BeforeQty,row.DeltaQty,row.AfterQty,null,null,
        row.BeforeLocation,row.AfterLocation,"WH_InventoryTransaction",row.Note,row.Unit,row.DeliveryId,row.PartCount);
}
