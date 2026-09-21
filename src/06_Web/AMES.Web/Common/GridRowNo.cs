using Radzen.Blazor;

namespace AMES.Web;

/// <summary>
/// 목록의 "No." 열 — 그리드가 지금 보여 주는 순서(정렬 반영)의 1부터 시작하는 행 번호.
/// 정렬·페이지 이동은 그리드 안에서만 다시 그려져 부모 페이지의 렌더 주기로는 캐시를 비울 수 없다.
/// 그래서 셀마다 현재 페이지(PagedView)에서 위치를 찾고 앞 페이지 행 수를 더한다 — 한 페이지 분량이라 가볍다.
/// </summary>
public static class GridRowNo
{
    public static int Of<T>(RadzenDataGrid<T>? grid, T row) where T : class
    {
        if (grid is null) return 0;
        var offset = grid.AllowPaging ? grid.CurrentPage * grid.PageSize : 0;
        var i = 0;
        foreach (var r in grid.PagedView)
        {
            if (ReferenceEquals(r, row)) return offset + i + 1;
            i++;
        }
        return 0;
    }
}
