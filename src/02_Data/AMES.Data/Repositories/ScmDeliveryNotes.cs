using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public record DeliveryNoteLine(int Line, string Item, string Name, string Unit, decimal Quantity, string DeliveryNumber = "", string OrderNumber = "", string Destination = "", DateTime? ShipDate = null, int DeliveryLineID = 0, int PoID = 0);
    public record DeliveryNote(string Number, string OrderNumber, string Vendor, string VendorName,
        string Buyer, string Destination, DateTime ShipDate, DateTime IssuedAt, string IssuedBy,
        List<DeliveryNoteLine> Lines);

    // Authorization is rechecked for both first issue and every subsequent read.
    // A header lock serializes concurrent issues; reprints never rebuild master-data values.
    public DeliveryNote? GetDeliveryNote(string number, string userId, bool adminOnBehalf = false,
        bool issue = false, string actor = "")
    {
        if (issue) return IssueDeliveryNote([number], userId, actor, adminOnBehalf);
        var batch = ReadBatchDeliveryNote(number, userId, adminOnBehalf);
        if (batch is not null) return batch;
        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction();
        using var header = new SqlCommand("""
            IF @Admin=1 AND NOT EXISTS(SELECT 1 FROM dbo.AspNetUserRoles ur
                JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId WHERE ur.UserId=@U AND r.Name='Admin')
                THROW 50031,'No delivery note permission.',1;
            IF @Admin=0 AND NOT EXISTS(SELECT 1 FROM dbo.SCM_Delivery d
                JOIN dbo.SCM_PortalVendorUser m WITH(HOLDLOCK) ON m.VendorID=d.VendorID
                JOIN dbo.MD_Vendor v WITH(HOLDLOCK) ON v.VendorID=m.VendorID
                WHERE d.DeliveryNumber=@N AND m.UserID=@U AND m.ActiveFlag=1
                AND m.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1)
                THROW 50031,'Vendor access denied.',1;
            SELECT DeliveryID,Status,NoteSnapshot,ShipDate,SYSDATETIME(),PoNumber,VendorID
            FROM dbo.SCM_Delivery WITH(UPDLOCK,HOLDLOCK) WHERE DeliveryNumber=@N;
            """, conn, tx);
        Add(header, ("@N", number), ("@U", userId), ("@Admin", adminOnBehalf));
        int id; DateTime shipDate, issuedAt; string order, vendor, status; string? snapshot;
        using (var r = header.ExecuteReader())
        {
            if (!r.Read()) throw new InvalidOperationException("Delivery not found.");
            id = r.GetInt32(0); status = r.GetString(1);
            snapshot = r.IsDBNull(2) ? null : r.GetString(2);
            if (status is not ("Shipped" or "Received") || r.IsDBNull(3))
                throw new InvalidOperationException("Confirm shipment before issuing a delivery note.");
            shipDate = r.GetDateTime(3); issuedAt = r.GetDateTime(4);
            order = r.GetString(5); vendor = r.GetString(6);
        }
        if (snapshot != null)
        {
            var existing = JsonSerializer.Deserialize<DeliveryNote>(snapshot)
                ?? throw new InvalidOperationException("Invalid delivery note snapshot.");
            tx.Commit(); return existing;
        }
        tx.Commit(); return null;
    }
}
