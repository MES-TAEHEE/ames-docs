using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public record PurchaseLine(int Id, string Item, string Name, string Unit, decimal Quantity,
        decimal Price, decimal Received, int PoID = 0, string Version = "");
    public record PurchaseOrder(string Number, string Vendor, DateTime Ordered, DateTime Due,
        string Destination, string Status, string Currency, List<PurchaseLine> Lines, string VendorName = "");

    public List<PurchaseOrder> ListPurchaseOrders(bool portal = false, string? portalUserId = null, bool internalAdminPreview = false)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT p.PoID,p.PoNumber,p.PoLineNo,p.VendorID,p.ItemNo,i.ItemName,p.UnitCode,
                   p.OrderQty,p.UnitPrice,p.ReceivedQty,p.OrderDate,p.DueDate,p.Status,
                   p.Currency,p.DeliveryDestination,p.ScmRowVersion,v.VendorName
            FROM dbo.WH_PurchaseOrder p LEFT JOIN dbo.MD_Item i ON i.ItemNo=p.ItemNo
            LEFT JOIN dbo.MD_Vendor v ON v.VendorID=p.VendorID
            WHERE NULLIF(p.PoNumber,'') IS NOT NULL
              AND (@Portal=0 OR (
                  p.Status IN ('Open','Partial','Complete','Received','Cancelled')
                  AND NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder d WHERE d.PoNumber=p.PoNumber AND (d.Status='Draft' OR d.Status IS NULL))
                  AND (@AdminPreview=1 OR (
                      ISNULL(v.ActiveFlag,1)=1 AND EXISTS (
                          SELECT 1 FROM dbo.SCM_PortalVendorUser u
                          WHERE u.UserID=@UserID AND u.VendorID=p.VendorID AND u.ActiveFlag=1
                      )
                  ))
              ))
            ORDER BY p.PoNumber DESC,p.PoLineNo,p.PoID;
            """, conn);
        Add(cmd,("@Portal",portal),("@AdminPreview",internalAdminPreview),("@UserID",portalUserId ?? ""));
        using var r = cmd.ExecuteReader();
        var orders = new Dictionary<string, PurchaseOrder>();
        while (r.Read())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            decimal D(int i) => r.IsDBNull(i) ? 0 : r.GetDecimal(i);
            var number = S(1);
            if (!orders.TryGetValue(number, out var order))
            {
                order = new(number, S(3), r.IsDBNull(10) ? DateTime.MinValue : r.GetDateTime(10),
                    r.IsDBNull(11) ? DateTime.MinValue : r.GetDateTime(11), S(14), S(12), S(13), [], S(16));
                orders.Add(number, order);
            }
            // Mixed legacy line states are displayed as an in-progress order until every line is complete.
            if (order.Status != S(12)) orders[number] = order = order with { Status = "Partial" };
            order.Lines.Add(new(r.IsDBNull(2) ? r.GetInt32(0) : r.GetInt32(2), S(4), S(5), S(6),
                D(7), D(8), D(9), r.GetInt32(0), Convert.ToHexString((byte[])r[15])));
        }
        return orders.Values.ToList();
    }

    public string SavePurchaseOrder(PurchaseOrder order, IReadOnlyDictionary<int,string> expectedVersions, string actor)
    {
        ValidateOrderValues(order);
        var number = string.IsNullOrEmpty(order.Number) ? ReservePurchaseOrderNumber() : order.Number;
        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction();
        var existing = LockPurchaseOrder(conn, tx, number, expectedVersions);
        if (existing.Any(x => x.Status != "Draft" || x.Received != 0))
            throw new InvalidOperationException("Only unreceived drafts may be edited.");
        if (existing.Count > 0) EnsureNoInboundReferences(conn, tx, number);
        ValidateOrderMasters(conn, tx, order);
        foreach (var line in order.Lines)
        {
            using var cmd = new SqlCommand("""
                UPDATE dbo.WH_PurchaseOrder
                SET VendorID=@Vendor,ItemNo=@Item,OrderQty=@Qty,UnitCode=@Unit,UnitPrice=@Price,
                    OrderDate=@Ordered,DueDate=@Due,DeliveryDestination=@Destination,
                    ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
                WHERE PoNumber=@Number AND PoLineNo=@Line;
                IF @@ROWCOUNT=0
                    INSERT dbo.WH_PurchaseOrder(PoNumber,PoLineNo,VendorID,ItemNo,OrderQty,ReceivedQty,
                        UnitCode,UnitPrice,Currency,OrderDate,DueDate,Status,DeliveryDestination,CreatedBy,CreatedTS)
                    VALUES(@Number,@Line,@Vendor,@Item,@Qty,0,@Unit,@Price,'USD',@Ordered,@Due,'Draft',@Destination,@Actor,SYSDATETIME());
                """, conn, tx);
            Add(cmd, ("@Number",number),("@Line",line.Id),("@Vendor",order.Vendor),("@Item",line.Item),
                ("@Qty",line.Quantity),("@Unit",line.Unit),("@Price",line.Price),("@Ordered",order.Ordered.Date),
                ("@Due",order.Due.Date),("@Destination",order.Destination.Trim()),("@Actor",actor));
            cmd.ExecuteNonQuery();
        }
        foreach (var removed in existing.Where(x => !order.Lines.Any(l => l.Id == x.LineNo)))
        {
            using var cmd = new SqlCommand("DELETE dbo.WH_PurchaseOrder WHERE PoID=@Id",conn,tx);
            Add(cmd,("@Id",removed.Id)); cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return number;
    }

    public void ChangePurchaseOrderStatus(PurchaseOrder order, IReadOnlyDictionary<int,string> expectedVersions, string status, string actor)
    {
        if (status is not ("Open" or "Cancelled")) throw new ArgumentException("Unsupported status.");
        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction();
        var existing = LockPurchaseOrder(conn, tx, order.Number, expectedVersions);
        if (existing.Count == 0 || existing.Any(x => x.Received != 0 ||
            (status == "Open" ? x.Status != "Draft" : x.Status is not ("Draft" or "Open"))))
            throw new InvalidOperationException("Order state changed or receipts already exist.");
        EnsureNoInboundReferences(conn, tx, order.Number);
        if (status == "Open") { ValidateOrderValues(order); ValidateOrderMasters(conn, tx, order); }
        using var cmd = new SqlCommand("UPDATE dbo.WH_PurchaseOrder SET Status=@Status,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE PoNumber=@Number",conn,tx);
        Add(cmd,("@Status",status),("@Actor",actor),("@Number",order.Number));
        cmd.ExecuteNonQuery(); tx.Commit();
    }

    record LockedLine(int Id,int LineNo,string Status,decimal Received);
    static List<LockedLine> LockPurchaseOrder(SqlConnection conn, SqlTransaction tx, string number, IReadOnlyDictionary<int,string> versions)
    {
        using var cmd = new SqlCommand("SELECT PoID,PoLineNo,Status,ISNULL(ReceivedQty,0),ScmRowVersion FROM dbo.WH_PurchaseOrder WITH (UPDLOCK,HOLDLOCK) WHERE PoNumber=@Number",conn,tx);
        Add(cmd,("@Number",number));
        using var r = cmd.ExecuteReader();
        var result = new List<LockedLine>();
        while(r.Read())
        {
            var id = r.GetInt32(0);
            if (!versions.TryGetValue(id,out var version) || version != Convert.ToHexString((byte[])r[4]))
                throw new InvalidOperationException("Order changed. Reload before editing.");
            result.Add(new(id,r.IsDBNull(1) ? id : r.GetInt32(1),r.IsDBNull(2) ? "" : r.GetString(2),r.GetDecimal(3)));
        }
        if (result.Count != versions.Count) throw new InvalidOperationException("Order lines changed. Reload before editing.");
        return result;
    }

    static void EnsureNoInboundReferences(SqlConnection conn, SqlTransaction tx, string number)
    {
        using var cmd = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.WH_InboundPackage b WITH (HOLDLOCK)
            JOIN dbo.WH_PurchaseOrder p ON p.PoID=b.PoID WHERE p.PoNumber=@Number;
            """,conn,tx);
        Add(cmd,("@Number",number));
        if ((int)cmd.ExecuteScalar()! > 0) throw new InvalidOperationException("Order is referenced by inbound packages.");
    }

    static void ValidateOrderValues(PurchaseOrder order)
    {
        if (order.Currency != "USD" || string.IsNullOrWhiteSpace(order.Destination) || order.Destination.Length > 200 ||
            order.Ordered.Year < 1900 || order.Due < order.Ordered || order.Lines.Count == 0 ||
            order.Lines.Select(x => x.Id).Distinct().Count() != order.Lines.Count || order.Lines.Any(x => x.Id <= 0 ||
                x.Quantity <= 0 || x.Quantity > 999999999.999m || decimal.Round(x.Quantity,3) != x.Quantity ||
                x.Price < 0 || x.Price > 9999999999.9999m || decimal.Round(x.Price,4) != x.Price))
            throw new ArgumentException("Invalid order fields, quantity or price precision.");
    }

    static void ValidateOrderMasters(SqlConnection conn, SqlTransaction tx, PurchaseOrder order)
    {
        foreach(var line in order.Lines)
        {
            using var cmd = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.SCM_ItemVendor m WITH (HOLDLOCK)
                JOIN dbo.MD_Item i WITH (HOLDLOCK) ON i.ItemNo=m.ItemNo
                JOIN dbo.MD_Vendor v WITH (HOLDLOCK) ON v.VendorID=m.VendorID
                WHERE m.VendorID=@Vendor AND m.ItemNo=@Item AND m.ActiveFlag=1
                  AND i.ItemType='MATERIAL' AND ISNULL(v.ActiveFlag,1)=1
                  AND ISNULL(i.DefaultUOM,'')=@Unit;
                """,conn,tx);
            Add(cmd,("@Vendor",order.Vendor),("@Item",line.Item),("@Unit",line.Unit));
            if ((int)cmd.ExecuteScalar()! != 1) throw new InvalidOperationException("Vendor/item mapping or unit changed. Reload before saving.");
        }
    }

    static void Add(SqlCommand cmd, params (string Name, object Value)[] values)
    { foreach(var (name,value) in values) cmd.Parameters.AddWithValue(name,value); }
}
