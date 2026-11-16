using System.Globalization;

namespace AMES.Data.Services;

public static class PurchaseOrderExcel
{
    public sealed record Line(string PartNo, decimal Quantity);
    public sealed record Result(IReadOnlyList<Line> Lines, IReadOnlyList<MasterExcel.Problem> Problems);

    public static Result Parse(IReadOnlyList<string[]> rows, IEnumerable<string> allowedParts)
    {
        var problems=new List<MasterExcel.Problem>();
        var lines=new List<Line>();
        if(rows.Count==0) return new([], [new(1,null,MasterExcel.Codes.NoHeader)]);
        var header=rows[0].Select(s=>s.Trim()).ToList();
        int Col(string name)=>header.FindIndex(s=>s.Equals(name,StringComparison.OrdinalIgnoreCase));
        int part=Col("PartNo"),qty=Col("Quantity");
        if(part<0 || qty<0) return new([], [new(1,"PartNo, Quantity",MasterExcel.Codes.NoHeader)]);
        var allowed=allowedParts.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p=>p,p=>p,StringComparer.OrdinalIgnoreCase);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(rows.Count>1001) return new([], [new(1,null,MasterExcel.Codes.OutOfRange,"1000")]);
        for(int i=1;i<rows.Count;i++)
        {
            var row=rows[i];
            if(row.All(string.IsNullOrWhiteSpace)) continue;
            var item=part<row.Length ? row[part].Trim() : "";
            var text=qty<row.Length ? row[qty].Trim() : "";
            if(!allowed.TryGetValue(item,out var canonical)) problems.Add(new(i+1,"PartNo",MasterExcel.Codes.Unknown,item));
            else if(!seen.Add(item)) problems.Add(new(i+1,"PartNo",MasterExcel.Codes.Duplicate,item));
            else if(!decimal.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value) || value<=0 || value>999999999.999m || decimal.Round(value,3)!=value)
                problems.Add(new(i+1,"Quantity",MasterExcel.Codes.BadNumber,text));
            else lines.Add(new(canonical,value));
        }
        return new(problems.Count==0 ? lines : [],problems);
    }
}
