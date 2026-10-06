using System.Globalization;
using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps.Domain;

/// 사출 계획 자동 배정 + 판단 근거(traces). 규칙은 원본 응답 4개(calc_LQ10 · plan_LQ10 · plan_LQ10_b · plan_all)로 확정
/// (tools/rules-lab/scheduler_lab.py 839/840 — 87753-R5000 09-28 주간/야간 경계 한 칸만 1 차이):
///   1. 품번별 날짜 순: opening = 전일 remain(첫날 = openingStock + sisProduced − used + defect),
///      need = requirement − opening, own = need > 0 ? 올림_pack(need) : 0 (pack 0 은 정수 올림).
///   2. 형제 통일: 같은 moldCode 의 own 중 최대 = 블록 수량. 블록 시간 = round2(qty / uph).
///   3. 앞당기기(수량만 옮기고 소요는 다시 계산하지 않는다): 라인별로 뒤 날짜부터, 합계 > 능력인 날의 over 를 시간 큰 블록부터 보며
///      한 박스 시간 ph = pack/uph ≤ over 인 블록에서 k = min(floor(over/ph), floor(앞날 여유/ph), 박스 수) 박스를 가장 가까운 앞 근무일(여유 ≥ ph)로 한 건에 옮긴다.
///      옮길 것이 없으면 「{line} {date} 는 앞당길 여유가 없어 {over:0.0}시간 초과 상태로 둡니다.」 (소수 한 자리 고정: 「2.0시간」)
///   4. 교대(옮긴 뒤 최종 수량으로): 블록을 시간 내림차순으로 돌며 hours ≤ 주간 잔여 → 전량 주간 / 잔여 ≈ 0 → 전량 야간 /
///      잔여 < ph → 전량 야간(잔여 = 0) / 아니면 야간 = ceil((hours − 잔여) × uph), 주간 = floor_pack(qty − 야간), 잔여 = 0.
///   5. 끝에 「사출기 능력을 넘겨 N건을 앞당겼습니다: {mold} {from} → {to} ({qty}개) · …」 (4건까지, 더 있으면 「 …」). 라인은 사출 행 등장 순.
/// 잠긴 칸은 값을 그대로 두고(형제 통일·앞당기기에서 제외) 시간에는 넣는다. Explain 은 모든 칸을 잠긴 것으로 보고 근거만 만든다.
public static class InjectionScheduler
{
    public static List<string> Reschedule(PlanBundle b, ShiftRules rules, StageRules stages) => RescheduleWithTraces(b, rules, stages).warnings;

    public static (List<string> warnings, List<Trace> traces) RescheduleWithTraces(PlanBundle b, ShiftRules rules, StageRules stages)
    {
        var ctx = new Ctx(b, rules, stages, fixedAll: false);
        var warnings = ctx.Run();
        return (warnings, ctx.BuildTraces());
    }

    /// 계획을 바꾸지 않고 입력된 planDay/planNight 를 설명한다 (reschedule 없는 /api/plan/calc).
    public static List<Trace> Explain(PlanBundle b, ShiftRules rules, StageRules stages)
    {
        var ctx = new Ctx(b, rules, stages, fixedAll: true);
        ctx.Run();
        return ctx.BuildTraces();
    }

    // ---------------------------------------------------------------- 내부
    enum ShiftCase { None, AllDay, DayUsed, NoBox, Split }

    sealed class Cell
    {
        public double Opening, Need, Own, Final, Day, Night, Closing, DayRemBefore, Ratio, Raw, Hours;
        public ShiftCase Case;
        public bool Fixed;
        public List<(string from, double qty)> PulledIn = new();
        public List<(string to, double qty)> PulledOut = new();
    }

    sealed class Block
    {
        public string Mold = ""; public List<int> Parts = new(); public List<int> Free = new(); public double Qty, Uph; public int Pack;
        public int PackOr1 => Math.Max(1, Pack);
    }

    sealed class Ctx
    {
        readonly PlanBundle b; readonly ShiftRules rules; readonly bool fixedAll;
        readonly double[][] req; readonly int n;
        readonly List<(string PartNo, double Qty, int OffsetDays)> folds;   // 기준일 앞으로 떨어진 소요를 첫 사출일에 당긴 목록(경고)
        readonly Cell[][] cells;                       // [part][date]
        readonly double[] lineTotal, lineCap;          // key = lineIndex * n + date
        readonly List<string> lines; readonly Dictionary<string, int> lineIx = new(StringComparer.Ordinal);
        readonly List<(string mold, string from, string to, double qty)> moves = new();

        public Ctx(PlanBundle bundle, ShiftRules r, StageRules stages, bool fixedAll)
        {
            b = bundle; rules = r; this.fixedAll = fixedAll;
            (req, folds) = PlanCalc.RequirementsWithFolds(b, stages); n = b.Dates.Count;
            cells = b.Injection.Select(_ => Enumerable.Range(0, n).Select(_ => new Cell()).ToArray()).ToArray();
            lines = b.Injection.Select(x => x.LineCd ?? "").Distinct().ToList();   // 사출 행(품번순)에 처음 나오는 순서 — 원본 경고 순서
            for (var i = 0; i < lines.Count; i++) lineIx[lines[i]] = i;
            lineTotal = new double[lines.Count * n]; lineCap = new double[lines.Count * n];
        }

        static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        static double FloorPack(double v, int pack) => pack > 1 ? Math.Floor(v / pack) * pack : Math.Floor(v);
        double Cap(string line, int i) => lineCap[lineIx[line] * n + i];

        public List<string> Run()
        {
            var warnings = new List<string>();
            foreach (var (part, qty, off) in folds)
                warnings.Add($"사출품 {part}: 기준일 이전에 찍었어야 할 소요 {N(qty)}개(선행일 {off}일)를 기준일 {b.Dates[0]} 사출로 당겼습니다.");
            foreach (var line in lines)
            {
                var parts = Enumerable.Range(0, b.Injection.Count).Where(k => (b.Injection[k].LineCd ?? "") == line).ToList();
                var molds = parts.GroupBy(k => b.Injection[k].MoldGroupKey(), StringComparer.Ordinal)
                                 .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (mold: g.Key, parts: g.ToList())).ToList();
                for (var i = 0; i < n; i++) { var sh = rules.ShiftFor(line, b.Dates[i], true); lineCap[lineIx[line] * n + i] = sh.day + sh.night; }
                foreach (var k in parts) for (var i = 0; i < n; i++) cells[k][i].Fixed = fixedAll || b.Injection[k].Days[i].Locked;

                var blocks = GrossBlocks(parts, molds);            // 1~2
                if (!fixedAll) PullForward(line, blocks, warnings);   // 3
                SplitShifts(line, blocks);                         // 4
                FinalPass(parts);                                  // 근거용 opening · own · closing
            }
            if (moves.Count > 0)
            {
                var list = string.Join(" · ", moves.Take(4).Select(m => $"{m.mold} {m.from} → {m.to} ({N(m.qty)}개)"));
                warnings.Add($"사출기 능력을 넘겨 {moves.Count}건을 앞당겼습니다: {list}" + (moves.Count > 4 ? " …" : ""));
            }
            for (var k = 0; k < b.Injection.Count; k++)
                for (var i = 0; i < n; i++)
                {
                    var d = b.Injection[k].Days[i];
                    if (fixedAll || d.Locked) continue;
                    d.PlanDay = cells[k][i].Day; d.PlanNight = cells[k][i].Night;
                }
            return warnings;
        }

        double FirstStock(int k) { var r = b.Injection[k]; return r.OpeningStock + r.SisProduced - r.Used + r.Defect; }

        /// 1~2: 소요 → own → 금형 블록 (한 번만). 잠긴 칸은 자기 값, 통일에서 제외.
        List<Dictionary<string, Block>> GrossBlocks(List<int> parts, List<(string mold, List<int> parts)> molds)
        {
            var remain = parts.ToDictionary(k => k, FirstStock);
            var blocks = new List<Dictionary<string, Block>>();
            for (var i = 0; i < n; i++)
            {
                var own = new Dictionary<int, double>();
                foreach (var k in parts)
                {
                    var r = b.Injection[k]; var d = r.Days[i];
                    var need = req[k][i] - remain[k];
                    own[k] = cells[k][i].Fixed ? d.PlanDay + d.PlanNight : need > 0 ? ShiftRules.RoundUp(need, r.PackSize) : 0;
                }
                var day = new Dictionary<string, Block>(StringComparer.Ordinal);
                foreach (var (mold, ks) in molds)
                {
                    var free = ks.Where(k => !cells[k][i].Fixed).ToList();
                    var qty = free.Count > 0 ? free.Max(k => own[k]) : 0;
                    day[mold] = new Block { Mold = mold, Parts = ks, Free = free, Qty = qty, Uph = ks.Max(k => b.Injection[k].Uph), Pack = ks.Max(k => b.Injection[k].PackSize) };
                    foreach (var k in ks) remain[k] += (cells[k][i].Fixed ? own[k] : qty) - req[k][i];
                }
                blocks.Add(day);
            }
            return blocks;
        }

        /// 블록 시간(잠긴 품번은 자기 수량으로 센다).
        double BlockHours(Block blk, int i)
        {
            var qty = blk.Qty;
            foreach (var k in blk.Parts.Where(k => cells[k][i].Fixed)) qty = Math.Max(qty, b.Injection[k].Days[i].PlanDay + b.Injection[k].Days[i].PlanNight);
            return blk.Uph > 0 && qty > 0 ? R2(qty / blk.Uph) : 0;
        }
        double Total(Dictionary<string, Block> day, int i) => day.Values.Sum(blk => BlockHours(blk, i));

        /// 3: 뒤 날짜부터, 초과분만큼 박스를 앞날로 옮긴다 (소요 재계산 없음).
        void PullForward(string line, List<Dictionary<string, Block>> blocks, List<string> warnings)
        {
            for (var i = n - 1; i >= 0; i--)
            {
                while (true)
                {
                    var over = Total(blocks[i], i) - Cap(line, i);
                    if (over <= 1e-9) break;
                    var moved = false;
                    foreach (var blk in blocks[i].Values.OrderByDescending(x => BlockHours(x, i)).ThenBy(x => x.Mold, StringComparer.Ordinal))
                    {
                        if (blk.Qty <= 0 || blk.Uph <= 0 || blk.Free.Count == 0) continue;
                        var pack = blk.PackOr1; var ph = pack / blk.Uph;
                        if (ph > over + 1e-9) continue;
                        // 잠긴 형제가 블록 시간을 붙들고 있으면(잠긴 수량 ≥ 자유 수량 − 한 박스) 자유 박스를 옮겨도 초과가 줄지 않는다
                        var lockedMax = blk.Parts.Where(k => cells[k][i].Fixed).Select(k => b.Injection[k].Days[i].PlanDay + b.Injection[k].Days[i].PlanNight).DefaultIfEmpty(0).Max();
                        if (blk.Qty - pack < lockedMax - 1e-9 && lockedMax >= blk.Qty - 1e-9) continue;
                        var movable = (int)Math.Floor((blk.Qty - Math.Max(0, lockedMax)) / pack + 1e-9);
                        if (movable < 1) continue;
                        for (var j = i - 1; j >= 0; j--)
                        {
                            if (blocks[j][blk.Mold].Free.Count == 0) continue;   // 그 날 금형 칸이 잠겨 있으면 받을 수 없다 (옮긴 박스가 사라진다)
                            var room = Cap(line, j) - Total(blocks[j], j);
                            if (room + 1e-9 < ph) continue;
                            var k = (int)Math.Max(1, Math.Min(Math.Floor(over / ph + 1e-9), Math.Min(Math.Floor(room / ph + 1e-9), movable)));
                            var qty = k * pack;
                            blk.Qty -= qty; blocks[j][blk.Mold].Qty += qty;
                            moves.Add((blk.Mold, b.Dates[i], b.Dates[j], qty));
                            foreach (var p in blk.Parts) { cells[p][j].PulledIn.Add((b.Dates[i], qty)); cells[p][i].PulledOut.Add((b.Dates[j], qty)); }
                            moved = true; break;
                        }
                        if (moved) break;
                    }
                    if (!moved)
                    {
                        warnings.Add($"{line} {b.Dates[i]} 는 앞당길 여유가 없어 {over.ToString("0.0", CultureInfo.InvariantCulture)}시간 초과 상태로 둡니다.");
                        break;
                    }
                }
            }
        }

        /// 4: 최종 수량으로 주간/야간을 나눈다 (시간 큰 블록부터).
        void SplitShifts(string line, List<Dictionary<string, Block>> blocks)
        {
            for (var i = 0; i < n; i++)
            {
                var sh = rules.ShiftFor(line, b.Dates[i], true);
                var dayRem = sh.day;
                foreach (var blk in blocks[i].Values.OrderByDescending(x => BlockHours(x, i)).ThenBy(x => x.Mold, StringComparer.Ordinal))
                {
                    var h = BlockHours(blk, i);
                    foreach (var k in blk.Parts) cells[k][i].Hours = h;
                    foreach (var k in blk.Parts.Where(k => cells[k][i].Fixed)) { var c = cells[k][i]; c.Day = b.Injection[k].Days[i].PlanDay; c.Night = b.Injection[k].Days[i].PlanNight; }
                    if (blk.Free.Count == 0)
                    {   // 잠긴 블록: 입력된 주간 몫만큼 주간을 쓴다
                        var dayQty = blk.Parts.Max(k => b.Injection[k].Days[i].PlanDay);
                        if (blk.Uph > 0) dayRem -= R2(dayQty / blk.Uph);
                        continue;
                    }
                    var q = blk.Qty; double day; var kase = ShiftCase.None; double ratio = 0, raw = 0; var before = dayRem;
                    if (q <= 0 || blk.Uph <= 0) day = q;
                    else if (h <= dayRem + 1e-9) { day = q; dayRem -= h; kase = ShiftCase.AllDay; }
                    else if (dayRem <= 1e-9) { day = 0; kase = ShiftCase.DayUsed; }
                    else if (dayRem < R2(blk.PackOr1 / blk.Uph) - 1e-9) { day = 0; kase = ShiftCase.NoBox; dayRem = 0; }   // 원본: 한 박스도 못 채운 뒤의 블록은 「앞 작업이 주간을 다 썼습니다」
                    else
                    {
                        ratio = dayRem / h; raw = Math.Round(q * ratio, MidpointRounding.AwayFromZero);   // 문구용 「324 × 0.898 = 291」
                        var night = Math.Ceiling((h - dayRem) * blk.Uph);                                // 주간 잔여를 넘는 만큼은 야간 (669: 39.00…02 → 40)
                        day = FloorPack(Math.Max(0, q - night), blk.Pack); dayRem = 0; kase = ShiftCase.Split;
                    }
                    foreach (var k in blk.Free)
                    {
                        var c = cells[k][i];
                        c.Day = day; c.Night = q - day; c.Case = kase; c.Ratio = ratio; c.Raw = raw; c.DayRemBefore = Math.Max(0, before);
                    }
                }
                lineTotal[lineIx[line] * n + i] = Total(blocks[i], i);
            }
        }

        /// 근거용: 최종 계획으로 opening · need · own · closing 을 다시 굴린다 (원본 트레이스 「356 → 368」은 옮긴 뒤 재고 기준).
        void FinalPass(List<int> parts)
        {
            var remain = parts.ToDictionary(k => k, FirstStock);
            for (var i = 0; i < n; i++)
                foreach (var k in parts)
                {
                    var r = b.Injection[k]; var c = cells[k][i];
                    c.Opening = remain[k]; c.Need = req[k][i] - remain[k];
                    c.Own = c.Fixed ? r.Days[i].PlanDay + r.Days[i].PlanNight : c.Need > 0 ? ShiftRules.RoundUp(c.Need, r.PackSize) : 0;
                    c.Final = c.Day + c.Night;
                    c.Closing = c.Opening + c.Final - req[k][i]; remain[k] = c.Closing;
                }
        }

        // ------------------------------------------------------------ 근거
        static readonly string[] Circled = { "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨" };
        static string Num(int i) => i <= 9 ? Circled[i - 1] : $"({i})";
        static string N(double v) => v.ToString("#,##0.##", CultureInfo.InvariantCulture);
        static string H1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        static string H2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string MD(string date) => date.Length >= 10 ? $"{date[5..7]}/{date[8..10]}" : date;

        public List<Trace> BuildTraces()
        {
            var traces = new List<Trace>();
            for (var k = 0; k < b.Injection.Count; k++)
            {
                var r = b.Injection[k]; var line = r.LineCd ?? "";
                for (var i = 0; i < n; i++)
                {
                    var c = cells[k][i]; var reqv = req[k][i]; var date = b.Dates[i];
                    var t = new Trace
                    {
                        PartNo = r.PartNo, Date = date, OpeningStock = c.Opening, Requirement = reqv, Shortfall = Math.Max(0, c.Need), Planned = c.Final,
                        PlanDay = c.Day, PlanNight = c.Night, Closing = c.Closing, PackSize = r.PackSize, Uph = r.Uph,
                        RunHours = r.Uph > 0 ? R2(c.Final / r.Uph) : 0, MoldGroup = r.MoldCode, Siblings = r.SiblingPartNos.ToList(),
                    };
                    var no = 0;
                    void Add(string label, string calc, string? screen = null, bool warn = false) =>
                        t.Steps.Add(new TraceStep { Label = label.Length > 0 ? $"{Num(++no)} {label}" : "", Calc = calc, Screen = screen, Warn = warn });

                    if (reqv > 0) Add("소요", $"상위 완제품 계획을 BOM 으로 전개 = {N(reqv)}", $"소요 {N(reqv)}");
                    else Add("소요", "이 날 상위 완제품 계획이 없습니다.", "소요 -");
                    Add("부족분", c.Need > 0 ? $"소요 {N(reqv)} − 실재고 {N(c.Opening)} = {N(c.Need)}"
                                          : $"소요 {N(reqv)} − 실재고 {N(c.Opening)} = {N(c.Need)} → 재고로 충당됩니다");
                    if (c.Final <= 0)
                    {
                        Add("판단", "만들지 않습니다.", "주간 0 · 야간 0");
                        Add("잔여재고", $"{N(c.Opening)} + 0 − {N(reqv)} = {N(c.Closing)}", $"잔여재고 {N(c.Closing)}");
                        traces.Add(t); continue;
                    }
                    var prodScreen = $"주간 {N(c.Day)} + 야간 {N(c.Night)}";
                    var hasProd = c.Fixed || Math.Abs(c.Final - c.Own) > 1e-9;
                    if (!c.Fixed && c.Need > 0)
                    {
                        var pack = r.PackSize; var boxes = pack > 1 ? (int)Math.Round(c.Own / pack) : 0;
                        if (pack > 1)
                            Add("포장 올림", Math.Abs(c.Need % pack) < 1e-9 ? $"{N(c.Need)} 은 {pack}개들이로 딱 떨어집니다 → {N(c.Own)} ({boxes}박스)"
                                                                            : $"{N(c.Need)} → {pack}개들이 → {N(c.Own)} ({boxes}박스)", hasProd ? null : prodScreen);
                        else
                            Add("생산량", $"포장 단위가 없어 부족분 그대로 {N(c.Own)}", hasProd ? null : prodScreen);   // 원본 plan_all (pack 0 품번)
                    }
                    if (c.PulledIn.Count > 0)   // 여러 날에서 당겨 오면 「09/29 물량 600개 · 09/28 물량 69개를 …」
                        Add("앞당김", string.Join(" · ", c.PulledIn.Select(x => $"{MD(x.from)} 물량 {N(x.qty)}개")) + "를 이 날로 당겨 왔습니다 — 그날은 사출기 능력을 넘깁니다.", null, true);
                    if (c.PulledOut.Count > 0)
                        Add("앞당김", "이 날 물량 중 " + string.Join(" · ", c.PulledOut.Select(x => $"{N(x.qty)}개는 {MD(x.to)}로")) + " 앞당겼습니다 — 이 날은 사출기 능력을 넘깁니다.", null, true);
                    if (hasProd)
                        Add("생산량", c.Fixed ? $"입력한 계획대로 이 날은 {N(c.Final)}개" : $"앞당기기를 반영해 이 날은 {N(c.Final)}개", prodScreen);
                    if (r.SiblingPartNos.Count > 0)
                        Add("동시취출", $"{r.MoldCode} 금형에서 {string.Join(", ", r.SiblingPartNos)} 와 같이 나오므로 가동시간을 함께 셉니다.");
                    if (r.Uph > 0) Add("가동시간", $"{N(c.Final)} ÷ UPH {H2(r.Uph)} = {H2(t.RunHours)}h", $"{N(c.Final)} · {H2(t.RunHours)}h");   // UPH 는 소수 2자리(「70.00」)
                    else Add("가동시간", "UPH 가 없어 가동시간을 계산할 수 없습니다.", $"{N(c.Final)} · -");
                    switch (c.Case)
                    {
                        case ShiftCase.AllDay:
                            Add("주간 몫", $"{H2(t.RunHours)}h 가 주간 잔여 {H1(c.DayRemBefore)}h 안에 들어갑니다 → 전량 주간", $"주간 {N(c.Final)}"); break;
                        case ShiftCase.DayUsed:
                            Add("주간 몫", "같은 사출기의 앞 작업이 주간을 다 썼습니다 → 전량 야간", $"야간 {N(c.Final)}"); break;
                        case ShiftCase.NoBox:
                            Add("주간 몫", $"주간 잔여 {H1(c.DayRemBefore)}h 로는 {r.PackSize}개들이 한 박스도 못 채웁니다 → 전량 야간", $"야간 {N(c.Final)}"); break;
                        case ShiftCase.Split:
                            var ratio = c.Ratio.ToString("0.000", CultureInfo.InvariantCulture);
                            Add("주간 몫", $"{H2(t.RunHours)}h 중 주간은 {H1(c.DayRemBefore)}h 까지 → 비율 {ratio}");
                            Add("", r.PackSize > 1 ? $"{N(c.Final)} × {ratio} = {N(c.Raw)} → {r.PackSize} 으로 내림 = {N(c.Day)}" : $"{N(c.Final)} × {ratio} = {N(c.Raw)} → {N(c.Day)}", $"주간 {N(c.Day)}");
                            Add("나머지", $"{N(c.Final)} − {N(c.Day)} = {N(c.Night)} → 야간", $"야간 {N(c.Night)}"); break;
                    }
                    var total = lineTotal[lineIx[line] * n + i]; var cap = lineCap[lineIx[line] * n + i];
                    var pct = cap > 0 ? Math.Round(total / cap * 100, MidpointRounding.AwayFromZero) : 0;
                    var over = total > cap + 0.005;
                    Add("사출기 부하", $"{line} 이 날 합계 {H1(total)}h / {H1(cap)}h ({pct}%)" + (over ? " — 능력 초과" : ""), $"{H1(total)}h · {pct}%", over);
                    Add("잔여재고", $"{N(c.Opening)} + {N(c.Final)} − {N(reqv)} = {N(c.Closing)}", $"잔여재고 {N(c.Closing)}");
                    traces.Add(t);
                }
            }
            return traces;
        }
    }
}
