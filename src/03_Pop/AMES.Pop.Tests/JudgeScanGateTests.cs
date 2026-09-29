using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class JudgeScanGateTests
{
    const char RS = '\u001E', GS = '\u001D', EOT = '\u0004';

    static string Full(string lot) =>
        $"[)>{RS}06{GS}VBYU5{GS}P83345P8000RBQ{GS}S81328046{GS}T2609041P8A{lot}{GS}E{GS}C:{RS}{EOT}";

    [Fact]
    public void Plain_scan_of_the_lot_being_judged_is_the_current_lot()
        => Assert.True(JudgeScanGate.IsCurrentLot("A94W10002", "A94W10002\r"));

    [Fact]
    public void Finished_label_datamatrix_of_the_lot_being_judged_is_the_current_lot()
        => Assert.True(JudgeScanGate.IsCurrentLot("A94W10002", Full("A94W10002")));

    [Fact]
    public void Next_core_scanned_while_judging_is_not_the_current_lot()
        => Assert.False(JudgeScanGate.IsCurrentLot("A94W10002", "A91I10001"));

    [Fact]
    public void Unreadable_scan_is_not_the_current_lot()
        => Assert.False(JudgeScanGate.IsCurrentLot("A94W10002", "   "));
}
