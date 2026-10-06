using AMES.Data.Scheduling;

namespace AMES.Data.Aps;

/// <summary>
/// 스펙 §4.3·§8.3 — 가동 세그먼트 × 교대 SortOrder 를 주간/야간 구간으로 나누고, 수량을 분으로 바꿔 잔여 구간 앞에서부터 채운다.
/// 주간 = ShiftSort 최소 교대의 OPERATING 밴드, 야간 = 나머지 교대의 OPERATING 밴드. 구간은 축(dayStart) 순으로 정렬한다.
/// Subtract 규칙은 LineScheduleRepository.Subtract(:467) 과, 축 규칙은 SlotPacker 와 같다. 순수 함수 — 정본 테스트 ShiftBandsTests.
/// </summary>
public static class ShiftBands
{
    /// <summary>Day/Night 는 축(dayStart) 순 절대분 구간. occupied 를 뺀 잔여를 담는다(occupied 가 비면 전체 교대 시간).</summary>
    public sealed record DayNightBands(IReadOnlyList<SlotPacker.Interval> Day, IReadOnlyList<SlotPacker.Interval> Night, int DayStart)
    {
        public double DayHours   => Day.Sum(b => b.EndMin - b.StartMin) / 60.0;
        public double NightHours => Night.Sum(b => b.EndMin - b.StartMin) / 60.0;
    }

    public static DayNightBands Split(IReadOnlyList<(SlotPacker.Interval Band, int ShiftSort)> operating,
                                      IReadOnlyList<SlotPacker.Interval> occupied, int dayStart)
    {
        var valid = operating.Where(o => o.Band.EndMin > o.Band.StartMin).ToList();
        if (valid.Count == 0)
            return new DayNightBands(Array.Empty<SlotPacker.Interval>(), Array.Empty<SlotPacker.Interval>(), dayStart);

        int minSort = valid.Min(o => o.ShiftSort);
        var day   = Remaining(valid.Where(o => o.ShiftSort == minSort).Select(o => o.Band), occupied, dayStart);
        var night = Remaining(valid.Where(o => o.ShiftSort != minSort).Select(o => o.Band), occupied, dayStart);
        return new DayNightBands(day, night, dayStart);
    }

    /// <summary>등록 계획 슬롯의 StartMin 이 주간 구간(occupied 제외 전)에 속하는지(§4.5). 어느 밴드에도 안 걸리면(휴게 등) 축상 첫 야간 밴드보다 앞이면 주간.</summary>
    public static bool IsDay(DayNightBands full, int startMin)
    {
        if (full.Day.Any(b => b.StartMin <= startMin && startMin < b.EndMin)) return true;
        if (full.Night.Any(b => b.StartMin <= startMin && startMin < b.EndMin)) return false;
        if (full.Night.Count == 0) return true;
        return Axis(startMin, full.DayStart) < Axis(full.Night[0].StartMin, full.DayStart);
    }

    /// <summary>ceil(qty ÷ uph × 60). uph ≤ 0 이면 ArgumentOutOfRangeException — 호출자가 NoUph 로 거부한다.</summary>
    public static int MinutesFor(decimal qty, decimal uph)
    {
        if (uph <= 0) throw new ArgumentOutOfRangeException(nameof(uph), uph, "UPH 는 0 보다 커야 합니다.");
        if (qty <= 0) return 0;
        // 곱하고 나눈다 — qty / uph 를 먼저 하면 1.1666…67 같은 반올림 꼬리가 × 60 뒤 정수를 넘겨 1분이 늘어난다
        return (int)Math.Ceiling(qty * 60m / uph);
    }

    public sealed record Allocation(IReadOnlyList<SlotPacker.Interval> Slots, int PlacedMin, int ShortMin);

    /// <summary>remaining(이미 occupied 를 뺀 잔여) 앞에서부터 순서대로 분할해 wantMin 을 채운다. 모자라면 ShortMin &gt; 0.</summary>
    public static Allocation Allocate(IReadOnlyList<SlotPacker.Interval> remaining, int wantMin, int dayStart)
    {
        if (wantMin <= 0) return new Allocation(Array.Empty<SlotPacker.Interval>(), 0, 0);
        var slots  = SlotPacker.FillDay(remaining, Array.Empty<SlotPacker.Interval>(), wantMin, dayStart);
        int placed = slots.Sum(s => s.EndMin - s.StartMin);
        return new Allocation(slots, placed, wantMin - placed);
    }

    /// <summary>형제 대표 = ItemNo Ordinal 오름차순 첫 품번(§8.3).</summary>
    public static string Representative(IEnumerable<string> siblingItemNos)
    {
        var rep = siblingItemNos.OrderBy(s => s, StringComparer.Ordinal).FirstOrDefault();
        return rep ?? throw new ArgumentException("형제 품번이 비어 있습니다.", nameof(siblingItemNos));
    }

    static List<SlotPacker.Interval> Remaining(IEnumerable<SlotPacker.Interval> bands, IEnumerable<SlotPacker.Interval> holes, int dayStart)
        => bands.SelectMany(b => Subtract(b, holes)).OrderBy(b => Axis(b.StartMin, dayStart)).ToList();

    static int Axis(int m, int dayStart) { int r = (m - dayStart) % 1440; return r < 0 ? r + 1440 : r; }

    // band 에서 holes 를 뺀 잔여 구간 — LineScheduleRepository.Subtract 와 같은 규칙(그쪽은 private).
    static IEnumerable<SlotPacker.Interval> Subtract(SlotPacker.Interval band, IEnumerable<SlotPacker.Interval> holes)
    {
        int cur = band.StartMin;
        foreach (var h in holes.Where(h => h.StartMin < band.EndMin && band.StartMin < h.EndMin).OrderBy(h => h.StartMin))
        {
            int hs = Math.Max(band.StartMin, h.StartMin);
            if (hs > cur) yield return new(cur, hs);
            cur = Math.Max(cur, Math.Min(band.EndMin, h.EndMin));
        }
        if (cur < band.EndMin) yield return new(cur, band.EndMin);
    }
}
