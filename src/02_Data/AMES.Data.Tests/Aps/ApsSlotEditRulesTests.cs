using AMES.Data.Aps;
using AMES.Data.Scheduling;
using Xunit;
using static AMES.Data.Repositories.PpRepository;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// PP-APS WO 미리보기 슬롯 편집(2026-10-07) 순수 규칙 — 5분 스냅, 가동 밴드 안·겹침 검사, 편집을 결과(ApsWoOrder.Placements)에 덮기(형제 0분 슬롯 동반 이동).
/// </summary>
public class ApsSlotEditRulesTests
{
    static readonly DateOnly D0 = new(2026, 10, 12);
    static readonly DateOnly D1 = D0.AddDays(1);
    static DateTime T(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);
    static SlotPacker.Interval Iv(int s, int e) => new(s, e);

    static ApsWoOrder Order(string wo, int woId, string item, int? soId, params DeadlinePacker.Placement[] slots) =>
        new(wo, woId, 1, item, D0, soId, slots.Sum(s => s.Qty), null, null, slots.ToList(), Array.Empty<DeadlinePacker.StepShortfall>(), Array.Empty<DeadlinePacker.MoldChange>());

    static ApsSlotEdit Edit(string item, int? soId, int step, string line, DateOnly date, int s, int e, string nl, DateOnly nd, int ns, int ne, decimal? qty = null) =>
        new(item, D0, soId, step, line, date, s, e, nl, nd, ns, ne, qty);

    [Theory]
    [InlineData(0, 0)] [InlineData(2, 0)] [InlineData(3, 5)] [InlineData(487, 485)] [InlineData(488, 490)] [InlineData(1439, 1440)]
    public void Snap_rounds_to_five_minutes(int min, int expected) => Assert.Equal(expected, ApsSlotEditRules.Snap(min));

    [Fact]
    public void Validate_requires_slot_inside_one_operating_band_and_no_overlap()
    {
        var bands = new[] { Iv(480, 720), Iv(780, 1440) };
        var busy  = new[] { Iv(600, 660) };

        Assert.Null(ApsSlotEditRules.Validate(Iv(480, 600), bands, busy));
        Assert.Null(ApsSlotEditRules.Validate(Iv(660, 720), bands, busy));
        Assert.Equal(ApsSlotEditRules.ReasonOutsideBands, ApsSlotEditRules.Validate(Iv(700, 800), bands, busy));   // 휴게를 가로지름
        Assert.Equal(ApsSlotEditRules.ReasonOutsideBands, ApsSlotEditRules.Validate(Iv(400, 500), bands, busy));
        Assert.Equal(ApsSlotEditRules.ReasonOverlap,      ApsSlotEditRules.Validate(Iv(590, 610), bands, busy));
        Assert.Equal(ApsSlotEditRules.ReasonBadRange,     ApsSlotEditRules.Validate(Iv(600, 600), bands, busy));
        Assert.Equal(ApsSlotEditRules.ReasonNoPattern,    ApsSlotEditRules.Validate(Iv(480, 500), Array.Empty<SlotPacker.Interval>(), busy));
    }

    [Fact]
    public void Apply_moves_the_matching_placement_and_its_sibling_zero_minute_slots()
    {
        var rep = Order("WO-1", 1, "P-LH", null,
            new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 480, 600, 100, false, "M1"),
            new DeadlinePacker.Placement(20, "LINE-IMG-01", T(D0), 700, 760, 100, false));
        var sib = Order("WO-2", 2, "P-RH", null, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 480, 480, 100, false));
        var other = Order("WO-3", 3, "P-X", 7, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 900, 960, 50, false, "M2"));
        var result = new ApsWoResult(new() { rep, sib, other }, new(), 0, 0, true);

        var edit = Edit("P-LH", null, 10, "LINE-INJ-01", D0, 480, 600, "LINE-INJ-02", D1, 500, 620, 90);
        var (orders, outcomes, moves) = ApsSlotEditRules.Apply(result.Orders, new[] { edit }, (_, _, _) => null);

        var o = Assert.Single(outcomes);
        Assert.True(o.Applied); Assert.Null(o.Reason);
        var moved = orders.Single(x => x.WoNumber == "WO-1").Placements.Single(p => p.StepSeq == 10);
        Assert.Equal(("LINE-INJ-02", D1, 500, 620, 90m, "M1"), (moved.LineId, DateOnly.FromDateTime(moved.Date), moved.StartMin, moved.EndMin, moved.Qty, moved.MoldId));
        Assert.Equal(("LINE-IMG-01", 700), (orders.Single(x => x.WoNumber == "WO-1").Placements.Single(p => p.StepSeq == 20).LineId, orders.Single(x => x.WoNumber == "WO-1").Placements.Single(p => p.StepSeq == 20).StartMin));
        var sibMoved = orders.Single(x => x.WoNumber == "WO-2").Placements.Single();
        Assert.Equal(("LINE-INJ-02", D1, 500, 500, 100m), (sibMoved.LineId, DateOnly.FromDateTime(sibMoved.Date), sibMoved.StartMin, sibMoved.EndMin, sibMoved.Qty));
        Assert.Equal(("LINE-INJ-01", 900), (orders.Single(x => x.WoNumber == "WO-3").Placements.Single().LineId, orders.Single(x => x.WoNumber == "WO-3").Placements.Single().StartMin));
        Assert.Equal(2, moves.Count);   // 대표 + 형제
        Assert.Equal(new[] { 1, 2 }, moves.Select(m => m.WoId).OrderBy(x => x).ToArray());
        Assert.Same(result.Orders[0], rep);   // 원본은 바뀌지 않는다
    }

    [Fact]
    public void Apply_skips_edits_whose_slot_is_not_found_or_rejected_by_the_validator()
    {
        var rep = Order("WO-1", 1, "P-LH", 5, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 480, 600, 100, false, "M1"));
        var result = new ApsWoResult(new() { rep }, new(), 0, 0, true);
        var notFound = Edit("P-LH", 5, 10, "LINE-INJ-01", D0, 480, 660, "LINE-INJ-01", D0, 700, 880);   // 끝이 다름 → 배치가 달라진 슬롯
        var rejected = Edit("P-LH", 5, 10, "LINE-INJ-01", D0, 480, 600, "LINE-INJ-01", D0, 700, 820);

        var (orders, outcomes, moves) = ApsSlotEditRules.Apply(result.Orders, new[] { notFound, rejected }, (_, _, _) => ApsSlotEditRules.ReasonOverlap);

        Assert.Equal(new[] { (false, ApsSlotEditRules.ReasonNotFound), (false, ApsSlotEditRules.ReasonOverlap) }, outcomes.Select(o => (o.Applied, o.Reason)).ToArray());
        Assert.Empty(moves);
        Assert.Equal(480, orders.Single().Placements.Single().StartMin);
    }

    [Fact]
    public void Apply_lets_a_later_edit_see_the_earlier_one()
    {
        var a = Order("WO-1", 1, "P-A", null, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 480, 600, 100, false, "M1"));
        var b = Order("WO-2", 2, "P-B", null, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 600, 720, 100, false, "M2"));
        var result = new ApsWoResult(new() { a, b }, new(), 0, 0, true);
        var seen = new List<int>();
        string? Validator(ApsSlotEdit e, IReadOnlyList<ApsWoOrder> current, DeadlinePacker.Placement old)
        {
            seen.Add(current.Single(o => o.WoNumber == "WO-1").Placements.Single().StartMin);
            return null;
        }

        ApsSlotEditRules.Apply(result.Orders, new[]
        {
            Edit("P-A", null, 10, "LINE-INJ-01", D0, 480, 600, "LINE-INJ-01", D0, 800, 920),
            Edit("P-B", null, 10, "LINE-INJ-01", D0, 600, 720, "LINE-INJ-01", D0, 480, 600),
        }, Validator);

        Assert.Equal(new[] { 480, 800 }, seen.ToArray());   // 두 번째 편집의 검증은 첫 편집이 적용된 상태를 본다
    }
}
