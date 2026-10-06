using System.Text.Json.Serialization;

namespace AMES.Data.Aps.Contracts;

/// 필드명은 원본 /api/settings 응답(docs/reference/aps-new-ui/api/settings.json)과 같다.
public sealed class Settings
{
    public double DefaultDaysOfCover { get; set; } = 0.7;
    public List<CoverTier> CoverTiers { get; set; } = new();
    public int RoundTo { get; set; } = 5;
    public List<PackRule> PackRules { get; set; } = new();
    public List<UphRule> UphRules { get; set; } = new();
    public StageDefaults StageDefaults { get; set; } = new();
    public List<LineStage> LineStages { get; set; } = new();
    public Shift Shift { get; set; } = new();
    public double DailyCapacity { get; set; }
    public List<LineShift> LineShifts { get; set; } = new();
    public List<ShiftException> ShiftExceptions { get; set; } = new();
    public bool TrimLastDay { get; set; } = true;
    public DateTimeOffset? SavedAt { get; set; }

    public static Settings Default() => new()
    {
        CoverTiers = { new(100, 0.4), new(20, 0.7), new(0, 1.5) },
        Shift = new Shift { Day = 10.5, Night = 11.5 },
    };
}

public sealed record CoverTier(double MinDailyDemand, double DaysOfCover);
public sealed record PackRule(string Prefix, int PackSize);

public sealed class UphRule
{
    public string Prefix { get; set; } = "";
    public double Uph { get; set; }
    public int? Cavity { get; set; }
    public double? CycleTime { get; set; }
    public int? PackSize { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MoldGroup { get; set; }   // 원본은 null 이면 생략
    public string CleanPrefix => Prefix.Replace("%", "").Replace("*", "").Replace(" ", "");
}

public sealed class StageDefaults
{
    public StageDefault Injection { get; set; } = new();
    public StageDefault Assembly { get; set; } = new();
}
public sealed class StageDefault { public int OffsetDays { get; set; } public bool UseStock { get; set; } = true; }

public sealed class LineStage
{
    public string? RootLine { get; set; }
    public string LineCd { get; set; } = "";
    public int OffsetDays { get; set; }
    public bool UseStock { get; set; } = true;
    public int BatchDays { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; set; }   // 원본은 null 이면 생략
}

public sealed class Shift
{
    public double Day { get; set; } = 10.5;
    public double Night { get; set; } = 11.5;
    public double Total => Day + Night;
}

public sealed class LineShift
{
    public string LineCd { get; set; } = "";
    public double Day { get; set; }
    public double Night { get; set; }
    public double? Uph { get; set; }
    public int Stations { get; set; } = 1;
    public string? Note { get; set; }
    public int? DailyCap { get; set; }   // AMES 완제품 라인 MD_Line.DailyCap — 값이 있으면 AsmCapFor 가 이 값을 쓴다. 픽스처에는 없음(null → 종전 경로)
}

public sealed class ShiftException
{
    public string Date { get; set; } = "";
    public string? LineCd { get; set; }
    public double Day { get; set; }
    public double Night { get; set; }
    public string? Note { get; set; }
}
