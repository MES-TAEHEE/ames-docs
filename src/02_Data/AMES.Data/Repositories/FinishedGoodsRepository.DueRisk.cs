using AMES.Contracts.Dto;
using AMES.Data.Scheduling;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    public ShippingDueRiskSnapshot ListShippingDueRisks()
    {
        using var conn = _factory.OpenConnection();
        var today = PpRepository.ReadNow(conn, null).Date;
        var calendar = new WorkdayCalendar(PpRepository.ReadCalendar(conn, null, today, DateTime.MaxValue.Date));
        var next = ShippingDueRisk.NextWorkday(today, calendar);
        using var cmd = new SqlCommand(DueRiskSql, conn) { CommandTimeout = 30 };
        using var r = cmd.ExecuteReader();
        var rows = new List<ShippingDueRiskDto>();
        while (r.Read())
        {
            var due = GetDate(r, "RequestedDeliveryDate");
            decimal? ordered = r["OrderQty"] is DBNull ? null : Convert.ToDecimal(r["OrderQty"]);
            decimal? shipped = r["ShippedQty"] is DBNull ? null : Convert.ToDecimal(r["ShippedQty"]);
            var customer = GetString(r, "CustomerID");
            var part = GetString(r, "ItemNo");
            var risk = ShippingDueRisk.Evaluate(today, next, due, ordered, shipped, GetString(r, "Status"),
                Convert.ToBoolean(r["Identified"]) && !string.IsNullOrWhiteSpace(customer) && !string.IsNullOrWhiteSpace(part));
            if (risk == ShippingDueRiskLevel.None) continue;
            rows.Add(new(GetInt(r, "SoID"), GetString(r, "SoNumber"), customer, part, GetString(r, "ItemName"),
                due, ordered, shipped, ordered is >= 0 && shipped is >= 0 ? Math.Max(0, ordered.Value - shipped.Value) : null, risk));
        }
        return new(today, next, rows.OrderBy(x => x.Risk switch {
            ShippingDueRiskLevel.Overdue => 0, ShippingDueRiskLevel.DueSoon => 1,
            ShippingDueRiskLevel.Unknown => 2, _ => 3 }).ThenBy(x => x.DueDate).ThenBy(x => x.SoId).ToList());
    }

    // No shipment-order/date filter: an unplanned or long-overdue SO must remain visible.
    internal const string DueRiskSql = """
        SELECT S.SoID,S.SoNumber,S.CustomerID,S.ItemNo,I.ItemName,S.RequestedDeliveryDate,S.OrderQty,S.ShippedQty,S.Status,
            CAST(CASE WHEN C.CustomerID IS NOT NULL AND I.ItemNo IS NOT NULL THEN 1 ELSE 0 END AS bit) Identified
        FROM dbo.PP_CustomerOrder S LEFT JOIN dbo.MD_Item I ON I.ItemNo=S.ItemNo
        LEFT JOIN dbo.MD_Customer C ON C.CustomerID=S.CustomerID
        WHERE UPPER(LTRIM(RTRIM(COALESCE(S.Status,'')))) NOT IN('CANCELLED','CANCELED','CLOSED','SHIPPED')
          AND (S.OrderQty IS NULL OR S.ShippedQty IS NULL OR S.OrderQty<0 OR S.ShippedQty<0 OR S.OrderQty>S.ShippedQty);
        """;
}
