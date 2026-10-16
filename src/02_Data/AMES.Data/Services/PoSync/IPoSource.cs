namespace AMES.Data.Services.PoSync;

/// <summary>고객사 SRM 에서 발주일(PO_DATE) 창 [from, to] 의 INQUERY 행을 가져온다. 실패는 예외로.</summary>
public interface IPoSource
{
    Task<IReadOnlyList<SrmPoRow>> FetchAsync(PoSyncSource source, DateOnly from, DateOnly to, CancellationToken ct);
}
