using AMES.Api.Auth;
using AMES.Api.Logging;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static partial class FgEndpoints
{
    private static void MapPutAwayBatch(RouteGroupBuilder group,AmesConnectionFactory factory)
    {
        group.MapPost("/putaway/batch",(HttpContext context,List<PutAwayConfirmReq> items)=>
        {
            if(context.GetSession() is not {} session)return Results.Unauthorized();
            using var connection=factory.OpenConnection();
            using var transaction=connection.BeginTransaction();
            try
            {
                var result=SavePutAwayBatch(connection,transaction,items,session.EmployeeNo);
                transaction.Commit();
                WarehouseOperationLogger.TryWrite(factory,context,WarehouseOperationLogger.FromSession(
                    session,"FG_PUTAWAY","FG002","BATCH",items[0].Barcode,"SUCCESS",result.Message,locationId:items[0].LocationId));
                return Results.Ok(result);
            }
            catch(Exception ex) when(ex is InvalidOperationException or ArgumentException || ex is SqlException sql && sql.Number is >=52510 and <=52518 or 52614)
            {
                if(transaction.Connection is not null)transaction.Rollback();
                return Results.BadRequest(new PutAwayResult(false,ex.Message,null,null,null));
            }
        });
    }

    internal static PutAwayResult SavePutAwayBatch(SqlConnection connection,SqlTransaction transaction,
        IReadOnlyList<PutAwayConfirmReq> requests,string actor)
    {
        if(requests.Count is <1 or >200 || requests.Any(r=>r is null || string.IsNullOrWhiteSpace(r.Barcode) || r.Barcode.Length>50
            || string.IsNullOrWhiteSpace(r.LocationId) || r.LocationId.Length>50))
            throw new ArgumentException("Scan 1-200 stock barcodes and one destination location.");
        if(requests.Select(r=>r.Barcode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=requests.Count)
            throw new ArgumentException("The same stock barcode was selected more than once.");
        var destination=ParseFgBarcode(requests[0].LocationId).Value;
        if(requests.Any(r=>!string.Equals(ParseFgBarcode(r.LocationId).Value,destination,StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("All items must use the same destination location.");
        var groups=new List<(PutAwayScanRow Row,Dictionary<string,decimal>? Expected)>();
        var lots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var request in requests.OrderBy(r=>r.Barcode,StringComparer.OrdinalIgnoreCase))
        {
            var parsed=ParseFgBarcode(request.Barcode);
            if(parsed.Kind==BarcodeWo)throw new InvalidOperationException("Scan a LOT or pallet, not a work order.");
            if(!AcquirePutAwayLock(connection,transaction,"LOT",parsed.Value))
                throw new InvalidOperationException("Selected FG stock is being processed. Scan it again.");
            var row=FindPutAwayScanRow(connection,transaction,parsed.Value)
                ?? throw new InvalidOperationException($"{request.Barcode}: FG stock was not found.");
            if(!row.IsProductionCompleted || row.AlreadyStocked || row.Qty<=0)
                throw new InvalidOperationException($"{row.LotNo}: {row.Message}");
            var contents=row.Items ?? [row];
            if(!MatchesPutAwayContents(contents.Select(i=>(i.LotNo,i.Qty)).ToList(),request.ExpectedItems))
                throw new InvalidOperationException($"{row.LotNo}: contents or quantities changed. Scan the stock again.");
            if(contents.Any(item=>!lots.Add(item.LotNo)))
                throw new InvalidOperationException("A LOT was selected both individually and inside a pallet. Nothing was moved.");
            if(lots.Count>1000)throw new InvalidOperationException("Select at most 1,000 LOTs per move.");
            groups.Add((row,request.ExpectedItems));
        }
        if(groups.Select(g=>g.Row.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1)
            throw new InvalidOperationException("All ASSY stock must use the same quantity unit.");
        if(!AcquirePutAwayLock(connection,transaction,"LOCATION",destination))
            throw new InvalidOperationException("This FG location is being updated. Scan it again.");
        var first=groups[0].Row;
        var location=ValidatePutAwayLocation(connection,transaction,requests[0].LocationId,first.ItemNo,first.CustomerCode,
            groups.Sum(g=>g.Row.Qty),BarcodeLocation);
        if(location is not {IsValid:true})throw new InvalidOperationException(location?.Message ?? "FG location was not found.");
        foreach(var group in groups)SavePutAwayUnit(connection,transaction,group.Row,location.LocationId,actor,group.Expected);
        return new(true,$"{lots.Count} ASSY LOT(s) were assigned successfully.",null,null,location);
    }
}
