using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public void CancelSupplierDeliveryNote(string number,IReadOnlyDictionary<string,string> versions,string userId,string actor)
    {
        if(string.IsNullOrWhiteSpace(number) || number.Length>30 || string.IsNullOrWhiteSpace(userId) || userId.Length>450
            || string.IsNullOrWhiteSpace(actor) || actor.Length>20 || versions.Count is <1 or >100
            || versions.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=versions.Count
            || versions.Any(v=>string.IsNullOrWhiteSpace(v.Key) || v.Key.Length>30 || v.Value is null
                || v.Value.Length!=16 || v.Value.Any(c=>!Uri.IsHexDigit(c))))
            throw new ArgumentException("Select a delivery note and reload its current version before cancelling.");
        using var c=factory.OpenConnection();using var tx=c.BeginTransaction();
        var orders=new List<string>();
        using(var lookup=new SqlCommand($"""
            SELECT DISTINCT d.PoNumber FROM dbo.SCM_Delivery d
            LEFT JOIN dbo.SCM_DeliveryNoteDelivery x ON x.DeliveryID=d.DeliveryID
            LEFT JOIN dbo.SCM_DeliveryNote n ON n.NoteID=x.NoteID
            WHERE (n.NoteNumber=@N OR (x.NoteID IS NULL AND d.DeliveryNumber=@N)) AND {NoteVendorAccess};
            """,c,tx))
        {
            Filter(lookup,("@N","SCM_DeliveryNote.NoteNumber",number),("@U","SCM_PortalVendorUser.UserID",userId));
            using var r=lookup.ExecuteReader();while(r.Read())orders.Add(r.GetString(0));
        }
        if(orders.Count==0)throw new InvalidOperationException("Delivery note not found or vendor access denied.");
        // Serialize cancellation with other registrations for every affected PO.
        LockCaseOrders(c,tx,orders);
        using var cmd=new SqlCommand(CancelSupplierDeliveryNoteSql,c,tx);
        Add(cmd,("@N","SCM_DeliveryNote.NoteNumber",number),("@U","SCM_Delivery.ModifiedUserID",userId),
            ("@Actor","SCM_Delivery.ModifiedBy",actor),("@Versions",null,JsonSerializer.Serialize(versions)));
        try {cmd.ExecuteNonQuery();tx.Commit();}
        catch(SqlException ex) when(ex.Number==51445) {throw new InvalidOperationException(ex.Message,ex);}
    }

    internal const string CancelSupplierDeliveryNoteSql="""
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        SET ANSI_PADDING ON;
        SET ANSI_WARNINGS ON;
        SET ARITHABORT ON;
        SET CONCAT_NULL_YIELDS_NULL ON;
        SET NUMERIC_ROUNDABORT OFF;
        SET XACT_ABORT ON;
        DECLARE @Targets TABLE(DeliveryID int PRIMARY KEY,DeliveryNumber varchar(30),PoNumber varchar(30),VendorID varchar(20));
        INSERT @Targets
        SELECT d.DeliveryID,d.DeliveryNumber,d.PoNumber,d.VendorID FROM dbo.SCM_Delivery d WITH(UPDLOCK,HOLDLOCK)
        LEFT JOIN dbo.SCM_DeliveryNoteDelivery x WITH(HOLDLOCK) ON x.DeliveryID=d.DeliveryID
        LEFT JOIN dbo.SCM_DeliveryNote n WITH(HOLDLOCK) ON n.NoteID=x.NoteID
        WHERE n.NoteNumber=@N OR (x.NoteID IS NULL AND d.DeliveryNumber=@N);
        IF NOT EXISTS(SELECT 1 FROM @Targets)
            THROW 51445,'Delivery note not found. Refresh the list.',1;
        IF EXISTS(SELECT 1 FROM @Targets t WHERE NOT EXISTS(
            SELECT 1 FROM dbo.SCM_PortalVendorUser m WITH(HOLDLOCK) JOIN dbo.MD_Vendor v WITH(HOLDLOCK) ON v.VendorID=m.VendorID
            WHERE m.UserID=@U AND m.VendorID=t.VendorID AND m.ActiveFlag=1 AND m.LockedFlag=0 AND ISNULL(v.ActiveFlag,1)=1))
            THROW 51445,'Vendor access denied.',1;
        IF EXISTS(SELECT 1 FROM @Targets t JOIN dbo.SCM_Delivery d ON d.DeliveryID=t.DeliveryID WHERE d.Status='Cancelled')
            THROW 51445,'This delivery note is already cancelled.',1;
        IF EXISTS(SELECT 1 FROM @Targets t JOIN dbo.SCM_Delivery d ON d.DeliveryID=t.DeliveryID WHERE d.Status='Received')
            THROW 51445,'This delivery note has already been received and cannot be cancelled.',1;
        IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryLine l WITH(UPDLOCK,HOLDLOCK) JOIN @Targets t ON t.DeliveryID=l.DeliveryID WHERE l.ReceivedQty>0)
            OR EXISTS(SELECT 1 FROM dbo.WH_Inventory w WITH(HOLDLOCK)
                JOIN dbo.SCM_DeliveryBox b ON b.BoxNumber=w.LotNo
                JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID JOIN @Targets t ON t.DeliveryID=l.DeliveryID)
            OR EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction h WITH(HOLDLOCK)
                JOIN dbo.SCM_DeliveryLine l ON h.SourceType='SCM_DeliveryLine' AND h.SourceID=l.DeliveryLineID
                JOIN @Targets t ON t.DeliveryID=l.DeliveryID WHERE h.TransactionType='IN')
            THROW 51445,'This delivery note has receiving history, including partial receipts, and cannot be cancelled.',1;
        IF (SELECT COUNT(*) FROM @Targets)<>(SELECT COUNT(*) FROM OPENJSON(@Versions)) OR EXISTS(
            SELECT 1 FROM @Targets t JOIN dbo.SCM_Delivery d ON d.DeliveryID=t.DeliveryID
            WHERE d.Status NOT IN('Registered','Shipped') OR NOT EXISTS(
                SELECT 1 FROM OPENJSON(@Versions) j WHERE j.[key] COLLATE DATABASE_DEFAULT=d.DeliveryNumber
                    AND j.value COLLATE DATABASE_DEFAULT=CONVERT(varchar(16),CONVERT(binary(8),d.Version),2)))
            THROW 51445,'The delivery note changed. Refresh the list and try again.',1;
        IF EXISTS(SELECT 1 FROM dbo.SCM_DeliveryBox b JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID
            JOIN @Targets t ON t.DeliveryID=l.DeliveryID
            JOIN dbo.SCM_DeliveryBox otherBox ON otherBox.CaseNo=b.CaseNo AND otherBox.ActiveFlag=1
            JOIN dbo.SCM_DeliveryLine otherLine ON otherLine.DeliveryLineID=otherBox.DeliveryLineID
            WHERE b.ActiveFlag=1 AND NOT EXISTS(SELECT 1 FROM @Targets otherTarget WHERE otherTarget.DeliveryID=otherLine.DeliveryID))
            THROW 51445,'A Shipping Unit belongs to another delivery note. Cancel the complete original note.',1;
        UPDATE b SET ActiveFlag=0,VoidedTS=SYSDATETIME() FROM dbo.SCM_DeliveryBox b
            JOIN dbo.SCM_DeliveryLine l ON l.DeliveryLineID=b.DeliveryLineID JOIN @Targets t ON t.DeliveryID=l.DeliveryID WHERE b.ActiveFlag=1;
        UPDATE d SET Status='Cancelled',ModifiedBy=@Actor,ModifiedUserID=@U,ModifiedTS=SYSDATETIME()
            FROM dbo.SCM_Delivery d JOIN @Targets t ON t.DeliveryID=d.DeliveryID;
        UPDATE p SET ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() FROM dbo.WH_PurchaseOrder p
            WHERE EXISTS(SELECT 1 FROM @Targets t WHERE t.PoNumber=p.PoNumber);
        """;
}
