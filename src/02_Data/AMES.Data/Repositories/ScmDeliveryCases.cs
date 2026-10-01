using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public record CaseBox(long Id, string Number, string Item, string Name, string Unit,
        decimal Quantity, string Order, string? CaseNo, bool Received, int PoID, decimal PackingQty);
    public record DeliveryCase(string Number, string NoteNumber, string Vendor, string VendorName,
        DateTime CreatedAt, List<CaseBox> Boxes, string Order, string? Delivery, string Status)
    {
        public bool ReadyToReceive => (Status is "Shipped" or "Received") && !string.IsNullOrWhiteSpace(NoteNumber);
    }
    public record CasePart(int PoID, string Item, string Name, string Unit, decimal PackingQty, decimal Available, string Issue="");
    public record CaseBoxInput(int PoID, decimal Quantity);

    const string CaseVendorAccess = """
        EXISTS(SELECT 1 FROM dbo.SCM_PortalVendorUser u JOIN dbo.MD_Vendor v ON v.VendorID=u.VendorID
        WHERE u.UserID=@U AND u.VendorID=k.VendorID AND u.ActiveFlag=1 AND u.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1)
        """;

    public List<CasePart> ListCaseParts(string order, string userId)
    {
        using var c = factory.OpenConnection();
        return ReadCaseParts(c, null, order, userId);
    }

    static List<CasePart> ReadCaseParts(SqlConnection c, SqlTransaction? tx, string order, string userId)
    {
        using var cmd = new SqlCommand("""
            SELECT p.PoID,p.ItemNo,COALESCE(i.ItemName,p.ItemNo),COALESCE(p.UnitCode,''),
                   CASE WHEN i.ItemNo IS NULL OR ISNULL(i.ActiveFlag,1)=0 THEN 0 ELSE COALESCE(m.PackingQty,0) END,
                   p.OrderQty-COALESCE(p.ReceivedQty,0)
                   -COALESCE((SELECT SUM(l.Quantity-l.ReceivedQty) FROM dbo.SCM_DeliveryLine l
                     JOIN dbo.SCM_Delivery d ON d.DeliveryID=l.DeliveryID WHERE l.PoID=p.PoID AND d.Status<>'Cancelled'),0)
                   -COALESCE((SELECT SUM(b.Quantity) FROM dbo.SCM_DeliveryBox b
                     WHERE b.PoID=p.PoID AND b.CaseNo IS NOT NULL AND b.DeliveryLineID IS NULL AND b.ActiveFlag=1),0),
                   CASE WHEN i.ItemNo IS NULL THEN 'Item master missing' WHEN ISNULL(i.ActiveFlag,1)=0 THEN 'Item inactive'
                        WHEN m.ItemNo IS NULL THEN 'Vendor mapping missing' WHEN COALESCE(m.PackingQty,0)<=0 THEN 'Packing quantity missing' ELSE '' END
            FROM dbo.WH_PurchaseOrder p
            LEFT JOIN dbo.MD_Item i ON i.ItemNo=p.ItemNo
            LEFT JOIN dbo.SCM_ItemVendor m ON m.ItemNo=p.ItemNo AND m.VendorID=p.VendorID AND m.ActiveFlag=1
            WHERE p.PoNumber=@N AND p.Status IN('Open','Partial') AND p.SupplierConfirmedAt IS NOT NULL
              AND EXISTS(SELECT 1 FROM dbo.SCM_PortalVendorUser u JOIN dbo.MD_Vendor v ON v.VendorID=u.VendorID
                  WHERE u.UserID=@U AND u.VendorID=p.VendorID AND u.ActiveFlag=1 AND u.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1)
            ORDER BY p.PoID;
            """, c, tx);
        Add(cmd, ("@N", order), ("@U", userId));
        using var r = cmd.ExecuteReader();
        var rows = new List<CasePart>();
        while (r.Read()) rows.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4), Math.Max(0,r.GetDecimal(5)),r.GetString(6)));
        return rows;
    }

    public string CreatePreparedCase(string order, IReadOnlyList<CaseBoxInput> boxes, string userId)
    {
        using var c=factory.OpenConnection();using var tx=c.BeginTransaction();
        var number=CreatePreparedCase(c,tx,order,boxes,userId);
        tx.Commit();return number;
    }

    static string CreatePreparedCase(SqlConnection c,SqlTransaction tx,string order,IReadOnlyList<CaseBoxInput> boxes,string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || boxes.Count is < 1 or > 1000
            || boxes.Any(b => b.Quantity <= 0 || b.Quantity > 999999999.999m || decimal.Round(b.Quantity,3) != b.Quantity))
            throw new ArgumentException("Select 1 to 1,000 boxes with positive quantities.");
        using (var gate = new SqlCommand("SELECT PoID FROM dbo.WH_PurchaseOrder WITH(UPDLOCK,HOLDLOCK) WHERE PoNumber=@N;",c,tx))
        { Add(gate,("@N",order)); using var r=gate.ExecuteReader(); while(r.Read()){} }
        var parts = ReadCaseParts(c,tx,order,userId).ToDictionary(p=>p.PoID);
        foreach (var group in boxes.GroupBy(b=>b.PoID))
        {
            if (!parts.TryGetValue(group.Key,out var part) || group.Sum(b=>b.Quantity)>part.Available)
                throw new InvalidOperationException("Part unavailable or case quantity exceeds the remaining PO balance. Refresh the list.");
            if (part.PackingQty<=0 || group.Any(b=>b.Quantity>part.PackingQty))
                throw new InvalidOperationException("Check Packing Quantities. Each box must not exceed its packing quantity.");
        }
        using var header = new SqlCommand("""
            IF (SELECT COUNT(DISTINCT VendorID) FROM dbo.WH_PurchaseOrder WHERE PoNumber=@N)<>1
                THROW 51440,'Order must belong to one vendor.',1;
            DECLARE @Sequence varchar(20)=CONVERT(varchar(20),NEXT VALUE FOR dbo.SCM_CaseNumberSequence);
            DECLARE @CaseNo varchar(50)=CONCAT('CASE-',CASE WHEN LEN(@Sequence)<6 THEN RIGHT('000000'+@Sequence,6) ELSE @Sequence END);
            INSERT dbo.SCM_DeliveryCase(CaseNo,PoNumber,VendorID,CreatedBy)
            OUTPUT INSERTED.CaseNo,INSERTED.VendorID,CONVERT(date,INSERTED.CreatedAt)
            SELECT @CaseNo,@N,MIN(VendorID),@U FROM dbo.WH_PurchaseOrder WHERE PoNumber=@N;
            """,c,tx);
        Add(header,("@N",order),("@U",userId));
        string number,vendor; DateTime date;
        using(var r=header.ExecuteReader()){r.Read();number=r.GetString(0);vendor=r.GetString(1);date=r.GetDateTime(2);}
        var first=ReserveBoxNumbers(c,tx,vendor,date,boxes.Count);
        for(var index=0;index<boxes.Count;index++)
        {
            var b=boxes[index];var p=parts[b.PoID];
            using var insert=new SqlCommand("""
                INSERT dbo.SCM_DeliveryBox(DeliveryLineID,PoID,CaseNo,BoxSeq,ItemNo,ItemName,UnitCode,Quantity,PackingQty,IssuedBoxNumber)
                VALUES(NULL,@P,@Case,@Seq,@Item,@Name,@Unit,@Qty,@Pack,@Barcode);
                """,c,tx);
            Add(insert,("@P",b.PoID),("@Case",number),("@Seq",index+1),("@Item",p.Item),("@Name",p.Name),("@Unit",p.Unit),
                ("@Qty",b.Quantity),("@Pack",p.PackingQty),("@Barcode",$"BX-{vendor}-{date:yyyyMMdd}-{first+index:D4}"));
            insert.ExecuteNonQuery();
        }
        return number;
    }

    public void CancelPreparedCase(string number,string userId)
    {
        using var c=factory.OpenConnection();using var tx=c.BeginTransaction();
        string order;
        using(var lookup=new SqlCommand($"SELECT k.PoNumber FROM dbo.SCM_DeliveryCase k WHERE k.CaseNo=@N AND {CaseVendorAccess}",c,tx))
        {Add(lookup,("@N",number),("@U",userId));order=lookup.ExecuteScalar() as string ?? throw new InvalidOperationException("Case not found.");}
        using(var gate=new SqlCommand("SELECT PoID FROM dbo.WH_PurchaseOrder WITH(UPDLOCK,HOLDLOCK) WHERE PoNumber=@N",c,tx))
        {Add(gate,("@N",order));using var r=gate.ExecuteReader();while(r.Read()){}}
        using var cmd=new SqlCommand($"""
            IF NOT EXISTS(SELECT 1 FROM dbo.SCM_DeliveryCase k WITH(UPDLOCK,HOLDLOCK)
                WHERE k.CaseNo=@N AND k.DeliveryID IS NULL AND k.NoteID IS NULL AND {CaseVendorAccess})
                THROW 51441,'Only unassigned cases can be cancelled.',1;
            IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b JOIN dbo.WH_Inventory w
                ON w.LotNo COLLATE DATABASE_DEFAULT=b.BoxNumber COLLATE DATABASE_DEFAULT WHERE b.CaseNo=@N)
                THROW 51441,'Received cases cannot be cancelled here.',1;
            UPDATE dbo.SCM_DeliveryBox SET ActiveFlag=0,VoidedTS=SYSDATETIME() WHERE CaseNo=@N AND ActiveFlag=1;
            """,c,tx);
        Add(cmd,("@N",number),("@U",userId));cmd.ExecuteNonQuery();tx.Commit();
    }

    public List<DeliveryCase> ListDeliveryCases(string? order,string userId,string? note=null)
        => ReadDeliveryCases(null,order,userId,note);

    public DeliveryCase? GetDeliveryCase(string number,string? userId=null)
        => ReadDeliveryCases(number,null,userId,null).SingleOrDefault();

    List<DeliveryCase> ReadDeliveryCases(string? number,string? order,string? userId,string? note)
    {
        using var c=factory.OpenConnection();
        using var cmd=new SqlCommand($"""
            SELECT k.CaseNo,COALESCE(n.NoteNumber,''),k.VendorID,COALESCE(v.VendorName,k.VendorID),k.CreatedAt,
                   COALESCE(k.PoNumber,''),d.DeliveryNumber,
                   COALESCE(d.Status,CASE WHEN k.NoteID IS NOT NULL THEN 'Shipped' ELSE 'Prepared' END),
                   b.BoxID,b.BoxNumber,b.ItemNo,b.ItemName,b.UnitCode,b.Quantity,COALESCE(p.PoNumber,''),
                   CONVERT(bit,CASE WHEN w.LotNo IS NULL THEN 0 ELSE 1 END),COALESCE(b.PoID,l.PoID),COALESCE(b.PackingQty,l.PackingQty,b.Quantity)
            FROM dbo.SCM_DeliveryCase k
            JOIN dbo.SCM_DeliveryBox b ON b.CaseNo=k.CaseNo AND b.ActiveFlag=1
            LEFT JOIN dbo.SCM_Delivery d ON d.DeliveryID=k.DeliveryID
            LEFT JOIN dbo.SCM_DeliveryNoteDelivery x ON x.DeliveryID=k.DeliveryID
            LEFT JOIN dbo.SCM_DeliveryNote n ON n.NoteID=COALESCE(k.NoteID,x.NoteID)
            LEFT JOIN dbo.MD_Vendor v ON v.VendorID=k.VendorID
            LEFT JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID
            LEFT JOIN dbo.WH_PurchaseOrder p ON p.PoID=COALESCE(b.PoID,l.PoID)
            LEFT JOIN dbo.WH_Inventory w ON w.LotNo COLLATE DATABASE_DEFAULT=b.BoxNumber COLLATE DATABASE_DEFAULT
            WHERE (@N IS NULL OR k.CaseNo=@N) AND (@Order IS NULL OR k.PoNumber=@Order)
              AND (@Note IS NULL OR n.NoteNumber=@Note)
              {(userId is null ? "" : $"AND {CaseVendorAccess}")}
            ORDER BY k.CreatedAt DESC,k.CaseNo DESC,b.BoxID;
            """,c);
        Add(cmd,("@N",(object?)number??DBNull.Value),("@Order",(object?)order??DBNull.Value),("@Note",(object?)note??DBNull.Value),("@U",userId??""));
        using var r=cmd.ExecuteReader();var cases=new Dictionary<string,DeliveryCase>();
        while(r.Read())
        {
            var no=r.GetString(0);
            if(!cases.TryGetValue(no,out var item))
            {
                item=new(no,r.GetString(1),r.GetString(2),r.GetString(3),r.GetDateTime(4),[],r.GetString(5),
                    r.IsDBNull(6)?null:r.GetString(6),r.GetString(7));
                cases.Add(no,item);
            }
            item.Boxes.Add(new(r.GetInt64(8),r.GetString(9),r.GetString(10),r.GetString(11),r.GetString(12),
                r.GetDecimal(13),r.GetString(14),no,r.GetBoolean(15),r.GetInt32(16),r.GetDecimal(17)));
        }
        return cases.Values.ToList();
    }

    // Caller already holds the PO lock used by all delivery registrations.
    static void ValidatePreparedCases(SqlConnection c,SqlTransaction tx,string order,IReadOnlyList<string> cases,
        IReadOnlyList<DeliveryInput> items)
    {
        if(cases.Count is <1 or >1000
            || cases.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=cases.Count)
            throw new ArgumentException("Select different prepared cases.");
        using var cmd=new SqlCommand("""
            DECLARE @Cases TABLE(CaseNo varchar(50) PRIMARY KEY);
            INSERT @Cases SELECT CONVERT(varchar(50),value) FROM OPENJSON(@CasesJson);
            IF (SELECT COUNT(*) FROM dbo.SCM_DeliveryCase k WITH(UPDLOCK,HOLDLOCK) JOIN @Cases s ON s.CaseNo=k.CaseNo
                WHERE k.PoNumber=@N AND k.DeliveryID IS NULL AND k.NoteID IS NULL)<>(SELECT COUNT(*) FROM @Cases)
                THROW 51441,'A case is already assigned or belongs to another PO. Refresh the list.',1;
            IF EXISTS(SELECT 1 FROM @Cases s WHERE NOT EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b
                      WHERE b.CaseNo=s.CaseNo AND b.ActiveFlag=1))
                THROW 51441,'A case has been cancelled. Refresh the list.',1;
            IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b JOIN @Cases s ON s.CaseNo=b.CaseNo
                LEFT JOIN dbo.WH_PurchaseOrder p ON p.PoID=b.PoID
                WHERE b.ActiveFlag=1 AND (b.DeliveryLineID IS NOT NULL OR p.PoNumber<>@N OR p.PoID IS NULL
                  OR EXISTS(SELECT 1 FROM dbo.WH_Inventory w WHERE w.LotNo COLLATE DATABASE_DEFAULT=b.BoxNumber COLLATE DATABASE_DEFAULT)))
                THROW 51441,'A case contains unavailable boxes.',1;
            SELECT b.PoID,SUM(b.Quantity) Qty FROM dbo.SCM_DeliveryBox b
            JOIN @Cases s ON s.CaseNo=b.CaseNo WHERE b.ActiveFlag=1 GROUP BY b.PoID;
            """,c,tx);
        Add(cmd,("@N",order),("@CasesJson",JsonSerializer.Serialize(cases)));
        var totals=new Dictionary<int,decimal>();
        using(var r=cmd.ExecuteReader())while(r.Read()) totals.Add(r.GetInt32(0),r.GetDecimal(1));
        if(totals.Count!=items.Count || items.Any(i=>!totals.TryGetValue(i.PoID,out var q)||q!=i.Quantity))
            throw new InvalidOperationException("Delivery quantities must match the selected cases.");
    }

    static void AttachPreparedCases(SqlConnection c,SqlTransaction tx,int deliveryId,IReadOnlyList<string> cases)
    {
        using var cmd=new SqlCommand("""
            DECLARE @Cases TABLE(CaseNo varchar(50) PRIMARY KEY);
            INSERT @Cases SELECT CONVERT(varchar(50),value) FROM OPENJSON(@CasesJson);
            ;WITH Numbered AS(
                SELECT b.BoxID,l.DeliveryLineID,ROW_NUMBER() OVER(PARTITION BY l.DeliveryLineID ORDER BY b.BoxID) Seq
                FROM dbo.SCM_DeliveryBox b JOIN @Cases s ON s.CaseNo=b.CaseNo
                JOIN dbo.SCM_DeliveryLine l ON l.PoID=b.PoID AND l.DeliveryID=@ID WHERE b.ActiveFlag=1)
            UPDATE b SET DeliveryLineID=n.DeliveryLineID,BoxSeq=n.Seq
            FROM dbo.SCM_DeliveryBox b JOIN Numbered n ON n.BoxID=b.BoxID;
            UPDATE k SET DeliveryID=@ID FROM dbo.SCM_DeliveryCase k JOIN @Cases s ON s.CaseNo=k.CaseNo;
            UPDATE l SET PackingQty=x.Pack FROM dbo.SCM_DeliveryLine l
            CROSS APPLY(SELECT MIN(b.PackingQty) Pack FROM dbo.SCM_DeliveryBox b WHERE b.DeliveryLineID=l.DeliveryLineID AND b.ActiveFlag=1) x
            WHERE l.DeliveryID=@ID;
            """,c,tx);
        Add(cmd,("@ID",deliveryId),("@CasesJson",JsonSerializer.Serialize(cases)));cmd.ExecuteNonQuery();
    }

    public int ReceiveDeliveryCase(string caseNo,string mode,string actor)
    {
        if(mode is not ("LOCAL" or "CKD"))throw new ArgumentException("Invalid receive type.");
        using var c=factory.OpenConnection();using var tx=c.BeginTransaction();
        using var select=new SqlCommand("""
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.SCM_DeliveryCase WITH(UPDLOCK,HOLDLOCK) WHERE CaseNo=@N)
                THROW 51440,'Case barcode was not found.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox WHERE CaseNo=@N AND ActiveFlag=1)
                THROW 51442,'This case has been cancelled.',1;
            IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b WITH(UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID
                LEFT JOIN dbo.SCM_Delivery d ON d.DeliveryID=l.DeliveryID
                WHERE b.CaseNo=@N AND (b.ActiveFlag=0 OR d.DeliveryID IS NULL OR d.Status NOT IN('Shipped','Received')))
                THROW 51442,'This case has not been shipped or contains cancelled boxes.',1;
            SELECT b.BoxNumber FROM dbo.SCM_DeliveryBox b WITH(UPDLOCK,HOLDLOCK)
            WHERE b.CaseNo=@N AND b.ActiveFlag=1 AND NOT EXISTS(
                SELECT 1 FROM dbo.WH_Inventory w WITH(UPDLOCK,HOLDLOCK)
                WHERE w.LotNo COLLATE DATABASE_DEFAULT=b.BoxNumber COLLATE DATABASE_DEFAULT)
            ORDER BY b.BoxID;
            """,c,tx);
        Add(select,("@N",caseNo));var boxes=new List<string>();
        using(var r=select.ExecuteReader())while(r.Read())boxes.Add(r.GetString(0));
        if(boxes.Count==0)throw new InvalidOperationException("This case has already been fully received.");
        foreach(var barcode in boxes)
        {
            using var receive=new SqlCommand("dbo.WH_PDA_INBOUND_RECEIVE_LOT",c,tx){CommandType=CommandType.StoredProcedure,CommandTimeout=60};
            Add(receive,("@ReceiveMode",mode),("@LotBarcode",barcode),("@LocationId",""),("@UserId",actor),("@SimulateFailure",false));
            receive.ExecuteNonQuery();LinkReceivedCase(c,tx,barcode);
        }
        tx.Commit();return boxes.Count;
    }

    public static void LinkReceivedCase(SqlConnection c,SqlTransaction tx,string barcode)
    {
        using var cmd=new SqlCommand("""
            DECLARE @Case varchar(50),@Note varchar(30);
            SELECT @Case=b.CaseNo,@Note=n.NoteNumber
            FROM dbo.SCM_DeliveryBox b JOIN dbo.SCM_DeliveryCase k ON k.CaseNo=b.CaseNo
            LEFT JOIN dbo.SCM_DeliveryNoteDelivery x ON x.DeliveryID=k.DeliveryID
            LEFT JOIN dbo.SCM_DeliveryNote n ON n.NoteID=COALESCE(k.NoteID,x.NoteID)
            WHERE b.BoxNumber=@B AND b.ActiveFlag=1;
            IF @Case IS NULL RETURN;
            IF NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Case)
                INSERT dbo.WH_Inventory(LotNo,UnitType,PartNo,PartName,Qty,DeliveryNoteNo,ReceivedAt,CreatedAt,UpdatedAt)
                VALUES(@Case,'CASE',NULL,NULL,0,@Note,SYSDATETIME(),SYSDATETIME(),SYSDATETIME());
            UPDATE dbo.WH_Inventory SET UnitType='BOX',ParentLotNo=@Case,CaseNo=@Case,UpdatedAt=SYSDATETIME()
            WHERE LotNo=@B;
            """,c,tx);
        Add(cmd,("@B",barcode));cmd.ExecuteNonQuery();
    }
}
