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
                           ModifiedTS   = SYSDATETIME()
                    WHERE  WoID = @WoID;
                    """, conn, tx))
                {
                    upd.Parameters.Add("@WoID",       SqlDbType.Int        ).Value = woId;
                    upd.Parameters.Add("@TerminalID", SqlDbType.VarChar, 20).Value = terminalId;
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
    public decimal AddCompletedQty(int woId, int qty)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
            SET    CompletedQty = ISNULL(CompletedQty,0) + @Qty,
                   Status       = CASE WHEN ISNULL(CompletedQty,0) + @Qty >= ISNULL(OrderQty,0)
                                       THEN 'Closed' ELSE Status END,
                   ActualEnd    = CASE WHEN ISNULL(CompletedQty,0) + @Qty >= ISNULL(OrderQty,0)
                                       THEN SYSDATETIME() ELSE ActualEnd END,
                   ModifiedTS   = SYSDATETIME()
            OUTPUT INSERTED.CompletedQty
            WHERE  WoID = @WoID;
            """;

        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
        cmd.Parameters.Add("@Qty",  SqlDbType.Int).Value = qty;
        return (decimal)(cmd.ExecuteScalar() ?? 0m);
    }

    // ── helpers ─────────────────────────────────────────────────────────────
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
                WoId         = (int)rdr["WoID"],
                WoNumber     = rdr["WoNumber"] as string ?? string.Empty,
                ItemNo       = (string)rdr["ItemNo"],
                ItemName     = (string)rdr["ItemName"],
                ItemNameEn   = rdr["ItemNameEN"] as string,
                OrderQty     = rdr["OrderQty"]     as decimal? ?? 0,
                OpenQty      = rdr["OpenQty"]      as decimal? ?? 0,
                CompletedQty = rdr["CompletedQty"] as decimal? ?? 0,
                LineId       = rdr["LineID"]      as string ?? string.Empty,
                MoldId       = rdr["MoldID"]      as string,
                RecipeId     = rdr["RecipeID"]    as string,
                DueDate      = rdr["DueDate"]     as DateTime?,
                Status       = rdr["Status"]      as string ?? "Unknown",
                TerminalLock = rdr["TerminalLock"] as string,
                Priority     = Convert.ToInt32(rdr["Priority"]),
            });
        }
        return list;
    }
}
