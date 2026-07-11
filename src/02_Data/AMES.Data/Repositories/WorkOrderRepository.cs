using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// CRUD over PP_WorkOrder + PR_WoAcceptance.
/// Powers INJ-03 (Confirm), INJ-04 (progress + qty bump), INJ-02 (active WO tile).
/// </summary>
public sealed class WorkOrderRepository
{
    private readonly AmesConnectionFactory _factory;
    public WorkOrderRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>
    /// All WOs across all lines for the PP-004 management grid.
    /// Returns Draft + Planned + Released + In Progress always; Closed/Cancelled only within the last <paramref name="recentClosedDays"/> days.
    /// </summary>
    public List<WorkOrderDto> ListAll(int recentClosedDays = 30)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName, i.ItemNameEN,
                   w.OrderQty, w.OpenQty, w.CompletedQty, w.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, w.Status, w.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority,
                   CASE WHEN w.LineID LIKE 'LINE-IMG%' THEN 'A'
                        WHEN w.LineID LIKE 'LINE-PNT%' THEN 'B'
                        ELSE NULL END AS RoutingType,
                   CAST(
                       CASE WHEN EXISTS(SELECT 1 FROM dbo.MD_BOM bm WHERE bm.ParentItemNo = w.ItemNo)
                             AND EXISTS(SELECT 1 FROM dbo.MD_BOP bp WHERE bp.ItemNo        = w.ItemNo)
                            THEN 1 ELSE 0 END
                   AS BIT) AS Phase0Complete,
                   NULL AS SapRef,
                   so.SoNumber AS SoNumber
            FROM   dbo.PP_WorkOrder w
            JOIN   dbo.MD_Item      i  ON i.ItemNo = w.ItemNo
            LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
            WHERE  w.Status IN ('Draft','Planned','Released','In Progress')
               OR (w.Status = 'Closed'
                   AND w.ActualEnd >= DATEADD(day, -@Days, CAST(GETDATE() AS date)))
               OR (w.Status = 'Cancelled'
                   AND w.ModifiedTS >= DATEADD(day, -@Days, CAST(GETDATE() AS date)))
            ORDER  BY CASE w.Status
                           WHEN 'In Progress' THEN 0
                           WHEN 'Released'    THEN 1
                           WHEN 'Draft'       THEN 2
                           ELSE 3 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID;
            """;

        return Query(sql, cmd => cmd.Parameters.Add("@Days", SqlDbType.Int).Value = recentClosedDays);
    }

    /// <summary>WOs eligible to be accepted on this line (Status='Released' or 'In Progress').</summary>
    public List<WorkOrderDto> ListForLine(string lineId)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName, i.ItemNameEN,
                   w.OrderQty, w.OpenQty, w.CompletedQty, w.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, w.Status, w.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority
            FROM   dbo.PP_WorkOrder w
            JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  w.LineID = @LineID
              AND  w.Status IN ('Released','In Progress')
            ORDER  BY CASE WHEN w.Status='In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID;
            """;

        return Query(sql, cmd => cmd.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value = lineId);
    }

    /// <summary>Single WO by id (used by INJ-04 after Accept).</summary>
    public WorkOrderDto? GetById(int woId)
    {
        const string sql = """
            SELECT TOP 1 w.WoID, w.WoNumber, w.ItemNo, i.ItemName, i.ItemNameEN,
                   w.OrderQty, w.OpenQty, w.CompletedQty, w.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, w.Status, w.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority
            FROM   dbo.PP_WorkOrder w
            JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  w.WoID = @WoID;
            """;

        return Query(sql, cmd => cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId)
               .FirstOrDefault();
    }

    /// <summary>The WO this terminal is actively running, if any.</summary>
    public WorkOrderDto? GetActiveForTerminal(string lineId, string terminalId)
    {
        const string sql = """
            SELECT TOP 1 w.WoID, w.WoNumber, w.ItemNo, i.ItemName, i.ItemNameEN,
                   w.OrderQty, w.OpenQty, w.CompletedQty, w.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, w.Status, w.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority
            FROM   dbo.PP_WorkOrder w
            JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  w.LineID       = @LineID
              AND  w.Status       = 'In Progress'
              AND (w.TerminalLock = @TerminalID OR w.TerminalLock IS NULL)
            ORDER  BY w.ActualStart DESC;
            """;

        return Query(sql, cmd =>
        {
            cmd.Parameters.Add("@LineID",     SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@TerminalID", SqlDbType.VarChar, 20).Value = terminalId;
        }).FirstOrDefault();
    }

    /// <summary>
    /// Accepts a WO onto this terminal + persists checklist results.
    /// Returns the new AcceptID.
    /// </summary>
    public int AcceptWo(int woId, string terminalId, string operatorId,
                        string employeeNo, string checkResultsJson)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using (var ins = new SqlCommand("""
                INSERT INTO dbo.PR_WoAcceptance
                    (WoID, TerminalID, OperatorID, AcceptedAt, CheckResults, CheckPassed, CreatedBy, CreatedTS)
                OUTPUT INSERTED.AcceptID
                VALUES (@WoID, @TerminalID, @OperatorID, SYSDATETIME(), @Checks, 1, @CreatedBy, SYSDATETIME());
                """, conn, tx))
            {
                ins.Parameters.Add("@WoID",       SqlDbType.Int           ).Value = woId;
                ins.Parameters.Add("@TerminalID", SqlDbType.VarChar, 20   ).Value = terminalId;
                ins.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450 ).Value = operatorId;
                ins.Parameters.Add("@Checks",     SqlDbType.NVarChar      ).Value = checkResultsJson;
                ins.Parameters.Add("@CreatedBy",  SqlDbType.VarChar, 50   ).Value = employeeNo;

                var acceptId = (int)ins.ExecuteScalar()!;

                using (var upd = new SqlCommand("""
                    UPDATE dbo.PP_WorkOrder
                    SET    Status       = 'In Progress',
                           TerminalLock = @TerminalID,
                           ActualStart  = ISNULL(ActualStart, SYSDATETIME()),
                           ModifiedBy   = @ModBy,
                           ModifiedTS   = SYSDATETIME()
                    WHERE  WoID = @WoID;
                    """, conn, tx))
                {
                    upd.Parameters.Add("@WoID",       SqlDbType.Int        ).Value = woId;
                    upd.Parameters.Add("@TerminalID", SqlDbType.VarChar, 20).Value = terminalId;
                    upd.Parameters.Add("@ModBy",      SqlDbType.NVarChar, 450).Value = operatorId;
                    upd.ExecuteNonQuery();
                }

                tx.Commit();
                return acceptId;
            }
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// Bumps CompletedQty + closes the WO when target met. Returns new CompletedQty.
    /// </summary>
    public decimal AddCompletedQty(int woId, int qty, string? userId = null)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
            SET    CompletedQty = ISNULL(CompletedQty,0) + @Qty,
                   Status       = CASE WHEN ISNULL(CompletedQty,0) + @Qty >= ISNULL(OrderQty,0)
                                       THEN 'Closed' ELSE Status END,
                   ActualEnd    = CASE WHEN ISNULL(CompletedQty,0) + @Qty >= ISNULL(OrderQty,0)
                                       THEN SYSDATETIME() ELSE ActualEnd END,
                   ModifiedBy   = @ModBy,
                   ModifiedTS   = SYSDATETIME()
            OUTPUT INSERTED.CompletedQty
            WHERE  WoID = @WoID;
            """;

        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID",  SqlDbType.Int           ).Value = woId;
        cmd.Parameters.Add("@Qty",   SqlDbType.Int           ).Value = qty;
        cmd.Parameters.Add("@ModBy", SqlDbType.NVarChar, 450 ).Value = (object?)userId ?? DBNull.Value;
        return (decimal)(cmd.ExecuteScalar() ?? 0m);
    }

    /// <summary>
    /// Updates DueDate for a WO (PP-CAL drag reschedule). Ignores Closed WOs.
    /// </summary>
    public void UpdateDueDate(int woId, DateTime newDate)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
               SET DueDate = @DueDate
             WHERE WoID   = @WoID
               AND Status NOT IN ('Closed');
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID",   SqlDbType.Int).Value          = woId;
        cmd.Parameters.Add("@DueDate", SqlDbType.Date).Value        = newDate.Date;
        cmd.ExecuteNonQuery();
    }

    // ── PP-004 lifecycle actions ─────────────────────────────────────────────

    /// <summary>WO를 Released로 전환하고 생산라인을 지정 (Draft/Planned만). 변경 행수 반환.</summary>
    public int ReleaseWo(int woId, string lineId, string actor)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
               SET Status     = 'Released',
                   LineID     = @LineID,
                   ReleasedAt = SYSDATETIME(),
                   ReleasedBy = @Actor,
                   ModifiedTS = SYSDATETIME(),
                   ModifiedBy = @Actor
             WHERE WoID   = @WoID
               AND Status IN ('Draft','Planned');
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID",   SqlDbType.Int).Value           = woId;
        cmd.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value   = lineId;
        cmd.Parameters.Add("@Actor",  SqlDbType.NVarChar, 450).Value = actor;
        return cmd.ExecuteNonQuery();
    }

    /// <summary>WO를 Cancelled로 전환 (Draft/Planned/Released만). 변경 행수 반환.</summary>
    public int CancelWo(int woId, string actor)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
               SET Status     = 'Cancelled',
                   ModifiedTS = SYSDATETIME(),
                   ModifiedBy = @Actor
             WHERE WoID   = @WoID
               AND Status IN ('Draft','Planned','Released');
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID",  SqlDbType.Int).Value          = woId;
        cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 수동 Draft WO 생성 (SO 미연계). WoNumber = WO-yyyyMMdd-NNN.
    /// 품목마스터에 존재하는 품번만 생성. 생성된 WoNumber 반환(실패 시 빈 문자열).
    /// </summary>
    public string CreateManualWo(string itemNo, decimal qty, DateTime? due, string actor)
    {
        var prefix = $"WO-{DateTime.Today:yyyyMMdd}-";
        const string insSql = """
            INSERT INTO dbo.PP_WorkOrder
                   (WoNumber, ItemNo, OrderQty, OpenQty, DueDate, Status, CreatedBy, CreatedTS)
            SELECT @Wo, i.ItemNo, @Qty, @Qty, @Due, 'Draft', @Actor, SYSDATETIME()
            FROM   dbo.MD_Item i
            WHERE  i.ItemNo = @ItemNo;
            """;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var seq = PpRepository.NextWoSeq(conn, tx, prefix);

            var wo = $"{prefix}{(seq + 1):D3}";
            using var ins = new SqlCommand(insSql, conn, tx);
            ins.Parameters.Add("@Wo",     SqlDbType.VarChar, 20).Value = wo;
            ins.Parameters.Add("@ItemNo", SqlDbType.VarChar, 20).Value = itemNo;
            ins.Parameters.Add("@Qty",    SqlDbType.Decimal).Precision = 14;
            ins.Parameters["@Qty"].Scale = 3;
            ins.Parameters["@Qty"].Value  = qty;
            ins.Parameters.Add("@Due",    SqlDbType.Date).Value        = (object?)due?.Date ?? DBNull.Value;
            ins.Parameters.Add("@Actor",  SqlDbType.NVarChar, 450).Value = actor;

            var affected = ins.ExecuteNonQuery();
            tx.Commit();
            return affected == 1 ? wo : string.Empty;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private static bool HasColumn(SqlDataReader rdr, string name)
    {
        for (int i = 0; i < rdr.FieldCount; i++)
            if (rdr.GetName(i) == name) return true;
        return false;
    }

    private List<WorkOrderDto> Query(string sql, Action<SqlCommand> bind)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        bind(cmd);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<WorkOrderDto>();
        while (rdr.Read())
        {
            list.Add(new WorkOrderDto
            {
                WoId          = (int)rdr["WoID"],
                WoNumber      = rdr["WoNumber"] as string ?? string.Empty,
                ItemNo        = (string)rdr["ItemNo"],
                ItemName      = (string)rdr["ItemName"],
                ItemNameEn    = rdr["ItemNameEN"] as string,
                OrderQty      = rdr["OrderQty"]     as decimal? ?? 0,
                OpenQty       = rdr["OpenQty"]      as decimal? ?? 0,
                CompletedQty  = rdr["CompletedQty"] as decimal? ?? 0,
                LineId        = rdr["LineID"]       as string ?? string.Empty,
                MoldId        = rdr["MoldID"]       as string,
                RecipeId      = rdr["RecipeID"]     as string,
                DueDate       = rdr["DueDate"]      as DateTime?,
                Status        = rdr["Status"]       as string ?? "Unknown",
                TerminalLock  = rdr["TerminalLock"] as string,
                Priority      = Convert.ToInt32(rdr["Priority"]),
                RoutingType   = HasColumn(rdr, "RoutingType")   ? rdr["RoutingType"]   as string  : null,
                Phase0Complete = HasColumn(rdr, "Phase0Complete") && rdr["Phase0Complete"] is true,
                SapRef        = HasColumn(rdr, "SapRef")        ? rdr["SapRef"]        as string  : null,
                SoNumber      = HasColumn(rdr, "SoNumber")      ? rdr["SoNumber"]      as string  : null,
            });
        }
        return list;
    }
}
