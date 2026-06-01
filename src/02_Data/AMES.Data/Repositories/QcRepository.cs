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
        const string sql = """
            SELECT TOP (@N)
                   i.InspectionID, i.InspectionNo, i.InspectionType, i.LotID, i.WoID,
                   i.LineID, i.ItemNo, m.ItemName, i.Mode,
                   ISNULL(i.SampleSize,0) AS SampleSize,
                   ISNULL(i.BatchQty,0)   AS BatchQty,
                   ISNULL(i.CumulativeGood,0) AS CumulativeGood,
                   ISNULL(i.DefectQtyTotal,0) AS DefectQtyTotal,
                   ISNULL(i.Verdict,'IN_PROGRESS') AS Verdict,
                   ISNULL(i.CriticalFlag,0) AS CriticalFlag,
                   i.InspectorID, i.InsStartTS, i.InsEndTS
            FROM   dbo.QC_Inspection i
            LEFT JOIN dbo.MD_Item m ON m.ItemNo = i.ItemNo
            WHERE  (@T = '' OR i.InspectionType = @T)
            ORDER BY i.InsStartTS DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@N", SqlDbType.Int).Value         = topN;
        cmd.Parameters.Add("@T", SqlDbType.VarChar, 15).Value = type ?? string.Empty;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcInspectionDto>();
        while (rdr.Read()) list.Add(MapInspection(rdr));
        return list;
    }

    public List<QcInspectionItemDto> ListInspectionItems(int inspectionId)
    {
        const string sql = """
            SELECT InspectionItemID, InspectionID, ISNULL(ItemSeq,0) AS ItemSeq,
                   ItemName, Standard, Measured, Result
            FROM   dbo.QC_InspectionItem
            WHERE  InspectionID = @I
            ORDER BY ItemSeq;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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

    /// <summary>Finalize an inspection — sets Verdict + InsEndTS.</summary>
    public void FinalizeInspection(int inspectionId, string verdict, string approverId, string employeeNo)
    {
        const string sql = """
            UPDATE dbo.QC_Inspection
            SET    Verdict = @V, InsEndTS = SYSDATETIME(), ApproverID = @A, ModifiedTS = SYSDATETIME()
            WHERE  InspectionID = @I;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@I", SqlDbType.Int).Value           = inspectionId;
        cmd.Parameters.Add("@V", SqlDbType.VarChar, 15).Value   = verdict;
        cmd.Parameters.Add("@A", SqlDbType.NVarChar, 450).Value = approverId;
        cmd.ExecuteNonQuery();
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
        cmd.Parameters.Add("@By", SqlDbType.VarChar,  50).Value  = employeeNo;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Create a new in-progress inspection (used by QC-01/02/03).</summary>
    public int CreateInspection(string type, string itemNo, int sampleSize, string inspectorId, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.QC_Inspection
                (InspectionNo, InspectionType, ItemNo, Mode, SampleSize, BatchQty,
                 CumulativeGood, DefectQtyTotal, Verdict, CriticalFlag,
                 InspectorID, InsStartTS, CreatedBy, CreatedTS)
            OUTPUT INSERTED.InspectionID
            VALUES (CONCAT(LEFT(@T,3),'-',FORMAT(SYSDATETIME(),'yyMMddHHmm')),
                    @T, @I, 'Normal', @S, @S*6, 0, 0,
                    'IN_PROGRESS', 0, @Op, SYSDATETIME(), @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@T",  SqlDbType.VarChar, 15).Value  = type;
        cmd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value  = itemNo;
        cmd.Parameters.Add("@S",  SqlDbType.Int).Value          = sampleSize;
        cmd.Parameters.Add("@Op", SqlDbType.NVarChar, 450).Value= inspectorId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value  = employeeNo;
        return (int)cmd.ExecuteScalar()!;
    }

    // ── QC-04  NCRs ──────────────────────────────────────────────────────
    public List<QcNcrDto> ListNcrs(string statusFilter = "Open,Investigating", int topN = 50)
    {
        var sql = $$"""
            SELECT TOP ({{topN}})
                   n.NcrID, n.NcrNumber, n.SourceType, n.SourceID, n.Severity,
                   n.LotID, n.ItemNo, m.ItemName,
                   ISNULL(n.AffectedQty,0) AS AffectedQty,
                   n.Disposition, n.Status, n.ReportedBy, n.ReportedAt,
                   n.HoldID, n.CapaID
            FROM   dbo.QC_NCR n
            LEFT JOIN dbo.MD_Item m ON m.ItemNo = n.ItemNo
            WHERE  (@F = '' OR n.Status IN (SELECT value FROM STRING_SPLIT(@F, ',')))
            ORDER BY n.ReportedAt DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@F", SqlDbType.VarChar, 80).Value = statusFilter ?? string.Empty;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcNcrDto>();
        while (rdr.Read()) list.Add(MapNcr(rdr));
        return list;
    }

    public int CreateNcr(string severity, string sourceType, string? sourceId, string itemNo,
                         decimal affectedQty, string disposition, string operatorId, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.QC_NCR
                (NcrNumber, SourceType, SourceID, Severity, ItemNo, AffectedQty,
                 Disposition, Status, ReportedBy, ReportedAt, CreatedBy, CreatedTS)
            OUTPUT INSERTED.NcrID
            VALUES (CONCAT('NCR-', FORMAT(SYSDATETIME(),'yyMMddHHmm')),
                    @ST, @SI, @SV, @I, @Q, @D, 'Open', @Op, SYSDATETIME(),
                    @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ST", SqlDbType.VarChar, 20).Value  = sourceType;
        cmd.Parameters.Add("@SI", SqlDbType.VarChar, 24).Value  = (object?)sourceId ?? DBNull.Value;
        cmd.Parameters.Add("@SV", SqlDbType.VarChar, 10).Value  = severity;
        cmd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value  = itemNo;
        cmd.Parameters.Add("@Q",  SqlDbType.Decimal).Value      = affectedQty;
        cmd.Parameters.Add("@D",  SqlDbType.VarChar, 15).Value  = disposition;
        cmd.Parameters.Add("@Op", SqlDbType.NVarChar, 450).Value= operatorId;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value  = employeeNo;
        return (int)cmd.ExecuteScalar()!;
    }

    // ── QC-05  Holds ─────────────────────────────────────────────────────
    public List<QcHoldDto> ListHolds(string statusFilter = "Held", int topN = 30)
    {
        var sql = $$"""
            SELECT TOP ({{topN}})
                   h.HoldID, h.HoldNumber, h.SourceNcrID, n.NcrNumber, h.Severity,
                   h.AffectedType, h.LotID, h.ItemNo, m.ItemName,
                   ISNULL(h.HeldQty,0) AS HeldQty,
                   h.PhysicalLocation, h.Status, h.HeldAt
            FROM   dbo.QC_Hold h
            LEFT JOIN dbo.QC_NCR  n ON n.NcrID = h.SourceNcrID
            LEFT JOIN dbo.MD_Item m ON m.ItemNo = h.ItemNo
            WHERE  (@F = '' OR h.Status IN (SELECT value FROM STRING_SPLIT(@F, ',')))
            ORDER BY h.HeldAt DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@F", SqlDbType.VarChar, 80).Value = statusFilter ?? string.Empty;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcHoldDto>();
        while (rdr.Read()) list.Add(MapHold(rdr));
        return list;
    }

    public void ReleaseHold(int holdId, string action, string reason, string releasedBy, string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var upd = new SqlCommand("""
                UPDATE dbo.QC_Hold
                SET    Status = CASE WHEN @A IN ('USE-AS-IS','REWORK') THEN 'Released' ELSE 'Rejected' END,
                       ModifiedTS = SYSDATETIME()
                WHERE  HoldID = @H;
                """, conn, tx))
            {
                upd.Parameters.Add("@H", SqlDbType.Int).Value          = holdId;
                upd.Parameters.Add("@A", SqlDbType.VarChar, 15).Value  = action;
                upd.ExecuteNonQuery();
            }

            using (var ins = new SqlCommand("""
                INSERT INTO dbo.QC_HoldRelease
                    (HoldID, EventType, ReleaseAction, ReleaseReason,
                     ReleasedBy, ReleasedAt, Note, CreatedBy, CreatedTS)
                VALUES (@H, 'Release', @A, @R, @By, SYSDATETIME(), @R, @Emp, SYSDATETIME());
                """, conn, tx))
            {
                ins.Parameters.Add("@H",  SqlDbType.Int).Value           = holdId;
                ins.Parameters.Add("@A",  SqlDbType.VarChar, 15).Value   = action;
                ins.Parameters.Add("@R",  SqlDbType.NVarChar, 500).Value = reason;
                ins.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = releasedBy;
                ins.Parameters.Add("@Emp",SqlDbType.VarChar, 50).Value   = employeeNo;
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // ── QC-06  CAPAs ─────────────────────────────────────────────────────
    public List<QcCapaDto> ListCapas(string statusFilter = "Open,In Progress,Plan,Action,Verify", int topN = 30)
    {
        var sql = $$"""
            SELECT TOP ({{topN}})
                   c.CapaID, c.CapaNumber, c.Type, c.TriggerType, c.Phase, c.Status,
                   c.RootCause, c.Cause4M, c.OwnerID, c.OpenedAt, c.DueDate, c.ClosedAt,
                   (SELECT COUNT(*) FROM dbo.QC_CAPA_Action a WHERE a.CapaID = c.CapaID) AS ActionsTotal,
                   (SELECT COUNT(*) FROM dbo.QC_CAPA_Action a WHERE a.CapaID = c.CapaID AND a.CompletedAt IS NOT NULL) AS ActionsCompleted
            FROM   dbo.QC_CAPA c
            WHERE  (@F = '' OR c.Status IN (SELECT value FROM STRING_SPLIT(@F, ',')))
            ORDER BY c.OpenedAt DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@F", SqlDbType.VarChar, 120).Value = statusFilter ?? string.Empty;
        using var rdr = cmd.ExecuteReader();
        var list = new List<QcCapaDto>();
        while (rdr.Read()) list.Add(MapCapa(rdr));
        return list;
    }

    public List<QcCapaActionDto> ListCapaActions(int capaId)
    {
        const string sql = """
            SELECT CapaActionID, CapaID, ActionType, ISNULL(CheckDay,0) AS CheckDay,
                   Description, Metric, TargetValue, ActualValue, Verdict,
                   DueDate, CompletedAt
            FROM   dbo.QC_CAPA_Action
            WHERE  CapaID = @C
            ORDER BY CheckDay, CapaActionID;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
        const string sql = """
            ;WITH prod AS (
              SELECT r.LineID,
                     CASE WHEN r.LineID LIKE 'LINE-INJ-%' THEN 'INJ'
                          WHEN r.LineID LIKE 'LINE-IMG-%' THEN 'IMG'
                          ELSE 'OTHER' END AS Module,
                     ISNULL(SUM(r.GoodQty),0) AS GoodQty,
                     ISNULL((SELECT SUM(d.Qty) FROM dbo.PR_DefectDetail d
                              JOIN dbo.PR_ProductionResult r2 ON r2.ResultID = d.ResultID
                              WHERE r2.LineID = r.LineID
                                AND CAST(d.DetectedAt AS DATE)=CAST(GETDATE() AS DATE)), 0) AS DefectQty
              FROM   dbo.PR_ProductionResult r
              WHERE  CAST(r.EntryAt AS DATE) = CAST(GETDATE() AS DATE)
              GROUP BY r.LineID
            )
            SELECT Module,
                   SUM(GoodQty)   AS GoodQty,
                   SUM(DefectQty) AS DefectQty
            FROM   prod
            WHERE  Module IN ('INJ','IMG')
            GROUP BY Module
            UNION ALL
            SELECT 'PNT' AS Module,
                   ISNULL(SUM(v.ConfirmedQty),0) AS GoodQty,
                   ISNULL(SUM(v.DefectQty),0)    AS DefectQty
            FROM   dbo.PNT_VirtualLot v
            JOIN   dbo.PNT_DailyPlan  p ON p.PlanID = v.PlanID
            WHERE  p.PlanDate = CAST(GETDATE() AS DATE);
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
        const string sql = """
            SELECT
              (SELECT COUNT(*) FROM dbo.QC_NCR  WHERE ISNULL(Status,'Open') IN ('Open','Investigating'))   AS OpenNcr,
              (SELECT COUNT(*) FROM dbo.QC_Hold WHERE ISNULL(Status,'Held') = 'Held')                      AS ActiveHold,
              (SELECT COUNT(*) FROM dbo.QC_CAPA WHERE ClosedAt IS NULL)                                    AS OpenCapa;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        rdr.Read();
        return ((int)rdr["OpenNcr"], (int)rdr["ActiveHold"], (int)rdr["OpenCapa"]);
    }

    public List<QcDefectParetoDto> GetDefectPareto(int topN = 6)
    {
        var sql = $$"""
            SELECT TOP ({{topN}})
                   d.DefectCode, m.DefectName, SUM(ISNULL(d.Qty,1)) AS Count
            FROM   dbo.PR_DefectDetail d
            LEFT JOIN dbo.MD_DefectCode m ON m.DefectCode = d.DefectCode
            WHERE  CAST(d.DetectedAt AS DATE) = CAST(GETDATE() AS DATE)
            GROUP BY d.DefectCode, m.DefectName
            ORDER BY SUM(ISNULL(d.Qty,1)) DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
        const string sql = """
            SELECT s.StdID, s.StdCode, s.VerNo, s.StdName, s.InsType,
                   s.ItemNo, m.ItemName, ISNULL(s.AQLLevel,0) AS AQLLevel,
                   ISNULL(s.SampleInterval,0) AS SampleInterval, s.InspItemsJSON,
                   s.Status, s.EffectiveDate
            FROM   dbo.QC_InspectionStd s
            LEFT JOIN dbo.MD_Item m ON m.ItemNo = s.ItemNo
            WHERE  ISNULL(s.Status,'Active') = 'Active'
            ORDER BY s.StdCode, s.VerNo DESC;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
        const string sql = """
            -- ROOT
            SELECT 'LOT' AS Kind, CAST(l.LotID AS VARCHAR(24)) AS RefID,
                   l.LotCode AS Title,
                   CONCAT(l.ItemNo, ' · ', ISNULL(l.Status,'')) AS Subtitle,
                   l.ProducedAt AS OccurredAt
            FROM   dbo.tbl_Lot l WHERE l.LotID = @L

            UNION ALL
            -- WO (parent)
            SELECT 'WO', CAST(w.WoID AS VARCHAR(24)), w.WoNumber,
                   CONCAT(w.ItemNo, ' · ', w.Status),
                   w.ReleasedAt
            FROM   dbo.tbl_Lot l
            JOIN   dbo.PP_WorkOrder w ON w.WoID = l.WoID
            WHERE  l.LotID = @L

            UNION ALL
            -- Item master
            SELECT 'ITEM', l.ItemNo, m.ItemName,
                   CONCAT(ISNULL(m.ItemType,'?'),' · ', m.ItemCategory), NULL
            FROM   dbo.tbl_Lot l
            JOIN   dbo.MD_Item  m ON m.ItemNo = l.ItemNo
            WHERE  l.LotID = @L

            UNION ALL
            -- Sibling lots from same WO (downstream within prod)
            SELECT 'LOT', CAST(s.LotID AS VARCHAR(24)), s.LotCode,
                   CONCAT(s.ItemNo, ' · ', ISNULL(s.Status,'')),
                   s.ProducedAt
            FROM   dbo.tbl_Lot l
            JOIN   dbo.tbl_Lot s ON s.WoID = l.WoID AND s.LotID <> l.LotID
            WHERE  l.LotID = @L

            UNION ALL
            -- NCRs touching this lot
            SELECT 'NCR', n.NcrNumber, CONCAT(n.Severity,' · ',n.Status),
                   ISNULL(n.Disposition,'?'), n.ReportedAt
            FROM   dbo.QC_NCR n
            WHERE  n.LotID = @L

            UNION ALL
            -- Holds
            SELECT 'HOLD', h.HoldNumber, CONCAT(h.Severity,' · ',h.Status),
                   ISNULL(h.PhysicalLocation,'?'), h.HeldAt
            FROM   dbo.QC_Hold h WHERE h.LotID = @L
            ;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
        const string sql = "SELECT TOP 1 LotID FROM dbo.tbl_Lot WHERE LotCode = @C;";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
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
}
