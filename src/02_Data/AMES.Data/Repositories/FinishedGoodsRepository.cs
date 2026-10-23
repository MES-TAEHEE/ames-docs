using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    private readonly AmesConnectionFactory _factory;
    public FinishedGoodsRepository(AmesConnectionFactory factory) => _factory = factory;

    public record ReturnRow(int ReturnId, string CustomerId, string? CustomerName,
        int ShipmentId, string? OutboundBarcode, string PartNo, string PartName, string? LotNo,
        decimal Qty, string? ReturnReason, string? Status, DateTime? ReceivedAt, string? ReceivedBy, string? Note,
        DateTime? ClosedAt = null, string? ClosedBy = null, string? InspectionNote = null);
    public record ShipmentRow(int ShipmentId, DateTime ShippedAt, string LotNo, string UnitType,
        string CustomerId, string? CustomerName, int ItemCount, decimal Qty, string? ShippedBy, string? PalletNo,string? PartNo,string? PartName) { public bool HasChildren => UnitType is "PALLET" or "CASE" || ItemCount > 1; }
    public record ShipmentItemRow(long TransactionId, string LotNo, string? PartNo, string? PartName,
        string? LocationNo, decimal Qty, string? Unit);
    public record HistoryRow(DateTime EventAt, string EventType, string? ReferenceNo, string? ItemOrOrder,
        decimal Qty, string? Location, string? WorkerId, string? Status, string? Details);

    public List<ReturnRow> ListReturns(string? search = null, string? status = null, DateTime? from = null, DateTime? to = null)
    {
        const string sql = """
            SELECT R.ReturnID,R.CustomerID,C.CustomerName,R.ShipmentID,S.LotNo AS OutboundBarcode,
                R.PartNo,R.PartName,R.LotNo,R.ReturnQty AS Qty,R.ReturnReason,R.Status,R.ReceivedAt,R.ReceivedBy,R.Note,R.ClosedAt,R.ClosedBy,
                Inspection.Note AS InspectionNote
            FROM dbo.FG_CustomerReturn R
            JOIN dbo.FG_Shipment S ON S.ShipmentID=R.ShipmentID
            JOIN dbo.MD_Customer C ON C.CustomerID=R.CustomerID
            OUTER APPLY(SELECT TOP(1) A.Note FROM dbo.SYS_AuditLog A
                WHERE A.TargetEntity='FG_CustomerReturn' AND A.TargetID=CONVERT(varchar(20),R.ReturnID)
                    AND A.ModuleCode='FG' AND A.ScreenCode='FG-01' AND A.Result='OK'
                ORDER BY A.EventTS DESC,A.LogID DESC) Inspection
            WHERE (@Status IS NULL OR UPPER(R.Status)=UPPER(@Status))
              AND (@From IS NULL OR R.ReceivedAt>=@From)
              AND (@To IS NULL OR R.ReceivedAt<DATEADD(day,1,@To))
              AND (@Search IS NULL OR CONVERT(varchar(20),R.ReturnID) LIKE @Search
                OR R.CustomerID LIKE @Search OR S.LotNo LIKE @Search
                OR R.PartNo LIKE @Search OR R.PartName LIKE @Search OR R.LotNo LIKE @Search)
            ORDER BY R.ReceivedAt DESC,R.ReturnID DESC;
            """;
        return Query(sql,r=>new ReturnRow(GetInt(r,"ReturnID"),GetString(r,"CustomerID") ?? "",
            GetString(r,"CustomerName"),GetInt(r,"ShipmentID"),GetString(r,"OutboundBarcode"),
            GetString(r,"PartNo") ?? "",GetString(r,"PartName") ?? "",GetString(r,"LotNo"),GetDecimal(r,"Qty"),
            GetString(r,"ReturnReason"),GetString(r,"Status"),GetDate(r,"ReceivedAt"),GetString(r,"ReceivedBy"),GetString(r,"Note"),GetDate(r,"ClosedAt"),GetString(r,"ClosedBy"),GetString(r,"InspectionNote")),
            ("@Search",null,Like(search)),("@Status","FG_CustomerReturn.Status",NullIfBlank(status)),
            ("@From",null,from?.Date),("@To",null,to?.Date));
    }

    public List<ShipmentRow> ListShipments(string? search = null, DateTime? from = null, DateTime? to = null)
    {
        const string sql = """
            SELECT TOP(500) S.ShipmentID,S.ShippedAt,S.LotNo,S.UnitType,S.CustomerID,C.CustomerName,
                D.ItemCount,D.Qty,S.ShippedBy,S.PalletNo,D.PartNo,P.ItemName AS PartName
            FROM dbo.FG_Shipment S JOIN dbo.MD_Customer C ON C.CustomerID=S.CustomerID
            CROSS APPLY
            (
                SELECT COUNT(*) ItemCount,COALESCE(SUM(-T.QtyChange),0) Qty,
                    CASE WHEN COUNT(DISTINCT T.PartNo)=1 THEN MIN(T.PartNo) END PartNo
                FROM dbo.WH_InventoryTransaction T
                WHERE T.SourceType='FG_OUTBOUND' AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
            ) D
            LEFT JOIN dbo.MD_Item P ON P.ItemNo=D.PartNo
            WHERE (@From IS NULL OR S.ShippedAt>=@From)
              AND (@To IS NULL OR S.ShippedAt<DATEADD(day,1,@To))
              AND (@Search IS NULL OR S.LotNo LIKE @Search OR S.CustomerID LIKE @Search
                OR C.CustomerName LIKE @Search OR S.ShippedBy LIKE @Search
                OR EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction T
                    WHERE T.SourceType='FG_OUTBOUND' AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
                      AND (T.LotNo LIKE @Search OR T.PartNo LIKE @Search OR T.LocationNo LIKE @Search)))
            ORDER BY S.ShippedAt DESC,S.ShipmentID DESC;
            """;
        return Query(sql,r=>new ShipmentRow(GetInt(r,"ShipmentID"),GetDate(r,"ShippedAt") ?? DateTime.MinValue,
            GetString(r,"LotNo") ?? "",GetString(r,"UnitType") ?? "",GetString(r,"CustomerID") ?? "",
            GetString(r,"CustomerName"),GetInt(r,"ItemCount"),GetDecimal(r,"Qty"),GetString(r,"ShippedBy"),GetString(r,"PalletNo"),GetString(r,"PartNo"),GetString(r,"PartName")),
            ("@Search",null,Like(search)),("@From",null,from?.Date),("@To",null,to?.Date));
    }

    public List<ShipmentItemRow> ListShipmentItems(int shipmentId)
    {
        const string sql = """
            SELECT T.TransactionID,T.LotNo,T.PartNo,COALESCE(I.PartName,M.ItemName) PartName,
                T.LocationNo,-T.QtyChange Qty,M.DefaultUOM Unit
            FROM dbo.WH_InventoryTransaction T
            LEFT JOIN dbo.WH_Inventory I ON I.LotNo=T.LotNo
            LEFT JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            WHERE T.SourceType='FG_OUTBOUND' AND T.SourceID=@ShipmentID AND T.TransactionType='OUT'
            ORDER BY T.TransactionID;
            """;
        return Query(sql,r=>new ShipmentItemRow(Convert.ToInt64(r["TransactionID"]),GetString(r,"LotNo") ?? "",
            GetString(r,"PartNo"),GetString(r,"PartName"),GetString(r,"LocationNo"),GetDecimal(r,"Qty"),GetString(r,"Unit")),
            ("@ShipmentID",null,shipmentId));
    }

    public List<HistoryRow> ListHistory(string? search = null, string? eventType = null, DateTime? from = null, DateTime? to = null)
    {
        const string sql = """
            SELECT TOP(1000) H.EventAt,H.EventType,H.ReferenceNo,H.ItemOrOrder,H.Qty,H.Location,H.WorkerID,H.Status,H.Details
            FROM
            (
                SELECT T.TransactionTime EventAt,CAST('PUT AWAY' AS varchar(20)) EventType,
                    CONVERT(nvarchar(80),T.TransactionID) COLLATE DATABASE_DEFAULT ReferenceNo,
                    T.PartNo COLLATE DATABASE_DEFAULT ItemOrOrder,
                    CASE WHEN T.TransactionType='MOVE' THEN T.QtyAfter ELSE T.QtyChange END Qty,
                    T.LocationNo COLLATE DATABASE_DEFAULT Location,T.OperatorID COLLATE DATABASE_DEFAULT WorkerID,
                    CAST('Confirmed' AS nvarchar(30)) COLLATE DATABASE_DEFAULT Status,
                    CONVERT(nvarchar(300),T.Note) COLLATE DATABASE_DEFAULT Details
                FROM dbo.WH_InventoryTransaction T
                WHERE T.SourceType='FG_PUTAWAY' AND T.TransactionType IN('IN','MOVE')
                UNION ALL
                SELECT S.ShippedAt,'SHIPPED',CONVERT(nvarchar(80),S.ShipmentID) COLLATE DATABASE_DEFAULT,
                    T.PartNo COLLATE DATABASE_DEFAULT,-T.QtyChange,T.LocationNo COLLATE DATABASE_DEFAULT,
                    S.ShippedBy COLLATE DATABASE_DEFAULT,'SHIPPED',CONVERT(nvarchar(300),S.LotNo) COLLATE DATABASE_DEFAULT
                FROM dbo.FG_Shipment S JOIN dbo.WH_InventoryTransaction T
                    ON T.SourceType='FG_OUTBOUND' AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
                UNION ALL
                SELECT COALESCE(R.ReceivedAt,R.CreatedTS),'RETURN',CONVERT(nvarchar(80),R.ReturnID) COLLATE DATABASE_DEFAULT,
                    R.PartNo COLLATE DATABASE_DEFAULT,R.ReturnQty,NULL,
                    COALESCE(R.ReceivedBy,R.CreatedBy) COLLATE DATABASE_DEFAULT,R.Status COLLATE DATABASE_DEFAULT,
                    CONVERT(nvarchar(300),R.ReturnReason) COLLATE DATABASE_DEFAULT FROM dbo.FG_CustomerReturn R
            ) H
            WHERE (@EventType IS NULL OR H.EventType=@EventType)
              AND (@From IS NULL OR H.EventAt>=@From) AND (@To IS NULL OR H.EventAt<DATEADD(day,1,@To))
              AND (@Search IS NULL OR H.ReferenceNo LIKE @Search OR H.ItemOrOrder LIKE @Search
                OR H.Location LIKE @Search OR H.WorkerID LIKE @Search OR H.Details LIKE @Search)
            ORDER BY H.EventAt DESC,H.ReferenceNo DESC;
            """;
        return Query(sql,r=>new HistoryRow(GetDate(r,"EventAt") ?? DateTime.MinValue,GetString(r,"EventType") ?? "",
            GetString(r,"ReferenceNo"),GetString(r,"ItemOrOrder"),GetDecimal(r,"Qty"),GetString(r,"Location"),
            GetString(r,"WorkerID"),GetString(r,"Status"),GetString(r,"Details")),
            ("@Search",null,Like(search)),("@EventType",null,NullIfBlank(eventType)?.ToUpperInvariant()),
            ("@From",null,from?.Date),("@To",null,to?.Date));
    }

    private List<T> Query<T>(string sql, Func<SqlDataReader,T> map, params (string Name,string? Column,object? Value)[] parameters)
    {
        using var conn=_factory.OpenConnection();
        using var cmd=new SqlCommand(sql,conn) { CommandTimeout=15 };
        foreach(var (name,column,value) in parameters) ColumnParams.AddFilter(cmd,name,column,value);
        using var reader=cmd.ExecuteReader();
        var rows=new List<T>();
        while(reader.Read()) rows.Add(map(reader));
        return rows;
    }
    private static string? Like(string? value)=>string.IsNullOrWhiteSpace(value)?null:$"%{value.Trim()}%";
    private static string? NullIfBlank(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string? GetString(SqlDataReader r,string name)=>r[name]==DBNull.Value?null:Convert.ToString(r[name]);
    private static int GetInt(SqlDataReader r,string name)=>r[name]==DBNull.Value?0:Convert.ToInt32(r[name]);
    private static decimal GetDecimal(SqlDataReader r,string name)=>r[name]==DBNull.Value?0:Convert.ToDecimal(r[name]);
    private static DateTime? GetDate(SqlDataReader r,string name)=>r[name]==DBNull.Value?null:Convert.ToDateTime(r[name]);
}
