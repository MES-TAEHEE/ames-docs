using System.Text.Json.Serialization;

namespace AMES.Data.Aps.Contracts;

/// 원본 /api/lines 의 lines[] 한 줄. tonnage 는 값이 있을 때만 필드가 나온다(원본과 같게).
public sealed class LineInfo
{
    public string LineCd { get; set; } = "";
    public string? LineName { get; set; }
    public string Type { get; set; } = "assembly";
    public double Uph { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Tonnage { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MipDiv { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PrdtDiv { get; set; }   // 원본은 PD10 처럼 null 이면 생략
    public bool Active { get; set; } = true;
}

public sealed class LinesResponse
{
    public List<LineInfo> Lines { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
