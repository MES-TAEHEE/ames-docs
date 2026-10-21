using AMES.Contracts.Dto;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    // Internal receiving view. PO balances and current stock are not delivery receipt balances.
    public List<SupplierDeliveryProgressDto> ListSupplierDeliveryProgress(DateTime? from = null, DateTime? to = null,
        string? search = null, string? vendor = null, bool includeReceived = false, bool shippedOnly = false)
    {
        if (from?.Date > to?.Date) throw new ArgumentException("From date cannot be after To date.");
        if (to?.Date == DateTime.MaxValue.Date) throw new ArgumentException("To date is outside the supported range.");
        if ((search?.Length ?? 0) > 120 || (vendor?.Length ?? 0) > 50)
            throw new ArgumentException("Delivery search or supplier code is too long.");
        using var c = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            WITH Headers AS (
                SELECT d.DeliveryID,d.DeliveryNumber,d.PoNumber,d.VendorID,d.DeliveryDate,d.Status,
                    COALESCE(d.ShipDate,CONVERT(date,d.ShippedAt)) ShipDate,
                    CONVERT(bit,CASE WHEN d.ShippedAt IS NOT NULL OR d.Status IN('Shipped','Received') THEN 1 ELSE 0 END) Shipped,
                    COALESCE(note.NoteNumber,d.DeliveryNumber) NoteNumber,COALESCE(v.VendorName,d.VendorID) VendorName
                FROM dbo.SCM_Delivery d LEFT JOIN dbo.MD_Vendor v ON v.VendorID=d.VendorID
                OUTER APPLY(SELECT TOP(1) n.NoteNumber FROM dbo.SCM_DeliveryNoteDelivery x
                    JOIN dbo.SCM_DeliveryNote n ON n.NoteID=x.NoteID
                    WHERE x.DeliveryID=d.DeliveryID ORDER BY n.NoteID DESC) note
                WHERE d.Status<>'Cancelled'
            ), SelectedNotes AS (
                SELECT DISTINCT h.NoteNumber FROM Headers h
                WHERE (@From IS NULL OR COALESCE(h.ShipDate,h.DeliveryDate)>=@From)
                    AND (@To IS NULL OR COALESCE(h.ShipDate,h.DeliveryDate)<DATEADD(day,1,@To))
                    AND (@Vendor='' OR h.VendorID=@Vendor)
                    AND (@Search='' OR CHARINDEX(@Search,h.NoteNumber)>0 OR CHARINDEX(@Search,h.PoNumber)>0
                        OR CHARINDEX(@Search,h.VendorName)>0 OR EXISTS(SELECT 1 FROM dbo.SCM_DeliveryLine l
                            JOIN dbo.WH_PurchaseOrder p ON p.PoID=l.PoID WHERE l.DeliveryID=h.DeliveryID AND CHARINDEX(@Search,p.ItemNo)>0))
                    AND (@ShippedOnly=0 OR NOT EXISTS(SELECT 1 FROM Headers other WHERE other.NoteNumber=h.NoteNumber AND other.Shipped=0))
                    AND (@IncludeReceived=1 OR EXISTS(SELECT 1 FROM Headers pending JOIN dbo.SCM_DeliveryLine remaining ON remaining.DeliveryID=pending.DeliveryID
                        WHERE pending.NoteNumber=h.NoteNumber AND remaining.Quantity>remaining.ReceivedQty))
            )
            SELECT h.NoteNumber,h.VendorID,h.VendorName,h.DeliveryDate,h.ShipDate,l.DeliveryLineID,
                h.DeliveryNumber,h.PoNumber,p.ItemNo,COALESCE(i.ItemName,p.ItemNo),COALESCE(p.UnitCode,''),
                l.Quantity,l.ReceivedQty,h.Shipped,b.BoxNumber,b.CaseNo,b.Quantity,
                CONVERT(bit,CASE WHEN receipt.TransactionType='IN' THEN 1 ELSE 0 END) ReceivedItem
            FROM Headers h JOIN SelectedNotes selected ON selected.NoteNumber=h.NoteNumber
            JOIN dbo.SCM_DeliveryLine l ON l.DeliveryID=h.DeliveryID
            JOIN dbo.WH_PurchaseOrder p ON p.PoID=l.PoID AND p.VendorID=h.VendorID
            LEFT JOIN dbo.MD_Item i ON i.ItemNo=p.ItemNo
            LEFT JOIN dbo.SCM_DeliveryBox b ON b.DeliveryLineID=l.DeliveryLineID AND b.ActiveFlag=1
            OUTER APPLY(SELECT TOP(1) t.TransactionType FROM dbo.WH_InventoryTransaction t
                WHERE t.LotNo=b.BoxNumber AND ((t.TransactionType='IN' AND t.SourceType='SCM_DeliveryLine' AND t.SourceID=l.DeliveryLineID)
                    OR t.ReasonCode='INBOUND_CANCEL') ORDER BY t.TransactionID DESC) receipt
            ORDER BY h.DeliveryDate,h.NoteNumber,l.DeliveryLineID,b.BoxSeq;
            """, c);
        Filter(cmd, ("@From", "SCM_Delivery.ShipDate", (object?)from?.Date ?? DBNull.Value),
            ("@To", "SCM_Delivery.ShipDate", (object?)to?.Date ?? DBNull.Value),
            ("@Search", null, search?.Trim() ?? ""), ("@Vendor", "SCM_Delivery.VendorID", vendor?.Trim() ?? ""),
            ("@ShippedOnly", null, shippedOnly), ("@IncludeReceived", null, includeReceived));
        using var r = cmd.ExecuteReader();
        var documents = new Dictionary<string, SupplierDeliveryProgressDto>(StringComparer.OrdinalIgnoreCase);
        var lines = new Dictionary<int, SupplierDeliveryLineDto>();
        while (r.Read())
        {
            var number = r.GetString(0);
            var date = r.GetDateTime(3);
            DateTime? shipped = r.IsDBNull(4) ? null : r.GetDateTime(4);
            if (!documents.TryGetValue(number, out var document))
                documents.Add(number, document = new(number, r.GetString(1), r.GetString(2), date, shipped, []));
            else if (date > document.DeliveryDate || shipped.HasValue && (!document.ShipDate.HasValue || shipped < document.ShipDate))
                documents[number] = document = document with
                {
                    DeliveryDate = date > document.DeliveryDate ? date : document.DeliveryDate,
                    ShipDate = shipped.HasValue && (!document.ShipDate.HasValue || shipped < document.ShipDate) ? shipped : document.ShipDate
                };
            var id = r.GetInt32(5);
            if (!lines.TryGetValue(id, out var line))
            {
                lines.Add(id, line = new(id, r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9),
                    r.GetString(10), r.GetDecimal(11), r.GetDecimal(12), r.GetBoolean(13), []));
                document.Lines.Add(line);
            }
            if (!r.IsDBNull(14))
                line.Items.Add(new(r.GetString(14), r.IsDBNull(15) ? null : r.GetString(15), r.GetDecimal(16), r.GetBoolean(17)));
        }
        return documents.Values.Where(d => includeReceived || !d.Complete).OrderBy(d => d.DeliveryDate).ThenBy(d => d.Number).ToList();
    }
}
