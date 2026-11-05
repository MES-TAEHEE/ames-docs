using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public sealed record PickingSlipFifoLotRow(string PickSlipNo, string PartNo, string LotNo, string? LocationNo, decimal Qty,
        DateTime? ProductionDate = null);

    public List<PickingSlipFifoLotRow> ListPickingSlipFifoLots(string pickSlipNo)
    {
        pickSlipNo = pickSlipNo.Trim();
        if (string.IsNullOrWhiteSpace(pickSlipNo)) return new List<PickingSlipFifoLotRow>();

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand($"""
            ;WITH ReleaseLines AS
            (
                SELECT
                    RS.PickSlipID,
                    COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.PickSlipID)) AS PickSlipNo,
                    RS.ItemNo,
                    RS.CreatedBy,
                    CASE
                        WHEN COALESCE(RS.DemandQty, 0) > COALESCE(RS.PickedQty, 0)
                            THEN COALESCE(RS.DemandQty, 0) - COALESCE(RS.PickedQty, 0)
                        ELSE 0
                    END AS RemainingQty
                FROM dbo.WH_PickSlip RS
                WHERE UPPER(COALESCE(NULLIF(RS.PickSlipNo, N''), CONCAT(N'RS-', RS.PickSlipID))) = UPPER(@PickSlipNo)
                  AND UPPER(COALESCE(RS.Status, N'OPEN')) NOT IN (N'CLOSED', N'RELEASED', N'CANCELED', N'CANCELLED')
            ),
            ReleaseDemand AS
            (
                SELECT MIN(PickSlipID) AS PickSlipID,PickSlipNo,ItemNo,CreatedBy,SUM(RemainingQty) AS RemainingQty
                FROM ReleaseLines GROUP BY PickSlipNo,ItemNo,CreatedBy
            ),
            RankedLots AS
            (
                SELECT
                    R.PickSlipNo,
                    R.PickSlipID,
                    R.ItemNo,
                    R.RemainingQty,
                    R.CreatedBy,
                    W.LotNo,
                    W.LocationNo,
                    W.Qty,
                    W.ReceivedAt,
                    COALESCE(CONVERT(date, L.ProducedAt), B.ProductionDate) AS ProductionDate,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY R.PickSlipID
                        ORDER BY W.ReceivedAt, W.CreatedAt, W.LotNo
                    ) AS FifoSeq,
                    COALESCE(SUM(W.Qty) OVER
                    (
                        PARTITION BY R.PickSlipID
                        ORDER BY W.ReceivedAt, W.CreatedAt, W.LotNo
                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                    ), 0) AS QtyBefore
                FROM ReleaseDemand R
                INNER JOIN dbo.WH_Inventory W ON W.PartNo COLLATE DATABASE_DEFAULT = R.ItemNo COLLATE DATABASE_DEFAULT
                LEFT JOIN dbo.tbl_Lot L ON L.LotCode COLLATE DATABASE_DEFAULT=W.LotNo COLLATE DATABASE_DEFAULT
                OUTER APPLY(SELECT TOP(1) DL.ProductionDate FROM dbo.SCM_DeliveryBox DB
                    JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID=DB.DeliveryLineID
                    WHERE DB.BoxNumber COLLATE DATABASE_DEFAULT=W.LotNo COLLATE DATABASE_DEFAULT
                    ORDER BY DB.BoxID DESC) B
                WHERE W.Qty > 0
                  AND NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn CR WHERE CR.LotNo=W.LotNo AND CR.Status IN('Open','Inspecting'))
                  AND (R.CreatedBy <> 'WH-AUTO' OR ({WarehouseRepository.WarehouseBoxPredicate}))
            )
            SELECT
                PickSlipNo AS PICK_SLIPNO,
                ItemNo AS PARTNO,
                LotNo AS LOTNO,
                LocationNo AS LOCATION_NO,
                Qty AS QTY,
                ProductionDate AS PROD_DATE
            FROM RankedLots
            WHERE (CreatedBy='WH-AUTO' AND QtyBefore < RemainingQty)
               OR (COALESCE(CreatedBy,'')<>'WH-AUTO' AND FifoSeq <= RemainingQty)
            ORDER BY PickSlipID, FifoSeq;
            """, conn);
        ColumnParams.AddFilter(cmd, "@PickSlipNo", "WH_PickSlip.PickSlipID", pickSlipNo);

        using var rdr = cmd.ExecuteReader();
        var rows = new List<PickingSlipFifoLotRow>();
        while (rdr.Read())
        {
            rows.Add(new PickingSlipFifoLotRow(
                GetString(rdr, "PICK_SLIPNO") ?? pickSlipNo,
                GetString(rdr, "PARTNO") ?? "",
                GetString(rdr, "LOTNO") ?? "",
                GetString(rdr, "LOCATION_NO"),
                GetDecimal(rdr, "QTY"), GetDateTime(rdr, "PROD_DATE")));
        }
        return rows;
    }

    public const string ReplenishmentActor = "WH-AUTO";
    public List<AMES.Contracts.Dto.PickingPartDto> ListOpenPickingParts()
    {
        var lines=Query($"""
            SELECT P.ItemNo,I.ItemName,COALESCE(NULLIF(P.PickSlipNo,''),CONCAT('RS-',P.PickSlipID)) Slip,
                COALESCE(P.ReqLocation,'') Destination,COALESCE(P.DemandQty,0)-COALESCE(P.PickedQty,0) Remaining,
                CASE WHEN P.CreatedBy='WH-AUTO' THEN COALESCE(NULLIF(I.DefaultUOM,''),'EA') ELSE 'BOX' END Unit,
                (SELECT STRING_AGG(A.LocationNo,', ') FROM(SELECT DISTINCT W.LocationNo FROM dbo.WH_Inventory W
                    WHERE W.PartNo=P.ItemNo AND {WarehouseBoxPredicate}) A) Locations
            FROM dbo.WH_PickSlip P LEFT JOIN dbo.MD_Item I ON I.ItemNo=P.ItemNo
            WHERE P.CloseDate IS NULL AND UPPER(COALESCE(P.Status,'OPEN')) NOT IN('CLOSED','RELEASED','CANCELED','CANCELLED')
              AND COALESCE(P.DemandQty,0)>COALESCE(P.PickedQty,0)
            ORDER BY P.ItemNo,P.RequiredAt,P.PickSlipID;
            """,r=>new {Part=GetString(r,"ItemNo")??"",Name=GetString(r,"ItemName"),Unit=GetString(r,"Unit")!,
                Slip=GetString(r,"Slip")!,Destination=GetString(r,"Destination")!,Qty=GetDecimal(r,"Remaining"),Locations=GetString(r,"Locations")});
        return lines.GroupBy(x=>(x.Part,x.Unit)).Select(g=>new AMES.Contracts.Dto.PickingPartDto(g.Key.Part,g.First().Name,g.Key.Unit,
            g.Sum(x=>x.Qty),g.First().Locations,g.Select(x=>new AMES.Contracts.Dto.PickingPartOrder(x.Slip,x.Destination,x.Qty)).ToList())).ToList();
    }
    // W is the inventory alias; shared by FIFO suggestions, scan validation and completion.
    public const string WarehouseBoxPredicate = """
        W.UnitType IN('BOX','PART') AND W.Qty>0
        AND NOT EXISTS (SELECT 1 FROM dbo.FG_CustomerReturn CR WHERE CR.LotNo=W.LotNo AND CR.Status IN('Open','Inspecting'))
        AND COALESCE(W.LocationNo,'') NOT LIKE 'FG%'
        AND NOT EXISTS (SELECT 1 FROM dbo.MD_Line ML
                        WHERE NULLIF(ML.LotPrefix,'') COLLATE DATABASE_DEFAULT=W.LocationNo COLLATE DATABASE_DEFAULT)
        AND NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)
        AND NOT EXISTS (SELECT 1 FROM (
            SELECT TOP(1) T.TransactionType,T.SourceType FROM dbo.WH_InventoryTransaction T
            WHERE T.LotNo=W.LotNo AND T.TransactionType IN('IN','OUT','MOVE') ORDER BY T.TransactionID DESC
        ) T WHERE NULLIF(W.LocationNo,'') IS NULL AND T.TransactionType='OUT' AND T.SourceType='PICK_SLIP')
        AND NOT EXISTS (SELECT 1 FROM dbo.MD_Location L
                        WHERE L.LocationID COLLATE DATABASE_DEFAULT=W.LocationNo COLLATE DATABASE_DEFAULT
                          AND (L.LocationLevel=1 OR L.AreaCode IN ('FG','FG_AREA','MNT_AREA','SP_AREA') OR L.LineID IS NOT NULL OR L.LocationType IN('FG','LINE')))
        """;

    public sealed record LineStockRow(string LineCode, string PartNo, string PartName,
        decimal Qty, decimal MinimumQty, bool HasOpenOrder, bool WrongPart=false, string? LineId=null,
        string Unit="EA", string? LineName=null, bool IsActive=true)
    {
        public decimal Shortage => Math.Max(0, MinimumQty - Qty);
        public decimal RequiredQty => decimal.Ceiling(Shortage);
        public decimal? DisplayQty => string.IsNullOrWhiteSpace(PartNo) ? null : Qty;
        public decimal? DisplayRequiredQty => string.IsNullOrWhiteSpace(PartNo) ? null : Shortage;
        public string State => !IsActive ? "Inactive" : string.IsNullOrWhiteSpace(PartNo) ? "Assign a part"
            : WrongPart ? "Different part at location" : Shortage == 0 ? "Sufficient" : HasOpenOrder ? "Requested"
            : PartNo.Length > 20 ? "Part number exceeds Pick Slip limit"
            : RequiredQty > 99999999999m ? "Request quantity exceeds Pick Slip limit" : "Pending";
    }

    public const string ActiveLineLocationsSql = """
        SELECT LotPrefix FROM dbo.MD_Line
        WHERE LEN(LotPrefix)=2
          AND UPPER(COALESCE(Status,'ACTIVE')) NOT IN ('INACTIVE','CLOSED')
        """;

    public static string? ResolveLineLocation(SqlConnection conn, string? value)
    {
        using var cmd = new SqlCommand(ActiveLineLocationsSql + " AND LotPrefix=@Location UNION ALL SELECT L.LocationID FROM dbo.MD_Location L JOIN dbo.MD_Line M ON M.LineID=L.LineID WHERE L.LocationID=@Location AND L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1 AND COALESCE(M.Status,'ACTIVE')='ACTIVE';", conn);
        cmd.Parameters.Add("@Location", SqlDbType.VarChar, 80).Value = (object?)value?.Trim() ?? DBNull.Value;
        return (cmd.ExecuteScalar() as string)?.Trim();
    }

    // Assigned line locations drive demand even at zero stock; container totals are not counted twice.
    private const string LineStockSql = """
        WITH Assignments AS ({Assignments}), Demand AS
        (
            SELECT L.LocationID AS LocationNo,M.LineID,A.PartNo,COALESCE(S.Qty,0) AS Qty,COALESCE(A.MinimumQty,0) AS MinimumQty,
                COALESCE(NULLIF(M.LineNameEn,''),M.LineID) AS LineName,
                CONVERT(bit,CASE WHEN COALESCE(L.ActiveFlag,1)=1 AND COALESCE(M.Status,'ACTIVE')='ACTIVE' THEN 1 ELSE 0 END) AS IsActive
            FROM dbo.MD_Location L JOIN dbo.MD_Line M ON M.LineID=L.LineID
            LEFT JOIN Assignments A ON A.LocationID=L.LocationID
            OUTER APPLY(SELECT SUM(W.Qty) Qty FROM dbo.WH_Inventory W
                WHERE (W.LocationNo=L.LocationID OR (W.LocationNo=M.LotPrefix AND NOT EXISTS(
                    SELECT 1 FROM dbo.MD_Location Other JOIN Assignments OA ON OA.LocationID=Other.LocationID
                    WHERE Other.LineID=L.LineID AND Other.LocationLevel=2 AND COALESCE(Other.ActiveFlag,1)=1
                      AND OA.PartNo=A.PartNo AND Other.LocationID<L.LocationID))) AND W.PartNo=A.PartNo
                  AND NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn CR WHERE CR.LotNo=W.LotNo AND CR.Status IN('Open','Inspecting'))
                  AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)) S
            WHERE L.LocationLevel=2
              AND (@Overview=1 OR (COALESCE(L.ActiveFlag,1)=1 AND COALESCE(M.Status,'ACTIVE')='ACTIVE'
                AND NULLIF(A.PartNo,'') IS NOT NULL AND A.MinimumQty>0))
        )
        SELECT S.LocationNo,S.PartNo,I.ItemName,S.Qty,S.MinimumQty,
               CONVERT(bit,CASE WHEN EXISTS
               (SELECT 1 FROM dbo.WH_PickSlip O
                WHERE O.ReqLocation COLLATE DATABASE_DEFAULT=S.LocationNo COLLATE DATABASE_DEFAULT
                  AND O.ItemNo COLLATE DATABASE_DEFAULT=S.PartNo COLLATE DATABASE_DEFAULT
                  AND O.CloseDate IS NULL
                  AND UPPER(COALESCE(O.Status,'OPEN')) NOT IN ('CLOSED','CANCELED','CANCELLED','RELEASED'))
               THEN 1 ELSE 0 END),
               CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.MD_Location L JOIN dbo.WH_Inventory W ON W.LocationNo=L.LocationID
                    WHERE L.LocationID=S.LocationNo AND L.LineID IS NOT NULL AND W.Qty>0
                      AND NOT EXISTS(SELECT 1 FROM Assignments Assigned WHERE Assigned.LocationID=L.LocationID AND Assigned.PartNo=W.PartNo)) THEN 1 ELSE 0 END),
               S.LineID,COALESCE(NULLIF(I.DefaultUOM,''),'') AS Unit,S.LineName,
               CONVERT(bit,CASE WHEN S.IsActive=1 AND COALESCE(I.ActiveFlag,1)=1 THEN 1 ELSE 0 END) AS IsActive
        FROM Demand S
        LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=S.PartNo COLLATE DATABASE_DEFAULT
        WHERE @Overview=1 OR (I.ItemNo IS NOT NULL AND COALESCE(I.ActiveFlag,1)=1)
        ORDER BY S.LocationNo,S.PartNo;
        """;

    public List<LineStockRow> ListLineStock()
    {
        using var conn = _factory.OpenConnection();
        return ReadLineStock(conn, null, overview:true)
            .OrderByDescending(row => row.DisplayRequiredQty)
            .ThenBy(row => row.LineId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.LineCode, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static List<LineStockRow> ReadLineStock(SqlConnection conn, SqlTransaction? tx, bool overview=false)
    {
        using var cmd = new SqlCommand(LineStockSql.Replace("{Assignments}",LocationPartAssignmentsSql(conn,tx)), conn, tx);
        cmd.Parameters.Add("@Overview", SqlDbType.Bit).Value = overview;
        using var reader = cmd.ExecuteReader();
        var rows = new List<LineStockRow>();
        while (reader.Read())
            rows.Add(new(reader.GetString(0), GetString(reader,"PartNo") ?? "", GetString(reader,"ItemName") ?? GetString(reader,"PartNo") ?? "",
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetBoolean(5),reader.GetBoolean(6),reader.GetString(7),
                GetString(reader,"Unit") ?? "", GetString(reader,"LineName"), GetBool(reader,"IsActive")));
        return rows;
    }

    public sealed record ReplenishmentResult(int Orders, int Lines, int InvalidRequests);

    public ReplenishmentResult GenerateLinePickingOrders()
    {
        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
        var result = GenerateWorkOrderPicking(conn, tx);
        tx.Commit();
        return result;
    }

}
