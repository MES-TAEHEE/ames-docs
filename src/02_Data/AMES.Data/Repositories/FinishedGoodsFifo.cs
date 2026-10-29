using System.Data;
using AMES.Contracts.Dto;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed class FifoConfirmationRequiredException(List<FifoWarningDto> warnings)
    : InvalidOperationException("Older available stock exists. Review FIFO stock before continuing.")
{
    public List<FifoWarningDto> Warnings { get; } = warnings;
}

public static class FinishedGoodsFifo
{
    public static void RequireConfirmation(SqlConnection conn, SqlTransaction tx, string barcode, string? customer, bool acknowledged)
    {
        FinishedGoodsRepository.Gate(conn, tx);
        if (!acknowledged && Check(conn, tx, barcode, customer) is { Count: > 0 } warnings)
            throw new FifoConfirmationRequiredException(warnings);
    }

    // Recommendations only: acknowledgement never bypasses outbound/customer/quantity validation.
    public static List<FifoWarningDto> Check(SqlConnection conn, SqlTransaction? tx, string barcode, string? customer, int? deliveryId = null)
    {
        using var cmd = new SqlCommand(CandidatesSql, conn, tx) { CommandTimeout = 30 };
        cmd.Parameters.Add("@Barcode", SqlDbType.NVarChar, 50).Value = barcode;
        cmd.Parameters.Add("@Customer", SqlDbType.VarChar, 20).Value = (object?)customer ?? DBNull.Value;
        cmd.Parameters.Add("@DeliveryId", SqlDbType.Int).Value = (object?)deliveryId ?? DBNull.Value;
        using var reader = cmd.ExecuteReader();
        var rows = new List<FifoWarningDto>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetDateTime(4)));
        return rows;
    }

    // Advisory reads must not retain locks on unrelated pallets inside a SERIALIZABLE shipment.
    internal const string CandidatesSql = """
        SET NOCOUNT ON;
        DECLARE @Leaves TABLE(LotNo nvarchar(50) PRIMARY KEY,PartNo varchar(20),LocationNo varchar(20),
            ReceivedAt datetime2,CustomerID varchar(20),Available bit);
        INSERT @Leaves
        SELECT W.LotNo,W.PartNo,W.LocationNo,W.ReceivedAt,C.CustomerID,
            CASE WHEN W.PartNo IS NOT NULL AND L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1
                AND UPPER(COALESCE(L.LocationType,''))='FG'
                AND NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WITH(READCOMMITTED) WHERE R.LotNo=W.LotNo AND R.Status IN('Open','Inspecting','Rejected'))
                AND NOT EXISTS(SELECT 1 FROM dbo.tbl_Lot T WITH(READCOMMITTED) WHERE T.LotCode COLLATE DATABASE_DEFAULT=W.LotNo COLLATE DATABASE_DEFAULT
                    AND UPPER(COALESCE(T.Status,'')) IN('DEFECT','SCRAP','SCRAPPED'))
                THEN 1 ELSE 0 END
        FROM dbo.WH_Inventory W WITH(READCOMMITTED) LEFT JOIN dbo.MD_Location L WITH(READCOMMITTED) ON L.LocationID=W.LocationNo
        OUTER APPLY(
            SELECT MIN(M.CustomerID) CustomerID FROM dbo.tbl_Lot T WITH(READCOMMITTED)
            LEFT JOIN dbo.PR_ImgLot I WITH(READCOMMITTED) ON I.LotID=T.LotID
            LEFT JOIN dbo.PR_PntLot N WITH(READCOMMITTED) ON N.LotID=T.LotID
            JOIN dbo.MD_Customer M WITH(READCOMMITTED) ON M.CustomerID COLLATE DATABASE_DEFAULT=COALESCE(I.CustomerCode,N.CustomerCode) COLLATE DATABASE_DEFAULT
                OR M.CustomerCode COLLATE DATABASE_DEFAULT=COALESCE(I.CustomerCode,N.CustomerCode) COLLATE DATABASE_DEFAULT
            WHERE T.LotCode COLLATE DATABASE_DEFAULT=W.LotNo COLLATE DATABASE_DEFAULT
            HAVING COUNT(DISTINCT M.CustomerID)=1
        ) C
        WHERE W.Qty>0 AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory X WITH(READCOMMITTED) WHERE X.ParentLotNo=W.LotNo AND X.Qty>0);

        DECLARE @Roots TABLE(LotNo nvarchar(50) PRIMARY KEY,RootLotNo nvarchar(50));
        -- Ignore malformed candidate ancestry instead of failing unrelated outbound stock.
        ;WITH Ancestors AS(
            SELECT S.LotNo,W.LotNo NodeLot,W.ParentLotNo,0 Depth FROM @Leaves S JOIN dbo.WH_Inventory W WITH(READCOMMITTED) ON W.LotNo=S.LotNo
            UNION ALL
            SELECT A.LotNo,P.LotNo,P.ParentLotNo,A.Depth+1 FROM Ancestors A JOIN dbo.WH_Inventory P WITH(READCOMMITTED) ON P.LotNo=A.ParentLotNo WHERE A.Depth<99
        ), Ranked AS(
            SELECT LotNo,NodeLot,ROW_NUMBER() OVER(PARTITION BY LotNo ORDER BY Depth DESC) N FROM Ancestors A
            WHERE ParentLotNo IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory P WITH(READCOMMITTED) WHERE P.LotNo=A.ParentLotNo)
        )
        INSERT @Roots SELECT LotNo,NodeLot FROM Ranked WHERE N=1 OPTION(MAXRECURSION 100);

        DECLARE @Selected TABLE(LotNo nvarchar(50) PRIMARY KEY);
        ;WITH Scope AS(
            SELECT LotNo FROM dbo.WH_Inventory W WITH(READCOMMITTED) WHERE LotNo=@Barcode OR EXISTS(
                SELECT 1 FROM dbo.FG_DeliveryLoad D WITH(READCOMMITTED) WHERE D.DeliveryID=@DeliveryId AND D.RootLotNo=W.LotNo AND D.ShipmentID IS NULL)
            UNION ALL SELECT W.LotNo FROM dbo.WH_Inventory W WITH(READCOMMITTED) JOIN Scope S ON W.ParentLotNo=S.LotNo
        ) INSERT @Selected SELECT LotNo FROM Scope OPTION(MAXRECURSION 100);
        ;WITH EligibleRoots AS(
            SELECT R.RootLotNo FROM @Roots R JOIN @Leaves L ON L.LotNo=R.LotNo
            GROUP BY R.RootLotNo
            HAVING MIN(CONVERT(int,L.Available))=1 AND COUNT(DISTINCT L.CustomerID)=1
                AND COUNT(L.CustomerID)=COUNT(*)
                AND NOT EXISTS(SELECT 1 FROM @Roots X JOIN dbo.FG_DeliveryLoad D WITH(READCOMMITTED) ON D.LotNo=X.LotNo
                    WHERE X.RootLotNo=R.RootLotNo AND D.ShipmentID IS NULL)
        ), Older AS(
            SELECT C.PartNo,C.LotNo,CASE WHEN R.RootLotNo<>C.LotNo THEN R.RootLotNo END PalletNo,C.LocationNo,C.ReceivedAt,
                ROW_NUMBER() OVER(PARTITION BY C.PartNo ORDER BY C.ReceivedAt,C.LotNo) N
            FROM @Leaves C JOIN @Roots R ON R.LotNo=C.LotNo JOIN EligibleRoots E ON E.RootLotNo=R.RootLotNo
            WHERE NOT EXISTS(SELECT 1 FROM @Selected X WHERE X.LotNo=C.LotNo)
                AND EXISTS(SELECT 1 FROM @Selected X JOIN @Leaves S ON S.LotNo=X.LotNo
                    WHERE S.PartNo=C.PartNo AND COALESCE(S.CustomerID,@Customer)=C.CustomerID AND C.ReceivedAt<S.ReceivedAt)
        )
        SELECT PartNo,LotNo,PalletNo,LocationNo,ReceivedAt FROM Older WHERE N=1 ORDER BY PartNo;
        """;
}
