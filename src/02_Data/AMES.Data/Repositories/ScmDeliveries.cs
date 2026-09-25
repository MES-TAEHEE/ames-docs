using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public record DeliveryInput(int PoID, decimal Quantity, string? VendorLotNo=null, DateTime? ProductionDate=null);
    public sealed class PackingQuantityRequiredException() : InvalidOperationException("A positive packing quantity is required for every selected item.");
    public record SupplierDelivery(string Number, string OrderNumber, int PoID, int LineId,
        string Item, string Unit, DateTime Date, decimal Quantity, decimal Received, string Status, string Version, DateTime? ShipDate, DateTime? ShippedAt, string? ShippedBy, string VendorLotNo, DateTime ProductionDate);

    public List<SupplierDelivery> ListSupplierDeliveries(bool portal, string? userId, bool adminPreview)
    {
        using var conn=factory.OpenConnection();
        using var cmd=new SqlCommand("""
            SELECT d.DeliveryNumber,d.PoNumber,p.PoID,ISNULL(p.PoLineNo,p.PoID),p.ItemNo,ISNULL(p.UnitCode,''),
                d.DeliveryDate,l.Quantity,l.ReceivedQty,d.Status,d.Version,d.ShipDate,d.ShippedAt,d.ShippedBy,
                COALESCE(l.VendorLotNo,CONVERT(char(8),d.DeliveryDate,112)),COALESCE(l.ProductionDate,d.DeliveryDate)
            FROM dbo.SCM_Delivery d JOIN dbo.SCM_DeliveryLine l ON l.DeliveryID=d.DeliveryID
            JOIN dbo.WH_PurchaseOrder p ON p.PoID=l.PoID
            WHERE @Portal=0 OR @Admin=1 OR EXISTS(
                SELECT 1 FROM dbo.SCM_PortalVendorUser m JOIN dbo.MD_Vendor v ON v.VendorID=m.VendorID
                WHERE m.UserID=@User AND m.VendorID=d.VendorID AND m.ActiveFlag=1 AND m.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1)
            ORDER BY d.DeliveryID DESC,l.DeliveryLineID
            """,conn);
        Add(cmd,("@Portal",portal),("@Admin",adminPreview),("@User",userId??""));
        using var r=cmd.ExecuteReader(); var result=new List<SupplierDelivery>();
        while(r.Read()) result.Add(new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4),r.GetString(5),r.GetDateTime(6),r.GetDecimal(7),r.GetDecimal(8),r.GetString(9),Convert.ToHexString((byte[])r[10]),r.IsDBNull(11)?null:r.GetDateTime(11),r.IsDBNull(12)?null:r.GetDateTime(12),r.IsDBNull(13)?null:r.GetString(13),r.GetString(14),r.GetDateTime(15)));
        return result;
    }

    public string RegisterSupplierDelivery(string number, DateTime date, IReadOnlyList<DeliveryInput> items,
        IReadOnlyDictionary<int,string> versions, Guid requestId, string userId, string actor, bool adminOnBehalf=false)
    {
        if(requestId==Guid.Empty || items.Count==0 || items.Select(x=>x.PoID).Distinct().Count()!=items.Count ||
            items.Any(x=>x.Quantity<=0 || x.Quantity>999999999.999m || decimal.Round(x.Quantity,3)!=x.Quantity))
            throw new ArgumentException("Select lines and positive quantities with up to three decimal places.");
        ValidateDeliveryTrace(items);
        using var conn=factory.OpenConnection(); using var tx=conn.BeginTransaction();
        // Serialize all registrations and cancellations for this PO before checking balances.
        using(var gate=new SqlCommand("SELECT PoID FROM dbo.WH_PurchaseOrder WITH(UPDLOCK,HOLDLOCK) WHERE PoNumber=@N",conn,tx))
        { Add(gate,("@N",number)); using var reader=gate.ExecuteReader(); while(reader.Read()){} }
        using(var auth=new SqlCommand("""
            IF @Admin=1 AND NOT EXISTS(SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId
                WHERE ur.UserId=@U AND r.Name='Admin')
                THROW 50031,'No delivery permission.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber=@N)
                THROW 50032,'Order not found.',1;
            IF (SELECT COUNT(DISTINCT VendorID) FROM dbo.WH_PurchaseOrder WHERE PoNumber=@N)<>1
                THROW 50032,'Order must belong to one vendor.',1;
            IF @Admin=0 AND EXISTS(SELECT 1 FROM dbo.WH_PurchaseOrder p WHERE p.PoNumber=@N AND NOT EXISTS(
                SELECT 1 FROM dbo.SCM_PortalVendorUser m WITH(HOLDLOCK)
                JOIN dbo.MD_Vendor v WITH(HOLDLOCK) ON v.VendorID=m.VendorID
                WHERE m.UserID=@U AND m.VendorID=p.VendorID AND m.ActiveFlag=1 AND m.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1))
                THROW 50031,'Vendor access denied.',1;
            SELECT DeliveryNumber FROM dbo.SCM_Delivery WHERE RequestID=@Request AND PoNumber=@N AND CreatedUserID=@U;
            """,conn,tx))
        {
            Add(auth,("@N",number),("@U",userId),("@Admin",adminOnBehalf),("@Request",requestId));
            if(auth.ExecuteScalar() is string previous){tx.Commit();return previous;}
        }
        var locked=LockPurchaseOrder(conn,tx,number,versions);
        if(locked.Any(x=>x.Status is not ("Open" or "Partial" or "Complete" or "Received")))
            throw new InvalidOperationException("Order is not issued.");
        using(var confirmed=new SqlCommand("""
            SELECT COUNT(*) FROM dbo.WH_PurchaseOrder WHERE PoNumber=@N
              AND (SupplierConfirmedAt IS NULL OR @Date<OrderDate OR @Date<CONVERT(date,SYSDATETIME()));
            """,conn,tx))
        {Add(confirmed,("@N",number),("@Date",date.Date));if((int)confirmed.ExecuteScalar()!>0)throw new InvalidOperationException("Confirm the order and check the delivery date.");}
        foreach(var item in items)
        {
            using(var packing=new SqlCommand("""
                SELECT m.PackingQty FROM dbo.WH_PurchaseOrder p
                JOIN dbo.SCM_ItemVendor m WITH(HOLDLOCK) ON m.ItemNo=p.ItemNo AND m.VendorID=p.VendorID
                WHERE p.PoID=@ID AND p.PoNumber=@N AND m.ActiveFlag=1 AND m.PackingQty>0;
                """,conn,tx))
            {
                Add(packing,("@ID",item.PoID),("@N",number));
                if(packing.ExecuteScalar() is not decimal) throw new PackingQuantityRequiredException();
            }
            using var available=new SqlCommand("""
                SELECT p.OrderQty-ISNULL(p.ReceivedQty,0)-ISNULL((
                    SELECT SUM(l.Quantity-l.ReceivedQty) FROM dbo.SCM_DeliveryLine l
                    JOIN dbo.SCM_Delivery d ON d.DeliveryID=l.DeliveryID
                    WHERE l.PoID=p.PoID AND d.Status<>'Cancelled'),0)
                FROM dbo.WH_PurchaseOrder p WHERE p.PoID=@ID AND p.PoNumber=@N AND p.Status IN ('Open','Partial');
                """,conn,tx);
            Add(available,("@ID",item.PoID),("@N",number));
            if(available.ExecuteScalar() is not decimal remaining || item.Quantity>remaining)
                throw new InvalidOperationException("Line unavailable or delivery quantity exceeds remaining balance.");
        }
        // Temporary unique number is replaced in the same transaction with the generated identity.
        using var header=new SqlCommand("""
            INSERT dbo.SCM_Delivery(DeliveryNumber,RequestID,PoNumber,VendorID,DeliveryDate,CreatedBy,CreatedUserID)
            OUTPUT INSERTED.DeliveryID
            SELECT @Temp,@Request,@N,MIN(VendorID),@Date,@Actor,@User FROM dbo.WH_PurchaseOrder WHERE PoNumber=@N;
            """,conn,tx);
        Add(header,("@Temp",Guid.NewGuid().ToString("N")[..30]),("@Request",requestId),("@N",number),("@Date",date.Date),("@Actor",actor),("@User",userId));
        var id=(int)header.ExecuteScalar()!;
        using var assign=new SqlCommand("UPDATE dbo.SCM_Delivery SET DeliveryNumber=CONCAT('DN-',CONVERT(char(8),CreatedTS,112),'-',@ID) OUTPUT INSERTED.DeliveryNumber WHERE DeliveryID=@ID",conn,tx);
        Add(assign,("@ID",id)); var deliveryNumber=(string)assign.ExecuteScalar()!;
        foreach(var item in items)
        {
            using var line=new SqlCommand("INSERT dbo.SCM_DeliveryLine(DeliveryID,PoID,Quantity,VendorLotNo,ProductionDate) VALUES(@D,@P,@Q,@Lot,@Prod)",conn,tx);
            Add(line,("@D",id),("@P",item.PoID),("@Q",item.Quantity),("@Lot",DeliveryLot(item,date)),("@Prod",(item.ProductionDate??date).Date));line.ExecuteNonQuery();
        }
        SyncDeliveryBoxes(conn,tx,id);
        using var touch=new SqlCommand("UPDATE dbo.WH_PurchaseOrder SET ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE PoNumber=@N",conn,tx);
        Add(touch,("@Actor",actor),("@N",number));touch.ExecuteNonQuery();
        tx.Commit(); return deliveryNumber;
    }
    public void UpdateSupplierDelivery(string deliveryNumber, DateTime date, IReadOnlyList<DeliveryInput> items,
        string version, IReadOnlyDictionary<int,string> orderVersions, string userId, string actor,
        bool cancel=false, bool adminOnBehalf=false, bool ship=false)
    {
        if(ship && cancel) throw new ArgumentException("Invalid shipping date or operation.");
        if(!cancel && (items.Count==0 || items.Select(x=>x.PoID).Distinct().Count()!=items.Count ||
            items.Any(x=>x.Quantity<=0 || x.Quantity>999999999.999m || decimal.Round(x.Quantity,3)!=x.Quantity)))
            throw new ArgumentException("Invalid delivery quantities.");
        ValidateDeliveryTrace(items);
        using var conn=factory.OpenConnection(); using var tx=conn.BeginTransaction();
        string number;
        using(var lookup=new SqlCommand("SELECT PoNumber FROM dbo.SCM_Delivery WHERE DeliveryNumber=@D",conn,tx))
        {Add(lookup,("@D",deliveryNumber));number=lookup.ExecuteScalar() as string ?? throw new InvalidOperationException("Delivery not found.");}
        var locked=LockPurchaseOrder(conn,tx,number,orderVersions);
        using var check=new SqlCommand("""
            IF @Admin=1 AND NOT EXISTS(SELECT 1 FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetRoles r ON r.Id=ur.RoleId
                WHERE ur.UserId=@U AND r.Name='Admin')
                THROW 50031,'No delivery permission.',1;
            IF @Admin=0 AND EXISTS(SELECT 1 FROM dbo.SCM_Delivery d WHERE d.DeliveryNumber=@D AND NOT EXISTS(
                SELECT 1 FROM dbo.SCM_PortalVendorUser m WITH(HOLDLOCK) JOIN dbo.MD_Vendor v WITH(HOLDLOCK) ON v.VendorID=m.VendorID
                WHERE m.UserID=@U AND m.VendorID=d.VendorID AND m.ActiveFlag=1 AND m.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1))
                THROW 50031,'Vendor access denied.',1;
            SELECT DeliveryID,Version,Status FROM dbo.SCM_Delivery WITH(UPDLOCK,HOLDLOCK) WHERE DeliveryNumber=@D;
            """,conn,tx);
        Add(check,("@D",deliveryNumber),("@U",userId),("@Admin",adminOnBehalf));
        int id;
        using(var r=check.ExecuteReader())
        {
            if(!r.Read() || Convert.ToHexString((byte[])r[1])!=version || r.GetString(2)!="Registered")
                throw new InvalidOperationException("Delivery changed or is no longer editable. Reload.");
            id=r.GetInt32(0);
        }
        using(var inbound=new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.SCM_DeliveryLine WITH(UPDLOCK,HOLDLOCK) WHERE DeliveryID=@ID AND ReceivedQty>0)
                 + (SELECT COUNT(*) FROM dbo.WH_InboundPackage WITH(HOLDLOCK) WHERE DocumentNo=@D OR DocumentBarcode=@D);
            """,conn,tx))
        {Add(inbound,("@ID",id),("@D",deliveryNumber));if((int)inbound.ExecuteScalar()!>0)throw new InvalidOperationException("Delivery is linked to receiving.");}
        if(!cancel)
        {
            if(locked.Count==0 || locked.Any(x=>x.Status is not ("Open" or "Partial" or "Complete" or "Received")))
                throw new InvalidOperationException("Order is no longer issued.");
            using(var dates=new SqlCommand("SELECT COUNT(*) FROM dbo.WH_PurchaseOrder WHERE PoNumber=@N AND (SupplierConfirmedAt IS NULL OR @Date<OrderDate OR (@Ship=0 AND @Date<CONVERT(date,SYSDATETIME())) OR (@Ship=1 AND @Date>CONVERT(date,SYSDATETIME())))",conn,tx))
            {Add(dates,("@N",number),("@Date",date.Date),("@Ship",ship));if((int)dates.ExecuteScalar()!>0)throw new InvalidOperationException("Invalid date or unconfirmed order.");}
            var existing=new HashSet<int>();
            using(var lineIds=new SqlCommand("SELECT PoID FROM dbo.SCM_DeliveryLine WHERE DeliveryID=@ID",conn,tx))
            {Add(lineIds,("@ID",id));using var r=lineIds.ExecuteReader();while(r.Read())existing.Add(r.GetInt32(0));}
            if(!existing.SetEquals(items.Select(x=>x.PoID)))throw new InvalidOperationException("Delivery lines changed. Reload.");
            foreach(var item in items)
            {
                using var balance=new SqlCommand("""
                    SELECT p.OrderQty-ISNULL(p.ReceivedQty,0)-ISNULL((
                        SELECT SUM(l.Quantity-l.ReceivedQty) FROM dbo.SCM_DeliveryLine l JOIN dbo.SCM_Delivery d ON d.DeliveryID=l.DeliveryID
                        WHERE l.PoID=p.PoID AND d.Status<>'Cancelled' AND d.DeliveryID<>@ID),0)
                    FROM dbo.WH_PurchaseOrder p JOIN dbo.SCM_Delivery d ON d.DeliveryID=@ID AND d.VendorID=p.VendorID
                    WHERE p.PoID=@P AND p.PoNumber=@N AND p.Status IN ('Open','Partial');
                    """,conn,tx);
                Add(balance,("@ID",id),("@P",item.PoID),("@N",number));
                if(balance.ExecuteScalar() is not decimal available || item.Quantity>available)
                    throw new InvalidOperationException("Quantity exceeds available balance.");
                using var update=new SqlCommand("""
                    IF @Ship=1 AND NOT EXISTS(SELECT 1 FROM dbo.SCM_DeliveryLine WHERE DeliveryID=@ID AND PoID=@P AND Quantity=@Q)
                        THROW 50032,'Ship only saved delivery quantities. Reload.',1;
                    IF @Ship=0 UPDATE dbo.SCM_DeliveryLine SET Quantity=@Q,VendorLotNo=@Lot,ProductionDate=@Prod WHERE DeliveryID=@ID AND PoID=@P;
                    """,conn,tx);
                Add(update,("@Q",item.Quantity),("@ID",id),("@P",item.PoID),("@Ship",ship),("@Lot",DeliveryLot(item,date)),("@Prod",(item.ProductionDate??date).Date));update.ExecuteNonQuery();
            }
        }
        if(!cancel && !ship) SyncDeliveryBoxes(conn,tx,id);
        if(cancel)
        {
            using var voidBoxes=new SqlCommand("UPDATE b SET ActiveFlag=0,VoidedTS=SYSDATETIME() FROM dbo.SCM_DeliveryBox b JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID WHERE l.DeliveryID=@ID AND b.ActiveFlag=1",conn,tx);
            Add(voidBoxes,("@ID",id));voidBoxes.ExecuteNonQuery();
        }
        using var header=new SqlCommand("""
            UPDATE dbo.SCM_Delivery SET DeliveryDate=CASE WHEN @Cancel=1 OR @Ship=1 THEN DeliveryDate ELSE @Date END,
                Status=CASE WHEN @Cancel=1 THEN 'Cancelled' WHEN @Ship=1 THEN 'Shipped' ELSE Status END,
                ShipDate=CASE WHEN @Ship=1 THEN @Date ELSE ShipDate END,
                ShippedAt=CASE WHEN @Ship=1 THEN SYSDATETIME() ELSE ShippedAt END,
                ShippedBy=CASE WHEN @Ship=1 THEN @Actor ELSE ShippedBy END,
                ShippedUserID=CASE WHEN @Ship=1 THEN @U ELSE ShippedUserID END,
                ModifiedBy=@Actor,ModifiedUserID=@U,ModifiedTS=SYSDATETIME() WHERE DeliveryID=@ID;
            UPDATE dbo.WH_PurchaseOrder SET ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE PoNumber=@N;
            """,conn,tx);
        Add(header,("@ID",id),("@Date",date.Date),("@Cancel",cancel),("@Ship",ship),("@Actor",actor),("@U",userId),("@N",number));header.ExecuteNonQuery();
        tx.Commit();
    }

    static string DeliveryLot(DeliveryInput item,DateTime date) => string.IsNullOrWhiteSpace(item.VendorLotNo)
        ? date.ToString("yyyyMMdd",System.Globalization.CultureInfo.InvariantCulture) : item.VendorLotNo.Trim();
    static void ValidateDeliveryTrace(IReadOnlyList<DeliveryInput> items)
    {
        if(items.Any(x=>(x.VendorLotNo?.Trim().Length??0)>30))
            throw new ArgumentException("Vendor LOT must be at most 30 characters.");
    }

}
