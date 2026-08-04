using AMES.Devices;

namespace AMES.InjAgent.Core;

/// <summary>ILabelPrinter → AMES.Devices ZPL 빌더/프린터 위임.</summary>
public sealed class ZplLabelPrinter : ILabelPrinter
{
    private readonly ZplPrinter _printer;
    public ZplLabelPrinter(ZplPrinterOptions options) => _printer = new ZplPrinter(options);

    public void PrintLabel(string lotCode, string itemNo, string? itemName,
                           string? colorCode, string? cavityPos, string lineId)
    {
        // 에이전트 수집분은 PR_InjLot.PressType 이 항상 'M' (InjAgentStore.CreateRawLot).
        var zpl = ZplLabelBuilder.Build(new ZplLabel(
            lotCode, itemNo, itemName, colorCode, cavityPos, "M", lineId, DateTime.Now));
        _printer.Print(zpl, lotCode);
    }
}
