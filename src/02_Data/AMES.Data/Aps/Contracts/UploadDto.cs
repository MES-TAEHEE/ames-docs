namespace AMES.Data.Aps.Contracts;

/// 필드명은 원본 /api/upload/current 응답(docs/reference/aps-new-ui/api/upload_current.json)과 같다.
public sealed class FileMeta
{
    public string FileName { get; set; } = "";
    public string? Plant { get; set; }
    public string? Customer { get; set; }
    public string Kind { get; set; } = "unknown";
    public string? StdDate { get; set; }
    public string? Seq { get; set; }
    public string? Vendor { get; set; }
    public bool Recognized { get; set; }
}

public sealed class DemandRow
{
    public string? Plant { get; set; }
    public string? Model { get; set; }
    public string? Pgn { get; set; }
    public string? Pac { get; set; }
    public string Material { get; set; } = "";
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public string? Vendor { get; set; }
    public string? Line { get; set; }
    public double PrevDay { get; set; }
    public double Mitu { get; set; }
    public double SumD0 { get; set; }
    public double Wbs { get; set; }
    public double Prj { get; set; }
    public double Pbs { get; set; }
    public Dictionary<string, double> Buckets { get; set; } = new();
}

public sealed class DemandFile
{
    public FileMeta Meta { get; set; } = new();
    public long SizeBytes { get; set; }
    public string? SheetName { get; set; }
    public List<string> BucketKeys { get; set; } = new();
    public List<DemandRow> Rows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int RowCount => Rows.Count;
    public double TotalQty => Rows.Sum(r => r.Buckets.Values.Sum());
}

public sealed class UploadSet
{
    public string Id { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }
    public string? StdDate { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<DemandFile> Files { get; set; } = new();
    public DemandFile? Daily { get; set; }
    public DemandFile? Hourly { get; set; }
    public DemandFile? Weekly { get; set; }
}
