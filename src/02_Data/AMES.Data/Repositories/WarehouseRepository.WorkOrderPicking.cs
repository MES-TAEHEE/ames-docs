using System.Data;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public sealed record WorkOrderMaterialIssue(string WorkOrder, string Line, string Part, string Message);
    public sealed record LineMaterialOption(string ItemNo, string Display);

    // Closed pick orders still cover demand while their released LOTs await Set Location.
    public const string LineTransfersInTransitSql = """
        SELECT W.LotNo,W.PartNo,W.Qty,COALESCE(Legacy.LocationID,P.ReqLocation) ReqLocation,T.SourceID PickSlipID
        FROM dbo.WH_Inventory W
        CROSS APPLY(SELECT TOP(1) X.TransactionType,X.SourceType,X.SourceID
            FROM dbo.WH_InventoryTransaction X WHERE X.LotNo=W.LotNo AND X.TransactionType IN('IN','OUT','MOVE')
            ORDER BY X.TransactionID DESC) T
        JOIN dbo.WH_PickSlip P ON P.PickSlipID=T.SourceID
        OUTER APPLY(SELECT TOP(1) L.LocationID FROM dbo.MD_Location L JOIN dbo.MD_Line M ON M.LineID=L.LineID
            WHERE M.LotPrefix=P.ReqLocation AND L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1 ORDER BY L.LocationID) Legacy
        WHERE W.Qty>0 AND NULLIF(W.LocationNo,'') IS NULL
          AND T.TransactionType='OUT' AND T.SourceType='PICK_SLIP'
          AND NULLIF(P.ReqLocation,'') IS NOT NULL AND P.CreatedBy IN('WH-AUTO','WH-REQUEST')
        """;

    // Both API instances, WO release and PDA completion use the same transaction-owned gate.
    public static void LockLinePicking(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET ARITHABORT ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET NUMERIC_ROUNDABORT OFF;
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource='WH_LINE_REPLENISHMENT',
                @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0;
            SELECT @Result;
            """, conn, tx);
        if (Convert.ToInt32(cmd.ExecuteScalar()) < 0)
            throw new InvalidOperationException("Line picking is being updated. Please retry.");
    }

    private static List<MrpCalculator.BomLine> ReadPickingBom(SqlConnection conn, SqlTransaction? tx)
    {
        using var cmd = new SqlCommand(PpRepository.EffectiveBomSql, conn, tx);
        cmd.Parameters.Add("@Today", SqlDbType.Date).Value = ReadPickingDate(conn, tx);
        using var reader = cmd.ExecuteReader();
        var rows = new List<MrpCalculator.BomLine>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        return rows;
    }

    private static DateTime ReadPickingDate(SqlConnection conn, SqlTransaction? tx)
    {
        using var cmd = new SqlCommand("SELECT CAST(SYSDATETIME() AS date);", conn, tx);
        return (DateTime)cmd.ExecuteScalar()!;
    }

    private static LinePickingPlanner.Result ReadWorkOrderPicking(SqlConnection conn, SqlTransaction? tx)
    {
        var steps = new List<LinePickingPlanner.Step>();
        using (var cmd = new SqlCommand("""
            SELECT W.WoID,W.WoNumber,R.StepSeq,R.LineID,COALESCE(R.ItemNo,W.ItemNo),
                CASE WHEN R.Status IN ('Released','In Progress') AND COALESCE(W.OrderQty,0)>COALESCE(R.CompletedQty,0)
                     THEN COALESCE(W.OrderQty,0)-COALESCE(R.CompletedQty,0) ELSE 0 END Qty,W.DueDate
            FROM dbo.PP_WorkOrder W JOIN dbo.PP_WorkOrderRouting R ON R.WoID=W.WoID
            WHERE W.Status IN ('Released','In Progress')
              AND R.LineID IS NOT NULL
            ORDER BY W.WoID,R.StepSeq;
            """, conn, tx))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) steps.Add(new(reader.GetInt32(0), reader[1] as string ?? $"WO-{reader.GetInt32(0)}", Convert.ToInt32(reader[2]),
                reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader[6] as DateTime?));

        var locations = ReadLineStock(conn, tx, overview: true)
            .Where(l => l.IsActive && !string.IsNullOrWhiteSpace(l.PartNo) && l.LineId is not null)
            .Select(l => new LinePickingPlanner.Location(l.LineCode, l.LineId!, l.PartNo, l.MinimumQty,
                l.Qty, string.IsNullOrWhiteSpace(l.Unit) ? "EA" : l.Unit, l.WrongPart)).ToList();
        var pending = new List<LinePickingPlanner.Pending>();
        using (var cmd = new SqlCommand($"""
            SELECT COALESCE(Legacy.LocationID,P.ReqLocation),ItemNo,CASE WHEN COALESCE(DemandQty,0)>COALESCE(PickedQty,0)
                THEN COALESCE(DemandQty,0)-COALESCE(PickedQty,0) ELSE 0 END Qty,
                CONVERT(bit,CASE WHEN CreatedBy IN('WH-AUTO','WH-REQUEST') THEN 1 ELSE 0 END)
            FROM dbo.WH_PickSlip P
            OUTER APPLY(SELECT TOP(1) L.LocationID FROM dbo.MD_Location L JOIN dbo.MD_Line M ON M.LineID=L.LineID
                WHERE M.LotPrefix=P.ReqLocation AND L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1 ORDER BY L.LocationID) Legacy
            WHERE CloseDate IS NULL AND UPPER(COALESCE(Status,'OPEN')) NOT IN ('CLOSED','CANCELLED','CANCELED','RELEASED')
                AND ReqLocation IS NOT NULL AND ItemNo IS NOT NULL
            UNION ALL
            SELECT ReqLocation,PartNo,Qty,CONVERT(bit,1) FROM ({LineTransfersInTransitSql}) T;
            """, conn, tx))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) pending.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetBoolean(3)));
        return LinePickingPlanner.Plan(steps, ReadPickingBom(conn, tx), locations, pending);
    }

    public List<WorkOrderMaterialIssue> ListWorkOrderMaterialIssues()
    {
        using var conn = _factory.OpenConnection();
        return ReadWorkOrderPicking(conn, null).Issues.Select(i => new WorkOrderMaterialIssue(i.WoNumber, i.LineId, i.PartNo, i.Message)).ToList();
    }

    internal static ReplenishmentResult GenerateWorkOrderPicking(SqlConnection conn, SqlTransaction tx)
    {
        LockLinePicking(conn, tx);
        CloseFinishedWorkOrderPicking(conn, tx);
        var plan = ReadWorkOrderPicking(conn, tx);
        var groups = plan.Requests.GroupBy(r => (r.LineId, r.Unit)).ToList();
        foreach (var group in groups)
        {
            var slip = GeneratePickSlipNo(conn, tx, group.Key.LineId);
            var sequence = 0;
            foreach (var request in group)
            {
                if (request.PartNo.Length > 20 || request.LocationId.Length > 20 || request.Qty > 99999999999m)
                    throw new InvalidOperationException($"Picking quantity, part number or destination exceeds the limit: {request.PartNo} / {request.LocationId}.");
                using var cmd = new SqlCommand("""
                    INSERT dbo.WH_PickSlip
                        (WoID,PickSlipNo,ReqLocation,ReqSeqNo,ReqUserId,ItemNo,DemandQty,PickedQty,
                         RequiredAt,Priority,Status,CreatedBy,CreatedTS)
                    VALUES (@Wo,@Slip,@Location,@Seq,@Actor,@Part,@Qty,0,
                        COALESCE(@RequiredAt,SYSDATETIME()),1,'Open',@Actor,SYSDATETIME());
                    """, conn, tx);
                cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = (object?)request.WoId ?? DBNull.Value;
                cmd.Parameters.Add("@Slip", SqlDbType.NVarChar, 40).Value = slip;
                cmd.Parameters.Add("@Location", SqlDbType.VarChar, 20).Value = request.LocationId;
                cmd.Parameters.Add("@Seq", SqlDbType.Int).Value = ++sequence;
                cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = ReplenishmentActor;
                cmd.Parameters.Add("@Part", SqlDbType.VarChar, 20).Value = request.PartNo;
                AddQtyDecimal(cmd, "@Qty", request.Qty);
                cmd.Parameters.Add("@RequiredAt", SqlDbType.DateTime2).Value = (object?)request.RequiredAt ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
        }
        return new(groups.Count, plan.Requests.Count, plan.Issues.Count);
    }

    // Production is already committed. A replenishment failure must never turn a saved result into a retryable production error.
    public static void RecheckLinePicking(AmesConnectionFactory factory)
    {
        try { new WarehouseRepository(factory).GenerateLinePickingOrders(); }
        catch(Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Post-production picking check failed: {0}",ex);
            try { new SysRepository(factory).UpsertInterfaceMonitor("WH-REPLENISH","Line replenishment","",10,
                ok:false,recordCount:null,error:ex.Message,actor:ReplenishmentActor); }
            catch(Exception monitorError) { System.Diagnostics.Trace.TraceError("Picking monitor failed: {0}",monitorError); }
        }
    }

    internal static void CloseFinishedWorkOrderPicking(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            UPDATE P SET Status='Cancelled',CloseDate=SYSDATETIME(),CloseUserId='WH-AUTO',
                ModifiedBy='WH-AUTO',ModifiedTS=SYSDATETIME()
            FROM dbo.WH_PickSlip P JOIN dbo.PP_WorkOrder W ON W.WoID=P.WoID
            WHERE P.CreatedBy='WH-AUTO' AND P.CloseDate IS NULL
              AND W.Status NOT IN ('Released','In Progress');
            """, conn, tx);
        cmd.ExecuteNonQuery();
    }

    public List<LineMaterialOption> ListLineMaterialCandidates(string lineId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd=new SqlCommand($"SELECT PartNo FROM ({LineMaterialPartsSql}) P WHERE P.LineID=@Line ORDER BY PartNo;",conn);
        cmd.Parameters.Add("@Line",SqlDbType.VarChar,20).Value=lineId;
        using var reader=cmd.ExecuteReader();
        var parts=new List<LineMaterialOption>();
        while(reader.Read()) parts.Add(new(reader.GetString(0),reader.GetString(0)));
        return parts;
    }
}
