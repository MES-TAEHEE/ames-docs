namespace AMES.Data.Services;

/// <summary>
/// MD-028 라인 시간 패턴의 교대 창·세그먼트 배치 규칙(정본, 테스트 LineTimeLayoutTests).
/// · 교대 창 = 공통코드 WORK_SHIFT.Attribute1 'HHMM-HHMM'(하루 24시간 분할). 끝이 시작보다 이르면 자정을 넘는 창이며
///   편집 좌표는 "확장 분"(시작 ~ 시작+길이, 1440 을 넘을 수 있음)으로 다룬다.
/// · 저장(MD_LineTimeSegment)은 하루 안의 분(0~1440, 시작 &lt; 끝)이라 자정을 넘는 구간은 두 조각으로 나눈다(<see cref="Split"/>).
/// · 패턴의 가동 교대 = SHIFT_PATTERN.Attribute1 'A,B'(<see cref="ParseShiftList"/>). 가동 교대 안에서도 실제 생산 시간
///   (WORK_SHIFT.Attribute2) 안만 기본 OPERATING 이고 창의 나머지와 가동하지 않는 교대는 IDLE(<see cref="ShiftWindow.DefaultAt"/>).
/// · 교대 경계가 바뀐 기존 데이터는 <see cref="Realign"/> 이 하루 시각 기준으로 옮긴다 — 교대 기본 상태와 다른 구간(휴식·점검·계획비가동 등
///   "칠한" 구간)만 시각 그대로 보존하고, 나머지는 새 교대의 기본 상태로 채운다.
/// </summary>
public static class LineTimeLayout
{
    public const int Day = 1440;
    public const string Operating = "OPERATING", Idle = "IDLE";

    /// <summary>교대 창. WorkStart/WorkEnd = 실제 생산 시간(WORK_SHIFT.Attribute2)을 창 안 확장 분으로 맞춘 값(없으면 창 전체).</summary>
    public sealed record ShiftWindow(string Code, string Name, int Start, int End, int? WorkStart = null, int? WorkEnd = null)
    {
        public int Length => End - Start;

        /// <summary>확장 분 x 의 기본 상태 — 가동 교대이고 실제 생산 시간 안이면 OPERATING, 아니면 IDLE.</summary>
        public string DefaultAt(int x, bool runs)
            => runs && (WorkStart is not { } ws || WorkEnd is not { } we || (x >= ws && x < we)) ? Operating : Idle;
    }

    /// <summary>저장·편집 공용 구간. 편집 중에는 Start/End 가 확장 분일 수 있다.</summary>
    public sealed record Band(int Start, int End, string State, string? Reason, string? Shift, string? Description);

    /// <summary>WORK_SHIFT 행(정렬순) → 교대 창. 형식 오류·길이 0 은 건너뛴다. 끝 ≤ 시작이면 자정을 넘는 창(끝 + 1440).</summary>
    public static List<ShiftWindow> Windows(IEnumerable<(string Code, string Name, string? Attribute1)> shifts)
        => Windows(shifts.Select(s => (s.Code, s.Name, s.Attribute1, (string?)null)));

    /// <summary>Attribute2(실제 생산 시간)까지 받는 판. 생산 시간은 창 안으로 잘라 두며, 형식 오류·창과 안 겹치면 창 전체를 생산 시간으로 본다.</summary>
    public static List<ShiftWindow> Windows(IEnumerable<(string Code, string Name, string? Attribute1, string? Attribute2)> shifts)
    {
        var list = new List<ShiftWindow>();
        foreach (var (code, name, attr, work) in shifts)
        {
            if (string.IsNullOrWhiteSpace(code) || !ProdCalendar.TryParseWindow(attr, out var s, out var e)) continue;
            if (s >= Day || s == e) continue;
            if (e < s) e += Day;
            var w = WorkWindow(s, e, work);
            list.Add(new ShiftWindow(code, name, s, e, w?.Start, w?.End));
        }
        return list;
    }

    static (int Start, int End)? WorkWindow(int start, int end, string? attribute2)
    {
        if (!ProdCalendar.TryParseWindow(attribute2, out var a, out var b) || a == b) return null;
        if (b < a) b += Day;
        if (b <= start) { a += Day; b += Day; }   // 자정을 넘는 창에서 생산 시간이 자정 뒤에만 있는 경우
        int ws = Math.Max(start, a), we = Math.Min(end, b);
        return we > ws ? (ws, we) : null;
    }

    /// <summary>SHIFT_PATTERN.Attribute1 'A,B' → 가동 교대 코드(공백·빈 값 제외, 순서 유지).</summary>
    public static List<string> ParseShiftList(string? attribute1)
        => (attribute1 ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>확장 분 → 하루 시각 표시. 1440 은 24:00, 그보다 크면 다음 날 시각.</summary>
    public static string ToClock(int m)
    {
        if (m > Day) m -= Day;
        return $"{m / 60:00}:{m % 60:00}";
    }

    /// <summary>
    /// 저장 세그먼트(하루 분, 교대 코드 포함)를 새 교대 창의 확장 분 세그먼트로 다시 배치한다.
    /// 저장 세그먼트 상태가 그 교대의 기본 상태와 같으면 "바탕"으로 보고 버리고, 다르면 하루 시각 그대로 보존한다.
    /// 창 안에서 보존 구간이 없는 분은 새 교대의 기본 상태로 채운다. 결과는 교대·시작 순, 인접 같은 값은 병합.
    /// </summary>
    public static List<Band> Realign(IReadOnlyList<ShiftWindow> windows, IEnumerable<Band> stored, Func<string?, string> defaultStateOf)
    {
        // 하루 분 m 의 기본 상태: 그 교대 창 안이면 실제 생산 시간 기준, 창 밖이면 교대 가동 여부만
        string DefaultAtDay(string code, int m)
        {
            bool runs = defaultStateOf(code) == Operating;
            var w = windows.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.Ordinal));
            if (w is null) return runs ? Operating : Idle;
            int x = m >= w.Start ? m : m + Day;
            return x < w.End ? w.DefaultAt(x, runs) : (runs ? Operating : Idle);
        }

        var state  = new string?[Day];
        var reason = new string?[Day];
        var desc   = new string?[Day];
        foreach (var b in stored)
        {
            if (string.IsNullOrEmpty(b.State) || b.End <= b.Start) continue;
            for (int m = Math.Max(0, b.Start); m < Math.Min(Day, b.End); m++)
            {
                if (b.Shift is not null && string.Equals(b.State, DefaultAtDay(b.Shift, m), StringComparison.Ordinal)) continue;
                state[m] = b.State; reason[m] = b.Reason; desc[m] = b.Description;
            }
        }

        var result = new List<Band>();
        foreach (var w in windows)
        {
            bool runs = defaultStateOf(w.Code) == Operating;
            int i = w.Start;
            while (i < w.End)
            {
                int d = i % Day;
                var st = state[d] ?? w.DefaultAt(i, runs); var rs = state[d] is null ? null : reason[d]; var ds = state[d] is null ? null : desc[d];
                int j = i + 1;
                while (j < w.End)
                {
                    int dj = j % Day;
                    var st2 = state[dj] ?? w.DefaultAt(j, runs); var rs2 = state[dj] is null ? null : reason[dj];
                    if (st2 != st || rs2 != rs) break;
                    j++;
                }
                result.Add(new Band(i, j, st, rs, w.Code, ds));
                i = j;
            }
        }
        return result;
    }

    /// <summary>확장 분 구간 → 저장용 하루 분 조각(자정에서 둘로 나눔).</summary>
    public static IEnumerable<Band> Split(Band b)
    {
        if (b.End <= b.Start) yield break;
        if (b.End <= Day) { yield return b; yield break; }
        if (b.Start >= Day) { yield return b with { Start = b.Start - Day, End = b.End - Day }; yield break; }
        yield return b with { End = Day };
        yield return b with { Start = 0, End = b.End - Day };
    }

    /// <summary>
    /// 저장본이 현재 교대 창·패턴 교대와 맞지 않는지(MD-028 목록 표시용, 저장본은 바꾸지 않는다).
    /// 세그먼트가 없거나 교대 창이 하나도 없으면 판단하지 않는다(false).
    /// </summary>
    public static bool IsOutdated(IReadOnlyList<ShiftWindow> windows, IReadOnlyCollection<Band> stored, Func<string?, string> defaultStateOf)
        => windows.Count > 0 && stored.Count > 0
           && !SameLayout(stored, Realign(windows, stored, defaultStateOf).SelectMany(Split));

    /// <summary>하루 커버리지 문제 구간(하루 분). Overlap=false 는 빈 시간, true 는 겹친 시간.</summary>
    public sealed record CoverageIssue(int Start, int End, bool Overlap);

    /// <summary>
    /// 하루 분 구간들이 00:00–24:00 을 정확히 한 번씩 덮는지 검사한다. 빈 시간·겹친 시간을 시각 순으로 돌려주며, 문제가 없으면 빈 목록.
    /// 범위 밖(0 미만·1440 초과)은 잘라서 본다.
    /// </summary>
    public static List<CoverageIssue> CoverageIssues(IEnumerable<(int Start, int End)> dayRanges)
    {
        var count = new int[Day];
        foreach (var (s, e) in dayRanges)
            for (int m = Math.Max(0, s); m < Math.Min(Day, e); m++) count[m]++;
        var issues = new List<CoverageIssue>();
        int i = 0;
        while (i < Day)
        {
            if (count[i] == 1) { i++; continue; }
            bool overlap = count[i] > 1;
            int j = i + 1;
            while (j < Day && count[j] != 1 && count[j] > 1 == overlap) j++;
            issues.Add(new CoverageIssue(i, j, overlap));
            i = j;
        }
        return issues;
    }

    /// <summary>교대 창들의 하루 커버리지 문제 — 자정을 넘는 창은 두 조각으로 나눠 본다.</summary>
    public static List<CoverageIssue> CoverageIssues(IEnumerable<ShiftWindow> windows)
        => CoverageIssues(windows.SelectMany(w => Split(new Band(w.Start, w.End, Idle, null, w.Code, null))).Select(b => (b.Start, b.End)));

    /// <summary>문제 구간 표시용 문자열 'HH:mm–HH:mm'(쉼표 구분).</summary>
    public static string Describe(IEnumerable<CoverageIssue> issues)
        => string.Join(", ", issues.Select(x => $"{ToClock(x.Start)}–{ToClock(x.End)}"));

    /// <summary>두 저장 세그먼트 묶음이 같은 배치인지(순서·ID 무관, 하루 분·상태·교대·사유 비교).</summary>
    public static bool SameLayout(IEnumerable<Band> a, IEnumerable<Band> b)
    {
        static IEnumerable<string> Key(IEnumerable<Band> x) => x.Where(s => s.End > s.Start)
            .Select(s => $"{s.Start}|{s.End}|{s.State}|{s.Shift}|{s.Reason}").OrderBy(s => s, StringComparer.Ordinal);
        return Key(a).SequenceEqual(Key(b));
    }
}
