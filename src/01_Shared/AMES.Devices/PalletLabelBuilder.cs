using System.Globalization;
using System.Text;

namespace AMES.Devices;

/// <summary>팔레트 라벨 한 장 — 바코드 내용은 PalletNo 그대로(POP·PDA 가 EOS-PLT- 로 시작하는 값을 팔레트로 읽는다).</summary>
/// <param name="Use">용도 표시(INTERNAL / EXTERNAL).</param>
/// <param name="Customer">전용 고객 코드, 공용 외부 팔레트면 SHARED, 내부용이면 null.</param>
public sealed record PalletLabel(string PalletNo, string Use, string? Customer, DateTime PrintedAt);

/// <summary>
/// 팔레트 라벨 — 현장 라벨지 70×40mm = 560×320 dot @ 203dpi(POP 라벨과 같은 라벨지).
/// 원점 보정(^LH) 없음. 좌표는 현장 프린터에서 바르게 나오는 라벨 생성기(Virtual_Bar_QR_Code_Printer)와 같다 —
/// 왼쪽 여백 40·위 25, QR ^BQN 배율 6(한 칸 0.75mm), 글자 x=220. 아래 팔레트 번호는 왼쪽 여백 40 에 크게.
/// 웹은 이 ZPL 을 Zebra Browser Print 로 PC 프린터에 보내고, 화면 미리보기(PalletLabelPaper.razor + app.css)는 같은 점 좌표 ÷ 8 = mm 를 쓴다.
/// </summary>
public static class PalletLabelBuilder
{
    public const int WidthDots  = 560;
    public const int HeightDots = 320;
    /// <summary>브라우저 인쇄 방식의 용지 크기(CSS @page size) — Zebra Browser Print 가 없는 PC 용.</summary>
    public const string PageSizeCss = "70mm 40mm";

    public static string Build(PalletLabel l, int copies = 1)
    {
        copies = Math.Clamp(copies, 1, 99);
        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine("^CI28");
        sb.AppendLine($"^PW{WidthDots}");
        sb.AppendLine($"^LL{HeightDots}");
        sb.AppendLine("^FO40,25");
        sb.AppendLine("^BQN,2,6,Q");
        sb.AppendLine($"^FH^FDQA,{Zf(l.PalletNo)}^FS");
        sb.AppendLine("^FO220,40");
        sb.AppendLine("^A0N,48,48");
        sb.AppendLine("^FH^FDPALLET^FS");
        sb.AppendLine("^FO220,100");
        sb.AppendLine("^A0N,30,30");
        sb.AppendLine($"^FH^FD{Zf(UseLine(l))}^FS");
        sb.AppendLine("^FO220,145");
        sb.AppendLine("^A0N,26,26");
        sb.AppendLine($"^FH^FD{l.PrintedAt.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)}^FS");
        sb.AppendLine("^FO40,228");
        sb.AppendLine("^A0N,56,46");
        sb.AppendLine($"^FH^FD{Zf(l.PalletNo)}^FS");
        sb.AppendLine($"^PQ{copies}");
        sb.Append("^XZ");
        return sb.ToString();
    }

    /// <summary>용도 줄 — 「EXTERNAL · SAV」, 고객이 없으면 용도만.</summary>
    public static string UseLine(PalletLabel l)
        => string.IsNullOrWhiteSpace(l.Customer) ? l.Use : $"{l.Use} · {l.Customer}";

    // ^FH 아래에서는 _ 가 16진 이스케이프 시작이라 _5F 로 바꾼다. ^·~ 는 필드를 끊으므로 뺀다.
    private static string Zf(string? value)
        => value is null ? string.Empty : value.Replace("^", "").Replace("~", "").Replace("_", "_5F");
}
