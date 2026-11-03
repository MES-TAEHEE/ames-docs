using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// 일별 발주(SRM MM31007, PP_DailyPo) · 대조(수주·일별 발주·일별 구매계획·주간 예측) · 수주 취소 후보.
/// 저장은 일별 구매계획과 같이 "고객사 × 날짜 창" 교체다. 창 밖은 건드리지 않는다.
/// </summary>
public sealed partial class PpRepository
{
    public const string DailyPoStatusCancelled = "Cancelled";

    public sealed record DailyPoCell(string ItemNo, string? PartName, string? Unit, decimal? PackQty, DateOnly PlanDate, decimal OrderQty, decimal DeliveredQty);
    public sealed record DailyPoImportResult(string Batch, int ItemCount, int RowCount, DateOnly From, DateOnly To);

    public sealed record DayReconRow(string CustomerId, string ItemNo, DateOnly Date, decimal? PoQty, decimal? DailyPoQty, decimal? PlanPoQty, decimal? PlanScheduled);
    public sealed record CancelCandidate(int SoId, string? SoNumber, int? SoLineNo, string ItemNo, decimal OrderQty, DateTime? Due);

    /// <summary>일별 발주 창 교체. 같은 (품번, 날짜) 는 합쳐서 한 행(유니크 키). 발주·납품이 모두 0 인 칸은 넣지 않는다.</summary>
    public DailyPoImportResult ReplaceDailyPo(string customerId, string batch, string? fileName, DateOnly from, DateOnly to,
        IReadOnlyList<DailyPoCell> cells, string actor)
    {
        if (from > to) throw new ArgumentException("from > to", nameof(from));
        var merged = cells.Where(c => c.PlanDate >= from && c.PlanDate <= to)
            .GroupBy(c => (Item: c.ItemNo.Trim(), c.PlanDate))
            .Select(g => new DailyPoCell(g.Key.Item, g.First().PartName, g.First().Unit, g.First().PackQty, g.Key.PlanDate,
                                         g.Sum(x => x.OrderQty), g.Sum(x => x.DeliveredQty)))
            .Where(c => c.OrderQty != 0 || c.DeliveredQty != 0)
            .ToList();
        foreach (var c in merged)
            if (c.ItemNo.Length > 20) throw new ArgumentException($"품번 '{c.ItemNo}' 은 20자를 넘습니다.");

        using var conn = _f.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var del = new SqlCommand("DELETE FROM dbo.PP_DailyPo WHERE CustomerID = @Cust AND PlanDate BETWEEN @From AND @To;", conn, tx))
            {
                del.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = customerId;
                del.Parameters.Add("@From", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
                del.Parameters.Add("@To",   SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
                del.ExecuteNonQuery();
            }

            using var ins = new SqlCommand("""
                INSERT INTO dbo.PP_DailyPo (CustomerID, ItemNo, PlanDate, OrderQty, DeliveredQty, PackQty, PartName, Unit, Batch, FileName, ImportedAt, CreatedBy)
                VALUES (@Cust, @Item, @Date, @Order, @Deliv, @Pack, @Name, @Unit, @Batch, @File, SYSDATETIME(), @By);
                """, conn, tx);
            ins.Parameters.Add("@Cust",  SqlDbType.VarChar, 20).Value = customerId;
            ins.Parameters.Add("@Item",  SqlDbType.VarChar, 20);
            ins.Parameters.Add("@Date",  SqlDbType.Date);
            foreach (var n in new[] { "@Order", "@Deliv", "@Pack" }) { var p = ins.Parameters.Add(n, SqlDbType.Decimal); p.Precision = 14; p.Scale = 3; }
            ins.Parameters.Add("@Name",  SqlDbType.NVarChar, 100);
            ins.Parameters.Add("@Unit",  SqlDbType.VarChar, 10);
            ins.Parameters.Add("@Batch", SqlDbType.VarChar, 20).Value = batch;
            ins.Parameters.Add("@File",  SqlDbType.NVarChar, 200);
            ins.Parameters.Add("@By",    SqlDbType.VarChar, 20).Value = actor;
            foreach (var c in merged)
            {
                ins.Parameters["@Item"].Value  = c.ItemNo;
                ins.Parameters["@Date"].Value  = c.PlanDate.ToDateTime(TimeOnly.MinValue);
                ins.Parameters["@Order"].Value = c.OrderQty;
                ins.Parameters["@Deliv"].Value = c.DeliveredQty;
                ins.Parameters["@Pack"].Value  = (object?)c.PackQty ?? DBNull.Value;
                ins.Parameters["@Name"].Value  = (object?)Trunc(c.PartName, 100) ?? DBNull.Value;
                ins.Parameters["@Unit"].Value  = (object?)Trunc(c.Unit, 10) ?? DBNull.Value;
                ins.Parameters["@File"].Value  = (object?)Trunc(fileName, 200) ?? DBNull.Value;
                ins.ExecuteNonQuery();
            }
            tx.Commit();
            return new DailyPoImportResult(batch, merged.Select(c => c.ItemNo).Distinct().Count(), merged.Count, from, to);
        }
        catch { tx.Rollback(); throw; }

        static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
    }

    /// <summary>고객(null = 전체) × 품번 × 날짜 대조 행. 세 출처 중 하나라도 있는 칸만 나온다(null = 그 출처에 행 없음).</summary>
    public List<DayReconRow> ListDayReconciliation(string? customerId, DateOnly from, DateOnly to)
    {
        const string sql = """
            WITH po AS (
                SELECT CustomerID, ItemNo, RequestedDeliveryDate AS D, SUM(OrderQty) AS Q
                FROM dbo.PP_CustomerOrder
                WHERE Status IN ('Confirmed', 'Open')
                  AND RequestedDeliveryDate BETWEEN @From AND @To AND (@Cust IS NULL OR CustomerID = @Cust)
                GROUP BY CustomerID, ItemNo, RequestedDeliveryDate),
            dpo AS (
                SELECT CustomerID, ItemNo, PlanDate AS D, SUM(OrderQty) AS Q FROM dbo.PP_DailyPo
                WHERE PlanDate BETWEEN @From AND @To AND (@Cust IS NULL OR CustomerID = @Cust)
                GROUP BY CustomerID, ItemNo, PlanDate),
            pl AS (
                SELECT CustomerID, ItemNo, PlanDate AS D, SUM(PoQty) AS P, SUM(ScheduledQty) AS S FROM dbo.PP_DemandPlan
                WHERE PlanDate BETWEEN @From AND @To AND (@Cust IS NULL OR CustomerID = @Cust)
                GROUP BY CustomerID, ItemNo, PlanDate),
            k AS (SELECT CustomerID, ItemNo, D FROM po UNION SELECT CustomerID, ItemNo, D FROM dpo UNION SELECT CustomerID, ItemNo, D FROM pl)
            SELECT k.CustomerID, k.ItemNo, k.D, po.Q AS PoQty, dpo.Q AS DailyPoQty, pl.P AS PlanPoQty, pl.S AS PlanScheduled
            FROM k
            LEFT JOIN po  ON po.CustomerID  = k.CustomerID AND po.ItemNo  = k.ItemNo AND po.D  = k.D
            LEFT JOIN dpo ON dpo.CustomerID = k.CustomerID AND dpo.ItemNo = k.ItemNo AND dpo.D = k.D
            LEFT JOIN pl  ON pl.CustomerID  = k.CustomerID AND pl.ItemNo  = k.ItemNo AND pl.D  = k.D
            ORDER BY k.CustomerID, k.ItemNo, k.D;
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = string.IsNullOrEmpty(customerId) ? DBNull.Value : customerId;
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        using var rdr = cmd.ExecuteReader();
        var list = new List<DayReconRow>();
        while (rdr.Read())
            list.Add(new DayReconRow((string)rdr["CustomerID"], (string)rdr["ItemNo"], DateOnly.FromDateTime((DateTime)rdr["D"]),
                Dec(rdr["PoQty"]), Dec(rdr["DailyPoQty"]), Dec(rdr["PlanPoQty"]), Dec(rdr["PlanScheduled"])));
        return list;
    }

    /// <summary>납기가 창 안이고 아직 출하가 없는 미확정·확정 수주 라인. 업로드 파일에 없으면 취소 후보다(호출측이 대조).</summary>
    public List<CancelCandidate> ListCancelCandidates(string customerId, DateOnly from, DateOnly to)
    {
        const string sql = """
            SELECT SoID, SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate
            FROM dbo.PP_CustomerOrder
            WHERE CustomerID = @Cust AND Status IN ('Open', 'Confirmed')
              AND RequestedDeliveryDate BETWEEN @From AND @To AND ISNULL(ShippedQty, 0) = 0;
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        AddWindow(cmd, customerId, from, to);
        using var rdr = cmd.ExecuteReader();
        var list = new List<CancelCandidate>();
        while (rdr.Read())
            list.Add(new CancelCandidate((int)rdr["SoID"], rdr["SoNumber"] as string,
                rdr["SoLineNo"] is int ln ? ln : null, (string)rdr["ItemNo"], (decimal)rdr["OrderQty"],
                rdr["RequestedDeliveryDate"] as DateTime?));
        return list;
    }

    /// <summary>취소 처리: 출하가 없는 Open·Confirmed 라인만 Cancelled 로 바꾼다(조건을 UPDATE 에서 다시 확인). 바뀐 건수.</summary>
    public int CancelOrders(IReadOnlyList<int> soIds, string actor)
    {
        if (soIds.Count == 0) return 0;
        using var conn = _f.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            int n = 0;
            using var cmd = new SqlCommand("""
                UPDATE dbo.PP_CustomerOrder
                SET    Status = 'Cancelled', ModifiedTS = SYSDATETIME(), ModifiedBy = @By
                WHERE  SoID = @Id AND Status IN ('Open', 'Confirmed') AND ISNULL(ShippedQty, 0) = 0;
                """, conn, tx);
            cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = actor;
            cmd.Parameters.Add("@Id", SqlDbType.Int);
            foreach (var id in soIds)
            {
                cmd.Parameters["@Id"].Value = id;
                n += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return n;
        }
        catch { tx.Rollback(); throw; }
    }

    static void AddWindow(SqlCommand cmd, string customerId, DateOnly from, DateOnly to)
    {
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = customerId;
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
    }

    static decimal? Dec(object v) => v is decimal d ? d : null;

    /// <summary>고객의 SRM 연동 설정 설명란(CORCD·VENDCD 등). 협력사 코드 대조에 쓴다(SrmVendorCheck).</summary>
    public List<string?> ListCustomerSrmDescriptions(string customerId)
    {
        const string sql = """
            SELECT Description FROM dbo.MD_CodeItem
            WHERE GroupCode IN ('SW_POSYNC_SOURCE', 'SW_DPSYNC_SOURCE') AND Attribute1 = @Cust AND ISNULL(UseFlag, 1) = 1;
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = customerId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<string?>();
        while (rdr.Read()) list.Add(rdr["Description"] as string);
        return list;
    }
}
