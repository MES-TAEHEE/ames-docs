using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public record PortalReceiptLine(int Id,string Delivery,string Note,string Vendor,string VendorName,string Order,
        string Item,string Name,string Unit,DateTime? ShipDate,decimal Quantity,decimal Received)
    {
        public decimal Remaining => Math.Max(0,Quantity-Received);
        public string ReceiptStatus => Received<=0 ? "미입고" : Received<Quantity ? "부분입고" : "입고완료";
    }

    // Read only: EOS owns all receipt writes. Never infer delivery receipts from PO totals.
    public List<PortalReceiptLine> ListPortalReceiptResults(string userId)
    {
        using var c=factory.OpenConnection();
        using var cmd=new SqlCommand($"""
            SELECT l.DeliveryLineID,d.DeliveryNumber,
                COALESCE(n.NoteNumber,CASE WHEN d.NoteSnapshot IS NOT NULL THEN d.DeliveryNumber END,''),
                d.VendorID,ISNULL(v.VendorName,d.VendorID),d.PoNumber,p.ItemNo,ISNULL(i.ItemName,p.ItemNo),
                ISNULL(p.UnitCode,''),d.ShipDate,l.Quantity,l.ReceivedQty
            FROM dbo.SCM_Delivery d
            JOIN dbo.SCM_DeliveryLine l ON l.DeliveryID=d.DeliveryID
            JOIN dbo.WH_PurchaseOrder p ON p.PoID=l.PoID AND p.VendorID=d.VendorID
            LEFT JOIN dbo.MD_Item i ON i.ItemNo=p.ItemNo
            LEFT JOIN dbo.MD_Vendor v ON v.VendorID=d.VendorID
            LEFT JOIN dbo.SCM_DeliveryNoteDelivery x ON x.DeliveryID=d.DeliveryID
            LEFT JOIN dbo.SCM_DeliveryNote n ON n.NoteID=x.NoteID
            WHERE d.Status<>'Cancelled' AND (d.ShippedAt IS NOT NULL OR l.ReceivedQty>0 OR d.Status IN ('Shipped','Received'))
              AND {NoteVendorAccess}
            ORDER BY d.DeliveryID DESC,l.DeliveryLineID;
            """,c);
        Add(cmd,("@U",userId));
        using var r=cmd.ExecuteReader();var rows=new List<PortalReceiptLine>();
        while(r.Read())rows.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetDateTime(9),r.GetDecimal(10),r.GetDecimal(11)));
        return rows;
    }
}
