using System.Text;

namespace AMES.Devices;

/// <summary>
/// SEOYON 원본(Injection.cs ZPL 직조립)과 같은 방식으로 태그 ZPL을 만든다.
/// 구성: DataMatrix(내용=LotCode) + 품번/품명/색상·위치/일시/LOT 텍스트.
/// ^CI28 = UTF-8 (Zebra 최신 펌웨어; 품명 한글 대응).
/// </summary>
public static class ZplLabelBuilder
{
    public static string Build(ZplLabel l)
    {
        var sb = new StringBuilder();
        sb.Append("^XA^CI28");
        sb.Append($"^FO30,30^BXN,6,200^FD{Zf(l.LotCode)}^FS");
        sb.Append($"^FO260,30^A0N,28,28^FD{Zf(l.ItemNo)}^FS");
        if (!string.IsNullOrEmpty(l.ItemName))
            sb.Append($"^FO260,70^A0N,24,24^FD{Zf(l.ItemName)}^FS");
        sb.Append($"^FO260,110^A0N,24,24^FD{Zf(l.ColorCode)} {Zf(l.CavityPos)} {Zf(l.LineId)}^FS");
        sb.Append($"^FO260,150^A0N,24,24^FD{l.ProducedAt:yyyy-MM-dd HH:mm}^FS");
        sb.Append($"^FO30,210^A0N,28,28^FD{Zf(l.LotCode)}^FS");
        sb.Append("^XZ");
        return sb.ToString();
    }

    /// <summary>^FD 필드 데이터에서 ZPL 제어문자를 제거한다 (^, ~ 는 필드를 종료/오염시킴).</summary>
    private static string Zf(string? value)
        => value is null ? string.Empty : value.Replace("^", "").Replace("~", "");
}
