namespace AMES.Web;

/// <summary>공통 UI 헬퍼.</summary>
public static class Ui
{
    /// <summary>공정코드(PROCESS)별 pill 색상 클래스.</summary>
    public static string ProcessClass(string? code) => code switch
    {
        "INJ" => "info",
        "IMG" => "warn",
        "PNT" => "bad",
        "QC"  => "ok",
        "FG"  => "ok",
        _     => ""
    };
}
