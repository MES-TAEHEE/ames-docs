using AMES.Devices;

namespace AMES.Pop.Services;

/// <summary>
/// IMG 판정 팝업이 떠 있는 동안 들어온 스캔. 판정 중인 LOT 의 재스캔은 무시하고, 그 밖의 스캔(다음 Core 등)은
/// 판정을 가로채지 못하게 "현재 부품부터 판정" 안내만 한다. 단위 테스트: JudgeScanGateTests.
/// </summary>
internal static class JudgeScanGate
{
    public static bool IsCurrentLot(string currentLotCode, string raw)
        => string.Equals(ImgScanParser.ExtractLotCode(raw), currentLotCode, StringComparison.OrdinalIgnoreCase);
}
