using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public sealed record PickingSlipFifoLotRow(string PickSlipNo, string PartNo, string LotNo, string? LocationNo, decimal Qty);

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
                FROM ReleaseLines R
                INNER JOIN dbo.WH_Inventory W ON W.PartNo COLLATE DATABASE_DEFAULT = R.ItemNo COLLATE DATABASE_DEFAULT
                WHERE W.Qty > 0
                  AND (R.CreatedBy <> 'WH-AUTO' OR ({WarehouseRepository.WarehouseBoxPredicate}))
            )
            SELECT
                PickSlipNo AS PICK_SLIPNO,
                ItemNo AS PARTNO,
                LotNo AS LOTNO,
                LocationNo AS LOCATION_NO,
                Qty AS QTY,
                CAST(NULL AS nvarchar(20)) AS PROD_DATE
            FROM RankedLots
            WHERE (CreatedBy='WH-AUTO' AND QtyBefore < RemainingQty)
               OR (COALESCE(CreatedBy,'')<>'WH-AUTO' AND FifoSeq <= RemainingQty)
            ORDER BY PickSlipID, FifoSeq;
            """, conn);
        cmd.Parameters.AddWithValue("@PickSlipNo", pickSlipNo);

        using var rdr = cmd.ExecuteReader();
        var rows = new List<PickingSlipFifoLotRow>();
        while (rdr.Read())
        {
            rows.Add(new PickingSlipFifoLotRow(
                GetString(rdr, "PICK_SLIPNO") ?? pickSlipNo,
                GetString(rdr, "PARTNO") ?? "",
                GetString(rdr, "LOTNO") ?? "",
                GetString(rdr, "LOCATION_NO"),
                GetDecimal(rdr, "QTY")));
        }
        return rows;
    }

    public const string ReplenishmentActor = "WH-AUTO";
    // W is the inventory alias; shared by FIFO suggestions, scan validation and completion.
    public const string WarehouseBoxPredicate = """
        W.UnitType='BOX' AND W.Qty>0
        AND NOT EXISTS (SELECT 1 FROM dbo.MD_Line ML
                        WHERE NULLIF(ML.LotPrefix,'') COLLATE DATABASE_DEFAULT=W.LocationNo COLLATE DATABASE_DEFAULT)
        AND NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)
        AND NOT EXISTS (SELECT 1 FROM dbo.MD_Location L
                        WHERE L.LocationID COLLATE DATABASE_DEFAULT=W.LocationNo COLLATE DATABASE_DEFAULT
                          AND L.AreaCode IN ('FG_AREA','MNT_AREA','SP_AREA'))
        """;

    public sealed record LineStockRow(string LineCode, string PartNo, string PartName,
        decimal Qty, decimal SafetyStock, bool HasOpenOrder)
    {
        public decimal Shortage => Math.Max(0, SafetyStock - Qty);
        public decimal RequiredQty => decimal.Ceiling(Shortage);
        public string State => Shortage == 0 ? "Sufficient" : HasOpenOrder ? "Requested"
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
        using var cmd = new SqlCommand(ActiveLineLocationsSql + " AND LotPrefix=@Location;", conn);
        cmd.Parameters.Add("@Location", SqlDbType.VarChar, 80).Value = (object?)value?.Trim() ?? DBNull.Value;
        return (cmd.ExecuteScalar() as string)?.Trim();
    }

    // Zero-quantity rows retain the line/part association. Container totals are not counted twice.
    private const string LineStockSql = """
        WITH Stock AS
        (
            SELECT W.LocationNo, W.PartNo, SUM(W.Qty) AS Qty
            FROM dbo.WH_Inventory W
            WHERE EXISTS (SELECT 1 FROM dbo.MD_Line ML
                          WHERE LEN(ML.LotPrefix)=2
                            AND ML.LotPrefix COLLATE DATABASE_DEFAULT=W.LocationNo COLLATE DATABASE_DEFAULT
                            AND UPPER(COALESCE(ML.Status,'ACTIVE')) NOT IN ('INACTIVE','CLOSED'))
              AND NULLIF(W.PartNo,'') IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)
            GROUP BY W.LocationNo,W.PartNo
        )
        SELECT S.LocationNo,S.PartNo,I.ItemName,S.Qty,COALESCE(I.SafetyStock,0),
               CONVERT(bit,CASE WHEN EXISTS
               (SELECT 1 FROM dbo.WH_PickSlip O
                WHERE O.ReqLocation COLLATE DATABASE_DEFAULT=S.LocationNo COLLATE DATABASE_DEFAULT
                  AND O.ItemNo COLLATE DATABASE_DEFAULT=S.PartNo COLLATE DATABASE_DEFAULT
                  AND O.CloseDate IS NULL
                  AND UPPER(COALESCE(O.Status,'OPEN')) NOT IN ('CLOSED','CANCELED','CANCELLED','RELEASED'))
               THEN 1 ELSE 0 END)
        FROM Stock S
        JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT=S.PartNo COLLATE DATABASE_DEFAULT
        WHERE COALESCE(I.ActiveFlag,1)=1
        ORDER BY S.LocationNo,S.PartNo;
        """;

    public List<LineStockRow> ListLineStock()
    {
        using var conn = _factory.OpenConnection();
        return ReadLineStock(conn, null);
    }

    private static List<LineStockRow> ReadLineStock(SqlConnection conn, SqlTransaction? tx)
    {
        using var cmd = new SqlCommand(LineStockSql, conn, tx);
        using var reader = cmd.ExecuteReader();
        var rows = new List<LineStockRow>();
        while (reader.Read())
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? reader.GetString(1) : reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetBoolean(5)));
        return rows;
    }

    public sealed record ReplenishmentResult(int Orders, int Lines, int InvalidRequests);

    public ReplenishmentResult GenerateLinePickingOrders()
    {
        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        // SQL transaction lock also serializes workers running on different API hosts.
        using (var gate = new SqlCommand("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource='WH_LINE_REPLENISHMENT',
                 @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0;
            SELECT @Result;
            """, conn, tx))
        {
            if (Convert.ToInt32(gate.ExecuteScalar()) < 0) return new(0, 0, 0);
        }
        var stock = ReadLineStock(conn, tx);
        var orders = 0;
        var count = 0;
        foreach (var line in stock.Where(x => x.State == "Pending").GroupBy(x => x.LineCode))
        {
            var slip = GeneratePickSlipNo(conn, tx, line.Key);
            var seq = 0;
            foreach (var item in line)
            {
                using var cmd = new SqlCommand("""
                    INSERT dbo.WH_PickSlip
                        (PickSlipNo,ReqLocation,ReqSeqNo,ReqUserId,ItemNo,DemandQty,PickedQty,
                         RequiredAt,Priority,Status,CreatedBy,CreatedTS)
                    VALUES (@Slip,@Line,@Seq,@Actor,@Part,@Qty,0,SYSDATETIME(),1,'Open',@Actor,SYSDATETIME());
                    """, conn, tx);
                cmd.Parameters.Add("@Slip", SqlDbType.NVarChar, 40).Value = slip;
                cmd.Parameters.Add("@Line", SqlDbType.NVarChar, 40).Value = line.Key;
                cmd.Parameters.Add("@Seq", SqlDbType.Int).Value = ++seq;
                cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = ReplenishmentActor;
                cmd.Parameters.Add("@Part", SqlDbType.VarChar, 20).Value = item.PartNo;
                AddQtyDecimal(cmd, "@Qty", item.RequiredQty);
                cmd.ExecuteNonQuery();
                count++;
            }
            orders++;
        }
        tx.Commit();
        return new(orders, count, stock.Count(x => x.Shortage > 0 && x.State != "Pending" && !x.HasOpenOrder));
    }

}
