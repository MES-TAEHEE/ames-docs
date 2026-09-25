using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public record NoteDeliveryChoice(string Number, string Vendor, string Order, DateTime? ShipDate, string? NoteNumber);
    const string NoteVendorAccess = """
        EXISTS(SELECT 1 FROM dbo.SCM_PortalVendorUser m JOIN dbo.MD_Vendor v ON v.VendorID=m.VendorID
        WHERE m.UserID=@U AND m.VendorID=d.VendorID AND m.ActiveFlag=1 AND m.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1)
        """;

    public List<NoteDeliveryChoice> ListNoteDeliveries(string userId)
    {
        using var c = factory.OpenConnection();
        using var cmd = new SqlCommand($"""
            SELECT d.DeliveryNumber,d.VendorID,d.PoNumber,d.ShipDate,
                COALESCE(n.NoteNumber,CASE WHEN d.NoteSnapshot IS NOT NULL THEN d.DeliveryNumber END)
            FROM dbo.SCM_Delivery d
            LEFT JOIN dbo.SCM_DeliveryNoteDelivery x ON x.DeliveryID=d.DeliveryID
            LEFT JOIN dbo.SCM_DeliveryNote n ON n.NoteID=x.NoteID
            WHERE d.Status IN ('Shipped','Received') AND {NoteVendorAccess}
            ORDER BY d.DeliveryID DESC;
            """, c);
        Add(cmd, ("@U", userId));
        using var r = cmd.ExecuteReader(); var rows = new List<NoteDeliveryChoice>();
        while (r.Read()) rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetDateTime(3),r.IsDBNull(4)?null:r.GetString(4)));
        return rows;
    }

    DeliveryNote? ReadBatchDeliveryNote(string number, string userId)
    {
        using var c = factory.OpenConnection();
        using var cmd = new SqlCommand($"""
            SELECT d.Snapshot FROM dbo.SCM_DeliveryNote d
            WHERE d.NoteNumber=@N AND {NoteVendorAccess};
            """, c);
        Add(cmd,("@U",userId),("@N",number));
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<DeliveryNote>(json) : null;
    }

    public DeliveryNote IssueDeliveryNote(IReadOnlyList<string> deliveries, string userId, string actor)
    {
        var numbers = deliveries.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        if(numbers.Length==0 || numbers.Length>100 || numbers.Length!=deliveries.Count || string.IsNullOrWhiteSpace(actor) || actor.Length>20)
            throw new ArgumentException("Select 1–100 different deliveries.");
        using var c=factory.OpenConnection(); using var tx=c.BeginTransaction();
        var ids=new List<int>(); var vendors=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var linked=new HashSet<int>(); var lines=new List<DeliveryNoteLine>();
        string vendorName=""; DateTime at;
        using(var clock=new SqlCommand("SELECT SYSDATETIME()",c,tx)) at=(DateTime)clock.ExecuteScalar()!;
        foreach(var number in numbers)
        {
            // Serialize competing groupings in a stable order. A delivery can belong to one note only.
            using var cmd=new SqlCommand($"""
                SELECT d.DeliveryID,d.VendorID,ISNULL(v.VendorName,d.VendorID),d.Status,d.ShipDate,d.NoteSnapshot,x.NoteID
                FROM dbo.SCM_Delivery d WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.MD_Vendor v ON v.VendorID=d.VendorID
                LEFT JOIN dbo.SCM_DeliveryNoteDelivery x ON x.DeliveryID=d.DeliveryID
                WHERE d.DeliveryNumber=@N AND {NoteVendorAccess};
                """,c,tx);
            Add(cmd,("@U",userId),("@N",number));
            using var r=cmd.ExecuteReader();
            if(!r.Read()) throw new InvalidOperationException("납품서를 조회할 권한이 없습니다.");
            if(r.GetString(3)!="Shipped" || r.IsDBNull(4)) throw new InvalidOperationException("출하 완료 납품서만 발행할 수 있습니다.");
            if(!r.IsDBNull(5)) throw new InvalidOperationException("기존에 발행된 납품서입니다. 기존 문서를 재출력해 주세요.");
            ids.Add(r.GetInt32(0)); vendors.Add(r.GetString(1)); vendorName=r.GetString(2);
            if(!r.IsDBNull(6)) linked.Add(r.GetInt32(6));
        }
        if(vendors.Count!=1) throw new InvalidOperationException("같은 협력업체의 납품서만 묶을 수 있습니다.");
        if(linked.Count>0)
        {
            if(linked.Count!=1) throw new InvalidOperationException("이미 다른 문서에 포함된 납품서입니다.");
            using var previous=new SqlCommand("SELECT DeliveryID FROM dbo.SCM_DeliveryNoteDelivery WHERE NoteID=@ID",c,tx);
            Add(previous,("@ID",linked.Single())); var existing=new HashSet<int>();
            using(var r=previous.ExecuteReader()) while(r.Read()) existing.Add(r.GetInt32(0));
            if(!existing.SetEquals(ids)) throw new InvalidOperationException("이미 발행된 납품서는 다른 문서에 중복 포함할 수 없습니다.");
            using var read=new SqlCommand("SELECT Snapshot FROM dbo.SCM_DeliveryNote WHERE NoteID=@ID",c,tx); Add(read,("@ID",linked.Single()));
            var result=JsonSerializer.Deserialize<DeliveryNote>((string)read.ExecuteScalar()!)!; tx.Commit(); return result;
        }
        foreach(var id in ids)
        {
            using var cmd=new SqlCommand("""
                SELECT ISNULL(p.PoLineNo,p.PoID),p.ItemNo,ISNULL(i.ItemName,p.ItemNo),ISNULL(p.UnitCode,''),l.Quantity,
                    d.DeliveryNumber,d.PoNumber,ISNULL(p.DeliveryDestination,''),d.ShipDate,l.DeliveryLineID,p.PoID
                FROM dbo.SCM_Delivery d JOIN dbo.SCM_DeliveryLine l ON l.DeliveryID=d.DeliveryID
                JOIN dbo.WH_PurchaseOrder p ON p.PoID=l.PoID LEFT JOIN dbo.MD_Item i ON i.ItemNo=p.ItemNo
                WHERE d.DeliveryID=@ID ORDER BY l.DeliveryLineID;
                """,c,tx);
            Add(cmd,("@ID",id)); using var r=cmd.ExecuteReader(); var count=lines.Count;
            while(r.Read()) lines.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDecimal(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetDateTime(8),r.GetInt32(9),r.GetInt32(10)));
            if(lines.Count==count) throw new InvalidOperationException("납품 품목이 없습니다.");
        }
        using var insert=new SqlCommand("""
            INSERT dbo.SCM_DeliveryNote(NoteNumber,VendorID,Snapshot,IssuedAt,IssuedBy,IssuedUserID)
            OUTPUT INSERTED.NoteID VALUES(@Temp,@V,'{}',@At,@Actor,@U);
            """,c,tx);
        Add(insert,("@Temp",Guid.NewGuid().ToString("N")[..30]),("@V",vendors.Single()),("@At",at),("@Actor",actor),("@U",userId));
        insert.Parameters["@At"].SqlDbType=System.Data.SqlDbType.DateTime2;
        var noteId=(int)insert.ExecuteScalar()!;
        var noteNumber=$"DLN-{at:yyyyMMdd}-{noteId}";
        var note=new DeliveryNote(noteNumber,string.Join(", ",lines.Select(l=>l.OrderNumber).Distinct()),vendors.Single(),vendorName,"EOS",
            string.Join(" / ",lines.Select(l=>l.Destination).Distinct()),lines.Max(l=>l.ShipDate)!.Value,at,actor,lines);
        using var save=new SqlCommand("UPDATE dbo.SCM_DeliveryNote SET NoteNumber=@N,Snapshot=@Json WHERE NoteID=@ID",c,tx);
        Add(save,("@N",noteNumber),("@Json",JsonSerializer.Serialize(note)),("@ID",noteId)); save.ExecuteNonQuery();
        foreach(var id in ids)
        {
            using var link=new SqlCommand("INSERT dbo.SCM_DeliveryNoteDelivery(DeliveryID,NoteID) VALUES(@D,@N)",c,tx);
            Add(link,("@D",id),("@N",noteId)); link.ExecuteNonQuery();
        }
        tx.Commit(); return note;
    }
}
