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
    public record CasePart(int PoID, string Item, string Name, string Unit, decimal PackingQty, decimal Available, string Issue="",
        decimal Ordered=0, decimal Received=0, decimal PendingDelivery=0, decimal Prepared=0);
    public record CaseBoxInput(int PoID, decimal Quantity);
    public record CaseDraftInput(IReadOnlyList<string> Orders, IReadOnlyList<CaseBoxInput> Boxes);

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
                   CASE WHEN i.ItemNo IS NULL OR ISNULL(i.ActiveFlag,1)=0 OR m.ActiveFlag=0 THEN 0 ELSE COALESCE(m.PackingQty,0) END,
                   COALESCE(p.OrderQty,0)-COALESCE(p.ReceivedQty,0)-pending.Qty-prepared.Qty,
                   CASE WHEN i.ItemNo IS NULL THEN 'Item master missing' WHEN ISNULL(i.ActiveFlag,1)=0 THEN 'Item inactive'
                        WHEN m.ActiveFlag=0 THEN 'Vendor mapping inactive'
                        WHEN m.ItemNo IS NULL THEN 'Vendor mapping missing' WHEN COALESCE(m.PackingQty,0)<=0 THEN 'Packing quantity missing' ELSE '' END,
                   COALESCE(p.OrderQty,0),COALESCE(p.ReceivedQty,0),pending.Qty,prepared.Qty
            FROM dbo.WH_PurchaseOrder p
            JOIN dbo.MD_Item i ON i.ItemNo=p.ItemNo
            LEFT JOIN dbo.SCM_ItemVendor m ON m.ItemNo=p.ItemNo AND m.VendorID=p.VendorID
            OUTER APPLY(SELECT COALESCE(SUM(l.Quantity-l.ReceivedQty),0) Qty FROM dbo.SCM_DeliveryLine l
                JOIN dbo.SCM_Delivery d ON d.DeliveryID=l.DeliveryID WHERE l.PoID=p.PoID AND d.Status<>'Cancelled') pending
            OUTER APPLY(SELECT COALESCE(SUM(b.Quantity),0) Qty FROM dbo.SCM_DeliveryBox b
                WHERE b.PoID=p.PoID AND b.CaseNo IS NOT NULL AND b.DeliveryLineID IS NULL AND b.ActiveFlag=1) prepared
            WHERE p.PoNumber=@N AND p.Status IN('Open','Partial') AND p.SupplierConfirmedAt IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_PurchaseOrder d WHERE d.PoNumber=p.PoNumber
                  AND (d.SupplierConfirmedAt IS NULL OR d.Status IS NULL OR d.Status NOT IN('Open','Partial','Complete','Received')))
              AND EXISTS(SELECT 1 FROM dbo.SCM_PortalVendorUser u JOIN dbo.MD_Vendor v ON v.VendorID=u.VendorID
                  WHERE u.UserID=@U AND u.VendorID=p.VendorID AND u.ActiveFlag=1 AND u.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1)
            ORDER BY p.PoID;
            """, c, tx);
        Add(cmd, ("@N", order), ("@U", userId));
        using var r = cmd.ExecuteReader();
        var rows = new List<CasePart>();
        while (r.Read()) rows.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4), Math.Max(0,r.GetDecimal(5)),r.GetString(6),
            r.GetDecimal(7),r.GetDecimal(8),r.GetDecimal(9),r.GetDecimal(10)));
        return rows;
    }

    public string CreatePreparedCase(string order, IReadOnlyList<CaseBoxInput> boxes, string userId)
    {
        using var c=factory.OpenConnection();using var tx=c.BeginTransaction();
        var number=CreatePreparedCase(c,tx,order,boxes,userId);
        tx.Commit();return number;
    }

    static string CreatePreparedCase(SqlConnection c,SqlTransaction tx,string order,IReadOnlyList<CaseBoxInput> boxes,string userId)
        => CreatePreparedCase(c,tx,[order],boxes,userId);

    public string CreatePreparedCase(IReadOnlyList<string> orders,IReadOnlyList<CaseBoxInput> boxes,string userId)
    {
        using var c=factory.OpenConnection(); using var tx=c.BeginTransaction();
        var number=CreatePreparedCase(c,tx,orders,boxes,userId);
        tx.Commit(); return number;
    }

    static string CreatePreparedCase(SqlConnection c,SqlTransaction tx,IReadOnlyList<string> orders,IReadOnlyList<CaseBoxInput> boxes,string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || boxes.Count is < 1 or > 1000
            || boxes.Any(b => b.Quantity <= 0 || b.Quantity > 999999999.999m || decimal.Round(b.Quantity,3) != b.Quantity))
            throw new ArgumentException("Select 1 to 1,000 boxes with positive quantities.");
        if(orders.Count is <1 or >100 || orders.Any(string.IsNullOrWhiteSpace)
            || orders.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=orders.Count)
            throw new ArgumentException("Select 1 to 100 different purchase orders.");
        LockCaseOrders(c,tx,orders);
        var parts=orders.SelectMany(order=>ReadCaseParts(c,tx,order,userId)).ToDictionary(p=>p.PoID);
        foreach (var group in boxes.GroupBy(b=>b.PoID))
        {
            if (!parts.TryGetValue(group.Key,out var part) || group.Sum(b=>b.Quantity)>part.Available)
                throw new InvalidOperationException("Part unavailable or case quantity exceeds the remaining PO balance. Refresh the list.");
            if (part.PackingQty<=0 || group.Any(b=>b.Quantity>part.PackingQty))
                throw new InvalidOperationException("Check Packing Quantities. Each box must not exceed its packing quantity.");
        }
        var vendor=ValidateCaseDestination(c,tx,boxes.Select(b=>b.PoID).Distinct().ToArray());
        using var header = new SqlCommand("""
            DECLARE @Sequence varchar(20)=CONVERT(varchar(20),NEXT VALUE FOR dbo.SCM_CaseNumberSequence);
            DECLARE @CaseNo varchar(50)=CONCAT('CASE-',CASE WHEN LEN(@Sequence)<6 THEN RIGHT('000000'+@Sequence,6) ELSE @Sequence END);
            INSERT dbo.SCM_DeliveryCase(CaseNo,PoNumber,VendorID,CreatedBy)
            OUTPUT INSERTED.CaseNo,INSERTED.VendorID,CONVERT(date,INSERTED.CreatedAt)
            VALUES(@CaseNo,@N,@Vendor,@U);
            """,c,tx);
        Add(header,("@N",orders.Count==1?(object)orders[0]:DBNull.Value),("@Vendor",vendor),("@U",userId));
        string number; DateTime date;
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

    static Dictionary<string,Dictionary<int,string>> LockCaseOrders(SqlConnection c,SqlTransaction tx,IEnumerable<string> orders)
    {
        var result=new Dictionary<string,Dictionary<int,string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var order in orders.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            using var cmd=new SqlCommand("SELECT PoID,ScmRowVersion FROM dbo.WH_PurchaseOrder WITH(UPDLOCK,HOLDLOCK) WHERE PoNumber=@N",c,tx);
            Add(cmd,("@N",order)); using var r=cmd.ExecuteReader(); var versions=new Dictionary<int,string>();
            while(r.Read())versions.Add(r.GetInt32(0),Convert.ToHexString((byte[])r[1]));
            result.Add(order,versions);
        }
        return result;
    }

    static string ValidateCaseDestination(SqlConnection c,SqlTransaction tx,IReadOnlyList<int> poIds)
    {
        using var cmd=new SqlCommand("""
            SELECT DISTINCT p.VendorID,COALESCE(NULLIF(LTRIM(RTRIM(p.DeliveryDestination)),''),'')
            FROM dbo.WH_PurchaseOrder p JOIN OPENJSON(@Ids) j ON p.PoID=CONVERT(int,j.value);
            """,c,tx);
        Add(cmd,("@Ids",JsonSerializer.Serialize(poIds))); using var r=cmd.ExecuteReader();
        if(!r.Read())throw new InvalidOperationException("Purchase order items were not found.");
        var vendor=r.GetString(0);var destination=r.GetString(1);
        if(destination.Length==0 || r.Read())
            throw new InvalidOperationException("Select boxes for the same supplier and delivery destination.");
        return vendor;
    }

    public void CancelPreparedCase(string number,string userId)
    {
        using var c=factory.OpenConnection();using var tx=c.BeginTransaction();
        var orders=new List<string>();
        using(var lookup=new SqlCommand($"SELECT DISTINCT p.PoNumber FROM dbo.SCM_DeliveryCase k JOIN dbo.SCM_DeliveryBox b ON b.CaseNo=k.CaseNo JOIN dbo.WH_PurchaseOrder p ON p.PoID=b.PoID WHERE k.CaseNo=@N AND {CaseVendorAccess}",c,tx))
        {Add(lookup,("@N",number),("@U",userId));using var r=lookup.ExecuteReader();while(r.Read())orders.Add(r.GetString(0));}
        if(orders.Count==0)throw new InvalidOperationException("Case not found.");
        LockCaseOrders(c,tx,orders);
        using var cmd=new SqlCommand($"""
            IF NOT EXISTS(SELECT 1 FROM dbo.SCM_DeliveryCase k WITH(UPDLOCK,HOLDLOCK)
                WHERE k.CaseNo=@N AND k.DeliveryID IS NULL AND k.NoteID IS NULL AND {CaseVendorAccess})
                THROW 51441,'Only unassigned cases can be cancelled.',1;
            IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b JOIN dbo.WH_Inventory w
                ON w.LotNo COLLATE DATABASE_DEFAULT=b.BoxNumber COLLATE DATABASE_DEFAULT WHERE b.CaseNo=@N)
                THROW 51441,'Received cases cannot be cancelled here.',1;
            IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox WHERE CaseNo=@N AND DeliveryLineID IS NOT NULL)
                THROW 51441,'Only unassigned cases can be cancelled.',1;
            UPDATE dbo.SCM_DeliveryBox SET ActiveFlag=0,VoidedTS=SYSDATETIME() WHERE CaseNo=@N AND ActiveFlag=1;
            """,c,tx);
        Add(cmd,("@N",number),("@U",userId));cmd.ExecuteNonQuery();tx.Commit();
    }

    public List<DeliveryCase> ListDeliveryCases(string? order,string userId,string? note=null,string? delivery=null)
        => ReadDeliveryCases(null,order,userId,note,delivery);

    public DeliveryCase? GetDeliveryCase(string number,string? userId=null)
        => ReadDeliveryCases(number,null,userId,null).SingleOrDefault();

    public DeliveryNote CreateDeliveryNoteFromCases(IReadOnlyList<string> numbers,DateTime date,string userId,string actor)
    {
        using var c=factory.OpenConnection();using var tx=c.BeginTransaction();
        var note=CreateDeliveryNoteFromCases(c,tx,numbers,date,userId,actor);
        tx.Commit();return note;
    }

    public DeliveryNote CreateDeliveryNoteFromDraftCases(IReadOnlyList<CaseDraftInput> drafts,DateTime date,string userId,string actor)
    {
        if(drafts.Count is <1 or >1000 || drafts.Sum(d=>d.Boxes.Count)>1000)
            throw new ArgumentException("Select 1 to 1,000 cases with at most 1,000 boxes.");
        using var c=factory.OpenConnection();using var tx=c.BeginTransaction();
        // Lock every PO in a consistent order before reserving any draft case quantities.
        LockCaseOrders(c,tx,drafts.SelectMany(d=>d.Orders).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray());
        var numbers=drafts.Select(d=>CreatePreparedCase(c,tx,d.Orders,d.Boxes,userId)).ToArray();
        var note=CreateDeliveryNoteFromCases(c,tx,numbers,date,userId,actor);
        tx.Commit();return note;
    }

    DeliveryNote CreateDeliveryNoteFromCases(SqlConnection c,SqlTransaction tx,IReadOnlyList<string> numbers,DateTime date,string userId,string actor)
    {
        if(numbers.Count is <1 or >1000 || numbers.Any(n=>string.IsNullOrWhiteSpace(n)||n.Length>50)
            || numbers.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=numbers.Count)
            throw new ArgumentException("Select 1 to 1,000 different cases.");
        var selected=ReadDeliveryCases(c,tx,null,null,userId,null,null).Where(k=>numbers.Contains(k.Number,StringComparer.OrdinalIgnoreCase)).ToList();
        if(selected.Count!=numbers.Count)throw new InvalidOperationException("A case is unavailable or access was denied. Refresh the list.");
        // Exact retries return the issued note, without generating new deliveries or labels.
        var notes=selected.Select(c=>c.NoteNumber).Distinct().ToArray();
        if(notes.Length==1 && notes[0].Length>0)
        {
            var existing=ListDeliveryCases(null,userId,notes[0]);
            if(existing.Count==numbers.Count && existing.All(c=>numbers.Contains(c.Number,StringComparer.OrdinalIgnoreCase)))
                return GetDeliveryNote(notes[0],userId) ?? throw new InvalidOperationException("Delivery note is no longer available.");
        }
        if(selected.Any(c=>c.Status!="Prepared" || c.NoteNumber.Length>0 || c.Delivery is not null || c.Boxes.Any(b=>b.Received)))
            throw new InvalidOperationException("Select only unassigned, unreceived cases.");
        var boxes=selected.SelectMany(c=>c.Boxes).ToArray();
        var orders=boxes.Select(b=>b.Order).Distinct().Order(StringComparer.Ordinal).ToArray();
        if(orders.Length>100 || boxes.Length>1000)throw new InvalidOperationException("A note may contain at most 100 POs and 1,000 boxes.");
        var versions=LockCaseOrders(c,tx,orders);
        ValidateCaseDestination(c,tx,boxes.Select(b=>b.PoID).Distinct().ToArray());
        using(var guard=new SqlCommand($"""
            IF (SELECT COUNT(*) FROM dbo.SCM_DeliveryCase k WITH(UPDLOCK,HOLDLOCK)
                JOIN OPENJSON(@Cases) j ON k.CaseNo=CONVERT(varchar(50),j.value)
                WHERE k.DeliveryID IS NULL AND k.NoteID IS NULL AND {CaseVendorAccess})<>@Count
                THROW 51441,'A case has changed or is already assigned. Refresh the list.',1;
            IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b JOIN OPENJSON(@Cases) j ON b.CaseNo=CONVERT(varchar(50),j.value)
                WHERE b.ActiveFlag=0 OR b.DeliveryLineID IS NOT NULL)
                THROW 51441,'A case has been cancelled or assigned. Refresh the list.',1;
            """,c,tx))
        {Add(guard,("@Cases",JsonSerializer.Serialize(numbers)),("@Count",numbers.Count),("@U",userId));guard.ExecuteNonQuery();}
        var deliveries=new List<string>();
        foreach(var order in orders)
        {
            var inputs=boxes.Where(b=>b.Order==order).GroupBy(b=>b.PoID).Select(g=>new DeliveryInput(g.Key,g.Sum(b=>b.Quantity))).ToArray();
            var cases=selected.Where(k=>k.Boxes.Any(b=>b.Order==order)).Select(k=>k.Number).ToArray();
            deliveries.Add(RegisterSupplierDelivery(c,tx,order,date,inputs,versions[order],Guid.NewGuid(),userId,actor,cases,caseBatch:true));
        }
        var note=IssueDeliveryNote(c,tx,deliveries,userId,actor);
        using(var link=new SqlCommand("""
            UPDATE k SET NoteID=n.NoteID,DeliveryID=x.DeliveryID
            FROM dbo.SCM_DeliveryCase k JOIN OPENJSON(@Cases) j ON k.CaseNo=CONVERT(varchar(50),j.value)
            CROSS JOIN dbo.SCM_DeliveryNote n
            CROSS APPLY(SELECT CASE WHEN COUNT(DISTINCT l.DeliveryID)=1 THEN MIN(l.DeliveryID) END DeliveryID
                FROM dbo.SCM_DeliveryBox b JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID
                WHERE b.CaseNo=k.CaseNo AND b.ActiveFlag=1) x
            WHERE n.NoteNumber=@Note;
            """,c,tx))
        {Add(link,("@Cases",JsonSerializer.Serialize(numbers)),("@Note",note.Number));link.ExecuteNonQuery();}
        return note;
    }

    List<DeliveryCase> ReadDeliveryCases(string? number,string? order,string? userId,string? note,string? delivery=null)
    {
        using var c=factory.OpenConnection();
        return ReadDeliveryCases(c,null,number,order,userId,note,delivery);
    }

    static List<DeliveryCase> ReadDeliveryCases(SqlConnection c,SqlTransaction? tx,string? number,string? order,string? userId,string? note,string? delivery)
    {
        using var cmd=new SqlCommand($"""
            SELECT k.CaseNo,COALESCE(n.NoteNumber,''),k.VendorID,COALESCE(v.VendorName,k.VendorID),k.CreatedAt,
                   COALESCE(k.PoNumber,''),d.DeliveryNumber,
                   COALESCE(state.Status,d.Status,CASE WHEN k.NoteID IS NOT NULL THEN 'Shipped' ELSE 'Prepared' END),
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
            OUTER APPLY(SELECT CASE WHEN COUNT(*)=0 THEN NULL
                WHEN SUM(CASE WHEN sd.Status='Cancelled' THEN 1 ELSE 0 END)>0 THEN 'Cancelled'
                WHEN SUM(CASE WHEN sd.Status='Registered' THEN 1 ELSE 0 END)>0 THEN 'Registered'
                WHEN SUM(CASE WHEN sd.Status='Received' THEN 1 ELSE 0 END)=COUNT(*) THEN 'Received'
                ELSE 'Shipped' END Status
                FROM dbo.SCM_DeliveryBox cb JOIN dbo.SCM_DeliveryLine cl ON cl.DeliveryLineID=cb.DeliveryLineID
                JOIN dbo.SCM_Delivery sd ON sd.DeliveryID=cl.DeliveryID WHERE cb.CaseNo=k.CaseNo AND cb.ActiveFlag=1) state
            WHERE (@N IS NULL OR k.CaseNo=@N) AND (@Order IS NULL OR EXISTS(
                SELECT 1 FROM dbo.SCM_DeliveryBox cb JOIN dbo.WH_PurchaseOrder cp ON cp.PoID=cb.PoID
                WHERE cb.CaseNo=k.CaseNo AND cb.ActiveFlag=1 AND cp.PoNumber=@Order))
              AND (@Note IS NULL OR n.NoteNumber=@Note)
              AND (@Delivery IS NULL OR EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox cb
                  JOIN dbo.SCM_DeliveryLine cl ON cl.DeliveryLineID=cb.DeliveryLineID
                  JOIN dbo.SCM_Delivery sd ON sd.DeliveryID=cl.DeliveryID
                  WHERE cb.CaseNo=k.CaseNo AND cb.ActiveFlag=1 AND sd.DeliveryNumber=@Delivery))
              {(userId is null ? "" : $"AND {CaseVendorAccess}")}
            ORDER BY k.CreatedAt DESC,k.CaseNo DESC,b.BoxID;
            """,c,tx);
        Add(cmd,("@N",(object?)number??DBNull.Value),("@Order",(object?)order??DBNull.Value),("@Note",(object?)note??DBNull.Value),("@Delivery",(object?)delivery??DBNull.Value),("@U",userId??""));
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
        return cases.Values.Select(item=>item with {Order=string.Join(", ",item.Boxes.Select(b=>b.Order).Distinct())}).ToList();
    }

    // Caller already holds the PO lock used by all delivery registrations.
    static void ValidatePreparedCases(SqlConnection c,SqlTransaction tx,string order,IReadOnlyList<string> cases,
        IReadOnlyList<DeliveryInput> items,bool mixed=false)
    {
        if(cases.Count is <1 or >1000
            || cases.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=cases.Count)
            throw new ArgumentException("Select different prepared cases.");
        using var cmd=new SqlCommand("""
            DECLARE @Cases TABLE(CaseNo varchar(50) PRIMARY KEY);
            INSERT @Cases SELECT CONVERT(varchar(50),value) FROM OPENJSON(@CasesJson);
            IF (SELECT COUNT(*) FROM dbo.SCM_DeliveryCase k WITH(UPDLOCK,HOLDLOCK) JOIN @Cases s ON s.CaseNo=k.CaseNo
                WHERE (@Mixed=1 OR k.PoNumber=@N) AND k.DeliveryID IS NULL AND k.NoteID IS NULL)<>(SELECT COUNT(*) FROM @Cases)
                THROW 51441,'A case is already assigned or belongs to another PO. Refresh the list.',1;
            IF EXISTS(SELECT 1 FROM @Cases s WHERE NOT EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b
                      WHERE b.CaseNo=s.CaseNo AND b.ActiveFlag=1))
                THROW 51441,'A case has been cancelled. Refresh the list.',1;
            IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b JOIN @Cases s ON s.CaseNo=b.CaseNo
                LEFT JOIN dbo.WH_PurchaseOrder p ON p.PoID=b.PoID
                WHERE b.ActiveFlag=1 AND (@Mixed=0 OR p.PoNumber=@N) AND (b.DeliveryLineID IS NOT NULL OR p.PoNumber<>@N OR p.PoID IS NULL
                  OR EXISTS(SELECT 1 FROM dbo.WH_Inventory w WHERE w.LotNo COLLATE DATABASE_DEFAULT=b.BoxNumber COLLATE DATABASE_DEFAULT)))
                THROW 51441,'A case contains unavailable boxes.',1;
            SELECT b.PoID,SUM(b.Quantity) Qty FROM dbo.SCM_DeliveryBox b
            JOIN @Cases s ON s.CaseNo=b.CaseNo JOIN dbo.WH_PurchaseOrder p ON p.PoID=b.PoID
            WHERE b.ActiveFlag=1 AND p.PoNumber=@N GROUP BY b.PoID;
            """,c,tx);
        Add(cmd,("@N",order),("@CasesJson",JsonSerializer.Serialize(cases)),("@Mixed",mixed));
        var totals=new Dictionary<int,decimal>();
        using(var r=cmd.ExecuteReader())while(r.Read()) totals.Add(r.GetInt32(0),r.GetDecimal(1));
        if(totals.Count!=items.Count || items.Any(i=>!totals.TryGetValue(i.PoID,out var q)||q!=i.Quantity))
            throw new InvalidOperationException("Delivery quantities must match the selected cases.");
    }

    static void AttachPreparedCases(SqlConnection c,SqlTransaction tx,int deliveryId,IReadOnlyList<string> cases,bool updateHeader=true)
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
            IF @Header=1 UPDATE k SET DeliveryID=@ID FROM dbo.SCM_DeliveryCase k JOIN @Cases s ON s.CaseNo=k.CaseNo;
            UPDATE l SET PackingQty=x.Pack FROM dbo.SCM_DeliveryLine l
            CROSS APPLY(SELECT MIN(b.PackingQty) Pack FROM dbo.SCM_DeliveryBox b WHERE b.DeliveryLineID=l.DeliveryLineID AND b.ActiveFlag=1) x
            WHERE l.DeliveryID=@ID;
            """,c,tx);
        Add(cmd,("@ID",deliveryId),("@CasesJson",JsonSerializer.Serialize(cases)),("@Header",updateHeader));cmd.ExecuteNonQuery();
    }

    public int ReceiveDeliveryCase(string caseNo,string mode,string actor,IReadOnlyCollection<string>? scannedBoxes = null)
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
            SELECT b.BoxNumber, ISNULL(i.ScanRequired,1)
            FROM dbo.SCM_DeliveryBox b WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.MD_Item i WITH(HOLDLOCK) ON i.ItemNo COLLATE DATABASE_DEFAULT=b.ItemNo COLLATE DATABASE_DEFAULT
            WHERE b.CaseNo=@N AND b.ActiveFlag=1 AND NOT EXISTS(
                SELECT 1 FROM dbo.WH_Inventory w WITH(UPDLOCK,HOLDLOCK)
                WHERE w.LotNo COLLATE DATABASE_DEFAULT=b.BoxNumber COLLATE DATABASE_DEFAULT)
            ORDER BY b.BoxID;
            """,c,tx);
        Add(select,("@N",caseNo));var boxes=new List<string>();
        var scanned = new HashSet<string>(scannedBoxes ?? [], StringComparer.OrdinalIgnoreCase);
        using(var r=select.ExecuteReader())while(r.Read())
        {
            var barcode=r.GetString(0);
            if(r.GetBoolean(1) && !scanned.Contains(barcode))
                throw new InvalidOperationException($"Scan required box {barcode} before receiving this case.");
            boxes.Add(barcode);
        }
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
