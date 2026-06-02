using System.Text.RegularExpressions;

namespace AMES.Tools.MdDriftCheck;

/// <summary>
/// Drift-check tool for MD L3 entity docs.
///
/// Parses dist/AMES_Schema.sql and each VOL13_MDxx_*.html DDL section,
/// then reports per-entity differences in column name, type, and
/// nullability. SQL is treated as the source of truth.
///
/// Usage:
///   dotnet run --project tools/md_drift_check
///       → prints a per-entity drift report to stdout
///       (writes tools/md_drift_check/_report.md next to the project)
/// </summary>
internal static class Program
{
    // Entity number → SQL table name
    private static readonly Dictionary<int, string> Mapping = new()
    {
        [1]  = "MD_Item",
        [2]  = "MD_Bom",
        [3]  = "MD_BomVersion",
        [4]  = "MD_Bop",
        [5]  = "MD_WorkCenter",
        [6]  = "MD_InspectionStandard",
        [7]  = "MD_Vendor",
        [8]  = "MD_Equipment",
        [9]  = "MD_Mold",
        [10] = "MD_PaintFabric",
        [11] = "MD_ShipmentDest",
        [12] = "MD_Customer",
        [13] = "MD_Uom",
        [14] = "MD_Calendar",
        [15] = "MD_Jig",
        [16] = "MD_RfidTag",
        [17] = "MD_RalColor",
        [18] = "MD_Oven",
        [19] = "MD_RfidReader",
        [20] = "MD_Line",
        [21] = "MD_DefectCode",
        [22] = "MD_DefectCause",
        [23] = "MD_PackagingSpec",
        [24] = "MD_LabelTemplate",
        [25] = "MD_ReasonCode",
        [26] = "MD_CodeGroup",
        [27] = "MD_SparePart",
        [28] = "MD_PmTemplate",
    };

    // Type normalization — many doc/SQL combos are semantically equal.
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BOOLEAN"]   = "BIT",
        ["BIT(1)"]    = "BIT",
        ["DATETIME"]  = "DATETIME2",
        ["NUMERIC"]   = "DECIMAL",
        ["FLOAT"]     = "FLOAT",
        ["INTEGER"]   = "INT",
        ["BIGINT IDENTITY"] = "BIGINT",
        ["INT IDENTITY"]    = "INT",
    };

    private sealed record SqlCol(string Name, string Type, bool Nullable);
    private sealed record DocCol(string Name, string Type, bool Nullable);

    private static int Main(string[] args)
    {
        var fix = args.Contains("--fix");

        // Locate repo root by walking up from cwd looking for dist/AMES_Schema.sql
        var root = FindRepoRoot();
        if (root is null) { Console.WriteLine("Could not locate repo root (no dist/AMES_Schema.sql found upward)"); return 1; }
        Console.WriteLine($"repo: {root}");
        Console.WriteLine($"mode: {(fix ? "FIX (write HTML)" : "report only — pass --fix to write")}");

        var schemaPath = Path.Combine(root, "dist", "AMES_Schema.sql");
        var sqlText    = File.ReadAllText(schemaPath);
        var sqlTables  = ParseSqlTables(sqlText);
        Console.WriteLine($"sql:  {sqlTables.Count} CREATE TABLE statements parsed");

        var lines = new List<string>();
        lines.Add("# MD L3 Drift Report");
        lines.Add("");
        lines.Add($"Generated against `{Path.GetRelativePath(root, schemaPath).Replace('\\','/')}`.");
        lines.Add("SQL is the source of truth — every drift line below is a doc to correct.");
        lines.Add("");

        var grandMissing  = 0;
        var grandExtra    = 0;
        var grandTypeMis  = 0;
        var grandNullMis  = 0;

        foreach (var (n, tbl) in Mapping)
        {
            var entityFile = Directory.GetFiles(root, $"VOL13_MD{n:00}_*.html").FirstOrDefault();
            if (entityFile is null) { lines.Add($"## MD-{n:00}  ⚠  no HTML doc found"); continue; }

            if (!sqlTables.TryGetValue(tbl, out var sqlCols))
            { lines.Add($"## MD-{n:00} {tbl}  ⚠  no SQL table found"); continue; }

            var docHtml = File.ReadAllText(entityFile);
            var docCols = ParseDocColumns(docHtml);

            var sqlByName = sqlCols.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var docByName = docCols.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            var missingInDoc  = sqlCols.Where(s => !docByName.ContainsKey(s.Name)).ToList();
            var extraInDoc    = docCols.Where(d => !sqlByName.ContainsKey(d.Name)).ToList();
            var typeMismatch  = new List<(string Name, string Sql, string Doc)>();
            var nullMismatch  = new List<(string Name, bool SqlNull, bool DocNull)>();
            foreach (var d in docCols)
            {
                if (!sqlByName.TryGetValue(d.Name, out var s)) continue;
                if (!TypeEquiv(s.Type, d.Type))
                    typeMismatch.Add((d.Name, s.Type, d.Type));
                if (s.Nullable != d.Nullable)
                    nullMismatch.Add((d.Name, s.Nullable, d.Nullable));
            }

            var total = missingInDoc.Count + extraInDoc.Count + typeMismatch.Count + nullMismatch.Count;
            grandMissing += missingInDoc.Count;
            grandExtra   += extraInDoc.Count;
            grandTypeMis += typeMismatch.Count;
            grandNullMis += nullMismatch.Count;

            var icon = total == 0 ? "✅" : "⚠";
            lines.Add($"## MD-{n:00}  `{tbl}`  {icon}  {sqlCols.Count} sql / {docCols.Count} doc  ·  drift={total}");
            lines.Add("");

            if (fix && total > 0)
                ApplyFix(entityFile, docHtml, sqlCols, docCols);
            if (total == 0) { lines.Add("_clean_"); lines.Add(""); continue; }

            if (missingInDoc.Count > 0)
            {
                lines.Add($"**Missing from doc (in SQL, not in HTML) — {missingInDoc.Count}**");
                foreach (var m in missingInDoc)
                    lines.Add($"- `{m.Name}` `{m.Type}` {(m.Nullable ? "NULL" : "NOT NULL")}");
                lines.Add("");
            }
            if (extraInDoc.Count > 0)
            {
                lines.Add($"**Extra in doc (HTML has, SQL doesn't) — {extraInDoc.Count}**");
                foreach (var x in extraInDoc) lines.Add($"- `{x.Name}` `{x.Type}`");
                lines.Add("");
            }
            if (typeMismatch.Count > 0)
            {
                lines.Add($"**Type mismatch — {typeMismatch.Count}**");
                foreach (var t in typeMismatch)
                    lines.Add($"- `{t.Name}`  doc:`{t.Doc}`  →  sql:`{t.Sql}`");
                lines.Add("");
            }
            if (nullMismatch.Count > 0)
            {
                lines.Add($"**Nullability mismatch — {nullMismatch.Count}**");
                foreach (var nm in nullMismatch)
                    lines.Add($"- `{nm.Name}`  doc:{(nm.DocNull ? "NULL" : "NOT NULL")}  →  sql:{(nm.SqlNull ? "NULL" : "NOT NULL")}");
                lines.Add("");
            }
        }

        lines.Insert(4, $"**Summary — missing:{grandMissing} · extra:{grandExtra} · type:{grandTypeMis} · null:{grandNullMis}**");
        lines.Insert(5, "");

        var outPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "_report.md");
        outPath = Path.GetFullPath(outPath);
        File.WriteAllText(outPath, string.Join("\n", lines));
        Console.WriteLine($"report: {outPath}");
        Console.WriteLine();
        Console.WriteLine($"drift totals — missing:{grandMissing} extra:{grandExtra} type:{grandTypeMis} null:{grandNullMis}");
        return 0;
    }

    // ── HTML auto-fix ───────────────────────────────────────────────────
    // Replaces every row inside <table class="ddl-tbl"> ... </table> with
    // rows that match the SQL schema. Descriptions from the original HTML
    // are preserved on a name-match basis; new columns get a placeholder.
    private static void ApplyFix(string filePath, string html, List<SqlCol> sqlCols, List<DocCol> docCols)
    {
        // Build a name → original-html-row map so we can carry over the
        // Description cell (and Key cell) verbatim.
        var rxFullRow = new Regex(
            @"<tr>\s*" +
            @"<td\s+class=""col"">([^<]+)</td>\s*" +
            @"<td\s+class=""typ"">[^<]+</td>\s*" +
            @"<td\s+class=""nn[^""]*"">[^<]*</td>\s*" +
            @"(<td[^>]*>[\s\S]*?</td>)\s*" +    // key/badges cell (PK/FK/UK)
            @"(<td[^>]*>[\s\S]*?</td>)\s*" +    // default cell
            @"(<td[^>]*>[\s\S]*?</td>)\s*" +    // description cell
            @"</tr>",
            RegexOptions.IgnoreCase);

        var perCol = new Dictionary<string, (string Key, string Def, string Desc)>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in rxFullRow.Matches(html))
        {
            var name = m.Groups[1].Value.Trim();
            perCol[name] = (m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
        }

        // Build new tbody
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<tbody>");
        foreach (var s in sqlCols)
        {
            perCol.TryGetValue(s.Name, out var prior);
            var keyCell  = prior.Key  ?? "<td></td>";
            var defCell  = prior.Def  ?? "<td>—</td>";
            var descCell = prior.Desc ?? "<td><em>TODO — column added since L3 doc; describe purpose.</em></td>";
            var nnClass  = s.Nullable ? "nn nn-n" : "nn nn-y";
            var nnLabel  = s.Nullable ? "N" : "Y";
            sb.AppendLine($"<tr>");
            sb.AppendLine($"  <td class=\"col\">{s.Name}</td>");
            sb.AppendLine($"  <td class=\"typ\">{s.Type}</td>");
            sb.AppendLine($"  <td class=\"{nnClass}\">{nnLabel}</td>");
            sb.AppendLine($"  {keyCell.Trim()}");
            sb.AppendLine($"  {defCell.Trim()}");
            sb.AppendLine($"  {descCell.Trim()}");
            sb.AppendLine($"</tr>");
        }
        sb.Append("</tbody>");

        // Find the FIRST <table class="ddl-tbl"> ... </table> and replace its tbody
        var rxTbl = new Regex(@"(<table\s+class=""ddl-tbl"">[\s\S]*?)<tbody>[\s\S]*?</tbody>([\s\S]*?</table>)",
                              RegexOptions.IgnoreCase);
        var newHtml = rxTbl.Replace(html, m => m.Groups[1].Value + sb + m.Groups[2].Value, 1);

        if (!ReferenceEquals(newHtml, html) && newHtml != html)
        {
            var utf8NoBom = new System.Text.UTF8Encoding(false);
            File.WriteAllText(filePath, newHtml, utf8NoBom);
            Console.WriteLine($"  fixed: {Path.GetFileName(filePath)}  ({sqlCols.Count} rows written)");
        }
    }

    // ── repo location helper ────────────────────────────────────────────
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "dist", "AMES_Schema.sql"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    // ── SQL parser ──────────────────────────────────────────────────────
    private static Dictionary<string, List<SqlCol>> ParseSqlTables(string sql)
    {
        var result = new Dictionary<string, List<SqlCol>>(StringComparer.OrdinalIgnoreCase);
        // Match: CREATE TABLE dbo.<name> ( ... );
        var rxTable = new Regex(@"CREATE\s+TABLE\s+dbo\.(\w+)\s*\(([\s\S]*?)\);", RegexOptions.IgnoreCase);
        var rxCol   = new Regex(@"\[(\w+)\s*\]\s+([A-Z][A-Z0-9\(\),\s]+?)\s+(NOT\s+NULL|NULL)", RegexOptions.IgnoreCase);

        foreach (Match t in rxTable.Matches(sql))
        {
            var tableName = t.Groups[1].Value;
            var body      = t.Groups[2].Value;
            var cols = new List<SqlCol>();
            foreach (Match m in rxCol.Matches(body))
            {
                var name  = m.Groups[1].Value;
                var type  = NormalizeType(m.Groups[2].Value.Trim());
                var nflag = m.Groups[3].Value.Replace(" ", "").ToUpperInvariant();
                cols.Add(new SqlCol(name, type, nflag == "NULL"));
            }
            result[tableName] = cols;
        }
        return result;
    }

    // ── HTML doc parser ─────────────────────────────────────────────────
    private static List<DocCol> ParseDocColumns(string html)
    {
        var cols = new List<DocCol>();
        // The DDL table rows look like:
        // <td class="col">Name</td>
        // <td class="typ">VARCHAR(20)</td>
        // <td class="nn nn-y">Y</td>   (or nn-n N)
        var rxRow = new Regex(
            @"<td\s+class=""col"">([^<]+)</td>\s*" +
            @"<td\s+class=""typ"">([^<]+)</td>\s*" +
            @"<td\s+class=""nn[^""]*"">\s*([YN])\s*</td>",
            RegexOptions.IgnoreCase);
        foreach (Match m in rxRow.Matches(html))
        {
            var name  = m.Groups[1].Value.Trim();
            var type  = NormalizeType(m.Groups[2].Value.Trim());
            var nflag = m.Groups[3].Value.ToUpperInvariant();
            cols.Add(new DocCol(name, type, nflag == "N"));
        }
        return cols;
    }

    // ── Type normalization ──────────────────────────────────────────────
    private static string NormalizeType(string raw)
    {
        var t = raw.ToUpperInvariant()
                   .Replace(" ", "")
                   .Replace("IDENTITY", "");
        if (TypeAliases.TryGetValue(t, out var alias)) return alias;
        return t;
    }
    private static bool TypeEquiv(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (TypeAliases.TryGetValue(a, out var aa) && string.Equals(aa, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (TypeAliases.TryGetValue(b, out var bb) && string.Equals(bb, a, StringComparison.OrdinalIgnoreCase)) return true;
        // Allow doc to omit precision: "DECIMAL" vs "DECIMAL(14,4)" → match
        var aBase = a.Split('(')[0];
        var bBase = b.Split('(')[0];
        return string.Equals(aBase, bBase, StringComparison.OrdinalIgnoreCase)
            && (!a.Contains('(') || !b.Contains('('));
    }
}
