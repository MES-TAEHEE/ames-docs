using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Services;

/// <summary>IInjLotClaimStore → PopServices.InjLots 위임.</summary>
internal sealed class RepoInjLotClaimStore : IInjLotClaimStore
{
    public int GetMaxLotId(string lineId) => PopServices.InjLots.GetMaxLotId(lineId);

    // staleSeconds / top 은 저장소 기본값(60/5)을 쓴다 — 둘은 staleSeconds > top × 5초
    // 라는 불변식으로 묶여 있어 설정으로 따로 노출하면 손으로 유지해야 한다.
    public List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId)
        => PopServices.InjLots.ClaimForPrint(lineId, afterLotId, stationId);

    public void ReleasePrintClaim(int lotId, string stationId)
        => PopServices.InjLots.ReleasePrintClaim(lotId, stationId);

    public void IncrementPrintedCount(int lotId) => PopServices.InjLots.IncrementPrintedCount(lotId);
}

/// <summary>ILabelSink → 기존 LabelPrinter 위임 (ZPL 조립·출력 공용 경로).</summary>
internal sealed class ZplLabelSink : ILabelSink
{
    // fallbackLineId 를 넘기지 않는다: 클레임 쿼리가 LineID 로 걸러 반환하므로
    // lot.LineId 는 이 경로에서 절대 null 이 아니다.
    public void Print(InjLotDto lot) => LabelPrinter.Print(lot);
}
