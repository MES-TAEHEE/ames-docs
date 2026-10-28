using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed class AssyReadyRepository(AmesConnectionFactory factory)
{
    public List<AssyReadyPalletDto> Read()
    {
        using var conn=factory.OpenConnection();
        using var cmd=new SqlCommand(Sql,conn){CommandTimeout=30};
        var rows=new List<(string? Pallet,string Part,string? Name,decimal Qty,string? Unit,DateTime? ReadyAt,string LotNo)>();
        using(var r=cmd.ExecuteReader())while(r.Read())rows.Add((r.IsDBNull(0)?null:r.GetString(0),r.GetString(1),
            r.IsDBNull(2)?null:r.GetString(2),r.GetDecimal(3),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetDateTime(5),r.GetString(6)));
        return GroupRows(rows);
    }

    internal static List<AssyReadyPalletDto> GroupRows(IEnumerable<(string? Pallet,string Part,string? Name,decimal Qty,string? Unit,DateTime? ReadyAt,string LotNo)> rows)
        => rows.GroupBy(x=>x.Pallet??"",StringComparer.OrdinalIgnoreCase).Select(g=>new AssyReadyPalletDto(
            g.Key.Length==0?null:g.Key,g.Min(x=>x.ReadyAt),g.Count(),
            g.GroupBy(x=>(x.Part,x.Name,x.Unit)).OrderBy(p=>p.Key.Part).Select(p=>new AssyReadyPartDto(
                p.Key.Part,p.Key.Name,p.Sum(x=>x.Qty),p.Key.Unit,p.Count())).ToList(),
            g.OrderBy(x=>x.LotNo).Select(x=>new AssyReadyLotDto(x.LotNo,x.Part,x.Name,x.Qty,x.Unit,x.ReadyAt)).ToList()))
            .OrderBy(x=>x.ReadyAt??DateTime.MaxValue).ThenBy(x=>x.PalletNo).ToList();
    internal const string Sql="""
        WITH PalletContents AS (
            SELECT W.LotNo RootPallet,W.LotNo,0 Depth FROM dbo.WH_Inventory W WHERE W.UnitType='PALLET'
            UNION ALL
            SELECT P.RootPallet,C.LotNo,P.Depth+1 FROM PalletContents P
            JOIN dbo.WH_Inventory C ON C.ParentLotNo=P.LotNo
        ), Candidates AS (
            SELECT W.LotNo,W.PartNo,W.Qty,W.ReceivedAt ReadyAt,P.RootPallet PalletNo
            FROM dbo.WH_Inventory W
            OUTER APPLY(SELECT TOP(1) RootPallet FROM PalletContents P WHERE P.LotNo=W.LotNo AND P.Depth>0 ORDER BY P.Depth DESC,P.RootPallet) P
            WHERE W.Qty>0 AND NULLIF(LTRIM(RTRIM(W.LocationNo)),'') IS NULL
              AND ISNULL(W.UnitType,'PART')<>'PALLET'
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)
        )
        SELECT C.PalletNo,C.PartNo,I.ItemName,CAST(C.Qty AS decimal(18,3)),I.DefaultUOM,C.ReadyAt,C.LotNo
        FROM Candidates C JOIN dbo.MD_Item I ON I.ItemNo=C.PartNo
        WHERE I.ItemType='ASSY' AND ISNULL(I.ActiveFlag,1)=1
          AND NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WHERE R.LotNo=C.LotNo COLLATE DATABASE_DEFAULT AND R.Status IN('Open','Inspecting'))
          AND ISNULL((SELECT TOP(1) T.SourceType FROM dbo.WH_InventoryTransaction T
              WHERE T.LotNo=C.LotNo COLLATE DATABASE_DEFAULT AND T.QtyChange<>0 ORDER BY T.TransactionTime DESC,T.TransactionID DESC),'')
              NOT IN('FG_OUTBOUND','FG_DEFECT','INV_DEFECT')
        ORDER BY C.ReadyAt,C.PalletNo,C.PartNo OPTION(MAXRECURSION 100);
        """;
}
