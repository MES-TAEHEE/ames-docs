using AMES.Data.Repositories;
using static AMES.Data.Scheduling.SlotPacker;

namespace AMES.Data.Scheduling;

/// <summary>
/// PP-003 자동 배치. 단계를 StepSeq 순으로 돌며 오늘부터 앞으로 하루씩 채우되 마감일에서 멈추고,
/// 잔량이 남으면 납기일까지 이어 붙인다(Late). 납기일까지도 못 넣은 수량은 Shortfall.
/// 단계 간에는 시작 순서만 보장한다 — 뒤 단계 첫 슬롯은 앞 단계 첫 슬롯 이후(같은 날 시작 가능, 수량 흐름은 보지 않는다).
/// 능력은 IDayState 로 받고 배치할 때마다 Occupy 로 누적해, 같은 배치의 다음 WO 가 그 자리를 다시 쓰지 않게 한다.
/// </summary>
public static class DeadlinePacker
{
    public sealed record StepDemand(int StepSeq, string LineId, decimal Qty, int? CycleSec, int? DailyCap);
    public sealed record Placement(int StepSeq, string LineId, DateTime Date, int StartMin, int EndMin, decimal Qty, bool Late);
    public sealed record StepShortfall(int StepSeq, string LineId, decimal Qty);
    public sealed record Result(IReadOnlyList<Placement> Placements, IReadOnlyList<StepShortfall> Shortfalls);

    public interface IDayState
    {
        LineScheduleRepository.DayCapacity Get(string lineId, DateTime date);
        void Occupy(string lineId, DateTime date, Interval slot);
    }

    /// <summary>(라인, 날짜) 당 한 번만 읽고 이후는 메모리에 누적. 조회 횟수는 실제 배치된 일수에 비례한다.</summary>
    public sealed class DayStateCache : IDayState
    {
        readonly Func<string, DateTime, LineScheduleRepository.DayCapacity> _load;
        readonly Dictionary<(string Line, DateTime Date), LineScheduleRepository.DayCapacity> _days = new();

        public DayStateCache(Func<string, DateTime, LineScheduleRepository.DayCapacity> load) => _load = load;

        public LineScheduleRepository.DayCapacity Get(string lineId, DateTime date)
        {
            var key = (lineId, date.Date);
            if (!_days.TryGetValue(key, out var cap)) _days[key] = cap = _load(lineId, date.Date);
            return cap;
        }

        public void Occupy(string lineId, DateTime date, Interval slot)
        {
            var cap = Get(lineId, date);
            _days[(lineId, date.Date)] = cap with
            {
                Occupied  = cap.Occupied.Append(slot).ToList(),
                WoLoadMin = cap.WoLoadMin + (slot.EndMin - slot.StartMin),
                LastWoEnd = Later(cap.DayStart, cap.LastWoEnd, slot.EndMin),
            };
        }
    }

    public static Result Pack(IReadOnlyList<StepDemand> steps, DateTime today, int nowMinOfToday,
                              DateTime? deadline, DateTime? dueDate, WorkdayCalendar cal, IDayState days)
    {
        today = today.Date;
        var placements = new List<Placement>();
        var shortfalls = new List<StepShortfall>();
        // 탐색 상한 — 납기일. 없으면 마감일, 그것도 없으면 60일 (무한 루프 방지)
        var horizon = (dueDate ?? deadline ?? today.AddDays(60)).Date;
        // 납기가 이미 지난 수주는 상한이 오늘 앞이라 한 슬롯도 못 놓는다 — "최대한 빨리" 로 보고 60일 안에 전량 Late 로 넣는다
        if (horizon < today) horizon = today.AddDays(60);

        (DateTime Date, int Start)? prevStart = null;   // 앞 단계 첫 슬롯 — 뒤 공정은 앞 공정이 첫 개를 만들기 시작한 뒤부터
        decimal? prevPlaced = null;

        foreach (var step in steps.OrderBy(s => s.StepSeq))
        {
            decimal target    = Math.Floor(prevPlaced is decimal pp ? Math.Min(step.Qty, pp) : step.Qty);
            decimal remaining = target;
            decimal placed    = 0;
            (DateTime Date, int Start)? firstStart = null;
            var date = prevStart is { } ps && ps.Date > today ? ps.Date : today;

            while (remaining > 0 && date <= horizon)
            {
                if (!cal.IsWorkday(date)) { date = date.AddDays(1); continue; }

                var cap = days.Get(step.LineId, date);
                int? notBefore = cap.LastWoEnd;
                if (prevStart is { } p && p.Date == date) notBefore = Later(cap.DayStart, notBefore, p.Start);
                // now 는 캘린더 날짜의 절대 분이고 ScheduleDate 의 창은 DayStart 부터 시작하므로, DayStart 이전이면
                // 그 창의 어느 부분도 아직 지나지 않아 바닥이 필요 없다 — DayStart 이후라야 지난 구간이 [DayStart, now) 로 Later/Axis 와 맞는다
                if (date == today && nowMinOfToday >= cap.DayStart) notBefore = Later(cap.DayStart, notBefore, nowMinOfToday);
                bool late = deadline is DateTime dl && date > dl.Date;

                void Add(Interval slot, decimal qty)
                {
                    placements.Add(new Placement(step.StepSeq, step.LineId, date, slot.StartMin, slot.EndMin, qty, late));
                    days.Occupy(step.LineId, date, slot);
                    firstStart ??= (date, slot.StartMin);
                    placed    += qty;
                    remaining -= qty;
                }

                if (MinutesPerEa(step, cap.OperatingMin) is not decimal minPerEa)
                {
                    // 사이클·DailyCap 없음 — 분↔수량 환산이 없으니 60분 단일 블록으로 전량. 안 들어가면 다음 날.
                    if (Place(cap.OperatingBands, cap.Occupied, 60, cap.DayStart, notBefore) is { } block)
                        Add(block, remaining);
                }
                else
                {
                    int wantMin  = (int)Math.Ceiling(remaining * minPerEa);
                    int chunkMin = Math.Max(1, (int)Math.Ceiling(minPerEa));
                    foreach (var slot in FillDay(cap.OperatingBands, cap.Occupied, wantMin, cap.DayStart, notBefore, chunkMin))
                    {
                        decimal qty = Math.Min(remaining, Math.Floor((slot.EndMin - slot.StartMin) / minPerEa));
                        if (qty <= 0) continue;
                        // 내림으로 남는 분은 슬롯을 줄여 돌려준다 — 분 올림 때문에 총 배치 시간이 늘지 않게
                        int useMin = (int)Math.Ceiling(qty * minPerEa);
                        Add(new Interval(slot.StartMin, slot.StartMin + useMin), qty);
                        if (remaining <= 0) break;
                    }
                }
                date = date.AddDays(1);
            }

            // Shortfall 은 대상 수량이 아니라 단계 수량 기준 — 앞 단계가 줄여 준 수량도 "못 만든" 수량이다
            decimal missing = Math.Floor(step.Qty) - placed;
            if (missing > 0) shortfalls.Add(new StepShortfall(step.StepSeq, step.LineId, missing));

            if (firstStart is { } fs) prevStart = fs;
            prevPlaced = placed;
        }
        return new Result(placements, shortfalls);
    }

    // 분/EA: BOP 사이클 → 라인 DailyCap 비례(그 날 가동분 기준) → 없음(null = 분할 불가)
    static decimal? MinutesPerEa(StepDemand s, int operatingMin)
    {
        if (s.CycleSec is > 0 and var cyc) return cyc / 60m;
        if (s.DailyCap is > 0 and var cap && operatingMin > 0) return (decimal)operatingMin / cap;
        return null;
    }

    /// <summary>축(dayStart 기준)에서 더 늦은 시각. PpRepository 가 쓰던 규칙 그대로.</summary>
    internal static int Later(int dayStart, int? a, int b)
    {
        if (a is not int av) return b;
        int Axis(int m) { int r = (m - dayStart) % 1440; return r < 0 ? r + 1440 : r; }
        return Axis(av) >= Axis(b) ? av : b;
    }
}
