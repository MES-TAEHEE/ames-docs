using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    public record PackingQuantityRow(string ItemNo, string ItemName, string Unit, string VendorID,
        string VendorName, decimal? PackingQty);

    const string PackingAccess = """
        EXISTS(SELECT 1 FROM dbo.SCM_PortalVendorUser u
            WHERE u.UserID=@U AND u.VendorID=m.VendorID AND u.ActiveFlag=1 AND u.LockedFlag=0)
        """;

    public List<PackingQuantityRow> ListPortalPackingQuantities(string userId)
    {
        using var c = factory.OpenConnection();
        using var cmd = new SqlCommand($"""
            SELECT m.ItemNo,i.ItemName,ISNULL(i.DefaultUOM,''),m.VendorID,v.VendorName,m.PackingQty
            FROM dbo.SCM_ItemVendor m
            JOIN dbo.MD_Item i ON i.ItemNo=m.ItemNo
            JOIN dbo.MD_Vendor v ON v.VendorID=m.VendorID
            WHERE m.ActiveFlag=1 AND i.ItemType='MATERIAL' AND ISNULL(i.ActiveFlag,1)=1
                AND ISNULL(v.ActiveFlag,1)=1 AND {PackingAccess}
            ORDER BY m.VendorID,m.ItemNo;
            """, c);
        Add(cmd, ("@U", userId));
        using var r = cmd.ExecuteReader();
        var rows = new List<PackingQuantityRow>();
        while (r.Read()) rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetDecimal(5)));
        return rows;
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
