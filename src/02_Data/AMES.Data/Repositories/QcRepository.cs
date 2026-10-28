using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Quality (QC) module data access. Covers all 9 QC screens:
///   QC-01/02/03  Inspection list + items + verdict
///   QC-04        NCR register + open list
///   QC-05        Hold / Quarantine + release
///   QC-06        CAPA + action steps
///   QC-07        Dashboard rollups (module defect rate, top NCR/Hold/CAPA, pareto)
///   QC-08        Inspection standard browser
///   QC-TRC       Lot traceability (upstream + downstream)
/// </summary>
public sealed class QcRepository
{
    private readonly AmesConnectionFactory _factory;
    public QcRepository(AmesConnectionFactory f) => _factory = f;

    // ── QC-01/02/03  Inspections ─────────────────────────────────────────
    public List<QcInspectionDto> ListInspections(string type, int topN = 30)
    {
        const string sql = "dbo.SP_POP_QC_ListInspections";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@N", SqlDbType.Int).Value         = topN;
        cmd.Parameters.Add("@T", SqlDbType.VarChar, 15).Value = type ?? string.Empty;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcInspectionDto>();
        while (rdr.Read()) list.Add(MapInspection(rdr));
        return list;
    }

    public List<QcInspectionItemDto> ListInspectionItems(int inspectionId)
    {
        const string sql = "dbo.SP_POP_QC_ListInspectionItems";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@I", SqlDbType.Int).Value = inspectionId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcInspectionItemDto>();
        while (rdr.Read())
            list.Add(new QcInspectionItemDto
            {
                InspectionItemId = (int)rdr["InspectionItemID"],
                InspectionId     = (int)rdr["InspectionID"],
                ItemSeq          = (int)rdr["ItemSeq"],
                ItemName         = rdr["ItemName"] as string ?? "",
                Standard         = rdr["Standard"] as string,
                Measured         = rdr["Measured"] as string,
                Result           = rdr["Result"]   as string,
            });
        return list;
    }

    /// <summary>Finalize an in-progress inspection — sets Verdict + InsEndTS. False when it was already finalized.</summary>
    public bool FinalizeInspection(int inspectionId, string verdict, string approverId, string employeeNo, DateTime? at = null)
    {
        const string sql = "dbo.SP_POP_QC_FinalizeInspection";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@I", SqlDbType.Int).Value           = inspectionId;
        cmd.Parameters.Add("@V", SqlDbType.VarChar, 15).Value   = verdict;
        cmd.Parameters.Add("@A", SqlDbType.VarChar,   20).Value = approverId;
        AddAt(cmd, at);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    /// <summary>Append one measurement row to an in-progress inspection.</summary>
    public void AddInspectionItem(int inspectionId, int seq, string itemName, string? std, string measured,
                                   string result, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.QC_InspectionItem
                (InspectionID, ItemSeq, ItemName, Standard, Measured, Result, CreatedBy, CreatedTS)
            VALUES (@I, @S, @N, @St, @M, @R, @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@I",  SqlDbType.Int).Value           = inspectionId;
        cmd.Parameters.Add("@S",  SqlDbType.Int).Value           = seq;
        cmd.Parameters.Add("@N",  SqlDbType.NVarChar, 100).Value = itemName;
        cmd.Parameters.Add("@St", SqlDbType.NVarChar, 100).Value = (object?)std ?? DBNull.Value;
        cmd.Parameters.Add("@M",  SqlDbType.NVarChar, 100).Value = measured;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar,  10).Value  = result;
        cmd.Parameters.Add("@By", SqlDbType.VarChar,  20).Value  = employeeNo;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Create a new in-progress inspection (used by QC-01/02/03).</summary>
    /// <summary>검사 대상 — 스캔한 LOT(품번·WO·라인) 또는 품번. 둘 다 아니면 null.</summary>
    public sealed record InspectionTarget(int? LotId, string ItemNo, int? WoId, string? LineId);

    public InspectionTarget? ResolveInspectionTarget(string code)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand("dbo.SP_POP_QC_ResolveInspectionTarget", conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Code", SqlDbType.VarChar, 40).Value = code.Trim();
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new InspectionTarget(r["LotID"] as int?, (string)r["ItemNo"], r["WoID"] as int?, r["LineID"] as string);
    }

    /// <param name="lineId">LOT 이 없으면 검사한 터미널의 라인을 넘긴다.</param>
    public int CreateInspection(string type, InspectionTarget target, string? lineId, int sampleSize, string inspectorId, string employeeNo,
                                DateTime? at = null)
    {
        const string sql = "dbo.SP_POP_QC_CreateInspection";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@T",  SqlDbType.VarChar, 15).Value  = type;
        cmd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value  = target.ItemNo;
        cmd.Parameters.Add("@S",  SqlDbType.Int).Value          = sampleSize;
        cmd.Parameters.Add("@Op", SqlDbType.NVarChar, 450).Value= inspectorId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value  = employeeNo;
        cmd.Parameters.Add("@Lot",  SqlDbType.Int).Value         = (object?)target.LotId ?? DBNull.Value;
        cmd.Parameters.Add("@Wo",   SqlDbType.Int).Value         = (object?)target.WoId ?? DBNull.Value;
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = (object?)(target.LineId ?? lineId) ?? DBNull.Value;
        AddAt(cmd, at);
        return (int)cmd.ExecuteScalar()!;
    }

    // ── QC-04  NCRs ──────────────────────────────────────────────────────
    public List<QcNcrDto> ListNcrs(string statusFilter = "Open,Investigating", int topN = 50)
    {
        var sql = "dbo.SP_POP_QC_ListNcrs";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@TopN", SqlDbType.Int).Value = topN;
        cmd.Parameters.Add("@F", SqlDbType.VarChar, 80).Value = statusFilter ?? string.Empty;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcNcrDto>();
        while (rdr.Read()) list.Add(MapNcr(rdr));
        return list;
    }

    public int CreateNcr(string severity, string sourceType, string? sourceId, string itemNo,
                         decimal affectedQty, string disposition, string operatorId, string employeeNo, DateTime? at = null)
    {
        const string sql = "dbo.SP_POP_QC_CreateNcr";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@ST", SqlDbType.VarChar, 20).Value  = sourceType;
        cmd.Parameters.Add("@SI", SqlDbType.VarChar, 24).Value  = (object?)sourceId ?? DBNull.Value;
        cmd.Parameters.Add("@SV", SqlDbType.VarChar, 10).Value  = severity;
        cmd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value  = itemNo;
        cmd.Parameters.Add("@Q",  SqlDbType.Decimal).Value      = affectedQty;
        cmd.Parameters.Add("@D",  SqlDbType.VarChar, 15).Value  = disposition;
        cmd.Parameters.Add("@Op", SqlDbType.VarChar,   20).Value= operatorId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 20).Value  = employeeNo;
        AddAt(cmd, at);
        return (int)cmd.ExecuteScalar()!;
    }

    // ── QC-05  Holds ─────────────────────────────────────────────────────
    public List<QcHoldDto> ListHolds(string statusFilter = "Held", int topN = 30)
    {
        var sql = "dbo.SP_POP_QC_ListHolds";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@TopN", SqlDbType.Int).Value = topN;
        cmd.Parameters.Add("@F", SqlDbType.VarChar, 80).Value = statusFilter ?? string.Empty;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcHoldDto>();
        while (rdr.Read()) list.Add(MapHold(rdr));
        return list;
    }

    /// <summary>Release a held hold. False when it is no longer Held (already released/rejected) — nothing is written.</summary>
    public bool ReleaseHold(int holdId, string action, string reason, string releasedBy, string employeeNo, DateTime? at = null)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var upd = new SqlCommand("dbo.SP_POP_QC_ReleaseHold_UpdHold", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                upd.Parameters.Add("@H",  SqlDbType.Int            ).Value = holdId;
                upd.Parameters.Add("@A",  SqlDbType.VarChar, 15    ).Value = action;
                upd.Parameters.Add("@By", SqlDbType.VarChar,     20).Value = releasedBy;
                AddAt(upd, at);
                if (Convert.ToInt32(upd.ExecuteScalar() ?? 0) == 0) { tx.Rollback(); return false; }
            }

            using (var ins = new SqlCommand("dbo.SP_POP_QC_ReleaseHold_InsHoldRelease", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                ins.Parameters.Add("@H",  SqlDbType.Int).Value           = holdId;
                ins.Parameters.Add("@A",  SqlDbType.VarChar, 15).Value   = action;
                ins.Parameters.Add("@R",  SqlDbType.NVarChar, 500).Value = reason;
                ins.Parameters.Add("@By", SqlDbType.VarChar,    20).Value = releasedBy;
                ins.Parameters.Add("@Emp",SqlDbType.VarChar, 20).Value   = employeeNo;
                AddAt(ins, at);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }
        catch { tx.Rollback(); throw; }
    }

    // ── QC-06  CAPAs ─────────────────────────────────────────────────────
    public List<QcCapaDto> ListCapas(string statusFilter = "Open,In Progress,Plan,Action,Verify", int topN = 30)
    {
        var sql = "dbo.SP_POP_QC_ListCapas";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@TopN", SqlDbType.Int).Value = topN;
        cmd.Parameters.Add("@F", SqlDbType.VarChar, 120).Value = statusFilter ?? string.Empty;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcCapaDto>();
        while (rdr.Read()) list.Add(MapCapa(rdr));
        return list;
    }

    public List<QcCapaActionDto> ListCapaActions(int capaId)
    {
        const string sql = "dbo.SP_POP_QC_ListCapaActions";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@C", SqlDbType.Int).Value = capaId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcCapaActionDto>();
        while (rdr.Read())
            list.Add(new QcCapaActionDto
            {
                CapaActionId = (int)rdr["CapaActionID"],
                CapaId       = (int)rdr["CapaID"],
                ActionType   = rdr["ActionType"]  as string,
                CheckDay     = (int)rdr["CheckDay"],
                Description  = rdr["Description"] as string,
                Metric       = rdr["Metric"]      as string,
                TargetValue  = rdr["TargetValue"] as string,
                ActualValue  = rdr["ActualValue"] as string,
                Verdict      = rdr["Verdict"]     as string,
                DueDate      = rdr["DueDate"]     as DateTime?,
                CompletedAt  = rdr["CompletedAt"] as DateTime?,
            });
        return list;
    }

    // ── QC-07  Dashboard ─────────────────────────────────────────────────
    public List<QcModuleDefectDto> GetModuleDefectRates()
    {
        const string sql = "dbo.SP_POP_QC_GetModuleDefectRates";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        var list = new List<QcModuleDefectDto>();
        while (rdr.Read())
            list.Add(new QcModuleDefectDto
            {
                Module    = rdr["Module"] as string ?? "",
                GoodQty   = rdr["GoodQty"]   as int? ?? 0,
                DefectQty = rdr["DefectQty"] as int? ?? 0,
            });
        return list;
    }

    public (int OpenNcr, int ActiveHold, int OpenCapa) GetDashboardCounts()
    {
        const string sql = "dbo.SP_POP_QC_GetDashboardCounts";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        rdr.Read();
        return ((int)rdr["OpenNcr"], (int)rdr["ActiveHold"], (int)rdr["OpenCapa"]);
    }

    public List<QcDefectParetoDto> GetDefectPareto(int topN = 6)
    {
        var sql = "dbo.SP_POP_QC_GetDefectPareto";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@TopN", SqlDbType.Int).Value = topN;
        using var rdr  = cmd.ExecuteReader();
        var list = new List<QcDefectParetoDto>();
        while (rdr.Read())
            list.Add(new QcDefectParetoDto
            {
                DefectCode = rdr["DefectCode"] as string ?? "",
                DefectName = rdr["DefectName"] as string,
                Count      = (int)rdr["Count"],
            });
        return list;
    }

    // ── QC-08  Inspection standards ──────────────────────────────────────
    public List<QcInspectionStdDto> ListInspectionStds()
    {
        const string sql = "dbo.SP_POP_QC_ListInspectionStds";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        using var rdr  = cmd.ExecuteReader();
        var list = new List<QcInspectionStdDto>();
        while (rdr.Read())
            list.Add(new QcInspectionStdDto
            {
                StdId          = (int)rdr["StdID"],
                StdCode        = rdr["StdCode"] as string ?? "",
                VerNo          = rdr["VerNo"]   as string,
                StdName        = rdr["StdName"] as string,
                InsType        = rdr["InsType"] as string,
                ItemNo         = rdr["ItemNo"]  as string,
                ItemName       = rdr["ItemName"] as string,
                AqlLevel       = rdr["AQLLevel"] as decimal? ?? 0,
                SampleInterval = (int)rdr["SampleInterval"],
                InspItemsJson  = rdr["InspItemsJSON"] as string,
                Status         = rdr["Status"]       as string,
                EffectiveDate  = rdr["EffectiveDate"] as DateTime?,
            });
        return list;
    }

    // ── QC-TRC  Traceability ─────────────────────────────────────────────
    /// <summary>
    /// Returns a flat list of upstream + downstream nodes around the given lot.
    /// Upstream:   the lot's WO, item, mold, materials.
    /// Downstream: lots produced from this WO, FG stock, shipments.
    /// Designed for a "graph view" UI: each node is self-described.
    /// </summary>
    public List<QcTraceNodeDto> TraceLot(int lotId)
    {
        const string sql = "dbo.SP_POP_QC_TraceLot";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@L", SqlDbType.Int).Value = lotId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcTraceNodeDto>();
        while (rdr.Read())
            list.Add(new QcTraceNodeDto
            {
                Kind       = rdr["Kind"]  as string ?? "",
                RefId      = rdr["RefID"] as string ?? "",
                Title      = rdr["Title"] as string,
                Subtitle   = rdr["Subtitle"] as string,
                OccurredAt = rdr["OccurredAt"] as DateTime?,
            });
        return list;
    }

    public int? FindLotIdByCode(string lotCode)
    {
        const string sql = "dbo.SP_POP_QC_FindLotIdByCode";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 40).Value = lotCode;
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? null : (int)v;
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    private static QcInspectionDto MapInspection(IDataReader r) => new()
    {
        InspectionId   = (int)r["InspectionID"],
        InspectionNo   = r["InspectionNo"] as string ?? "",
        InspectionType = r["InspectionType"] as string ?? "",
        LotId          = r["LotID"] as int?,
        WoId           = r["WoID"]  as int?,
        LineId         = r["LineID"] as string,
        ItemNo         = r["ItemNo"] as string,
        ItemName       = r["ItemName"] as string,
        Mode           = r["Mode"]    as string,
        SampleSize     = (int)r["SampleSize"],
        BatchQty       = r["BatchQty"] as decimal? ?? 0,
        CumulativeGood = (int)r["CumulativeGood"],
        DefectQtyTotal = (int)r["DefectQtyTotal"],
        Verdict        = r["Verdict"] as string ?? "IN_PROGRESS",
        CriticalFlag   = Convert.ToBoolean(r["CriticalFlag"]),
        InspectorId    = r["InspectorID"] as string,
        InsStartTs     = (DateTime)r["InsStartTS"],
        InsEndTs       = r["InsEndTS"] as DateTime?,
    };

    private static QcNcrDto MapNcr(IDataReader r) => new()
    {
        NcrId       = (int)r["NcrID"],
        NcrNumber   = r["NcrNumber"] as string ?? "",
        SourceType  = r["SourceType"] as string,
        SourceId    = r["SourceID"]   as string,
        Severity    = r["Severity"]   as string,
        LotId       = r["LotID"]      as int?,
        ItemNo      = r["ItemNo"]     as string,
        ItemName    = r["ItemName"]   as string,
        AffectedQty = r["AffectedQty"] as decimal? ?? 0,
        Disposition = r["Disposition"] as string,
        Status      = r["Status"]      as string,
        ReportedBy  = r["ReportedBy"]  as string,
        ReportedAt  = r["ReportedAt"]  as DateTime?,
        HoldId      = r["HoldID"]      as int?,
        CapaId      = r["CapaID"]      as int?,
    };

    private static QcHoldDto MapHold(IDataReader r) => new()
    {
        HoldId       = (int)r["HoldID"],
        HoldNumber   = r["HoldNumber"] as string ?? "",
        SourceNcrId  = r["SourceNcrID"] as int?,
        NcrNumber    = r["NcrNumber"]   as string,
        Severity     = r["Severity"]    as string,
        AffectedType = r["AffectedType"] as string,
        LotId        = r["LotID"]       as int?,
        ItemNo       = r["ItemNo"]      as string,
        ItemName     = r["ItemName"]    as string,
        HeldQty      = r["HeldQty"]     as decimal? ?? 0,
        PhysicalLocation = r["PhysicalLocation"] as string,
        Status       = r["Status"]      as string,
        HeldAt       = r["HeldAt"]      as DateTime?,
    };

    private static QcCapaDto MapCapa(IDataReader r) => new()
    {
        CapaId      = (int)r["CapaID"],
        CapaNumber  = r["CapaNumber"] as string ?? "",
        Type        = r["Type"]       as string,
        TriggerType = r["TriggerType"] as string,
        Phase       = r["Phase"]      as string,
        Status      = r["Status"]     as string,
        RootCause   = r["RootCause"]  as string,
        Cause4M     = r["Cause4M"]    as string,
        OwnerId     = r["OwnerID"]    as string,
        OpenedAt    = r["OpenedAt"]   as DateTime?,
        DueDate     = r["DueDate"]    as DateTime?,
        ClosedAt    = r["ClosedAt"]   as DateTime?,
        ActionsTotal     = (int)r["ActionsTotal"],
        ActionsCompleted = (int)r["ActionsCompleted"],
    };

    // 오프라인 POP 복구 때만 넘긴다 — 없으면 붙이지 않아 @At 이 없는 구 프로시저에서도 돈다.
    private static void AddAt(SqlCommand cmd, DateTime? at)
    {
        if (at is { } v) cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = v;
    }
}
