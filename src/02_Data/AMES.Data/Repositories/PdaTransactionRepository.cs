using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed class PdaTransactionRepository(AmesConnectionFactory factory)
{
    public PdaTransactionPageDto ReadPage(string? search,DateTime from,DateTime to,string? worker=null,
        string? reason=null,string? direction=null,DateTime? beforeAt=null,long? beforeId=null,
        int pageSize=100,int? deliveryId=null,string? partNo=null)
    {
        if(pageSize is <1 or >100 || from.Date>to.Date || to.Date==DateTime.MaxValue.Date || search?.Length>120 || beforeAt.HasValue!=beforeId.HasValue
            || beforeId is <1 || deliveryId is <1)throw new ArgumentException("Invalid transaction page or date range.");
        using var conn=factory.OpenConnection();
        using var cmd=new SqlCommand(PageSql,conn){CommandTimeout=30};
        cmd.Parameters.Add("@From",SqlDbType.Date).Value=from.Date;
        cmd.Parameters.Add("@To",SqlDbType.Date).Value=to.Date;
        cmd.Parameters.Add("@Take",SqlDbType.Int).Value=pageSize+1;
        cmd.Parameters.Add("@BeforeAt",SqlDbType.DateTime2).Value=(object?)beforeAt??DBNull.Value;
        cmd.Parameters.Add("@BeforeID",SqlDbType.BigInt).Value=(object?)beforeId??DBNull.Value;
        cmd.Parameters.Add("@DeliveryID",SqlDbType.Int).Value=(object?)deliveryId??DBNull.Value;
        AddText(cmd,"@Search",122,string.IsNullOrWhiteSpace(search)?null:"%"+search.Trim()+"%");
        AddText(cmd,"@Worker",120,worker);AddText(cmd,"@Reason",100,reason);
        AddText(cmd,"@Direction",20,direction);AddText(cmd,"@PartNo",50,partNo);
        var rows=new List<PdaTransactionRowDto>();
        using(var r=cmd.ExecuteReader())while(r.Read())rows.Add(new(
            r.GetInt64(0),r.GetDateTime(1),r.GetString(2),Text(r,3),Text(r,4),r.GetDecimal(5),Text(r,6),
            r.GetString(7),Text(r,8),Text(r,9),Text(r,10),Text(r,11),Number(r,12),Number(r,13),Number(r,14),
            Text(r,15),Text(r,16),r.IsDBNull(17)?null:r.GetInt32(17),r.GetInt32(18)));
        var more=rows.Count>pageSize;if(more)rows.RemoveAt(pageSize);
        var last=rows.LastOrDefault();
        return new(rows,more,last?.OccurredAt,last?.Id);
    }

    public List<PdaTransactionPartDto> ReadParts(int deliveryId)
    {
        if(deliveryId<1)throw new ArgumentException("Invalid DCR.");
        using var conn=factory.OpenConnection();
        using var cmd=new SqlCommand("""
            SELECT T.PartNo,M.ItemName,SUM(-T.QtyChange),M.DefaultUOM,COUNT(DISTINCT T.LotNo)
            FROM dbo.WH_InventoryTransaction T LEFT JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            WHERE T.SourceType='FG_OUTBOUND' AND T.TransactionType='OUT'
              AND EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad L WHERE L.DeliveryID=@ID AND L.ShipmentID=T.SourceID)
            GROUP BY T.PartNo,M.ItemName,M.DefaultUOM ORDER BY T.PartNo;
            """,conn){CommandTimeout=30};
        cmd.Parameters.Add("@ID",SqlDbType.Int).Value=deliveryId;
        var rows=new List<PdaTransactionPartDto>();
        using var r=cmd.ExecuteReader();while(r.Read())rows.Add(new(r.GetString(0),Text(r,1),r.GetDecimal(2),Text(r,3),r.GetInt32(4)));
        return rows;
    }

    private static void AddText(SqlCommand cmd,string name,int size,string? text)
    {
        if(text?.Length>size)throw new ArgumentException("Transaction filter is too long.");
        cmd.Parameters.Add(name,SqlDbType.NVarChar,size).Value=string.IsNullOrWhiteSpace(text)?DBNull.Value:text.Trim();
    }
    private static string? Text(SqlDataReader r,int n)=>r.IsDBNull(n)?null:r.GetString(n);
    private static decimal? Number(SqlDataReader r,int n)=>r.IsDBNull(n)?null:r.GetDecimal(n);

    internal const string PageSql="""
        WITH Raw AS (
            SELECT T.TransactionID ID,T.TransactionTime OccurredAt,COALESCE(T.LotNo,'') Barcode,T.PartNo,M.ItemName PartName,
                CASE WHEN T.TransactionType='MOVE' THEN COALESCE(T.QtyAfter,ABS(T.QtyChange)) ELSE ABS(T.QtyChange) END Qty,M.DefaultUOM Unit,
                CASE WHEN T.SourceType='FG_RETURN' THEN 'RETURN' ELSE T.TransactionType END Direction,
                T.LocationNo,T.OperatorID WorkerID,T.ReasonCode,T.Note,T.QtyBefore,T.QtyChange,T.QtyAfter,
                CASE WHEN T.TransactionType='MOVE' AND T.Note LIKE N'PDA put-away % -> %'
                     THEN NULLIF(SUBSTRING(T.Note,14,CHARINDEX(N' -> ',T.Note)-14),N'UNASSIGNED')
                     WHEN T.TransactionType='MOVE' AND T.Note LIKE N'Moved from % to %'
                     THEN NULLIF(SUBSTRING(T.Note,12,CHARINDEX(N' to ',T.Note)-12),N'(unassigned)')
                     ELSE T.LocationNo END BeforeLocation,T.LocationNo AfterLocation,D.DeliveryID,D.DeliveryNo,D.ShippedAt DeliveryShippedAt
            FROM dbo.WH_InventoryTransaction T LEFT JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            LEFT JOIN (SELECT DISTINCT DeliveryID,ShipmentID FROM dbo.FG_DeliveryLoad WHERE ShipmentID IS NOT NULL) L
                ON T.SourceType='FG_OUTBOUND' AND T.TransactionType='OUT' AND L.ShipmentID=T.SourceID
            LEFT JOIN dbo.FG_Delivery D ON D.DeliveryID=L.DeliveryID
            WHERE (@DeliveryID IS NOT NULL AND D.DeliveryID=@DeliveryID AND (@PartNo IS NULL OR T.PartNo=@PartNo))
               OR (@DeliveryID IS NULL AND
                   ((D.DeliveryID IS NULL AND T.TransactionTime>=@From AND T.TransactionTime<DATEADD(day,1,@To))
                    OR (D.DeliveryID IS NOT NULL AND D.ShippedAt>=@From AND D.ShippedAt<DATEADD(day,1,@To))))
        ), Entries AS (
            SELECT ID,OccurredAt,Barcode,PartNo,PartName,Qty,Unit,Direction,LocationNo,WorkerID,ReasonCode,Note,
                QtyBefore,QtyChange,QtyAfter,BeforeLocation,AfterLocation,DeliveryID,1 PartCount
            FROM Raw WHERE DeliveryID IS NULL OR @DeliveryID IS NOT NULL
            UNION ALL
            SELECT MAX(ID),COALESCE(MAX(DeliveryShippedAt),MAX(OccurredAt)),DeliveryNo,
                CASE WHEN COUNT(DISTINCT PartNo)=1 THEN MIN(PartNo) END,
                CASE WHEN COUNT(DISTINCT PartNo)=1 THEN MIN(PartName) END,SUM(Qty),
                CASE WHEN COUNT(DISTINCT Unit)=1 THEN MIN(Unit) END,'OUT',NULL,MAX(WorkerID),NULL,NULL,
                NULL,NULL,NULL,NULL,NULL,DeliveryID,COUNT(DISTINCT PartNo)
            FROM Raw WHERE DeliveryID IS NOT NULL AND @DeliveryID IS NULL GROUP BY DeliveryID,DeliveryNo
        )
        SELECT TOP(@Take) E.* FROM Entries E
        WHERE (@DeliveryID IS NOT NULL OR (E.OccurredAt>=@From AND E.OccurredAt<DATEADD(day,1,@To)))
          AND (@DeliveryID IS NULL OR E.DeliveryID=@DeliveryID)
          AND (@PartNo IS NULL OR E.PartNo=@PartNo)
          AND (@Direction IS NULL OR E.Direction=@Direction)
          AND (@Worker IS NULL OR E.WorkerID LIKE N'%'+@Worker+N'%')
          AND (@Reason IS NULL OR E.ReasonCode=@Reason)
          AND (@Search IS NULL OR E.Barcode LIKE @Search OR E.PartNo LIKE @Search OR E.LocationNo LIKE @Search
               OR (E.DeliveryID IS NOT NULL AND EXISTS(SELECT 1 FROM Raw R WHERE R.DeliveryID=E.DeliveryID
                    AND (R.Barcode LIKE @Search OR R.PartNo LIKE @Search OR R.LocationNo LIKE @Search))))
          AND (@BeforeAt IS NULL OR E.OccurredAt<@BeforeAt OR (E.OccurredAt=@BeforeAt AND E.ID<@BeforeID))
        ORDER BY E.OccurredAt DESC,E.ID DESC;
        """;
}
