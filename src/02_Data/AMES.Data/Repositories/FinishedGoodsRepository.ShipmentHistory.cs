namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    public record ShipmentHistoryRow(int? DeliveryId, int ShipmentId, string ReferenceNo,
        DateTime ShippedAt, string CustomerId, string? CustomerName, string? TrailerNo,
        int PalletCount, int ItemCount, decimal Qty, string? ShippedBy)
    {
        public string Key => DeliveryId is {} id ? $"DCR:{id}" : $"SHIP:{ShipmentId}";
    }

    public List<ShipmentHistoryRow> ListShipmentHistory(string? search = null, DateTime? from = null, DateTime? to = null, int limit = 500)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        const string sql = """
            WITH ShipmentSource AS
            (
                SELECT S.*,D.DeliveryID,D.DeliveryNo,D.TrailerNo
                FROM dbo.FG_Shipment S
                LEFT JOIN (SELECT DISTINCT DeliveryID,ShipmentID FROM dbo.FG_DeliveryLoad WHERE ShipmentID IS NOT NULL) L
                    ON L.ShipmentID=S.ShipmentID
                LEFT JOIN dbo.FG_Delivery D ON D.DeliveryID=L.DeliveryID
            ), History AS
            (
                SELECT S.DeliveryID,MIN(S.ShipmentID) ShipmentID,
                    COALESCE(S.DeliveryNo,MIN(S.LotNo)) ReferenceNo,MAX(S.ShippedAt) ShippedAt,
                    MIN(S.CustomerID) CustomerID,MAX(S.TrailerNo) TrailerNo,
                    COUNT(DISTINCT CASE WHEN S.UnitType IN('PALLET','CASE') THEN S.ShipmentID END) PalletCount,
                    SUM(T.ItemCount) ItemCount,SUM(T.Qty) Qty,MAX(S.ShippedBy) ShippedBy
                FROM ShipmentSource S
                CROSS APPLY(SELECT COUNT(*) ItemCount,COALESCE(SUM(-QtyChange),0) Qty
                    FROM dbo.WH_InventoryTransaction
                    WHERE SourceType='FG_OUTBOUND' AND SourceID=S.ShipmentID AND TransactionType='OUT') T
                GROUP BY S.DeliveryID,S.DeliveryNo,CASE WHEN S.DeliveryID IS NULL THEN S.ShipmentID END
            )
            SELECT TOP(@Limit) H.*,C.CustomerName FROM History H
            LEFT JOIN dbo.MD_Customer C ON C.CustomerID=H.CustomerID
            WHERE (@From IS NULL OR H.ShippedAt>=@From)
              AND (@To IS NULL OR H.ShippedAt<DATEADD(day,1,@To))
              AND (@Search IS NULL OR H.ReferenceNo LIKE @Search OR H.CustomerID LIKE @Search
                OR C.CustomerName LIKE @Search OR H.TrailerNo LIKE @Search OR H.ShippedBy LIKE @Search
                OR EXISTS(SELECT 1 FROM ShipmentSource S
                    LEFT JOIN dbo.WH_InventoryTransaction T ON T.SourceType='FG_OUTBOUND'
                        AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
                    WHERE ((H.DeliveryID IS NOT NULL AND S.DeliveryID=H.DeliveryID)
                        OR (H.DeliveryID IS NULL AND S.ShipmentID=H.ShipmentID))
                        AND (S.LotNo LIKE @Search OR S.PalletNo LIKE @Search OR T.LotNo LIKE @Search
                            OR T.PartNo LIKE @Search OR T.LocationNo LIKE @Search)))
            ORDER BY H.ShippedAt DESC,H.ShipmentID DESC;
            """;
        return Query(sql,r=>new ShipmentHistoryRow(r["DeliveryID"] is DBNull ? null : GetInt(r,"DeliveryID"),
            GetInt(r,"ShipmentID"),GetString(r,"ReferenceNo") ?? "",GetDate(r,"ShippedAt") ?? DateTime.MinValue,
            GetString(r,"CustomerID") ?? "",GetString(r,"CustomerName"),GetString(r,"TrailerNo"),
            GetInt(r,"PalletCount"),GetInt(r,"ItemCount"),GetDecimal(r,"Qty"),GetString(r,"ShippedBy")),
            ("@Search",null,Like(search)),("@From",null,from?.Date),("@To",null,to?.Date),("@Limit",null,limit));
    }

    public record ShipmentPalletPartRow(string? PalletNo,string? PartNo,string? PartName,decimal Qty,string? Unit);

    public record ShipmentHistoryPartRow(ShipmentHistoryRow Shipment,string? PalletNo,string? PartNo,
        string? PartName,int LotCount,decimal Qty,string? Unit);

    public List<ShipmentHistoryPartRow> ListShipmentHistoryRows(string? search=null,DateTime? from=null,DateTime? to=null,int limit=500)
    {
        var history=ListShipmentHistory(search,from,to,limit);
        if(history.Count==0)return [];
        var references=System.Text.Json.JsonSerializer.Serialize(history.Select(row=>new{row.DeliveryId,row.ShipmentId}));
        const string sql="""
            SELECT CONVERT(int,H.[key]) ParentIndex,S.PalletNo,T.PartNo,M.ItemName PartName,
                COUNT(T.TransactionID) LotCount,COALESCE(SUM(-T.QtyChange),0) Qty,M.DefaultUOM Unit
            FROM OPENJSON(@History) H
            CROSS APPLY OPENJSON(H.value) WITH(DeliveryID int '$.DeliveryId',ShipmentID int '$.ShipmentId') R
            JOIN dbo.FG_Shipment S ON (R.DeliveryID IS NULL AND S.ShipmentID=R.ShipmentID)
                OR (R.DeliveryID IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad L
                    WHERE L.DeliveryID=R.DeliveryID AND L.ShipmentID=S.ShipmentID))
            LEFT JOIN dbo.WH_InventoryTransaction T ON T.SourceType='FG_OUTBOUND'
                AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
            LEFT JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            GROUP BY H.[key],S.PalletNo,T.PartNo,M.ItemName,M.DefaultUOM
            ORDER BY CONVERT(int,H.[key]),S.PalletNo,T.PartNo;
            """;
        return Query(sql,r=>new ShipmentHistoryPartRow(history[GetInt(r,"ParentIndex")],GetString(r,"PalletNo"),
            GetString(r,"PartNo"),GetString(r,"PartName"),GetInt(r,"LotCount"),GetDecimal(r,"Qty"),GetString(r,"Unit")),
            ("@History",null,references));
    }

    public List<ShipmentItemRow> ListShipmentHistoryLots(int? deliveryId, int shipmentId, string? palletNo, string? partNo)
    {
        const string sql = """
            SELECT T.TransactionID,T.LotNo,T.PartNo,M.ItemName PartName,T.LocationNo,-T.QtyChange Qty,M.DefaultUOM Unit
            FROM dbo.FG_Shipment S
            JOIN dbo.WH_InventoryTransaction T ON T.SourceType='FG_OUTBOUND'
                AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
            LEFT JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            WHERE ((@DeliveryID IS NULL AND S.ShipmentID=@ShipmentID)
                OR (@DeliveryID IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad L
                    WHERE L.DeliveryID=@DeliveryID AND L.ShipmentID=S.ShipmentID)))
                AND (S.PalletNo=@PalletNo OR (S.PalletNo IS NULL AND @PalletNo IS NULL))
                AND (T.PartNo=@PartNo OR (T.PartNo IS NULL AND @PartNo IS NULL))
            ORDER BY T.TransactionID;
            """;
        return Query(sql,r=>new ShipmentItemRow(Convert.ToInt64(r["TransactionID"]),GetString(r,"LotNo") ?? "",
            GetString(r,"PartNo"),GetString(r,"PartName"),GetString(r,"LocationNo"),GetDecimal(r,"Qty"),GetString(r,"Unit")),
            ("@DeliveryID",null,deliveryId),("@ShipmentID",null,shipmentId),("@PalletNo",null,palletNo),("@PartNo",null,partNo));
    }

    public List<ShipmentPalletPartRow> ListShipmentHistoryParts(int? deliveryId, int shipmentId)
    {
        const string sql = """
            SELECT S.PalletNo,T.PartNo,M.ItemName PartName,SUM(-T.QtyChange) Qty,M.DefaultUOM Unit
            FROM dbo.FG_Shipment S
            JOIN dbo.WH_InventoryTransaction T ON T.SourceType='FG_OUTBOUND'
                AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
            LEFT JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            WHERE (@DeliveryID IS NULL AND S.ShipmentID=@ShipmentID)
               OR (@DeliveryID IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad L
                    WHERE L.DeliveryID=@DeliveryID AND L.ShipmentID=S.ShipmentID))
            GROUP BY S.PalletNo,T.PartNo,M.ItemName,M.DefaultUOM
            ORDER BY S.PalletNo,T.PartNo;
            """;
        return Query(sql,r=>new ShipmentPalletPartRow(GetString(r,"PalletNo"),GetString(r,"PartNo"),
            GetString(r,"PartName"),GetDecimal(r,"Qty"),GetString(r,"Unit")),
            ("@DeliveryID",null,deliveryId),("@ShipmentID",null,shipmentId));
    }
}
