using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>라벨 디스패처가 보는 LOT 저장소 — 테스트에서 대체 가능하도록 좁게 정의.</summary>
internal interface IInjLotClaimStore
{
    int GetMaxLotId(string lineId);
    List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId);

    /// <summary>내가 선점한 것만 반납된다 — stationId 가 소유권 검증에 쓰인다.</summary>
    void ReleasePrintClaim(int lotId, string stationId);

    /// <summary>출력 전 선행 카운트 +1.</summary>
    void IncrementPrintedCount(int lotId, string employeeNo);

    /// <summary>출력이 실패했을 때 선행 카운트 되돌리기(-1).</summary>
    void DecrementPrintedCount(int lotId, string employeeNo);
}

/// <summary>
/// 이 PC 가 실제로 뽑은 LOT 의 원장. DB 카운트와 별개로 로컬에 남으므로 DB 가 끊겨도 기록이 남는다.
/// 자동 발행은 원장에 있는 LOT 을 다시 뽑지 않는다.
/// </summary>
internal interface ILabelLedger
{
    bool Contains(string lotCode);
    void Record(string lotCode);
}

/// <summary>
/// 라인별 워터마크를 로그아웃을 넘어 보관한다. 로그인할 때마다 "지금 최대 LotID"로 다시 잡으면
/// 로그아웃 동안 생긴 LOT 이 워터마크 아래로 들어가 자동 발행에서 영구히 빠진다.
/// </summary>
internal interface ILabelWatermarkStore
{
    /// <summary>저장된 워터마크. 없으면 null — 첫 로그인은 기존처럼 로그인 시점 최대값을 쓴다.</summary>
    int? Load(string lineId);
    void Save(string lineId, int watermark);
}

/// <summary>라벨 출력 대상 — 실패 시 예외.</summary>
internal interface ILabelSink
{
    void Print(InjLotDto lot);
}
