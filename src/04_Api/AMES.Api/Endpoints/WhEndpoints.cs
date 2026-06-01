using AMES.Api.Auth;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

/// <summary>
/// Warehouse endpoints (WH-01..WH-08). Single sample endpoint for now so
/// the PDA can prove end-to-end auth + data; the full module follows.
/// </summary>
public static class WhEndpoints
{
    public sealed record InboundRowDto(int LotId, string LotCode, string? ItemNo, string? ItemName,
                                        decimal Qty, string? Vendor, DateTime? ArrivedAt);

    public static void MapWh(this WebApplication app, AmesConnectionFactory factory)
    {
        var g = app.MapGroup("/api/wh").WithTags("Warehouse");

        g.MapGet("/inbound/today", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is null) return Results.Unauthorized();

            const string sql = """
                SELECT TOP 30 l.LotID, l.LotCode, l.ItemNo, i.ItemName,
                       ISNULL(l.BatchSize,0) AS Qty,
                       '' AS Vendor,
                       l.ProducedAt AS ArrivedAt
                FROM   dbo.tbl_Lot l
                LEFT JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
                WHERE  l.ProcessCode = 'WH'
                ORDER BY l.ProducedAt DESC;
                """;
            using var conn = factory.OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            using var rdr  = cmd.ExecuteReader();
            var rows = new List<InboundRowDto>();
            while (rdr.Read())
                rows.Add(new InboundRowDto(
                    (int)rdr["LotID"],
                    rdr["LotCode"] as string ?? "",
                    rdr["ItemNo"]   as string,
                    rdr["ItemName"] as string,
                    rdr["Qty"]      as decimal? ?? 0,
                    rdr["Vendor"]   as string,
                    rdr["ArrivedAt"] as DateTime?));
            return Results.Ok(rows);
        });
    }
}
