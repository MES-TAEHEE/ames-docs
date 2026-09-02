using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>라벨 디스패처가 보는 LOT 저장소 — 테스트에서 대체 가능하도록 좁게 정의.</summary>
internal interface IInjLotClaimStore
{
    int GetMaxLotId(string lineId);
    List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId);

    /// <summary>내가 선점한 것만 반납된다 — stationId 가 소유권 검증에 쓰인다.</summary>
    void ReleasePrintClaim(int lotId, string stationId);

    void IncrementPrintedCount(int lotId);
}

/// <summary>라벨 출력 대상 — 실패 시 예외.</summary>
internal interface ILabelSink
{
    void Print(InjLotDto lot);
}
