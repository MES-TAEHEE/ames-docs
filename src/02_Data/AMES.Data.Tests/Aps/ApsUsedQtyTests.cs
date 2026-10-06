using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// 사출품 사용량 = 다음 단계 실적 × 간선 QtyPer (스펙 §4.4), BOM 간선 QtyPer = QtyPer × (1 + ScrapPct/100) (§4.2).
/// ActualsRules 는 곱한 값을 받기만 하므로 곱셈은 여기서 고정한다. 순수 함수, DB 없음.
/// </summary>
public class ApsUsedQtyTests
{
    [Fact]
    public void Edge_qty_per_adds_scrap_percent()
    {
        Assert.Equal(1.5, ApsRepository.EdgeQtyPer(1.5m, 0m));
        Assert.Equal(2.2, ApsRepository.EdgeQtyPer(2m, 10m));
        Assert.Equal(0.0, ApsRepository.EdgeQtyPer(0m, 10m));
    }

    [Fact]
    public void Used_is_next_step_production_times_fractional_qty_per()
    {
        Assert.Equal(16.5m, ApsRepository.UsedQty(new[] { (11m, 1.5) }));
        Assert.Equal(24.2m, ApsRepository.UsedQty(new[] { (11m, ApsRepository.EdgeQtyPer(2m, 10m)) }));   // 스크랩 간선
        Assert.Equal(-1.5m, ApsRepository.UsedQty(new[] { (-1m, 1.5) }));   // 역분개 −1 도 그대로 곱한다(순생산)
    }

    [Fact]
    public void Used_sums_every_parent_edge_and_is_zero_without_parents()
    {
        // 같은 품번 간선(다음 라인 단계 5 × 1) + BOM 부모 간선(부모 완제품 4 × 1.5)
        Assert.Equal(11m, ApsRepository.UsedQty(new[] { (5m, 1.0), (4m, 1.5) }));
        Assert.Equal(0m,  ApsRepository.UsedQty(new[] { (7m, 0.0) }));
        Assert.Equal(0m,  ApsRepository.UsedQty(Array.Empty<(decimal, double)>()));
    }
}
