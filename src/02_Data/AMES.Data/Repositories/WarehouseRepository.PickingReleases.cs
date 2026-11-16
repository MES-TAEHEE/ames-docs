using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public record PickingReleaseRow(long TransactionID, DateTime ReleasedAt, string PickSlipNo,
        string LotNo, string PartNo, string PartName, decimal Quantity, string Unit,
        string FromLocation, string ToLocation, string OperatorID);

    internal const string PickingReleaseHistorySql = """
        SELECT t.TransactionID,t.TransactionTime,p.PickSlipNo,t.LotNo,t.PartNo,
            COALESCE(i.ItemName,'') AS PartName,ABS(t.QtyChange) AS Quantity,
            COALESCE(i.DefaultUOM,'') AS Unit,COALESCE(t.LocationNo,'') AS FromLocation,
            COALESCE(p.ReqLocation,'') AS ToLocation,COALESCE(t.OperatorID,'') AS OperatorID
        FROM dbo.WH_InventoryTransaction t
        JOIN dbo.WH_PickSlip p ON p.PickSlipID=TRY_CONVERT(int,t.SourceID)
        LEFT JOIN dbo.MD_Item i ON i.ItemNo=t.PartNo
        WHERE t.TransactionType='OUT' AND t.SourceType='PICK_SLIP'
          AND t.TransactionTime>=@From AND t.TransactionTime<@Until
          AND (@Search IS NULL OR p.PickSlipNo LIKE @Search OR t.LotNo LIKE @Search
               OR t.PartNo LIKE @Search OR i.ItemName LIKE @Search OR p.ReqLocation LIKE @Search)
        ORDER BY t.TransactionTime DESC,t.TransactionID DESC;
        """;

    public List<PickingReleaseRow> ListPickingReleases(DateTime from, DateTime to, string? search = null)
    {
        if (to.Date < from.Date) throw new ArgumentException("End date must be on or after start date.");
        using var c = _factory.OpenConnection();
        using var cmd = new SqlCommand(PickingReleaseHistorySql, c);
        cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = from.Date;
        cmd.Parameters.Add("@Until", SqlDbType.DateTime2).Value = to.Date.AddDays(1);
        cmd.Parameters.Add("@Search", SqlDbType.NVarChar, 242).Value = (object?)Like(search) ?? DBNull.Value;
        using var r = cmd.ExecuteReader();
        var rows = new List<PickingReleaseRow>();
        while (r.Read()) rows.Add(new(GetLong(r,"TransactionID"),r.GetDateTime(1),GetString(r,"PickSlipNo") ?? "",
            GetString(r,"LotNo") ?? "",GetString(r,"PartNo") ?? "",GetString(r,"PartName") ?? "",
            GetDecimal(r,"Quantity"),GetString(r,"Unit") ?? "",GetString(r,"FromLocation") ?? "",
            GetString(r,"ToLocation") ?? "",GetString(r,"OperatorID") ?? ""));
        return rows;
    }
}
