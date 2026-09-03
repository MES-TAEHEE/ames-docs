namespace AMES.Data.Scheduling;

/// <summary>
/// 라인 스케줄 자동 배치. 가동 밴드 안에서 기존 슬롯(WO·PM)과 겹치지 않는 첫 자리를 고른다.
/// 시각은 하루 절대분(0..1440), 밴드는 자정을 넘지 않는 구간. 야간 교대는 축 원점(dayStart)
/// 기준 상대 위치로 정렬해 22:00 밴드 뒤에 00:00 밴드가 오도록 한다 (PP-LSB 보드와 동일).
/// </summary>
public static class SlotPacker
{
    public readonly record struct Interval(int StartMin, int EndMin);

    /// <param name="operating">가동 밴드(절대분, 자정 미교차).</param>
    /// <param name="occupied">이미 차지된 구간 — 기존 WO 슬롯 + PM 밴드.</param>
    /// <param name="durationMin">소요 분.</param>
    /// <param name="dayStart">축 원점(첫 교대 시작 절대분).</param>
    /// <param name="notBeforeMin">이 시각(절대분) 이전에는 놓지 않음. 기본은 제한 없음.</param>
    /// <returns>배치 구간, 자리가 없으면 null.</returns>
    public static Interval? Place(IReadOnlyList<Interval> operating, IReadOnlyList<Interval> occupied,
                                  int durationMin, int dayStart, int? notBeforeMin = null)
    {
        if (durationMin <= 0) return null;

        int Axis(int m) { int r = (m - dayStart) % 1440; return r < 0 ? r + 1440 : r; }
        // 끝값이 축 원점과 같으면(예: 밴드 끝 1440, 원점 1320) 1440 으로 — 0 으로 감기면 길이가 음수가 된다.
        int AxisEnd(int startAbs, int endAbs) => Axis(startAbs) + (endAbs - startAbs);

        var bands = operating
            .Where(b => b.EndMin > b.StartMin)
            .Select(b => (Lo: Axis(b.StartMin), Hi: AxisEnd(b.StartMin, b.EndMin), Abs: b))
            .OrderBy(b => b.Lo)
            .ToList();
        var busy = occupied
            .Where(o => o.EndMin > o.StartMin)
            .Select(o => (Lo: Axis(o.StartMin), Hi: AxisEnd(o.StartMin, o.EndMin)))
            .OrderBy(o => o.Lo)
            .ToList();
        int floor = notBeforeMin is int nb ? Axis(nb) : 0;

        foreach (var band in bands)
        {
            int cursor = Math.Max(band.Lo, floor);
            while (cursor + durationMin <= band.Hi)
            {
                var hit = busy.FirstOrDefault(o => o.Lo < cursor + durationMin && cursor < o.Hi);
                if (hit == default)
                {
                    int startAbs = band.Abs.StartMin + (cursor - band.Lo);
                    return new Interval(startAbs, startAbs + durationMin);
                }
                cursor = hit.Hi;
            }
        }
        return null;
    }
}
