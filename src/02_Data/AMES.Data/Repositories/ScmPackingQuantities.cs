using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public record PackingQuantityRow(string ItemNo, string ItemName, string Unit, string VendorID,
        string VendorName, decimal? PackingQty, string Issue = "", bool Linked = true)
    {
        public bool CanSave => Issue.Length == 0;
    }

    const string PackingAccess = """
        EXISTS(SELECT 1 FROM dbo.SCM_PortalVendorUser u
            WHERE u.UserID=@U AND u.VendorID=m.VendorID AND u.ActiveFlag=1 AND u.LockedFlag=0)
        """;

    // Supplier links are managed by SCM-003, never inferred from PO history.
    const string PackingItems = """
        WITH PackingItems AS (
            SELECT ItemNo,VendorID FROM dbo.SCM_ItemVendor WHERE ActiveFlag=1
        )
        """;

    public List<PackingQuantityRow> ListPortalPackingQuantities(string userId, bool forCreate = false)
    {
        using var c = factory.OpenConnection();
        using var cmd = new SqlCommand($"""
            {PackingItems}
            SELECT m.ItemNo,COALESCE(i.ItemName,m.ItemNo),ISNULL(i.DefaultUOM,''),m.VendorID,v.VendorName,link.PackingQty,
                CASE WHEN i.ItemNo IS NULL THEN 'Item master missing. Ask EOS to register this part in Material Item Master.'
                     WHEN ISNULL(i.ActiveFlag,1)=0 THEN 'Item is inactive. Ask EOS to check the item master.'
                     WHEN COALESCE(i.ItemType,'')<>'MATERIAL' THEN 'Not a material item. Ask EOS to check the item type.'
                     WHEN link.ActiveFlag=0 THEN 'Supplier link is inactive. Ask EOS to check the supplier mapping.'
                     ELSE '' END,
                CAST(CASE WHEN link.ItemNo IS NULL THEN 0 ELSE 1 END AS bit)
            FROM PackingItems m
            LEFT JOIN dbo.SCM_ItemVendor link ON link.ItemNo=m.ItemNo AND link.VendorID=m.VendorID
            JOIN dbo.MD_Item i ON i.ItemNo=m.ItemNo
            JOIN dbo.MD_Vendor v ON v.VendorID=m.VendorID
            WHERE ISNULL(v.ActiveFlag,1)=1 AND {PackingAccess}
                AND (@Create=0 OR COALESCE(link.PackingQty,0)<=0)
            ORDER BY m.VendorID,m.ItemNo;
            """, c);
        Add(cmd, ("@U", userId), ("@Create", forCreate));
        using var r = cmd.ExecuteReader();
        var rows = new List<PackingQuantityRow>();
        while (r.Read()) rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetDecimal(5),r.GetString(6),r.GetBoolean(7)));
        return rows;
    }

    public bool CreatePortalPackingQuantity(string userId, string itemNo, string vendorId, decimal quantity)
    {
        if (quantity <= 0 || quantity > 999999999999999.999m || decimal.Round(quantity,3)!=quantity)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        using var c = factory.OpenConnection();
        using var tx = c.BeginTransaction();
        using var cmd = new SqlCommand($"""
            {PackingItems}
            SELECT m.ItemNo FROM PackingItems m
            JOIN dbo.MD_Item i WITH(HOLDLOCK) ON i.ItemNo=m.ItemNo
            JOIN dbo.MD_Vendor v WITH(HOLDLOCK) ON v.VendorID=m.VendorID
            WHERE m.ItemNo=@Item AND m.VendorID=@Vendor AND i.ItemType='MATERIAL'
                AND ISNULL(i.ActiveFlag,1)=1 AND ISNULL(v.ActiveFlag,1)=1 AND {PackingAccess};
            """, c, tx);
        Add(cmd,("@U",userId),("@Item",itemNo),("@Vendor",vendorId));
        if (cmd.ExecuteScalar() is null) return false;
        cmd.CommandText = """
            IF EXISTS(SELECT 1 FROM dbo.SCM_ItemVendor WITH(UPDLOCK,HOLDLOCK)
                WHERE ItemNo=@Item AND VendorID=@Vendor AND (ActiveFlag=0 OR PackingQty>0))
            BEGIN SELECT CAST(0 AS bit); RETURN; END;
            UPDATE dbo.SCM_ItemVendor WITH(UPDLOCK,HOLDLOCK) SET PackingQty=@Qty,ModifiedBy=LEFT(@U,20),ModifiedTS=SYSDATETIME()
            WHERE ItemNo=@Item AND VendorID=@Vendor AND ActiveFlag=1;
            SELECT CAST(CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END AS bit);
            """;
        var p = cmd.Parameters.Add("@Qty",System.Data.SqlDbType.Decimal);
        p.Precision=18;p.Scale=3;p.Value=quantity;
        var created=(bool)cmd.ExecuteScalar()!;
        tx.Commit();
        return created;
    }

    public bool SavePortalPackingQuantity(string userId, string itemNo, string vendorId,
        decimal? quantity, decimal? originalQuantity)
    {
        if (quantity is null or <= 0 or > 999999999999999.999m || decimal.Round(quantity.Value,3)!=quantity.Value)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        using var c = factory.OpenConnection();
        using var cmd = new SqlCommand($"""
            UPDATE m SET PackingQty=@Qty,ModifiedBy=LEFT(@U,20),ModifiedTS=SYSDATETIME()
            FROM dbo.SCM_ItemVendor m
            JOIN dbo.MD_Item i ON i.ItemNo=m.ItemNo
            JOIN dbo.MD_Vendor v ON v.VendorID=m.VendorID
            WHERE m.ItemNo=@Item AND m.VendorID=@Vendor AND m.ActiveFlag=1
                AND i.ItemType='MATERIAL' AND ISNULL(i.ActiveFlag,1)=1 AND ISNULL(v.ActiveFlag,1)=1
                AND {PackingAccess}
                AND (m.PackingQty=@Original OR (m.PackingQty IS NULL AND @Original IS NULL));
            """, c);
        Add(cmd, ("@U", userId), ("@Item", itemNo), ("@Vendor", vendorId));
        foreach (var (name, value) in new[] { ("@Qty", quantity), ("@Original", originalQuantity) })
        {
            var p = cmd.Parameters.Add(name, System.Data.SqlDbType.Decimal);
            p.Precision = 18; p.Scale = 3; p.Value = (object?)value ?? DBNull.Value;
        }
        return cmd.ExecuteNonQuery() == 1;
    }
}
