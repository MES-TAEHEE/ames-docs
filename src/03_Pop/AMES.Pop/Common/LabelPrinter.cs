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

    /// <summary>IMG 라벨 — 같은 양식에서 금형 색상·캐비티·프레스 칸만 비운다.</summary>
    public static void Print(ImgLotDto lot, string? fallbackLineId = null)
        => Print(new ZplLabel(
            lot.LotCode, lot.ItemNo, lot.ItemName, null, null,
            null, lot.LineId ?? fallbackLineId ?? "", lot.CreatedTS), lot.LotCode);

    private static void Print(ZplLabel label, string jobName)
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
        printer.Print(ZplLabelBuilder.Build(label), jobName);
    }
}
