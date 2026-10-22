using System.Globalization;
using System.Text.RegularExpressions;

namespace AMES.Data.Services;

/// <summary>
/// MD-003 품목·MD-004 BOM 엑셀 내려받기/올리기 공용 규칙(10-20). 올리기는 <b>없는 것만 등록하고 있는 것은 건너뛴다</b>(수정하지 않는다).
/// 머리글은 "화면 이름 (키)" — 키(괄호 안 영문)로 열을 찾으므로 언어가 달라도, 열 순서를 바꿔도 읽힌다. 키만 적어도 된다.
/// 파일 읽기(xlsx)는 Web 이 하고 여기는 셀 문자열 행만 받는다.
/// </summary>
public static class MasterExcel
{
    /// <summary>문제 하나 — 행 번호(엑셀 행, 머리글 = 1), 열 키, 사유 코드(화면이 다국어로 바꾼다), 값.</summary>
    public sealed record Problem(int Line, string? Key, string Code, string? Value = null);

    public static class Codes
    {
        public const string NoHeader    = "NoHeader";     // 필수 열이 머리글에 없다(양식이 아님)
        public const string Required    = "Required";
        public const string BadNumber   = "BadNumber";
        public const string BadFlag     = "BadFlag";
        public const string BadDate     = "BadDate";
        public const string Unknown     = "Unknown";      // 코드·품번·단위가 마스터에 없다
        public const string Duplicate   = "Duplicate";    // 같은 파일 안에 같은 키가 또 있다
        public const string OutOfRange  = "OutOfRange";
        public const string PastDate    = "PastDate";     // 등록은 오늘부터(MD-004 등록 규칙)
        public const string DateOrder   = "DateOrder";
        public const string TooLong     = "TooLong";
        public const string SelfComp    = "SelfComp";
        public const string Cycle       = "Cycle";
        public const string HeaderDiff  = "HeaderDiff";   // 같은 버전의 행끼리 버전 정보가 다르다
        public const string RootDiff    = "RootDiff";     // 이미 있는 버전과 최상위 품번이 다르다
        public const string Packaging   = "Packaging";    // BOX·PALLET 은 수량 단위가 아니다
    }

    static readonly Regex KeyInParens = new(@"\(([A-Za-z][A-Za-z0-9]*)\)\s*$", RegexOptions.CultureInvariant);

    /// <summary>머리글 텍스트 → 키. "품번 (ItemNo)" → ItemNo, 괄호가 없으면 텍스트 그대로.</summary>
    public static string HeaderKey(string? header)
    {
        var h = (header ?? "").Trim();
        var m = KeyInParens.Match(h);
        return m.Success ? m.Groups[1].Value : h;
    }

    public static string Header(string label, string key) => $"{label} ({key})";

    /// <summary>머리글 행에서 키 → 열 위치. 알 수 없는 열은 무시, 같은 키가 둘이면 앞 열.</summary>
    public static Dictionary<string, int> MapHeader(IReadOnlyList<string> header, IEnumerable<string> keys)
    {
        var known = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            var k = HeaderKey(header[i]);
            if (known.Contains(k) && !map.ContainsKey(k)) map[k] = i;
        }
        return map;
    }

    /// <summary>한 행의 값 읽기 — 열이 없거나 빈칸이면 null(앞뒤 공백 제거).</summary>
    public sealed class RowReader(string[] cells, IReadOnlyDictionary<string, int> map, int line, List<Problem> problems)
    {
        public int Line { get; } = line;
        public bool IsEmpty => cells.All(string.IsNullOrWhiteSpace);

        public string? Text(string key)
        {
            if (!map.TryGetValue(key, out var i) || i >= cells.Length) return null;
            var v = cells[i]?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public string? Code(string key) => Text(key)?.ToUpperInvariant();

        public string? Required(string key)
        {
            var v = Text(key);
            if (v is null) problems.Add(new(Line, key, Codes.Required));
            return v;
        }

        public decimal? Decimal(string key)
        {
            var v = Text(key);
            if (v is null) return null;
            if (decimal.TryParse(v, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var d)) return d;
            problems.Add(new(Line, key, Codes.BadNumber, v));
            return null;
        }

        public int? Int(string key)
        {
            var d = Decimal(key);
            if (d is null) return null;
            if (d == Math.Truncate(d.Value) && d >= int.MinValue && d <= int.MaxValue) return (int)d.Value;
            problems.Add(new(Line, key, Codes.BadNumber, Text(key)));
            return null;
        }

        /// <summary>1/0 · Y/N · TRUE/FALSE · 예/아니오. 빈칸은 기본값.</summary>
        public bool Flag(string key, bool dflt)
        {
            var v = Text(key);
            if (v is null) return dflt;
            switch (v.ToUpperInvariant())
            {
                case "1": case "Y": case "YES": case "TRUE": case "예": case "O": return true;
                case "0": case "N": case "NO": case "FALSE": case "아니오": case "X": return false;
            }
            problems.Add(new(Line, key, Codes.BadFlag, v));
            return dflt;
        }

        /// <summary>yyyy-MM-dd(또는 / .) · 엑셀이 날짜로 바꾼 일련번호.</summary>
        public DateOnly? Date(string key)
        {
            var v = Text(key);
            if (v is null) return null;
            if (DateOnly.TryParseExact(v, ["yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyy-M-d", "yyyy/M/d"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 2958466)
                return DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serial)));
            problems.Add(new(Line, key, Codes.BadDate, v));
            return null;
        }

        public void Problem(string? key, string code, string? value = null) => problems.Add(new(Line, key, code, value));
    }

    /// <summary>데이터 행(머리글 다음부터, 빈 행 제외)을 행 번호와 함께 돌려준다. 필수 열이 없으면 NoHeader 문제만 남기고 빈 목록.</summary>
    public static List<RowReader> Rows(List<string[]> sheet, IEnumerable<string> keys, IEnumerable<string> requiredKeys, List<Problem> problems)
    {
        if (sheet.Count == 0) { problems.Add(new(1, null, Codes.NoHeader)); return []; }
        var map = MapHeader(sheet[0], keys);
        var missing = requiredKeys.Where(k => !map.ContainsKey(k)).ToList();
        if (missing.Count > 0) { problems.Add(new(1, string.Join(", ", missing), Codes.NoHeader)); return []; }
        var list = new List<RowReader>();
        for (int i = 1; i < sheet.Count; i++)
        {
            var r = new RowReader(sheet[i], map, i + 1, problems);
            if (!r.IsEmpty) list.Add(r);
        }
        return list;
    }
}

/// <summary>MD-003 품목 엑셀 — 열 키·행 해석. 이미 있는 품번은 건너뛴다(수정하지 않음).</summary>
public static class ItemImport
{
    public static readonly string[] Keys =
    [
        "ItemNo", "ItemName", "ItemType", "ItemCategory", "InjFlag", "CarType", "DefaultUOM", "RoutingType",
        "SafetyStock", "MinStock", "MaxStock", "UnitCost", "PGN", "ALC", "DrawingNo",
        "PalletQty", "MaxPalletQty", "ToteFlag", "ScanRequired", "IssueAddPct", "MrpAllowPct",
        "ProcureType", "ActiveFlag",
    ];
    public static readonly string[] RequiredKeys = ["ItemNo", "ItemName"];

    public sealed record Row(int Line, string ItemNo, string ItemName, string? ItemType, string? ItemCategory, bool InjFlag,
        string? CarType, string? DefaultUom, string? RoutingType, decimal? SafetyStock, decimal? MinStock, decimal? MaxStock,
        decimal? UnitCost, string? Pgn, string? Alc, string? DrawingNo, int? PalletQty, int? MaxPalletQty, bool ToteFlag,
        bool ScanRequired, decimal? IssueAddPct, decimal? MrpAllowPct, string? ProcureType, bool ActiveFlag);

    /// <summary>검사에 쓰는 마스터 — 코드는 사용 중인 것만, 카테고리는 코드 → 허용 유형(빈 집합 = 제한 없음).</summary>
    public sealed record Lookup(
        IReadOnlySet<string> ExistingItems, IReadOnlySet<string> ItemTypes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> Categories, IReadOnlySet<string> Uoms,
        IReadOnlySet<string> Routings, IReadOnlySet<string> ProcureTypes);

    public sealed record Result(List<Row> New, List<(int Line, string ItemNo)> Existing, List<MasterExcel.Problem> Problems);

    public const int ItemNoMax = 20;

    public static Result Parse(List<string[]> sheet, Lookup lk)
    {
        var problems = new List<MasterExcel.Problem>();
        var rows = MasterExcel.Rows(sheet, Keys, RequiredKeys, problems);
        var fresh = new List<Row>();
        var existing = new List<(int, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            int before = problems.Count;
            var no = r.Required("ItemNo")?.ToUpperInvariant();
            var name = r.Required("ItemName");
            if (no is not null && no.Length > ItemNoMax) r.Problem("ItemNo", MasterExcel.Codes.TooLong, no);
            if (no is not null && !seen.Add(no)) { r.Problem("ItemNo", MasterExcel.Codes.Duplicate, no); continue; }
            if (no is not null && lk.ExistingItems.Contains(no)) { existing.Add((r.Line, no)); continue; }   // 있는 품번 — 다른 칸은 보지 않는다

            var type = r.Code("ItemType");
            if (type is not null && !lk.ItemTypes.Contains(type)) r.Problem("ItemType", MasterExcel.Codes.Unknown, type);
            var cat = r.Code("ItemCategory");
            if (cat is not null)
            {
                if (!lk.Categories.TryGetValue(cat, out var allowed)) r.Problem("ItemCategory", MasterExcel.Codes.Unknown, cat);
                else if (allowed.Count > 0 && (type is null || !allowed.Contains(type))) r.Problem("ItemCategory", MasterExcel.Codes.OutOfRange, cat);
            }
            var uom = r.Code("DefaultUOM");
            if (uom is not null)
            {
                if (!Repositories.MasterDataRepository.IsQuantityUnit(uom)) r.Problem("DefaultUOM", MasterExcel.Codes.Packaging, uom);
                else if (!lk.Uoms.Contains(uom)) r.Problem("DefaultUOM", MasterExcel.Codes.Unknown, uom);
            }
            var route = r.Code("RoutingType");
            if (route is not null && !lk.Routings.Contains(route)) r.Problem("RoutingType", MasterExcel.Codes.Unknown, route);
            var proc = r.Code("ProcureType");
            if (proc is not null && !lk.ProcureTypes.Contains(proc)) r.Problem("ProcureType", MasterExcel.Codes.Unknown, proc);

            decimal? Pct(string key)
            {
                var v = r.Decimal(key);
                if (v is < 0 or > Repositories.MasterDataRepository.AllowancePctMax) { r.Problem(key, MasterExcel.Codes.OutOfRange, v.Value.ToString(CultureInfo.InvariantCulture)); return null; }
                return v;
            }
            var row = new Row(r.Line, no ?? "", name ?? "", type, cat, r.Flag("InjFlag", false), r.Text("CarType"), uom, route,
                r.Decimal("SafetyStock"), r.Decimal("MinStock"), r.Decimal("MaxStock"), r.Decimal("UnitCost"),
                r.Text("PGN"), r.Text("ALC"), r.Text("DrawingNo"), r.Int("PalletQty"), r.Int("MaxPalletQty"), r.Flag("ToteFlag", false),
                r.Flag("ScanRequired", false), Pct("IssueAddPct"), Pct("MrpAllowPct"), proc, r.Flag("ActiveFlag", true));
            if (problems.Count == before) fresh.Add(row);
        }
        return new Result(fresh, existing, problems);
    }
}

/// <summary>
/// MD-004 BOM 엑셀 — 한 행 = 버전 정보 + 구성 라인 하나(라인 없는 버전은 구성품 칸을 비운 한 행).
/// 버전이 없으면 DRAFT 로 만들고 라인을 넣는다. 있는 버전이 DRAFT 면 그 버전에 없는 구성품만 더하고(있는 구성품은 건너뜀),
/// DRAFT 가 아니면(승인 요청·승인 등) 버전째 건너뛴다. 한 버전에 문제가 하나라도 있으면 그 버전은 통째로 등록하지 않는다(반쪽 BOM 방지).
/// 새 버전의 시작일·종료일은 화면 등록과 같이 오늘부터만.
/// 단위(UOM) 열은 내려받기 참고용이다 — 올리면 읽지 않고 구성품의 기본 단위로 저장한다(10-23).
/// </summary>
public static class BomImport
{
    public static readonly string[] Keys =
    [
        "VersionID", "RootItemNo", "VersionNo", "EffFrom", "EffTo", "ChangeType", "ChangeReason", "Status",
        "CompItemNo", "QtyPer", "UOM", "ScrapPct", "Position", "Note",
    ];
    public static readonly string[] RequiredKeys = ["VersionID", "RootItemNo", "CompItemNo", "QtyPer"];

    public sealed record Line(int Row, string CompItemNo, decimal QtyPer, decimal? ScrapPct, int Position, string? Note);

    public sealed record Version(string VersionId, bool IsNew, string RootItemNo, string? VersionNo, DateOnly? EffFrom, DateOnly? EffTo,
        string? ChangeType, string? ChangeReason, List<Line> Lines);

    /// <summary>있는 버전 — MaxPosition 은 그 버전 라인의 가장 큰 순서(순서를 비운 새 라인은 그 다음 10 단위로 붙인다).</summary>
    public sealed record ExistingVersion(string Status, string? RootItemNo, IReadOnlySet<string> CompItems, int MaxPosition = 0);

    public sealed record Lookup(IReadOnlyDictionary<string, ExistingVersion> Versions, IReadOnlySet<string> Items,
        DateOnly Today, Func<string, string, bool> WouldCycle);

    public sealed record Result(List<Version> Plans, List<(int Row, string VersionId, string Status)> SkippedVersions,
        List<(int Row, string VersionId, string CompItemNo)> SkippedLines, List<MasterExcel.Problem> Problems);

    public const int VersionIdMax = 24;

    public static Result Parse(List<string[]> sheet, Lookup lk)
    {
        var problems = new List<MasterExcel.Problem>();
        var rows = MasterExcel.Rows(sheet, Keys, RequiredKeys, problems);
        var plans = new List<Version>();
        var skippedVersions = new List<(int, string, string)>();
        var skippedLines = new List<(int, string, string)>();

        var groups = new List<(string Id, List<MasterExcel.RowReader> Rows)>();
        foreach (var r in rows)
        {
            var id = r.Required("VersionID")?.ToUpperInvariant();
            if (id is null) continue;
            var g = groups.FirstOrDefault(x => x.Id == id);
            if (g.Rows is null) groups.Add((id, [r])); else g.Rows.Add(r);
        }

        foreach (var (id, grp) in groups)
        {
            int before = problems.Count;
            var head = grp[0];
            if (id.Length > VersionIdMax) head.Problem("VersionID", MasterExcel.Codes.TooLong, id);
            var root = head.Required("RootItemNo")?.ToUpperInvariant();
            foreach (var r in grp.Skip(1))
                if (!string.Equals(r.Text("RootItemNo"), head.Text("RootItemNo"), StringComparison.OrdinalIgnoreCase)
                    || r.Text("VersionNo") != head.Text("VersionNo") || r.Text("EffFrom") != head.Text("EffFrom") || r.Text("EffTo") != head.Text("EffTo"))
                    r.Problem("VersionID", MasterExcel.Codes.HeaderDiff, id);

            lk.Versions.TryGetValue(id, out var exists);
            if (exists is not null && !string.Equals(exists.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
            {
                skippedVersions.Add((head.Line, id, exists.Status));
                problems.RemoveRange(before, problems.Count - before);   // 건너뛰는 버전은 따지지 않는다
                continue;
            }

            string? versionNo = null; DateOnly? from = null, to = null;
            if (exists is null)
            {
                if (root is not null && !lk.Items.Contains(root)) head.Problem("RootItemNo", MasterExcel.Codes.Unknown, root);
                var vno = head.Required("VersionNo");
                if (vno is not null)
                {
                    if (BomVersionRules.TryNormalizeVersionNo(vno, out var n)) versionNo = n;
                    else head.Problem("VersionNo", MasterExcel.Codes.BadNumber, vno);
                }
                from = head.Date("EffFrom");
                to = head.Date("EffTo");
                if (from is null && head.Text("EffFrom") is null) head.Problem("EffFrom", MasterExcel.Codes.Required);
                if (from < lk.Today) head.Problem("EffFrom", MasterExcel.Codes.PastDate, from.Value.ToString("yyyy-MM-dd"));
                if (to < lk.Today) head.Problem("EffTo", MasterExcel.Codes.PastDate, to.Value.ToString("yyyy-MM-dd"));
                if (from is not null && to is not null && to < from) head.Problem("EffTo", MasterExcel.Codes.DateOrder, to.Value.ToString("yyyy-MM-dd"));
            }
            else if (!string.Equals(exists.RootItemNo, root, StringComparison.OrdinalIgnoreCase))
                head.Problem("RootItemNo", MasterExcel.Codes.RootDiff, root);

            var lines = new List<Line>();
            var comps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nextPos = (exists?.MaxPosition ?? 0) + 10;
            foreach (var r in grp)
            {
                var comp = r.Code("CompItemNo");
                if (comp is null) { if (r.Text("QtyPer") is not null) r.Problem("CompItemNo", MasterExcel.Codes.Required); continue; }   // 라인 없는 버전 행
                if (!comps.Add(comp)) { r.Problem("CompItemNo", MasterExcel.Codes.Duplicate, comp); continue; }
                if (exists is not null && exists.CompItems.Contains(comp)) { skippedLines.Add((r.Line, id, comp)); continue; }
                if (!lk.Items.Contains(comp)) r.Problem("CompItemNo", MasterExcel.Codes.Unknown, comp);
                else if (string.Equals(comp, root, StringComparison.OrdinalIgnoreCase)) r.Problem("CompItemNo", MasterExcel.Codes.SelfComp, comp);
                else if (root is not null && lk.WouldCycle(root, comp)) r.Problem("CompItemNo", MasterExcel.Codes.Cycle, comp);
                var qty = r.Decimal("QtyPer");
                if (qty is null && r.Text("QtyPer") is null) r.Problem("QtyPer", MasterExcel.Codes.Required);
                else if (qty <= 0) r.Problem("QtyPer", MasterExcel.Codes.OutOfRange, qty.Value.ToString(CultureInfo.InvariantCulture));
                var scrap = r.Decimal("ScrapPct");
                if (scrap is < 0 or > 100) r.Problem("ScrapPct", MasterExcel.Codes.OutOfRange, scrap.Value.ToString(CultureInfo.InvariantCulture));
                var pos = r.Int("Position") ?? nextPos;
                nextPos = Math.Max(nextPos, pos + 10);
                lines.Add(new Line(r.Line, comp, qty ?? 0, scrap is 0 ? null : scrap, pos, r.Text("Note")));
            }

            if (problems.Count > before) continue;
            if (exists is not null && lines.Count == 0) continue;   // 있는 초안 버전에 더할 라인이 없다 — 전부 건너뜀
            plans.Add(new Version(id, exists is null, root!, versionNo, from, to, head.Text("ChangeType"), head.Text("ChangeReason"), lines));
        }
        return new Result(plans, skippedVersions, skippedLines, problems);
    }
}
