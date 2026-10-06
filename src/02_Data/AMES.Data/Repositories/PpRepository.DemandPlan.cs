using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// 일별 구매계획(PP_DemandPlan) — 스펙 2026-09-29 §3. 저장은 "고객사 × 날짜 창" 교체: 창 안의 기존 행을 지우고 0 아닌 예정량만 넣는다.
/// 파일/응답이 그 고객사·협력사의 전 품번을 담으므로 빠진 품번·0 이 된 날은 수요에서 사라져야 한다. 창 밖은 건드리지 않는다.
/// </summary>
public sealed partial class PpRepository
{
    public const string DemandPlanSourceUpload = "UPLOAD";
    public const string DemandPlanSourceSrm    = "SRM";

    public sealed record DemandPlanCell(string ItemNo, string? PartName, string? Unit, decimal? PackQty, DateOnly PlanDate, decimal ScheduledQty, decimal PoQty);
    public sealed record DemandPlanImportResult(string Batch, int ItemCount, int RowCount, int UnmatchedItems, int PackMismatch, DateOnly From, DateOnly To);
    public sealed record DemandPlanBatchRow(string Batch, string CustomerId, string Source, string? SourceKey, string? FileName,
                                            DateOnly DateFrom, DateOnly DateTo, int ItemCount, int RowCount, int UnmatchedItems, int PackMismatch,
                                            DateTime ImportedAt, string ImportedBy);
    public sealed record DemandPlanCellRow(string CustomerId, string ItemNo, string? ItemName, string? PartName, string? Unit, decimal? PackQty,
                                           decimal? BoxQty, DateOnly PlanDate, decimal ScheduledQty, decimal PoQty, bool ItemExists);

    public DemandPlanImportResult ReplaceDemandPlan(string customerId, string batch, string source, string? sourceKey, string? fileName,
        DateOnly from, DateOnly to, IReadOnlyList<DemandPlanCell> cells, string actor)
    {
        if (from > to) throw new ArgumentException("from > to", nameof(from));
        var inWindow = cells.Where(c => c.PlanDate >= from && c.PlanDate <= to).ToList();
        // 같은 (품번, 계획일) 셀이 여러 개면(응답 중복 등) 유니크 인덱스 위반이 나므로 합쳐서 한 행으로 만든다 —
        // 수량은 SUM, 그 밖의 표시용 필드(PartName·Unit·PackQty)는 첫 행 값을 쓴다.
        var deduped = inWindow
            .GroupBy(c => (Key: c.ItemNo.Trim().ToUpperInvariant(), c.PlanDate))
            .Select(g =>
            {
                var first = g.First();
                return first with
                {
                    ItemNo       = first.ItemNo.Trim(),
                    ScheduledQty = g.Sum(x => x.ScheduledQty),
                    PoQty        = g.Sum(x => x.PoQty),
                };
            })
            .ToList();
        var items    = deduped.Select(c => c.ItemNo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var nonZero  = deduped.Where(c => c.ScheduledQty > 0m).ToList();

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // 등록 여부·박스 수량 대조 — 저장을 막지 않고 집계만 한다
            var known = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            if (items.Count > 0)
            {
                using var chk = new SqlCommand("SELECT i.ItemNo, i.BoxQty FROM dbo.MD_Item i JOIN STRING_SPLIT(@List, ',') s ON s.value = i.ItemNo;", conn, tx);
                chk.Parameters.Add("@List", SqlDbType.NVarChar, -1).Value = string.Join(',', items);
                using var rdr = chk.ExecuteReader();
                while (rdr.Read()) known[(string)rdr["ItemNo"]] = rdr["BoxQty"] is DBNull ? null : Convert.ToDecimal(rdr["BoxQty"]);
            }
            int unmatched = items.Count(i => !known.ContainsKey(i));
            int mismatch  = items.Count(i => known.TryGetValue(i, out var box) && box is decimal b && b > 0
                                          && deduped.Any(c => c.ItemNo.Equals(i, StringComparison.OrdinalIgnoreCase)
                                                             && c.PackQty is decimal p && p > 0 && p != b));

            using (var hdr = new SqlCommand("""
                INSERT INTO dbo.PP_DemandPlanBatch (Batch, CustomerID, Source, SourceKey, FileName, DateFrom, DateTo, ItemCount, [RowCount], UnmatchedItems, PackMismatch, ImportedAt, ImportedBy, CreatedBy)
                VALUES (@Batch, @Cust, @Source, @Key, @File, @From, @To, @Items, @Rows, @Unmatched, @Mismatch, SYSDATETIME(), @By, @By);
                """, conn, tx))
            {
                hdr.Parameters.Add("@Batch",     SqlDbType.VarChar, 20).Value   = batch;
                hdr.Parameters.Add("@Cust",      SqlDbType.VarChar, 20).Value   = customerId;
                hdr.Parameters.Add("@Source",    SqlDbType.VarChar, 20).Value   = source;
                hdr.Parameters.Add("@Key",       SqlDbType.VarChar, 20).Value   = (object?)sourceKey ?? DBNull.Value;
                hdr.Parameters.Add("@File",      SqlDbType.NVarChar, 200).Value = (object?)(fileName is { Length: > 200 } fn ? fn[..200] : fileName) ?? DBNull.Value;
                hdr.Parameters.Add("@From",      SqlDbType.Date).Value          = from.ToDateTime(TimeOnly.MinValue);
                hdr.Parameters.Add("@To",        SqlDbType.Date).Value          = to.ToDateTime(TimeOnly.MinValue);
                hdr.Parameters.Add("@Items",     SqlDbType.Int).Value           = items.Count;
                hdr.Parameters.Add("@Rows",      SqlDbType.Int).Value           = nonZero.Count;
                hdr.Parameters.Add("@Unmatched", SqlDbType.Int).Value           = unmatched;
                hdr.Parameters.Add("@Mismatch",  SqlDbType.Int).Value           = mismatch;
                hdr.Parameters.Add("@By",        SqlDbType.VarChar, 20).Value   = actor;
                hdr.ExecuteNonQuery();
            }

            using (var del = new SqlCommand("DELETE FROM dbo.PP_DemandPlan WHERE CustomerID = @Cust AND PlanDate BETWEEN @From AND @To;", conn, tx))
            {
                del.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = customerId;
                del.Parameters.Add("@From", SqlDbType.Date).Value        = from.ToDateTime(TimeOnly.MinValue);
                del.Parameters.Add("@To",   SqlDbType.Date).Value        = to.ToDateTime(TimeOnly.MinValue);
                del.ExecuteNonQuery();
            }

            using (var ins = new SqlCommand("""
                INSERT INTO dbo.PP_DemandPlan (CustomerID, ItemNo, PlanDate, ScheduledQty, PoQty, PackQty, PartName, Unit, Batch, Source, CreatedBy)
                VALUES (@Cust, @Item, @Date, @Sched, @Po, @Pack, @Name, @Unit, @Batch, @Source, @By);
                """, conn, tx))
            {
                ins.Parameters.Add("@Cust",   SqlDbType.VarChar, 20).Value  = customerId;
                ins.Parameters.Add("@Item",   SqlDbType.VarChar, 20);
                ins.Parameters.Add("@Date",   SqlDbType.Date);
                foreach (var n in new[] { "@Sched", "@Po", "@Pack" }) { var p = ins.Parameters.Add(n, SqlDbType.Decimal); p.Precision = 14; p.Scale = 3; }
                ins.Parameters.Add("@Name",   SqlDbType.NVarChar, 100);
                ins.Parameters.Add("@Unit",   SqlDbType.VarChar, 10);
                ins.Parameters.Add("@Batch",  SqlDbType.VarChar, 20).Value  = batch;
                ins.Parameters.Add("@Source", SqlDbType.VarChar, 20).Value  = source;
                ins.Parameters.Add("@By",     SqlDbType.VarChar, 20).Value  = actor;
                foreach (var c in nonZero)
                {
                    var item = c.ItemNo.Trim();
                    if (item.Length > 20) throw new ArgumentException($"품번 '{item}' 은 20자를 넘습니다.");
                    ins.Parameters["@Item"].Value  = item;
                    ins.Parameters["@Date"].Value  = c.PlanDate.ToDateTime(TimeOnly.MinValue);
                    ins.Parameters["@Sched"].Value = c.ScheduledQty;
                    ins.Parameters["@Po"].Value    = c.PoQty;
                    ins.Parameters["@Pack"].Value  = (object?)c.PackQty ?? DBNull.Value;
                    ins.Parameters["@Name"].Value  = (object?)Trunc(c.PartName, 100) ?? DBNull.Value;
                    ins.Parameters["@Unit"].Value  = (object?)Trunc(c.Unit, 10) ?? DBNull.Value;
                    ins.ExecuteNonQuery();
                }
            }
            tx.Commit();
            return new DemandPlanImportResult(batch, items.Count, nonZero.Count, unmatched, mismatch, from, to);
        }
        catch { tx.Rollback(); throw; }

        static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
    }

    public List<DemandPlanBatchRow> ListDemandPlanBatches(string? customerId, DateTime? importedFrom = null, DateTime? importedTo = null, int take = 100)
    {
        const string sql = """
            SELECT TOP (@Take) Batch, CustomerID, Source, SourceKey, FileName, DateFrom, DateTo, ItemCount, [RowCount], UnmatchedItems, PackMismatch, ImportedAt, ImportedBy
            FROM   dbo.PP_DemandPlanBatch
            WHERE  (@Cust IS NULL OR CustomerID = @Cust)
              AND  (@From IS NULL OR ImportedAt >= @From)
              AND  (@To   IS NULL OR ImportedAt <  DATEADD(day, 1, @To))
            ORDER  BY ImportedAt DESC, Batch DESC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = string.IsNullOrEmpty(customerId) ? DBNull.Value : customerId;
        cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = (object?)importedFrom?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@To",   SqlDbType.DateTime2).Value = (object?)importedTo?.Date   ?? DBNull.Value;
        using var rdr = cmd.ExecuteReader();
        var list = new List<DemandPlanBatchRow>();
        while (rdr.Read())
            list.Add(new((string)rdr["Batch"], (string)rdr["CustomerID"], (string)rdr["Source"], rdr["SourceKey"] as string, rdr["FileName"] as string,
                DateOnly.FromDateTime((DateTime)rdr["DateFrom"]), DateOnly.FromDateTime((DateTime)rdr["DateTo"]),
                (int)rdr["ItemCount"], (int)rdr["RowCount"], (int)rdr["UnmatchedItems"], (int)rdr["PackMismatch"],
                (DateTime)rdr["ImportedAt"], (string)rdr["ImportedBy"]));
        return list;
    }

    public List<DemandPlanCellRow> ListDemandPlan(string? customerId, DateOnly from, DateOnly to)
    {
        const string sql = """
            SELECT p.CustomerID, p.ItemNo, i.ItemName, p.PartName, p.Unit, p.PackQty, i.BoxQty, p.PlanDate, p.ScheduledQty, COALESCE(p.PoQty, 0) AS PoQty,
                   CASE WHEN i.ItemNo IS NULL THEN 0 ELSE 1 END AS ItemExists
            FROM   dbo.PP_DemandPlan p
            LEFT   JOIN dbo.MD_Item i ON i.ItemNo = p.ItemNo
            WHERE  (@Cust IS NULL OR p.CustomerID = @Cust)
              AND  p.PlanDate BETWEEN @From AND @To
            ORDER  BY p.CustomerID, p.ItemNo, p.PlanDate;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = string.IsNullOrEmpty(customerId) ? DBNull.Value : customerId;
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        using var rdr = cmd.ExecuteReader();
        var list = new List<DemandPlanCellRow>();
        while (rdr.Read())
            list.Add(new((string)rdr["CustomerID"], (string)rdr["ItemNo"], rdr["ItemName"] as string, rdr["PartName"] as string, rdr["Unit"] as string,
                rdr["PackQty"] as decimal?, rdr["BoxQty"] is DBNull ? null : Convert.ToDecimal(rdr["BoxQty"]),
                DateOnly.FromDateTime((DateTime)rdr["PlanDate"]), rdr.GetDecimal(rdr.GetOrdinal("ScheduledQty")), rdr.GetDecimal(rdr.GetOrdinal("PoQty")),
                (int)rdr["ItemExists"] == 1));
        return list;
    }

    /// <summary>PP-001 일별 업로드 미리보기 — BoxQty 가 있는 품번만 반환(포장 수량 대조용).</summary>
    public Dictionary<string, decimal> ListItemBoxQty(IEnumerable<string> itemNos)
    {
        const string sql = """
            SELECT i.ItemNo, i.BoxQty
            FROM   dbo.MD_Item i
            JOIN   STRING_SPLIT(@List, ',') s ON s.value = i.ItemNo
            WHERE  i.BoxQty IS NOT NULL;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@List", SqlDbType.NVarChar, -1).Value = string.Join(',', itemNos.Distinct());
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        while (rdr.Read()) map[(string)rdr["ItemNo"]] = Convert.ToDecimal(rdr["BoxQty"]);
        return map;
    }
}
