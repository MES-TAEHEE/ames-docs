namespace AMES.Data.Services.PoSync;

/// <summary>고객사 SRM 에서 발주일(PO_DATE) 창 [from, to] 의 INQUERY 행을 가져온다. 실패는 예외로.</summary>
public interface IPoSource
{
    Task<IReadOnlyList<SrmPoRow>> FetchAsync(PoSyncSource source, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>
    /// 자동 실행용 — [from, minTo] 는 반드시 받고, 그 뒤는 원격이 연속으로 비거나 maxTo 에 닿을 때까지 이어 받는다.
    /// To = 실제로 받은 마지막 날. 기본 구현은 확장하지 않는다(minTo 까지).
    /// </summary>
    async Task<(IReadOnlyList<SrmPoRow> Rows, DateOnly To)> FetchExtendingAsync(
        PoSyncSource source, DateOnly from, DateOnly minTo, DateOnly maxTo, CancellationToken ct)
        => (await FetchAsync(source, from, minTo, ct), minTo);
}
