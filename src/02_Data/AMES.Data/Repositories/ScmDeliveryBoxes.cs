using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public sealed class DeliveryBoxLimitException() : InvalidOperationException("A delivery line may contain at most 1,000 boxes.");
    public record BoxDelivery(string Number,string Vendor,string Order,DateTime Date,string Status,int Boxes);
    public record DeliveryBox(long Id,string Number,int Sequence,string Item,string Name,string Unit,decimal Quantity,decimal PackingQty,string Delivery,string Order,string Vendor,string VendorLotNo,DateTime ProductionDate,string VendorName,DateTime DeliveryDate,string Destination,int BoxCount);

    public List<BoxDelivery> ListBoxDeliveries(string userId)
    {
        using var c=factory.OpenConnection();
        using var cmd=new SqlCommand($"""
            SELECT d.DeliveryNumber,d.VendorID,d.PoNumber,d.DeliveryDate,d.Status,
                (SELECT COUNT(*) FROM dbo.SCM_DeliveryBox b JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID
                 WHERE l.DeliveryID=d.DeliveryID AND b.ActiveFlag=1)
            FROM dbo.SCM_Delivery d WHERE {NoteVendorAccess}
            ORDER BY d.DeliveryID DESC;
            """,c);
        Add(cmd,("@U",userId));
        using var r=cmd.ExecuteReader();var rows=new List<BoxDelivery>();
        while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetDateTime(3),r.GetString(4),r.GetInt32(5)));
        return rows;
    }

    public List<DeliveryBox> ListDeliveryBoxes(string number,string userId)
    {
        using var c=factory.OpenConnection();
        using var cmd=new SqlCommand($"""
            SELECT b.BoxID,b.BoxNumber,b.BoxSeq,b.ItemNo,b.ItemName,b.UnitCode,b.Quantity,l.PackingQty,d.DeliveryNumber,d.PoNumber,d.VendorID,
                COALESCE(l.VendorLotNo,CONVERT(char(8),d.DeliveryDate,112)),COALESCE(l.ProductionDate,d.DeliveryDate),
                ISNULL(v.VendorName,d.VendorID),d.DeliveryDate,ISNULL(p.DeliveryDestination,''),
                (SELECT COUNT(*) FROM dbo.SCM_DeliveryBox bx WHERE bx.DeliveryLineID=l.DeliveryLineID AND bx.ActiveFlag=1)
            FROM dbo.SCM_Delivery d JOIN dbo.SCM_DeliveryLine l ON l.DeliveryID=d.DeliveryID
            JOIN dbo.SCM_DeliveryBox b ON b.DeliveryLineID=l.DeliveryLineID
            JOIN dbo.WH_PurchaseOrder p ON p.PoID=l.PoID
            LEFT JOIN dbo.MD_Vendor v ON v.VendorID=d.VendorID
            WHERE d.DeliveryNumber=@N AND d.Status<>'Cancelled' AND b.ActiveFlag=1 AND {NoteVendorAccess}
            ORDER BY l.DeliveryLineID,b.BoxSeq;
            """,c);
        Add(cmd,("@U",userId),("@N",number));
        using var r=cmd.ExecuteReader();var rows=new List<DeliveryBox>();
        while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDecimal(6),r.GetDecimal(7),r.GetString(8),r.GetString(9),r.GetString(10),r.GetString(11),r.GetDateTime(12),r.GetString(13),r.GetDateTime(14),r.GetString(15),r.GetInt32(16)));
        return rows;
    }

    // Called only while the PO and delivery are locked by the registration/edit transaction.
    // Retain the packing snapshot. A quantity edit voids old box IDs instead of reusing them.
    static void SyncDeliveryBoxes(SqlConnection c,SqlTransaction tx,int deliveryId)
    {
        string vendor; DateTime numberDate;
        using(var header=new SqlCommand("SELECT VendorID,CONVERT(date,SYSDATETIME()) FROM dbo.SCM_Delivery WHERE DeliveryID=@D",c,tx))
        {
            Add(header,("@D",deliveryId));using var r=header.ExecuteReader();
            if(!r.Read())throw new InvalidOperationException("Delivery not found.");
            vendor=r.GetString(0);numberDate=r.GetDateTime(1);
        }
        var lines=new List<(int Id,decimal Qty,decimal Pack,string Item,string Name,string Unit,decimal SavedQty,int Count)>();
        using(var cmd=new SqlCommand("""
            SELECT l.DeliveryLineID,l.Quantity,COALESCE(l.PackingQty,m.PackingQty,0),p.ItemNo,
                ISNULL(i.ItemName,p.ItemNo),ISNULL(p.UnitCode,''),
                ISNULL((SELECT SUM(b.Quantity) FROM dbo.SCM_DeliveryBox b WHERE b.DeliveryLineID=l.DeliveryLineID AND b.ActiveFlag=1),0),
                (SELECT COUNT(*) FROM dbo.SCM_DeliveryBox b WHERE b.DeliveryLineID=l.DeliveryLineID AND b.ActiveFlag=1)
            FROM dbo.SCM_DeliveryLine l JOIN dbo.WH_PurchaseOrder p ON p.PoID=l.PoID
            JOIN dbo.MD_Item i ON i.ItemNo=p.ItemNo
            LEFT JOIN dbo.SCM_ItemVendor m WITH(HOLDLOCK) ON m.ItemNo=p.ItemNo AND m.VendorID=p.VendorID AND m.ActiveFlag=1
            WHERE l.DeliveryID=@D ORDER BY l.DeliveryLineID;
            """,c,tx))
        {
            Add(cmd,("@D",deliveryId));using var r=cmd.ExecuteReader();
            while(r.Read())lines.Add((r.GetInt32(0),r.GetDecimal(1),r.GetDecimal(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetDecimal(6),r.GetInt32(7)));
        }
        foreach(var l in lines)
        {
            if(l.Pack<=0)throw new PackingQuantityRequiredException();
            var count=decimal.Ceiling(l.Qty/l.Pack);
            if(count>1000)throw new DeliveryBoxLimitException();
            if(l.SavedQty==l.Qty && l.Count==count)continue;
            using(var reset=new SqlCommand("""
                UPDATE dbo.SCM_DeliveryBox SET ActiveFlag=0,VoidedTS=SYSDATETIME() WHERE DeliveryLineID=@L AND ActiveFlag=1;
                UPDATE dbo.SCM_DeliveryLine SET PackingQty=@Pack WHERE DeliveryLineID=@L;
                """,c,tx))
            {Add(reset,("@L",l.Id),("@Pack",l.Pack));reset.ExecuteNonQuery();}
            var firstNumber=ReserveBoxNumbers(c,tx,vendor,numberDate,(int)count);
            for(var seq=1;seq<=count;seq++)
            {
                using var insert=new SqlCommand("""
                    INSERT dbo.SCM_DeliveryBox(DeliveryLineID,BoxSeq,ItemNo,ItemName,UnitCode,Quantity,IssuedBoxNumber)
                    VALUES(@L,@S,@I,@Name,@Unit,@Q,@Number);
                    """,c,tx);
                Add(insert,("@L",l.Id),("@S",seq),("@I",l.Item),("@Name",l.Name),("@Unit",l.Unit),("@Q",Math.Min(l.Pack,l.Qty-(seq-1)*l.Pack)));
                Add(insert,("@Number",$"BX-{vendor}-{numberDate.ToString("yyyyMMdd",System.Globalization.CultureInfo.InvariantCulture)}-{(firstNumber+seq-1).ToString("D4",System.Globalization.CultureInfo.InvariantCulture)}"));
                insert.ExecuteNonQuery();
            }
        }
    }
    // Allocate a range under the same transaction as the boxes. The PK range lock
    // serializes concurrent deliveries for this vendor/day, including the first allocation.
    static int ReserveBoxNumbers(SqlConnection c,SqlTransaction tx,string vendor,DateTime date,int count)
    {
        if(count<=0)throw new ArgumentOutOfRangeException(nameof(count));
        using var cmd=new SqlCommand("""
            DECLARE @Last int;
            SELECT @Last=LastNumber FROM dbo.SCM_BoxNumberSequence WITH(UPDLOCK,HOLDLOCK)
            WHERE VendorID=@V AND NumberDate=@Day;
            IF @Last IS NULL
            BEGIN
                SET @Last=0;
                INSERT dbo.SCM_BoxNumberSequence(VendorID,NumberDate,LastNumber) VALUES(@V,@Day,@Count);
            END
            ELSE UPDATE dbo.SCM_BoxNumberSequence SET LastNumber=@Last+@Count WHERE VendorID=@V AND NumberDate=@Day;
            SELECT @Last+1;
            """,c,tx);
        Add(cmd,("@V",vendor),("@Day",date.Date),("@Count",count));
        return (int)cmd.ExecuteScalar()!;
    }

}
