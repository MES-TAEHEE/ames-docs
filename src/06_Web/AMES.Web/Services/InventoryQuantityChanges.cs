using AMES.Data.Repositories;

namespace AMES.Web.Services;

public static class InventoryQuantityChanges
{
    public sealed record DayRow(DateTime Day,string Source,string Category,string Unit,decimal Opening,decimal Closing,
        decimal Received,decimal Released)
    {
        public decimal Change=>Closing-Opening;
    }

    public static List<DayRow> Build(IEnumerable<RptRepository.InventoryDailyBalance> balances,
        IEnumerable<RptRepository.InventoryEvent> events,DateTime from,DateTime through)
    {
        if(from.Date>through.Date) throw new ArgumentException("Invalid period.");
        var stock=balances.GroupBy(x=>(x.Source,x.Category,x.Unit)).ToDictionary(g=>g.Key,
            g=>g.GroupBy(x=>x.Day.Date).ToDictionary(d=>d.Key,d=>d.Sum(x=>x.Qty)));
        var moves=events.Where(x=>x.Time.Date>=from.Date && x.Time.Date<=through.Date)
            .GroupBy(x=>(x.Source,x.Category,x.Unit,x.Time.Date)).ToDictionary(g=>g.Key,
                g=>(Received:g.Sum(x=>x.Received),Released:g.Sum(x=>x.Issued)));
        var keys=stock.Keys.Concat(moves.Keys.Select(x=>(x.Source,x.Category,x.Unit))).Distinct();
        var result=new List<DayRow>();
        foreach(var key in keys)
        {
            var quantities=stock.GetValueOrDefault(key)??[];
            var opening=quantities.GetValueOrDefault(from.Date.AddDays(-1));
            for(var day=from.Date;day<=through.Date;day=day.AddDays(1))
            {
                var closing=quantities.GetValueOrDefault(day);
                var movement=moves.GetValueOrDefault((key.Source,key.Category,key.Unit,day));
                result.Add(new(day,key.Source,key.Category,key.Unit,opening,closing,movement.Received,movement.Released));
                opening=closing;
            }
        }
        return result;
    }
}
