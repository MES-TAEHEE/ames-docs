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

/// <summary>교대 1개의 가동 시간(h) — AMES 경로: 라인 APS 패턴에서 그 교대의 OPERATING 세그먼트 합, WORK_SHIFT SortOrder 순. 골든 픽스처에는 없다(null → Day/Night).</summary>
public sealed record ShiftHours(string Code, double Hours);
/// <summary>교대 1개의 계획 수량 — PlanDay/PlanNight 는 이 목록의 파생값(첫 교대 / 나머지 합, ApsShiftCompat.Derive).</summary>
public sealed record ShiftQty(string Code, double Qty);

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
    /// <summary>교대 목록 모드(스펙 §1). null 이면 Day/Night 를 [day, night] 두 교대로 읽는다 — 골든 경로. 있으면 Day/Night 는 파생값.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ShiftHours>? Shifts { get; set; }
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
    /// <summary>교대 목록 모드의 예외 날짜 교대 시간. null 이면 Day/Night 두 교대.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ShiftHours>? Shifts { get; set; }
    public string? Note { get; set; }
}
