using System.Globalization;
using System.Text;

namespace AMES.Devices;

/// <summary>
/// IMG 완제품 라벨 2"×1"(406×203 dot @ 203dpi).
/// 좌측 DataMatrix 는 고객 표준(ISO 15434 / MH10.8.2) 포맷:
///   [)> RS 06 GS V{수주처코드} GS P{품번(하이픈 제거)} GS S{PGN+ALC} GS T{yyMMdd}{part4M}{LotNo} GS E GS C: RS EOT
/// 제어문자는 ^FH_ 로 16진 이스케이프(_1E RS · _1D GS · _04 EOT)한다 — 없으면 글자 그대로 찍힌다.
/// 우측 글자: ALC(대) · 장착위치 · 발행일 · 품번 · LotNo. 우측 하단 한 칸(샘플의 'D')은 아직 정의 전이라 비운다.
/// </summary>
public static class ImgLabelBuilder
{
    const string RS = "_1E", GS = "_1D", EOT = "_04";

    public static string Build(ImgLabel l)
    {
        var itemNoFlat = Zf(l.ItemNo).Replace("-", "");
        var dm = new StringBuilder()
            .Append("[)>").Append(RS).Append("06")
            .Append(GS).Append('V').Append(Zf(l.CustomerCode))
            .Append(GS).Append('P').Append(itemNoFlat)
            .Append(GS).Append('S').Append(Zf(l.Pgn)).Append(Zf(l.Alc))
            .Append(GS).Append('T')
                .Append(l.IssuedAt.ToString("yyMMdd", CultureInfo.InvariantCulture))
                .Append(Part4M(l.ItemNo, l.ShiftLetter))
                .Append(Zf(l.LotCode))
            .Append(GS).Append('E')
            .Append(GS).Append("C:")
            .Append(RS).Append(EOT)
            .ToString();

        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine("^PW406");
        sb.AppendLine("^LL203");
        sb.AppendLine("^LH0,0");
        sb.AppendLine("^CI28");
        sb.AppendLine($"^FO18,34^BXN,4,200^FH_^FD{dm}^FS");
        Field(sb, "^FO160,25^A0N,62,48", l.Alc);
        Field(sb, "^FO280,25^A0N,40,32", l.MountPos);
        // Invariant 고정 — 서식의 '/' 는 문화권 날짜구분자로 치환된다(ko → '-').
        sb.AppendLine($"^FO160,80^A0N,29,23^FD{l.IssuedAt.ToString("M/d/yyyy", CultureInfo.InvariantCulture)}^FS");
        sb.AppendLine($"^FO160,110^A0N,39,28^FD{Zf(l.ItemNo)}^FS");
        sb.AppendLine($"^FO160,150^A0N,39,28^FD{Zf(l.LotCode)}^FS");
        sb.Append("^XZ");
        return sb.ToString();
    }

    /// <summary>
    /// part4M = "1" + 하이픈 뺀 품번의 6·7번째 글자 + 교대 글자(A/B/C).
    /// 예) 83345-P8000RBQ · A → "1P8A". 품번이 짧으면 있는 글자까지만 쓴다.
    /// </summary>
    public static string Part4M(string itemNo, string shiftLetter)
    {
        var flat = Zf(itemNo).Replace("-", "");
        var mid  = flat.Length >= 7 ? flat.Substring(5, 2)
                 : flat.Length == 6 ? flat.Substring(5, 1)
                 : string.Empty;
        return "1" + mid + Zf(shiftLetter);
    }

    private static void Field(StringBuilder sb, string origin, string? value)
    {
        var v = Zf(value);
        if (v.Length > 0) sb.AppendLine($"{origin}^FD{v}^FS");
    }

    /// <summary>^FD 데이터에서 ZPL 제어문자를 제거한다. '_' 는 ^FH 이스케이프 문자라 같이 뺀다.</summary>
    private static string Zf(string? value)
        => value is null ? string.Empty : value.Replace("^", "").Replace("~", "").Replace("_", "");
}
