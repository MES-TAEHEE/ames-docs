using System.Data;
using System.Text.Json;
using AMES.Contracts.Dto;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    private static (List<DispatchRecommendationDto> Recommendations,Dictionary<string,decimal> Shortages) ReadDispatchRecommendations(SqlConnection conn,SqlTransaction? tx,ShippingDeliveryDto delivery)
    {
        var needs=delivery.Parts.Select(p=>new{p.PartNo,Missing=Math.Max(0,p.RequiredQty-p.PreparedQty)}).ToList();
        if(delivery.Status is not ("Open" or "Loading") || needs.All(p=>p.Missing==0))return ([],new());
        using var cmd=new SqlCommand("""
            DECLARE @Need TABLE(PartNo varchar(20) PRIMARY KEY,Missing decimal(18,3));
            INSERT @Need SELECT PartNo,Missing FROM OPENJSON(@Needs) WITH(PartNo varchar(20),Missing decimal(18,3));
            DECLARE @Roots TABLE(RootLotNo nvarchar(50) PRIMARY KEY);
            ;WITH Ancestors AS(
                SELECT W.LotNo NodeLot,W.ParentLotNo,0 Depth
                FROM dbo.WH_Inventory W WITH(READCOMMITTED) JOIN @Need N ON N.PartNo=W.PartNo AND N.Missing>0
                WHERE W.Qty>0 AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory X WITH(READCOMMITTED) WHERE X.ParentLotNo=W.LotNo AND X.Qty>0)
                  AND NOT EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad D WITH(READCOMMITTED) WHERE D.LotNo=W.LotNo AND D.ShipmentID IS NULL)
                UNION ALL
                SELECT P.LotNo,P.ParentLotNo,A.Depth+1 FROM Ancestors A JOIN dbo.WH_Inventory P WITH(READCOMMITTED) ON P.LotNo=A.ParentLotNo WHERE A.Depth<99
            )
            INSERT @Roots SELECT DISTINCT NodeLot FROM Ancestors A
                WHERE ParentLotNo IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory P WITH(READCOMMITTED) WHERE P.LotNo=A.ParentLotNo)
                OPTION(MAXRECURSION 100);
            DECLARE @Leaves TABLE(RootLotNo nvarchar(50),LotNo nvarchar(50),PartNo varchar(20),UnitType varchar(10),
                LocationNo varchar(20),Qty decimal(18,3),ReceivedAt datetime2,Eligible bit);
            ;WITH Units AS(
                SELECT R.RootLotNo,W.LotNo,W.ParentLotNo,W.PartNo,W.UnitType,W.LocationNo,W.Qty,W.ReceivedAt
                FROM @Roots R JOIN dbo.WH_Inventory W WITH(READCOMMITTED) ON W.LotNo=R.RootLotNo
                UNION ALL
                SELECT U.RootLotNo,W.LotNo,W.ParentLotNo,W.PartNo,W.UnitType,W.LocationNo,W.Qty,W.ReceivedAt
                FROM Units U JOIN dbo.WH_Inventory W WITH(READCOMMITTED) ON W.ParentLotNo=U.LotNo
            )
            INSERT @Leaves
            SELECT U.RootLotNo,U.LotNo,U.PartNo,U.UnitType,U.LocationNo,U.Qty,U.ReceivedAt,
                CASE WHEN L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1 AND UPPER(COALESCE(L.LocationType,''))='FG'
                    AND U.PartNo IS NOT NULL AND C.CustomerCount<=1 AND (C.CustomerID IS NULL OR C.CustomerID=@Customer)
                    AND NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WITH(READCOMMITTED) WHERE R.LotNo=U.LotNo AND R.Status IN('Open','Inspecting','Rejected'))
                    AND NOT EXISTS(SELECT 1 FROM dbo.tbl_Lot T WITH(READCOMMITTED) WHERE T.LotCode COLLATE DATABASE_DEFAULT=U.LotNo COLLATE DATABASE_DEFAULT AND UPPER(COALESCE(T.Status,'')) IN('DEFECT','SCRAP','SCRAPPED'))
                    AND NOT EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad D WITH(READCOMMITTED) WHERE D.LotNo=U.LotNo AND D.ShipmentID IS NULL)
                    THEN 1 ELSE 0 END
            FROM Units U LEFT JOIN dbo.MD_Location L WITH(READCOMMITTED) ON L.LocationID=U.LocationNo
            OUTER APPLY(
                SELECT MAX(M.CustomerID) CustomerID,COUNT(DISTINCT M.CustomerID) CustomerCount FROM dbo.tbl_Lot T WITH(READCOMMITTED)
                LEFT JOIN dbo.PR_ImgLot I WITH(READCOMMITTED) ON I.LotID=T.LotID
                LEFT JOIN dbo.PR_PntLot P WITH(READCOMMITTED) ON P.LotID=T.LotID
                JOIN dbo.MD_Customer M WITH(READCOMMITTED) ON M.CustomerID COLLATE DATABASE_DEFAULT=COALESCE(I.CustomerCode,P.CustomerCode) COLLATE DATABASE_DEFAULT OR M.CustomerCode COLLATE DATABASE_DEFAULT=COALESCE(I.CustomerCode,P.CustomerCode) COLLATE DATABASE_DEFAULT
                WHERE T.LotCode COLLATE DATABASE_DEFAULT=U.LotNo COLLATE DATABASE_DEFAULT
            ) C
            WHERE U.Qty>0 AND NOT EXISTS(SELECT 1 FROM Units X WHERE X.ParentLotNo=U.LotNo AND X.Qty>0)
            OPTION(MAXRECURSION 100);
            DECLARE @WholeRoots TABLE(RootLotNo nvarchar(50) PRIMARY KEY);
            INSERT @WholeRoots SELECT RootLotNo FROM @Leaves L GROUP BY RootLotNo
            HAVING MIN(CONVERT(int,Eligible))=1
                AND NOT EXISTS(SELECT 1 FROM @Leaves X WHERE X.RootLotNo=L.RootLotNo AND NOT EXISTS(SELECT 1 FROM @Need N WHERE N.PartNo=X.PartNo));
            ;WITH Candidates AS(
                SELECT L.PartNo,L.RootLotNo Barcode,W.UnitType,L.LocationNo,SUM(L.Qty) Qty,MIN(L.ReceivedAt) ReceivedAt
                FROM @Leaves L JOIN @WholeRoots R ON R.RootLotNo=L.RootLotNo
                JOIN dbo.WH_Inventory W WITH(READCOMMITTED) ON W.LotNo=L.RootLotNo
                JOIN @Need N ON N.PartNo=L.PartNo AND N.Missing>0
                GROUP BY L.PartNo,L.RootLotNo,W.UnitType,L.LocationNo
                UNION ALL
                SELECT L.PartNo,L.LotNo,L.UnitType,L.LocationNo,L.Qty,L.ReceivedAt
                FROM @Leaves L JOIN @Need N ON N.PartNo=L.PartNo AND N.Missing>0
                WHERE L.Eligible=1 AND NOT EXISTS(SELECT 1 FROM @WholeRoots R WHERE R.RootLotNo=L.RootLotNo)
            ), Ranked AS(
                SELECT C.*,SUM(Qty) OVER(PARTITION BY PartNo ORDER BY ReceivedAt,Barcode,LocationNo ROWS UNBOUNDED PRECEDING)-Qty PriorQty,
                    ROW_NUMBER() OVER(PARTITION BY PartNo ORDER BY ReceivedAt,Barcode,LocationNo) Seq
                FROM Candidates C
            )
            SELECT R.PartNo,R.Barcode,R.UnitType,R.LocationNo,L.LocationName,R.Qty,R.ReceivedAt
            FROM Ranked R JOIN @Need N ON N.PartNo=R.PartNo
            LEFT JOIN dbo.MD_Location L WITH(READCOMMITTED) ON L.LocationID=R.LocationNo
            WHERE R.PriorQty<N.Missing AND R.Seq<=50
            ORDER BY R.PartNo,R.ReceivedAt,R.Barcode,R.LocationNo;
            SELECT N.PartNo,CASE WHEN N.Missing>COALESCE(S.Qty,0) THEN N.Missing-COALESCE(S.Qty,0) ELSE 0 END Shortage
            FROM @Need N OUTER APPLY(SELECT SUM(L.Qty) Qty FROM @Leaves L WHERE L.PartNo=N.PartNo AND L.Eligible=1) S;
            """,conn,tx){CommandTimeout=30};
        cmd.Parameters.Add("@Needs",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(needs);
        Add(cmd,"@Customer",SqlDbType.VarChar,20,delivery.CustomerId);
        using var r=cmd.ExecuteReader();var result=new List<DispatchRecommendationDto>();
        while(r.Read())result.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.GetDecimal(5),r.IsDBNull(6)?null:r.GetDateTime(6)));
        r.NextResult();var shortages=new Dictionary<string,decimal>(StringComparer.OrdinalIgnoreCase);
        while(r.Read())shortages[r.GetString(0)]=r.GetDecimal(1);
        return (result,shortages);
    }
}
