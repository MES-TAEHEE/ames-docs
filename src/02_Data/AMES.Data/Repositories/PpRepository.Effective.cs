using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// 화면이 함께 쓰는 "적용 수량"과 업로드 이력.
/// 같은 고객·품번·날짜는 수주(PO, 납기일 기준) → 일별 발주(MM31007) → 일별 계획(MM30011) 순서로 한 번만 적용한다.
/// 예측 현황·일별 구매계획·APS 수요가 이 규칙을 같이 쓰므로, 어느 업로드를 올려도 같은 숫자로 보이고 중복되지 않는다.
/// </summary>
public sealed partial class PpRepository
{
    public const string EffectiveSourcePo      = "PO";
    public const string EffectiveSourceDailyPo = "DAILYPO";
    public const string EffectiveSourcePlan    = "PLAN";

    public sealed record EffectiveRow(string CustomerId, string ItemNo, string? ItemName, string? PartName, string? Unit,
        decimal? PackQty, decimal? PalletQty, DateOnly PlanDate, decimal Qty, string Source, bool ItemExists);

    public sealed record ImportHistoryRow(string Kind, string Batch, string? CustomerId, string? FileName,
        DateTime? DateFrom, DateTime? DateTo, int RowCount, int ItemCount, DateTime ImportedAt, string? ImportedBy, string? Note);

    /// <summary>고객(null = 전체) × 기간의 적용 수량. 칸마다 출처(PO·DAILYPO·PLAN)를 함께 준다.</summary>
    public List<EffectiveRow> ListEffectiveDaily(string? customerId, DateOnly from, DateOnly to)
    {
        const string sql = """
            WITH po AS (
                SELECT CustomerID, ItemNo, RequestedDeliveryDate AS D, SUM(OrderQty) AS Q
                FROM dbo.PP_CustomerOrder
                WHERE Status IN ('Confirmed', 'Open')
                  AND RequestedDeliveryDate BETWEEN @From AND @To
                  AND (@Cust IS NULL OR CustomerID = @Cust)
                GROUP BY CustomerID, ItemNo, RequestedDeliveryDate),
            dpo AS (
                SELECT CustomerID, ItemNo, PlanDate AS D, SUM(OrderQty) AS Q,
                       MAX(PartName) AS PartName, MAX(Unit) AS Unit, MAX(PackQty) AS PackQty
                FROM dbo.PP_DailyPo
                WHERE PlanDate BETWEEN @From AND @To AND (@Cust IS NULL OR CustomerID = @Cust)
                GROUP BY CustomerID, ItemNo, PlanDate),
            pl AS (
                SELECT CustomerID, ItemNo, PlanDate AS D, SUM(ScheduledQty) AS Q,
                       MAX(PartName) AS PartName, MAX(Unit) AS Unit, MAX(PackQty) AS PackQty
                FROM dbo.PP_DemandPlan
                WHERE PlanDate BETWEEN @From AND @To AND (@Cust IS NULL OR CustomerID = @Cust)
                GROUP BY CustomerID, ItemNo, PlanDate),
            k AS (
                SELECT CustomerID, ItemNo, D FROM po
                UNION SELECT CustomerID, ItemNo, D FROM dpo
                UNION SELECT CustomerID, ItemNo, D FROM pl)
            SELECT k.CustomerID, k.ItemNo, k.D, i.ItemName, i.PalletQty,
                   COALESCE(dpo.PartName, pl.PartName) AS PartName,
                   COALESCE(dpo.Unit, pl.Unit, i.DefaultUOM) AS Unit,
                   COALESCE(dpo.PackQty, pl.PackQty) AS PackQty,
                   CASE WHEN po.Q IS NOT NULL THEN po.Q WHEN dpo.Q IS NOT NULL THEN dpo.Q ELSE pl.Q END AS Qty,
                   CASE WHEN po.Q IS NOT NULL THEN 'PO' WHEN dpo.Q IS NOT NULL THEN 'DAILYPO' ELSE 'PLAN' END AS Src,
                   CASE WHEN i.ItemNo IS NULL THEN 0 ELSE 1 END AS ItemExists
            FROM k
            LEFT JOIN po  ON po.CustomerID  = k.CustomerID AND po.ItemNo  = k.ItemNo AND po.D  = k.D
            LEFT JOIN dpo ON dpo.CustomerID = k.CustomerID AND dpo.ItemNo = k.ItemNo AND dpo.D = k.D
            LEFT JOIN pl  ON pl.CustomerID  = k.CustomerID AND pl.ItemNo  = k.ItemNo AND pl.D  = k.D
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = k.ItemNo
            ORDER BY k.CustomerID, k.ItemNo, k.D;
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = string.IsNullOrEmpty(customerId) ? DBNull.Value : customerId;
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        using var rdr = cmd.ExecuteReader();
        var list = new List<EffectiveRow>();
        while (rdr.Read())
            list.Add(new EffectiveRow((string)rdr["CustomerID"], (string)rdr["ItemNo"], rdr["ItemName"] as string,
                rdr["PartName"] as string, rdr["Unit"] as string,
                rdr["PackQty"] is decimal p ? p : null, rdr["PalletQty"] is decimal pq ? pq : null,
                DateOnly.FromDateTime((DateTime)rdr["D"]), (decimal)rdr["Qty"], (string)rdr["Src"], (int)rdr["ItemExists"] == 1));
        return list;
    }

    /// <summary>업로드 이력 전체: 구매오더(PO) · 일별 발주(DAILYPO) · 일별 계획(DAILYPLAN) · 이전 주간(WEEKLY). 최신순 300건.</summary>
    public List<ImportHistoryRow> ListImportHistory(string? customerId)
    {
        const string sql = """
            SELECT TOP 300 * FROM (
                SELECT 'PO' AS Kind, Batch, CustomerID, FileName, DateFrom, DateTo, [RowCount], ItemCount, ImportedAt, ImportedBy,
                       CONCAT('신규 ', Inserted, ' · 갱신 ', Updated, ' · 취소 ', Cancelled) AS Note
                FROM dbo.PP_ImportBatch
                WHERE (@Cust IS NULL OR CustomerID = @Cust)
                UNION ALL
                SELECT 'DAILYPLAN', Batch, CustomerID, FileName, DateFrom, DateTo, [RowCount], ItemCount, ImportedAt, ImportedBy,
                       CONCAT('미매칭 ', UnmatchedItems, ' · 적입 불일치 ', PackMismatch)
                FROM dbo.PP_DemandPlanBatch
                WHERE (@Cust IS NULL OR CustomerID = @Cust)
                UNION ALL
                SELECT 'DAILYPO', Batch, MAX(CustomerID), MAX(FileName), MIN(PlanDate), MAX(PlanDate), COUNT(*), COUNT(DISTINCT ItemNo),
                       MIN(ImportedAt), MAX(CreatedBy), NULL
                FROM dbo.PP_DailyPo
                WHERE (@Cust IS NULL OR CustomerID = @Cust)
                GROUP BY Batch
                UNION ALL
                SELECT 'WEEKLY', ForecastBatch, MAX(CustomerID), NULL, MIN(WeekStartDate), MAX(WeekStartDate), COUNT(*), COUNT(DISTINCT ItemNo),
                       MIN(ImportedAt), MAX(ImportedBy), N'이전 주간 업로드(참고, 화면에서 쓰지 않음)'
                FROM dbo.PP_Forecast
                WHERE ForecastBatch IS NOT NULL AND (@Cust IS NULL OR CustomerID = @Cust)
                GROUP BY ForecastBatch
            ) h
            ORDER BY ImportedAt DESC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = string.IsNullOrEmpty(customerId) ? DBNull.Value : customerId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ImportHistoryRow>();
        while (rdr.Read())
            list.Add(new ImportHistoryRow((string)rdr["Kind"], (string)rdr["Batch"], rdr["CustomerID"] as string, rdr["FileName"] as string,
                rdr["DateFrom"] as DateTime?, rdr["DateTo"] as DateTime?, (int)rdr["RowCount"], (int)rdr["ItemCount"],
                (DateTime)rdr["ImportedAt"], rdr["ImportedBy"] as string, rdr["Note"] as string));
        return list;
    }

    /// <summary>구매오더 업로드 1건의 기록(이력용). 업서트·취소 뒤에 같은 화면에서 부른다.</summary>
    public void InsertPoImportBatch(string customerId, string batch, string? fileName, DateOnly? from, DateOnly? to,
        int rowCount, int itemCount, int inserted, int updated, int cancelled, string actor)
    {
        const string sql = """
            INSERT INTO dbo.PP_ImportBatch (Batch, CustomerID, FileName, DateFrom, DateTo, [RowCount], ItemCount, Inserted, Updated, Cancelled, ImportedAt, ImportedBy, CreatedBy)
            VALUES (@Batch, @Cust, @File, @From, @To, @Rows, @Items, @Ins, @Upd, @Canc, SYSDATETIME(), @By, @By);
            """;
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Batch", SqlDbType.VarChar, 30).Value = batch;
        cmd.Parameters.Add("@Cust",  SqlDbType.VarChar, 20).Value = customerId;
        cmd.Parameters.Add("@File",  SqlDbType.NVarChar, 200).Value = (object?)(fileName is { Length: > 200 } fn ? fn[..200] : fileName) ?? DBNull.Value;
        cmd.Parameters.Add("@From",  SqlDbType.Date).Value = from is DateOnly f ? f.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@To",    SqlDbType.Date).Value = to is DateOnly t ? t.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@Rows",  SqlDbType.Int).Value = rowCount;
        cmd.Parameters.Add("@Items", SqlDbType.Int).Value = itemCount;
        cmd.Parameters.Add("@Ins",   SqlDbType.Int).Value = inserted;
        cmd.Parameters.Add("@Upd",   SqlDbType.Int).Value = updated;
        cmd.Parameters.Add("@Canc",  SqlDbType.Int).Value = cancelled;
        cmd.Parameters.Add("@By",    SqlDbType.VarChar, 20).Value = actor;
        cmd.ExecuteNonQuery();
    }
}
