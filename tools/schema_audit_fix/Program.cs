using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AMES.Tools.SchemaAuditFix;

/// <summary>
/// Three structural fixes in one tool:
///   1. ALTER TABLE …  ADD ModifiedBy NVARCHAR(450) NULL  for every user
///      table that has CreatedBy but is missing ModifiedBy.
///   2. sp_addextendedproperty MS_Description on every table
///      (Korean label parsed from AMES_ERD_data.js).
///   3. sp_addextendedproperty MS_Description on every column — comments
///      derived deterministically from name + PK/FK flag + audit category.
/// Idempotent: skips columns / properties that already exist.
/// </summary>
internal static class Program
{
    private const string Cs =
        "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
        "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    private static readonly string ErdPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                     "AMES_ERD_data.js");

    private static int Main()
    {
        Console.WriteLine("[schema-audit-fix] connecting ...");
        using var conn = new SqlConnection(Cs);
        conn.Open();

        var tableKo = ParseErdTableLabels(File.ReadAllText(ErdPath));
        Console.WriteLine($"[schema-audit-fix] parsed {tableKo.Count} table labels from ERD");

        int added = AddModifiedByWhereMissing(conn);
        Console.WriteLine($"[schema-audit-fix] added ModifiedBy to {added} tables");

        int tblComments = SetTableComments(conn, tableKo);
        Console.WriteLine($"[schema-audit-fix] set {tblComments} table comments");

        int colComments = SetColumnComments(conn);
        Console.WriteLine($"[schema-audit-fix] set {colComments} column comments");

        Console.WriteLine("[schema-audit-fix] done.");
        return 0;
    }

    // ── 1. Add ModifiedBy column ────────────────────────────────────────
    private static int AddModifiedByWhereMissing(SqlConnection conn)
    {
        var targets = ReadList(conn, """
            SELECT t.name
            FROM   sys.tables t
            JOIN   sys.columns c ON c.object_id = t.object_id
            WHERE  t.is_ms_shipped = 0
            GROUP  BY t.name
            HAVING MAX(CASE WHEN c.name='CreatedBy'  THEN 1 ELSE 0 END) = 1
               AND MAX(CASE WHEN c.name='ModifiedBy' THEN 1 ELSE 0 END) = 0
            ORDER BY t.name;
            """);
        int n = 0;
        foreach (var t in targets)
        {
            Exec(conn, $"ALTER TABLE dbo.{t} ADD ModifiedBy NVARCHAR(450) NULL;");
            n++;
        }
        return n;
    }

    // ── 2. Table-level MS_Description ───────────────────────────────────
    private static int SetTableComments(SqlConnection conn, Dictionary<string, string> tableKo)
    {
        int n = 0;
        var tables = ReadList(conn,
            "SELECT name FROM sys.tables WHERE is_ms_shipped=0 ORDER BY name;");
        foreach (var t in tables)
        {
            tableKo.TryGetValue(t, out var label);
            label ??= HumanizeName(t);
            SetExtendedProp(conn, "dbo", t, columnName: null, label);
            n++;
        }
        return n;
    }

    // ── 3. Column-level MS_Description ──────────────────────────────────
    private static int SetColumnComments(SqlConnection conn)
    {
        // Pull every (table, column, type, PK/FK info)
        const string sql = """
            SELECT  t.name                                                AS TableName,
                    c.name                                                AS ColumnName,
                    TYPE_NAME(c.user_type_id) + CASE
                       WHEN TYPE_NAME(c.user_type_id) IN ('varchar','nvarchar','char','nchar')
                            AND c.max_length > 0
                            THEN '(' + CAST(CASE WHEN TYPE_NAME(c.user_type_id) IN ('nvarchar','nchar')
                                                  THEN c.max_length / 2
                                                  ELSE c.max_length END AS VARCHAR(10)) + ')'
                       WHEN TYPE_NAME(c.user_type_id) IN ('decimal','numeric')
                            THEN '(' + CAST(c.precision AS VARCHAR(10)) + ',' + CAST(c.scale AS VARCHAR(10)) + ')'
                       ELSE ''
                    END                                                   AS SqlType,
                    CASE WHEN ic.column_id IS NOT NULL THEN 1 ELSE 0 END  AS IsPk,
                    fk_target.RefTable,
                    fk_target.RefColumn
            FROM    sys.tables t
            JOIN    sys.columns c ON c.object_id = t.object_id
            LEFT JOIN (
                SELECT i.object_id, ic.column_id
                FROM   sys.indexes      i
                JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE  i.is_primary_key = 1
            ) ic ON ic.object_id = t.object_id AND ic.column_id = c.column_id
            LEFT JOIN (
                SELECT  fkc.parent_object_id, fkc.parent_column_id,
                        OBJECT_NAME(fkc.referenced_object_id)       AS RefTable,
                        COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS RefColumn
                FROM    sys.foreign_key_columns fkc
            ) fk_target ON fk_target.parent_object_id = t.object_id
                       AND fk_target.parent_column_id = c.column_id
            WHERE   t.is_ms_shipped = 0
            ORDER BY t.name, c.column_id;
            """;
        var rows = new List<(string Table, string Column, string Type, bool IsPk, string? RefTable, string? RefColumn)>();
        using (var cmd = new SqlCommand(sql, conn))
        using (var rdr = cmd.ExecuteReader())
            while (rdr.Read())
                rows.Add(((string)rdr["TableName"], (string)rdr["ColumnName"],
                          (string)rdr["SqlType"], (int)rdr["IsPk"] == 1,
                          rdr["RefTable"] as string, rdr["RefColumn"] as string));

        int n = 0;
        foreach (var r in rows)
        {
            var desc = DescribeColumn(r.Column, r.Type, r.IsPk, r.RefTable, r.RefColumn);
            SetExtendedProp(conn, "dbo", r.Table, r.Column, desc);
            n++;
        }
        return n;
    }

    private static string DescribeColumn(string col, string type,
                                         bool isPk, string? refTable, string? refColumn)
    {
        // 1) Audit columns
        switch (col)
        {
            case "CreatedBy":   return $"생성자 (User ID / seed marker) · {type}";
            case "CreatedTS":   return $"생성 시각 · {type}";
            case "ModifiedBy":  return $"최종 수정자 (User ID) · {type}";
            case "ModifiedTS":  return $"최종 수정 시각 · {type}";
            case "ActiveFlag":  return $"활성 플래그 · {type}";
        }

        // 2) PK / FK takes precedence
        if (isPk) return $"기본 키 · {type}";
        if (refTable is not null && refColumn is not null)
            return $"외래 키 → {refTable}.{refColumn} · {type}";

        // 3) Suffix-based heuristics
        string suffixHint = col switch
        {
            _ when col.EndsWith("TS")     => "타임스탬프",
            _ when col.EndsWith("Date")   => "날짜",
            _ when col.EndsWith("Time")   => "시각",
            _ when col.EndsWith("ID")     => "식별자",
            _ when col.EndsWith("No")     => "번호",
            _ when col.EndsWith("Code")   => "코드",
            _ when col.EndsWith("Name")   => "명칭",
            _ when col.EndsWith("Qty")    => "수량",
            _ when col.EndsWith("Pct")    => "백분율",
            _ when col.EndsWith("Min")    => "(분)",
            _ when col.EndsWith("Sec")    => "(초)",
            _ when col.EndsWith("Hours")  => "시간",
            _ when col.EndsWith("JSON")   => "JSON",
            _ when col.EndsWith("URL")    => "URL",
            _ when col.EndsWith("Flag")   => "플래그",
            _                              => ""
        };

        var human = HumanizeName(col);
        return string.IsNullOrEmpty(suffixHint)
            ? $"{human} · {type}"
            : $"{human} ({suffixHint}) · {type}";
    }

    // ── helpers ─────────────────────────────────────────────────────────
    private static void SetExtendedProp(SqlConnection conn, string schema, string table,
                                        string? columnName, string description)
    {
        // sp_addextendedproperty errors if it already exists → use sp_updateextendedproperty in that case.
        var existsSql = columnName is null
            ? """
              SELECT 1 FROM sys.extended_properties ep
              JOIN   sys.tables t ON t.object_id = ep.major_id
              WHERE  ep.name='MS_Description' AND ep.minor_id = 0
                AND  t.name = @T;
              """
            : """
              SELECT 1 FROM sys.extended_properties ep
              JOIN   sys.tables  t ON t.object_id = ep.major_id
              JOIN   sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
              WHERE  ep.name='MS_Description' AND t.name=@T AND c.name=@C;
              """;
        bool exists;
        using (var cmd = new SqlCommand(existsSql, conn))
        {
            cmd.Parameters.AddWithValue("@T", table);
            if (columnName is not null) cmd.Parameters.AddWithValue("@C", columnName);
            exists = cmd.ExecuteScalar() is not null;
        }

        var spName = exists ? "sp_updateextendedproperty" : "sp_addextendedproperty";
        var spSql = columnName is null
            ? $"EXEC {spName} N'MS_Description', @V, 'SCHEMA', @S, 'TABLE', @T;"
            : $"EXEC {spName} N'MS_Description', @V, 'SCHEMA', @S, 'TABLE', @T, 'COLUMN', @C;";
        using var ex = new SqlCommand(spSql, conn);
        ex.Parameters.AddWithValue("@V", description);
        ex.Parameters.AddWithValue("@S", schema);
        ex.Parameters.AddWithValue("@T", table);
        if (columnName is not null) ex.Parameters.AddWithValue("@C", columnName);
        ex.ExecuteNonQuery();
    }

    private static Dictionary<string, string> ParseErdTableLabels(string js)
    {
        // matches:  {name:'MD_Item', ko:'품목 (MD-01)', ...
        var rx = new Regex(@"\{name\s*:\s*'([^']+)'\s*,\s*ko\s*:\s*'([^']+)'", RegexOptions.Compiled);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in rx.Matches(js))
            dict[m.Groups[1].Value] = m.Groups[2].Value;
        return dict;
    }

    private static string HumanizeName(string camelOrPascal)
    {
        // "EquipID" → "Equip ID",  "TheoreticalCycle" → "Theoretical Cycle"
        return Regex.Replace(camelOrPascal, "(?<!^)([A-Z][a-z])|(?<=[a-z0-9])([A-Z])", " $1$2");
    }

    private static void Exec(SqlConnection conn, string sql)
    {
        using var cmd = new SqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }
    private static List<string> ReadList(SqlConnection conn, string sql)
    {
        var list = new List<string>();
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) list.Add(rdr.GetString(0));
        return list;
    }
}
