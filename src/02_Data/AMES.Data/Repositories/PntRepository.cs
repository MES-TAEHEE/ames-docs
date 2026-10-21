using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Painting (PNT) module data access. Covers all 9 PNT screens:
///   PNT-01 ListTodayPlan          (daily plan + progress rollup)
///   PNT-02 ListLots / IssueLot    (virtual lot picker + signature)
///   PNT-03 LogLoad                (R1 RFID loading)
///   PNT-04 ListRecentEvents       (R1/R2/R3 line track board)
///   PNT-05 GetOvenStatus / Curve  (oven monitor + temp samples)
///   PNT-06 LogUnload              (R3 RFID unloading + confirm)
///   PNT-07 ApplyLabel             (label apply / FG handoff)
///   PNT-08 RegisterDefect         (defect entry)
///   PNT-09 GetShiftReport         (per-shift KPIs)
/// </summary>
public sealed class PntRepository
{
    private readonly AmesConnectionFactory _factory;
    public PntRepository(AmesConnectionFactory f) => _factory = f;

    // ── PNT-01 ────────────────────────────────────────────────────────────
    public List<PntDailyPlanDto> ListTodayPlan(string lineId)
    {
        const string sql = "dbo.SP_POP_PNT_ListTodayPlan";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PntDailyPlanDto>();
        while (rdr.Read())
            list.Add(new PntDailyPlanDto
            {
                PlanId       = (int)rdr["PlanID"],
                PlanDate     = (DateTime)rdr["PlanDate"],
                ItemNo       = rdr["ItemNo"] as string ?? "",
                ItemName     = rdr["ItemName"] as string,
                RalColor     = rdr["RalColor"] as string ?? "",
                ColorName    = rdr["ColorName"] as string,
                HexValue     = rdr["HexValue"]  as string,
                TargetQty    = rdr["TargetQty"] as int? ?? 0,
                LineId       = rdr["LineID"] as string,
                OvenId       = rdr["OvenID"] as string,
                StartTime    = rdr["StartTime"] as string,
                JigsRequired = rdr["JigsRequired"] as int? ?? 0,
                LotsRequired = rdr["LotsRequired"] as int? ?? 0,
                ReadyFlag    = (rdr["ReadyFlag"] as bool?) ?? false,
                LotsIssued   = (int)rdr["LotsIssued"],
                LotsBound    = (int)rdr["LotsBound"],
            });
        return list;
    }

    // ── PNT-02 ────────────────────────────────────────────────────────────
    public List<PntVirtualLotDto> ListLotsForPlan(int planId)
    {
        const string sql = """
            SELECT  v.VirtualLotID, v.LotID, v.PlanID, v.JigID,
                    v.ItemNo, i.ItemName,
                    v.RalColor, c.ColorName, c.HexValue,
                    v.TargetQty, v.LoadedQty, v.ConfirmedQty, v.DefectQty,
                    v.Status, ISNULL(v.EnhancedInspection,0) AS EnhancedInspection,
                    v.IssuedAt, v.BindAt, v.BindReason
            FROM    dbo.PNT_VirtualLot v
            LEFT JOIN dbo.MD_Item     i ON i.ItemNo  = v.ItemNo
            LEFT JOIN dbo.MD_RalColor c ON c.RALCode = v.RalColor
            WHERE   v.PlanID = @P
            ORDER BY v.IssuedAt;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@P", SqlDbType.Int).Value = planId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PntVirtualLotDto>();
        while (rdr.Read()) list.Add(MapLot(rdr));
        return list;
    }

    public List<PntVirtualLotDto> ListAllTodayLots(string lineId)
    {
        const string sql = "dbo.SP_POP_PNT_ListAllTodayLots";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PntVirtualLotDto>();
        while (rdr.Read()) list.Add(MapLot(rdr));
        return list;
    }

    /// <summary>Issue (pre-create) a new virtual lot — PNT-02 button.</summary>
    public int IssueLot(int planId, string itemNo, string ralColor, int targetQty, string operatorId, string employeeNo)
    {
        const string sql = "dbo.SP_POP_PNT_IssueLot";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@P",  SqlDbType.Int).Value          = planId;
        cmd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value  = itemNo;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar, 12).Value  = ralColor;
        cmd.Parameters.Add("@T",  SqlDbType.Int).Value          = targetQty;
        cmd.Parameters.Add("@Op", SqlDbType.VarChar,   20).Value= operatorId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value  = employeeNo;
        return (int)cmd.ExecuteScalar()!;
    }

    // ── PNT-03 (Loading / R1) ─────────────────────────────────────────────
    public List<PntJigDto> ListAvailableJigs()
    {
        const string sql = "dbo.SP_POP_PNT_ListAvailableJigs";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        var list = new List<PntJigDto>();
        while (rdr.Read())
            list.Add(new PntJigDto
            {
                JigId        = rdr["JigID"] as string ?? "",
                JigName      = rdr["JigName"] as string,
                HangerCount  = (int)rdr["HangerCount"],
                RatedCycle   = (int)rdr["RatedCycle"],
                CycleCount   = (int)rdr["CycleCount"],
                HealthStatus = rdr["HealthStatus"] as string,
                ReadFailRate = (decimal)rdr["ReadFailRate"],
                Available    = (int)rdr["Available"] == 1,
            });
        return list;
    }

    /// <summary>Bind a virtual lot to a jig and mark it loaded — PNT-03 button.</summary>
    public void BindAndLoad(int virtualLotId, string jigId, int loadedQty, string operatorId, string lineId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var upd = new SqlCommand("dbo.SP_POP_PNT_BindAndLoad_UpdPNT_VirtualLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                upd.Parameters.Add("@V",     SqlDbType.Int            ).Value = virtualLotId;
                upd.Parameters.Add("@J",     SqlDbType.VarChar,  20   ).Value = jigId;
                upd.Parameters.Add("@Q",     SqlDbType.Int            ).Value = loadedQty;
                upd.Parameters.Add("@ModBy", SqlDbType.VarChar,     20).Value = operatorId;
                upd.ExecuteNonQuery();
            }

            using (var ins = new SqlCommand("dbo.SP_POP_PNT_BindAndLoad_InsPNT_JigLoad", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                ins.Parameters.Add("@J",  SqlDbType.VarChar, 20).Value = jigId;
                ins.Parameters.Add("@Q",  SqlDbType.Int).Value         = loadedQty;
                ins.Parameters.Add("@Op", SqlDbType.NVarChar, 450).Value = operatorId;
                ins.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
                ins.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = employeeNo;
                ins.ExecuteNonQuery();
            }

            using (var ev = new SqlCommand("dbo.SP_POP_PNT_BindAndLoad_InsPNT_LineEvent", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                ev.Parameters.Add("@J",  SqlDbType.VarChar, 20).Value = jigId;
                ev.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = employeeNo;
                ev.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // ── PNT-04 (Line Track Board) ─────────────────────────────────────────
    public List<PntLineEventDto> ListRecentEvents(string lineId, int topN = 30)
    {
        const string sql = "dbo.SP_POP_PNT_ListRecentEvents";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@N", SqlDbType.Int).Value         = topN;
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PntLineEventDto>();
        while (rdr.Read())
            list.Add(new PntLineEventDto
            {
                EventId      = (long)rdr["EventID"],
                TagId        = rdr["TagID"] as string,
                JigId        = rdr["JigID"] as string,
                LotId        = rdr["LotID"] as int?,
                ReaderId     = rdr["ReaderID"] as string,
                GateLocation = rdr["GateLocation"] as string,
                EventTs      = (DateTime)rdr["EventTS"],
                Rssi         = rdr["Rssi"] as short?,
                TriggerType  = rdr["TriggerType"] as string,
            });
        return list;
    }

    // ── PNT-05 (Oven) ─────────────────────────────────────────────────────
    public PntOvenStatusDto? GetOvenStatus(string lineId)
    {
        const string sql = "dbo.SP_POP_PNT_GetOvenStatus";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        return new PntOvenStatusDto
        {
            OvenId      = rdr["OvenID"] as string ?? "",
            OvenName    = rdr["OvenName"] as string,
            TargetTemp  = (int)rdr["TargetTemp"],
            Tolerance   = (int)rdr["Tolerance"],
            DwellSec    = (int)rdr["DwellSec"],
            CurrentTemp = rdr["CurrentTemp"] as decimal? ?? 0,
            MinTemp24h  = rdr["MinTemp24h"]  as decimal? ?? 0,
            MaxTemp24h  = rdr["MaxTemp24h"]  as decimal? ?? 0,
            JigsInside  = (int)rdr["JigsInside"],
        };
    }

    public List<PntOvenSampleDto> ListOvenSamples(string ovenId, int minutes = 10)
    {
        const string sql = "dbo.SP_POP_PNT_ListOvenSamples";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@O", SqlDbType.VarChar, 20).Value = ovenId;
        cmd.Parameters.Add("@M", SqlDbType.Int).Value         = minutes;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PntOvenSampleDto>();
        while (rdr.Read())
            list.Add(new PntOvenSampleDto
            {
                OvenId   = rdr["OvenID"] as string ?? "",
                ZoneId   = rdr["ZoneID"] as byte?,
                TempC    = rdr["TempC"] as decimal? ?? 0,
                SampledAt= (DateTime)rdr["SampledAt"],
            });
        return list;
    }

    // ── PNT-06 (Unloading / R3) ───────────────────────────────────────────
    public void ConfirmUnload(int virtualLotId, int goodQty, int defectQty, string operatorId, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var upd = new SqlCommand("dbo.SP_POP_PNT_ConfirmUnload_UpdPNT_VirtualLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                upd.Parameters.Add("@V",     SqlDbType.Int            ).Value = virtualLotId;
                upd.Parameters.Add("@G",     SqlDbType.Int            ).Value = goodQty;
                upd.Parameters.Add("@D",     SqlDbType.Int            ).Value = defectQty;
                upd.Parameters.Add("@ModBy", SqlDbType.VarChar,     20).Value = operatorId;
                upd.ExecuteNonQuery();
            }

            using (var ev = new SqlCommand("dbo.SP_POP_PNT_ConfirmUnload_InsPNT_LineEvent", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                ev.Parameters.Add("@V",  SqlDbType.Int).Value         = virtualLotId;
                ev.Parameters.Add("@By", SqlDbType.VarChar, 20).Value = employeeNo;
                ev.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // ── PNT-07 (Label Apply) ──────────────────────────────────────────────
    public void ApplyLabel(int virtualLotId, string employeeNo, string? userId = null)
    {
        const string sql = "dbo.SP_POP_PNT_ApplyLabel";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@V",     SqlDbType.Int            ).Value = virtualLotId;
        cmd.Parameters.Add("@ModBy", SqlDbType.VarChar,     20).Value = (object?)userId ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    // ── PNT-08 (Defect) ───────────────────────────────────────────────────
    public List<DefectCodeDto> ListDefectCodes()
    {
        const string sql = "dbo.SP_POP_PNT_ListDefectCodes";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        var list = new List<DefectCodeDto>();
        while (rdr.Read())
            list.Add(new DefectCodeDto
            {
                DefectCode    = rdr["DefectCode"] as string ?? "",
                DefectName    = rdr["DefectName"] as string ?? "",
                DefectNameEn  = rdr["DefectNameEn"] as string,
                ProcessCode   = rdr["ProcessCode"] as string,
                SeverityLevel = rdr["SeverityLevel"] as string,
            });
        return list;
    }

    public void RegisterDefect(int virtualLotId, string defectCode, int qty, string operatorId, string employeeNo)
    {
        const string sql = "dbo.SP_POP_PNT_RegisterDefect";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@V",     SqlDbType.Int            ).Value = virtualLotId;
        cmd.Parameters.Add("@Q",     SqlDbType.Int            ).Value = qty;
        cmd.Parameters.Add("@ModBy", SqlDbType.VarChar,     20).Value = operatorId;
        cmd.ExecuteNonQuery();
    }

    // ── PNT-09 (Shift Report) ─────────────────────────────────────────────
    public List<PntShiftBucketDto> GetShiftBuckets(string lineId)
    {
        const string sql = "dbo.SP_POP_PNT_GetShiftBuckets";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PntShiftBucketDto>();
        while (rdr.Read())
            list.Add(new PntShiftBucketDto
            {
                ShiftCode  = rdr["ShiftCode"] as string ?? "",
                LotsClosed = (int)rdr["LotsClosed"],
                GoodQty    = rdr["GoodQty"]   as int? ?? 0,
                DefectQty  = rdr["DefectQty"] as int? ?? 0,
            });
        return list;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    public List<PntRalColorDto> ListRalColors()
    {
        const string sql = "dbo.SP_POP_PNT_ListRalColors";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        var list = new List<PntRalColorDto>();
        while (rdr.Read())
            list.Add(new PntRalColorDto
            {
                RalCode      = rdr["RALCode"] as string ?? "",
                ColorName    = rdr["ColorName"] as string,
                HexValue     = rdr["HexValue"]  as string,
                CureTemp     = (int)rdr["CureTemp"],
                CureDuration = (int)rdr["CureDuration"],
            });
        return list;
    }

    private static PntVirtualLotDto MapLot(IDataReader r) => new()
    {
        VirtualLotId       = (int)r["VirtualLotID"],
        LotId              = r["LotID"] as int?,
        PlanId             = (int)r["PlanID"],
        JigId              = r["JigID"] as string,
        ItemNo             = r["ItemNo"] as string ?? "",
        ItemName           = r["ItemName"] as string,
        RalColor           = r["RalColor"] as string ?? "",
        ColorName          = r["ColorName"] as string,
        HexValue           = r["HexValue"]  as string,
        TargetQty          = r["TargetQty"]    as int? ?? 0,
        LoadedQty          = r["LoadedQty"]    as int? ?? 0,
        ConfirmedQty       = r["ConfirmedQty"] as int? ?? 0,
        DefectQty          = r["DefectQty"]    as int? ?? 0,
        Status             = r["Status"] as string ?? "PRE",
        EnhancedInspection = Convert.ToBoolean(r["EnhancedInspection"]),
        IssuedAt           = (DateTime)r["IssuedAt"],
        BindAt             = r["BindAt"] as DateTime?,
        BindReason         = r["BindReason"] as string,
    };
}
