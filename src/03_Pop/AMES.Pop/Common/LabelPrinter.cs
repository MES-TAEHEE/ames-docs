using AMES.Contracts.Dto;
using AMES.Devices;

namespace AMES.Pop.Common;

/// <summary>
/// LOT 라벨 발행 — InjMain 재출력과 수동입력 최초 발행이 공용으로 쓴다.
/// 실패 시 예외 — 호출자가 토스트로 알린다.
/// </summary>
internal static class LabelPrinter
{
    public static void Print(InjLotDto lot, string? fallbackLineId = null)
    {
        var cfg = AppConfig.Current;
        var printer = new ZplPrinter(new ZplPrinterOptions
        {
            Mode      = cfg.PrinterMode,
            Host      = cfg.PrinterHost,
            Port      = cfg.PrinterPort,
            OutputDir = cfg.PrinterOutputDir,
        });
        var zpl = ZplLabelBuilder.Build(new ZplLabel(
            lot.LotCode, lot.ItemNo, lot.ItemName, lot.ColorCode, lot.CavityPos,
            lot.PressType, lot.LineId ?? fallbackLineId ?? "", lot.CreatedTS));
        printer.Print(zpl, lot.LotCode);
    }
}
