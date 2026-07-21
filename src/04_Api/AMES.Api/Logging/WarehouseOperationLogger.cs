using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Logging;

public static class WarehouseOperationLogger
{
    public sealed record Entry(
        string EventType,
        string? ScreenCode = null,
        string? EmployeeNo = null,
        string? EmployeeName = null,
        string? WorkerId = null,
        string? TerminalId = null,
        string? LineId = null,
        string? ShiftCode = null,
        string? ScanType = null,
        string? ScanValue = null,
        string Result = "INFO",
        string? Message = null,
        string? RefDocType = null,
        string? RefDocNo = null,
        string? LotNo = null,
        string? PartNo = null,
        string? LocationId = null,
        decimal? Qty = null);

    public static void TryWrite(AmesConnectionFactory factory, HttpContext ctx, Entry entry)
    {
        if (entry.EventType.Equals("LOGIN", StringComparison.OrdinalIgnoreCase) ||
            entry.EventType.Equals("LOGOUT", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using var conn = factory.OpenConnection();
            if (ProcedureExists(conn, "dbo.WH_PDA_OPERATION_LOG_WRITE"))
            {
                using var cmd = new SqlCommand("[dbo].[WH_PDA_OPERATION_LOG_WRITE]", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 5
                };
                AddLogParameters(cmd, ctx, entry);
                cmd.ExecuteNonQuery();
                return;
            }

            if (!TableExists(conn, "dbo.WH_OperationLog"))
                return;

            using var insert = new SqlCommand("""
                INSERT INTO dbo.WH_OperationLog
                (
                    EventType, ScreenCode, EmployeeNo, EmployeeName, WorkerID,
                    TerminalID, LineID, ShiftCode, ScanType, ScanValue,
                    Result, Message, ClientIP, UserAgent, RefDocType, RefDocNo,
                    LotNo, PartNo, LocationID, Qty, CreatedBy, CreatedTS
                )
                VALUES
                (
                    @EventType, @ScreenCode, @EmployeeNo, @EmployeeName, @WorkerID,
                    @TerminalID, @LineID, @ShiftCode, @ScanType, @ScanValue,
                    @Result, @Message, @ClientIP, @UserAgent, @RefDocType, @RefDocNo,
                    @LotNo, @PartNo, @LocationID, @Qty, @CreatedBy, SYSDATETIME()
                );
                """, conn)
            {
                CommandTimeout = 5
            };
            AddLogParameters(insert, ctx, entry);
            insert.ExecuteNonQuery();
        }
        catch
        {
            // Warehouse work must not fail only because the optional audit log is unavailable.
        }
    }

    public static Entry FromSession(PopSessionDto session, string eventType, string screenCode, string? scanType = null,
        string? scanValue = null, string result = "INFO", string? message = null, string? refDocType = null,
        string? refDocNo = null, string? lotNo = null, string? partNo = null, string? locationId = null, decimal? qty = null)
    {
        return new Entry(
            EventType: eventType,
            ScreenCode: screenCode,
            EmployeeNo: session.EmployeeNo,
            EmployeeName: session.EmployeeName,
            WorkerId: session.OperatorId,
            TerminalId: session.TerminalId,
            LineId: session.LineId,
            ShiftCode: session.ShiftCode,
            ScanType: scanType,
            ScanValue: scanValue,
            Result: result,
            Message: message,
            RefDocType: refDocType,
            RefDocNo: refDocNo,
            LotNo: lotNo,
            PartNo: partNo,
            LocationId: locationId,
            Qty: qty);
    }

    private static void AddLogParameters(SqlCommand cmd, HttpContext ctx, Entry entry)
    {
        Add(cmd, "@EventType", SqlDbType.VarChar, 40, entry.EventType);
        Add(cmd, "@ScreenCode", SqlDbType.VarChar, 20, entry.ScreenCode);
        Add(cmd, "@EmployeeNo", SqlDbType.NVarChar, 40, entry.EmployeeNo);
        Add(cmd, "@EmployeeName", SqlDbType.NVarChar, 120, entry.EmployeeName);
        Add(cmd, "@WorkerID", SqlDbType.NVarChar, 450, entry.WorkerId);
        Add(cmd, "@TerminalID", SqlDbType.NVarChar, 80, entry.TerminalId);
        Add(cmd, "@LineID", SqlDbType.NVarChar, 40, entry.LineId);
        Add(cmd, "@ShiftCode", SqlDbType.NVarChar, 20, entry.ShiftCode);
        Add(cmd, "@ScanType", SqlDbType.VarChar, 30, entry.ScanType);
        Add(cmd, "@ScanValue", SqlDbType.NVarChar, 120, entry.ScanValue);
        Add(cmd, "@Result", SqlDbType.VarChar, 20, entry.Result);
        Add(cmd, "@Message", SqlDbType.NVarChar, 500, entry.Message);
        Add(cmd, "@ClientIP", SqlDbType.NVarChar, 64, ctx.Connection.RemoteIpAddress?.ToString());
        Add(cmd, "@UserAgent", SqlDbType.NVarChar, 300, ctx.Request.Headers.UserAgent.ToString());
        Add(cmd, "@RefDocType", SqlDbType.VarChar, 30, entry.RefDocType);
        Add(cmd, "@RefDocNo", SqlDbType.NVarChar, 80, entry.RefDocNo);
        Add(cmd, "@LotNo", SqlDbType.NVarChar, 80, entry.LotNo);
        Add(cmd, "@PartNo", SqlDbType.NVarChar, 80, entry.PartNo);
        Add(cmd, "@LocationID", SqlDbType.NVarChar, 80, entry.LocationId);
        var qty = cmd.Parameters.Add("@Qty", SqlDbType.Decimal);
        qty.Precision = 14;
        qty.Scale = 3;
        qty.Value = entry.Qty.HasValue ? (object)entry.Qty.Value : DBNull.Value;
        Add(cmd, "@CreatedBy", SqlDbType.VarChar, 50, entry.EmployeeNo ?? entry.WorkerId ?? "system");
    }

    private static void Add(SqlCommand cmd, string name, SqlDbType type, int size, string? value)
    {
        var p = cmd.Parameters.Add(name, type, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static bool ProcedureExists(SqlConnection conn, string objectName)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'P') IS NULL THEN 0 ELSE 1 END;", conn);
        cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 256).Value = objectName;
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    private static bool TableExists(SqlConnection conn, string objectName)
    {
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'U') IS NULL THEN 0 ELSE 1 END;", conn);
        cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 256).Value = objectName;
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }
}
