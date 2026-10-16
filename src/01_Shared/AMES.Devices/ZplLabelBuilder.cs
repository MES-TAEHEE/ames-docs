using System.Globalization;
using System.Text;

namespace AMES.Devices;

/// <summary>
/// SEOYON 원본 태그와 같은 배치를 560×320 dot @ 203dpi(약 70×40mm, 좌측 여백 ^LH10 — 10-16 실제 라벨지에 맞춘 값)에 놓은 레이아웃.
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
        sb.AppendLine("^CI28");
        sb.AppendLine("^PW560");
        sb.AppendLine("^LL320");
        sb.AppendLine("^LH10,0");
        sb.AppendLine($"^FO30,40^BXN,9,200^FD{Zf(l.LotCode)}^FS");
        // Invariant 고정 — 서식의 '/' 는 문화권 날짜구분자로 치환되고(ko → '-'),
        // 비그레고리력 문화권에서는 연도까지 달라진다.
        sb.AppendLine($"^FO190,45^A0N,35,35^FD{l.ProducedAt.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)}^FS");
        sb.AppendLine($"^FO190,105^A0N,60,60^FD{Zf(l.LotCode)}^FS");
        // 색상·호기는 LOT 줄 아래, 캐비티는 일자 줄 오른쪽 — LOT 줄(9자 × 60dot)과 겹치지 않는 자리.
        Field(sb, "^FO190,170^A0N,40,34", l.ColorCode);
        Field(sb, "^FO450,30^A0N,60,48", l.CavityPos);
        Field(sb, "^FO450,170^A0N,60,48", l.PressType);
        sb.AppendLine($"^FO30,235^A0N,70,70^FD{Zf(l.ItemNo)}^FS");
        sb.AppendLine("^PQ1");
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
