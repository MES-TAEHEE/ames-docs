using AMES.Data.Repositories;

namespace AMES.Web.Components.Pages.Wh;

public partial class LogHistory
{
    internal static int CountEvents(IEnumerable<WarehouseRepository.OperationLogRow> rows, string type)
        => rows.Count(row => type switch
        {
            // Cancellation stays in the inbound activity filter, but is not a receipt.
            "INBOUND" => DisplayEvent(row) is "WH.LogHistory.Lbl.Inbound" or "WH.LogHistory.Lbl.Received" or "WH.LogHistory.Lbl.Stored" or "WH.LogHistory.Lbl.ReturnReceived",
            "OUTBOUND" => DisplayEvent(row) is "WH.LogHistory.Lbl.Outbound" or "WH.LogHistory.Lbl.Released",
            "ADJUSTMENT" => DisplayEvent(row) == "WH.LogHistory.Lbl.Adjustment",
            _ => false,
        });

    internal static string EventClass(WarehouseRepository.OperationLogRow row)
    {
        var upper = row.EventType.ToUpperInvariant();
        if (upper == "CANCEL_RECEIPT") return "bad";
        if (upper == "STATUS_CHANGE")
        {
            var status = ChangedStatus(row.Message);
            if (status is "STORED" or "RECEIVED" or "RETURN_RECEIVED" or "RELEASE_CANCELLED") return "ok";
            if (status is "DEFECTIVE" or "DISPOSED" or "RECEIPT_CANCELLED") return "bad";
            if (status == "RELEASED") return "warn";
        }
        if (upper.Contains("RECEIVE")) return "ok";
        if (upper.Contains("RELEASE")) return "warn";
        if (upper.Contains("ADJUST")) return "bad";
        if (upper.Contains("LOCATION")) return "ok";
        return "ghost";
    }

    internal static string DisplayEvent(WarehouseRepository.OperationLogRow row)
    {
        if (row.EventType.Equals("STATUS_CHANGE", StringComparison.OrdinalIgnoreCase))
            return StatusLabel(ChangedStatus(row.Message));

        return row.EventType.ToUpperInvariant() switch
        {
            "RECEIVE" => "WH.LogHistory.Lbl.Inbound",
            "CANCEL_RECEIPT" => "WH.LogHistory.Lbl.CancelInbound",
            "RELEASE_PICK" => "WH.LogHistory.Lbl.Outbound",
            "ADJUST_SAVE" => "WH.LogHistory.Lbl.Adjustment",
            "MOVE_LOCATION" => "WH.LogHistory.Lbl.Relocation",
            _ => row.EventType,
        };
    }

    static string ChangedStatus(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        var separator = message.LastIndexOf("->", StringComparison.Ordinal);
        return (separator >= 0 ? message[(separator + 2)..] : message).Trim().ToUpperInvariant();
    }

    static string StatusLabel(string status) => status switch
    {
        "CREATED" => "WH.LogHistory.Lbl.Created",
        "RECEIVED" => "WH.LogHistory.Lbl.Received",
        "STORED" => "WH.LogHistory.Lbl.Stored",
        "RELEASED" => "WH.LogHistory.Lbl.Released",
        "RECEIPT_CANCELLED" => "WH.LogHistory.Lbl.InboundCancelled",
        "RELEASE_CANCELLED" => "WH.LogHistory.Lbl.ReleaseCancelled",
        "RETURN_RECEIVED" => "WH.LogHistory.Lbl.ReturnReceived",
        "DEFECTIVE" => "WH.LogHistory.Lbl.Defective",
        "DISPOSED" => "WH.LogHistory.Lbl.Disposed",
        _ => string.IsNullOrWhiteSpace(status) ? "WH.LogHistory.Lbl.StatusChanged" : status.Replace('_', ' '),
    };
}
