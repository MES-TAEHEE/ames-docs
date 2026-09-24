using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>Persistent SCM master mappings and daily numbering.</summary>
public sealed partial class ScmRepository(AmesConnectionFactory factory)
{
    public record ItemVendorRow(string ItemNo, string VendorID, string VendorName,
        bool VendorActive, string VendorType, string CreatedBy, DateTime CreatedTS);

    public List<ItemVendorRow> ListItemVendors()
    {
        using var connection = factory.OpenConnection();
        using var command = new SqlCommand("""
            SELECT m.ItemNo, m.VendorID, COALESCE(v.VendorName, m.VendorID),
                   ISNULL(v.ActiveFlag,1), COALESCE(v.VendorType,''), m.CreatedBy, m.CreatedTS
            FROM dbo.SCM_ItemVendor m JOIN dbo.MD_Vendor v ON v.VendorID=m.VendorID
            WHERE m.ActiveFlag=1 ORDER BY m.ItemNo, m.VendorID;
            """, connection);
        using var reader = command.ExecuteReader();
        var rows = new List<ItemVendorRow>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetBoolean(3), reader.GetString(4), reader.GetString(5), reader.GetDateTime(6)));
        return rows;
    }

    /// <returns>False if already linked. Re-enables an existing inactive mapping.</returns>
    public bool LinkItemVendor(string itemNo, string vendorId, string actor)
        => LinkItemVendors(itemNo, [vendorId], actor).Count > 0;

    /// <summary>Links all selected vendors atomically and returns the changed vendor IDs.</summary>
    public List<string> LinkItemVendors(string itemNo, IEnumerable<string> vendorIds, string actor)
    {
        var ids = vendorIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return [];
        using var connection = factory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = new SqlCommand("""
            SET XACT_ABORT ON;
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WITH (HOLDLOCK)
                           WHERE ItemNo=@Item AND ItemType='MATERIAL')
                THROW 50020, 'Only MATERIAL items may be linked.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_Vendor WITH (HOLDLOCK)
                           WHERE VendorID=@Vendor AND ISNULL(ActiveFlag,1)=1)
                THROW 50021, 'An active vendor is required.', 1;
            DECLARE @Active bit;
            SELECT @Active=ActiveFlag FROM dbo.SCM_ItemVendor WITH (UPDLOCK,HOLDLOCK)
            WHERE ItemNo=@Item AND VendorID=@Vendor;
            IF @Active=1 BEGIN SELECT CAST(0 AS bit); RETURN; END;
            IF @Active IS NULL
                INSERT dbo.SCM_ItemVendor(ItemNo,VendorID,CreatedBy) VALUES(@Item,@Vendor,@Actor);
            ELSE
                UPDATE dbo.SCM_ItemVendor SET ActiveFlag=1,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
                WHERE ItemNo=@Item AND VendorID=@Vendor;
            SELECT CAST(1 AS bit);
            """, connection, transaction);
        var changed = new List<string>();
        foreach (var vendorId in ids)
        {
            command.Parameters.Clear();
            AddMappingParameters(command, itemNo, vendorId, actor);
            if ((bool)command.ExecuteScalar()!) changed.Add(vendorId);
        }
        transaction.Commit();
        return changed;
    }

    public bool UnlinkItemVendor(string itemNo, string vendorId, string actor)
    {
        using var connection = factory.OpenConnection();
        using var command = new SqlCommand("""
            UPDATE dbo.SCM_ItemVendor SET ActiveFlag=0,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            WHERE ItemNo=@Item AND VendorID=@Vendor AND ActiveFlag=1;
            """, connection);
        AddMappingParameters(command, itemNo, vendorId, actor);
        return command.ExecuteNonQuery() == 1;
    }

    static void AddMappingParameters(SqlCommand command, string itemNo, string vendorId, string actor)
    {
        command.Parameters.AddWithValue("@Item", itemNo);
        command.Parameters.AddWithValue("@Vendor", vendorId);
        command.Parameters.AddWithValue("@Actor", actor);
    }

    /// <summary>
    /// Reserves a unique PO-yyyyMMdd-NNNN using the DB server's date.
    /// Committed numbers are never recycled, even if the caller later cancels the order.
    /// </summary>
    public string ReservePurchaseOrderNumber()
    {
        using var connection = factory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = new SqlCommand("""
            SET XACT_ABORT ON;
            DECLARE @Day date = CONVERT(date, SYSDATETIME());
            DECLARE @Key nvarchar(255) = N'SCM:PO:' + CONVERT(nvarchar(8), @Day, 112);
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = @Key, @LockMode = 'Exclusive', @LockOwner = 'Transaction',
                @LockTimeout = 15000, @DbPrincipal = 'public';
            IF @LockResult < 0 THROW 50010, 'Unable to reserve purchase order number.', 1;

            DECLARE @Next int;
            SELECT @Next = LastNumber + 1
            FROM dbo.SCM_PurchaseOrderSequence WITH (UPDLOCK, HOLDLOCK)
            WHERE NumberDate = @Day;
            SET @Next = ISNULL(@Next, 1);
            IF @Next > 9999 THROW 50011, 'Daily purchase order number limit reached.', 1;

            UPDATE dbo.SCM_PurchaseOrderSequence SET LastNumber = @Next WHERE NumberDate = @Day;
            IF @@ROWCOUNT = 0
                INSERT dbo.SCM_PurchaseOrderSequence (NumberDate, LastNumber) VALUES (@Day, @Next);
            SELECT 'PO-' + CONVERT(varchar(8), @Day, 112) + '-' + RIGHT('0000' + CONVERT(varchar(4), @Next), 4);
            """, connection, transaction);
        var number = command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("Purchase order number was not returned.");
        transaction.Commit();
        return number;
    }
}
