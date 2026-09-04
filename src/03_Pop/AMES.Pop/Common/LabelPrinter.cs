using AMES.Contracts.Dto;
using AMES.Devices;

namespace AMES.Pop.Common;

/// <summary>
/// LOT 라벨 발행 — InjMain 재출력·LabelDispatcher 자동 발행(INJ)과 ImgMain 발행·재출력(IMG)이 공용으로 쓴다.
/// 실패 시 예외 — 호출자가 토스트로 알린다.
/// </summary>
internal static class LabelPrinter
{
    public static void Print(InjLotDto lot, string? fallbackLineId = null)
        => Print(new ZplLabel(
            lot.LotCode, lot.ItemNo, lot.ItemName, lot.ColorCode, lot.CavityPos,
            lot.PressType, lot.LineId ?? fallbackLineId ?? "", lot.CreatedTS), lot.LotCode);

    /// <summary>
    /// IMG 완제품 라벨 — 고객 표준 DataMatrix 양식(ImgLabelBuilder). 발행일은 지금 시각이다:
    /// 재출력도 다시 찍는 날짜가 라벨에 남는다.
    /// </summary>
    public static void Print(ImgLotDto lot, string shiftCode)
        => Print(ImgLabelBuilder.Build(new ImgLabel(
            lot.LotCode, lot.ItemNo, lot.CustomerCode, lot.Pgn, lot.Alc, lot.MountPos,
            ShiftLetter(shiftCode), DateTime.Now)), lot.LotCode);

    /// <summary>POP 교대 코드 → 라벨 part4M 교대 글자. DAY=A, NIGHT=B, 그 외(3교대 확장분)=C.</summary>
    public static string ShiftLetter(string? shiftCode) => shiftCode?.ToUpperInvariant() switch
    {
        "DAY"   => "A",
        "NIGHT" => "B",
        _       => "C",
    };

    private static void Print(ZplLabel label, string jobName)
        => Print(ZplLabelBuilder.Build(label), jobName);

    private static void Print(string zpl, string jobName)
    {
        var cfg = AppConfig.Current;
        var printer = new ZplPrinter(new ZplPrinterOptions
        {
            Mode        = cfg.PrinterMode,
            Host        = cfg.PrinterHost,
            Port        = cfg.PrinterPort,
            OutputDir   = cfg.PrinterOutputDir,
            PrinterName = cfg.PrinterName,
        });
        printer.Print(zpl, jobName);
    }
}
