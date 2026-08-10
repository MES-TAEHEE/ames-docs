using System.Globalization;
using System.Text;

namespace AMES.Devices;

/// <summary>
/// SEOYON 원본 태그와 동일한 2"×1"(406×203 dot @ 203dpi) 레이아웃.
/// 좌측 DataMatrix(내용=LotCode) · 중앙 일자/LOT/색상 3줄 ·
/// 우측 캐비티(LH/RH)·호기(1~5/M) 대문자 2줄 · 하단 품번(최대 폰트).
/// ^CI28 = UTF-8 (Zebra 최신 펌웨어).
/// </summary>
public static class ZplLabelBuilder
{
    public static string Build(ZplLabel l)
    {
        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine("^PW406");
        sb.AppendLine("^LL203");
        sb.AppendLine("^LH0,0");
        sb.AppendLine("^CI28");
        sb.AppendLine($"^FO20,10^BXN,5,200^FD{Zf(l.LotCode)}^FS");
        // Invariant 고정 — 서식의 '/' 는 문화권 날짜구분자로 치환되고(ko → '-'),
        // 비그레고리력 문화권에서는 연도까지 달라진다.
        sb.AppendLine($"^FO140,14^A0N,30,27^FD{l.ProducedAt.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)}^FS");
        sb.AppendLine($"^FO140,49^A0N,28,24^FD{Zf(l.LotCode)}^FS");
        Field(sb, "^FO140,82^A0N,31,27", l.ColorCode);
        Field(sb, "^FO320,14^A0N,43,34", l.CavityPos);
        Field(sb, "^FO320,73^A0N,43,34", l.PressType);
        sb.AppendLine($"^FO20,138^A0N,50,39^FD{Zf(l.ItemNo)}^FS");
        sb.Append("^XZ");
        return sb.ToString();
    }

    private static void Field(StringBuilder sb, string origin, string? value)
    {
        var v = Zf(value);
        if (v.Length > 0) sb.AppendLine($"{origin}^FD{v}^FS");
    }

    /// <summary>^FD 필드 데이터에서 ZPL 제어문자를 제거한다 (^, ~ 는 필드를 종료/오염시킴).</summary>
    private static string Zf(string? value)
        => value is null ? string.Empty : value.Replace("^", "").Replace("~", "");
}
