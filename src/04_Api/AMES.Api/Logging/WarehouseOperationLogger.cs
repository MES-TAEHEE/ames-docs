using AMES.Contracts.Dto;
using AMES.Data.Connection;

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
        // Operation-level audit storage was intentionally removed.
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

}
