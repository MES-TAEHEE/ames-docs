using System.Data;
using AMES.Data.Services.Upload;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>업로드 전 기존 데이터 스냅샷(비교용). 키·값 형식은 UploadDiffRules 와 같다.</summary>
public sealed partial class PpRepository
{
    /// <summary>구매오더: 고객 · 납기 범위의 살아 있는 라인(취소 제외).</summary>
    public Dictionary<string, string> ReadPoSnapshot(string customerId, DateOnly from, DateOnly to)
    {
        const string sql = """
            SELECT SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate
            FROM dbo.PP_CustomerOrder
            WHERE CustomerID = @Cust AND RequestedDeliveryDate BETWEEN @From AND @To AND Status <> 'Cancelled';
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        AddSnapshotWindow(cmd, customerId, from, to);
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        while (rdr.Read())
        {
            var key = UploadDiffRules.PoKey((string)rdr["SoNumber"], rdr["SoLineNo"] is int ln ? ln : null);
            map[key] = UploadDiffRules.PoValue((string)rdr["ItemNo"], (decimal)rdr["OrderQty"], rdr["RequestedDeliveryDate"] as DateTime?);
        }
        return map;
    }

    /// <summary>일별 구매계획(MM30011): 고객 · 날짜 범위, 품번·날짜별 합계.</summary>
    public Dictionary<string, string> ReadPlanSnapshot(string customerId, DateOnly from, DateOnly to)
    {
        const string sql = """
            SELECT ItemNo, PlanDate, SUM(ScheduledQty) AS Sched, SUM(PoQty) AS Po
            FROM dbo.PP_DemandPlan
            WHERE CustomerID = @Cust AND PlanDate BETWEEN @From AND @To
            GROUP BY ItemNo, PlanDate;
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        AddSnapshotWindow(cmd, customerId, from, to);
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        while (rdr.Read())
            map[UploadDiffRules.DateKey((string)rdr["ItemNo"], DateOnly.FromDateTime((DateTime)rdr["PlanDate"]))] =
                UploadDiffRules.PlanValue((decimal)rdr["Sched"], (decimal)rdr["Po"]);
        return map;
    }

    /// <summary>일별 발주(MM31007): 고객 · 날짜 범위, 품번·날짜별 합계.</summary>
    public Dictionary<string, string> ReadDailyPoSnapshot(string customerId, DateOnly from, DateOnly to)
    {
        const string sql = """
            SELECT ItemNo, PlanDate, SUM(OrderQty) AS Ord, SUM(DeliveredQty) AS Del
            FROM dbo.PP_DailyPo
            WHERE CustomerID = @Cust AND PlanDate BETWEEN @From AND @To
            GROUP BY ItemNo, PlanDate;
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        AddSnapshotWindow(cmd, customerId, from, to);
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        while (rdr.Read())
            map[UploadDiffRules.DateKey((string)rdr["ItemNo"], DateOnly.FromDateTime((DateTime)rdr["PlanDate"]))] =
                UploadDiffRules.DailyPoValue((decimal)rdr["Ord"], (decimal)rdr["Del"]);
        return map;
    }

    static void AddSnapshotWindow(SqlCommand cmd, string customerId, DateOnly from, DateOnly to)
    {
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = customerId;
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
    }
}
