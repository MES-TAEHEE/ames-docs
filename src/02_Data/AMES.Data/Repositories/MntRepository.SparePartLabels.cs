using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class MntRepository
{
    public sealed record SparePartLabelRow(string SerialNo, string SparePartNo, string? PartName,
        string? VendorName, string? LocationId, string? ExtraLocation);

    internal const string SparePartLabelsSql = """
        SELECT I.SerialNo,P.SparePartNo,P.PartName,V.VendorName,
            COALESCE(I.LocationID,
                CASE WHEN P.ZoneCode='SP_EXTRA' THEN 'SP-EXTRA'
                     WHEN NULLIF(P.ZoneCode,'') IS NOT NULL AND NULLIF(P.Slot,'') IS NOT NULL THEN CONCAT(P.ZoneCode,'-',P.Slot)
                     ELSE COALESCE(NULLIF(P.ZoneCode,''),NULLIF(P.Slot,'')) END) LocationID,
            CASE WHEN P.ZoneCode='SP_EXTRA' THEN P.ExtraLocation END ExtraLocation
        FROM dbo.MNT_SparePartItem I
        JOIN dbo.MD_SparePart P ON P.SparePartNo=I.SparePartNo
        LEFT JOIN dbo.MD_Vendor V ON V.VendorID=P.SupplierID
        WHERE P.SparePartNo=@Part AND COALESCE(P.ActiveFlag,1)=1
          AND NULLIF(I.SerialNo,'') IS NOT NULL
        ORDER BY I.SparePartItemID DESC;
        """;

    public List<SparePartLabelRow> ListSparePartLabels(string sparePartNo)
        => Query(SparePartLabelsSql, r => new SparePartLabelRow((string)r["SerialNo"],
            (string)r["SparePartNo"], r["PartName"] as string, r["VendorName"] as string,
            r["LocationID"] as string, r["ExtraLocation"] as string),
            ("@Part", "MD_SparePart.SparePartNo", sparePartNo.Trim()));

    public string CreateSparePartLabel(string sparePartNo, string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sparePartNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        using var conn = _f.OpenConnection();
        using var cmd = new SqlCommand(PdaSparePartsSql.WebSerialCreate, conn);
        ColumnParams.Add(cmd, "@SparePartNo", "MD_SparePart.SparePartNo", sparePartNo.Trim());
        cmd.Parameters.Add("@Count", SqlDbType.Int).Value = 1;
        ColumnParams.Add(cmd, "@UserId", "MNT_SparePartItem.CreatedBy", actor);
        return cmd.ExecuteScalar() as string
            ?? throw new InvalidOperationException("The label serial result could not be confirmed. Check the saved serial list before creating another label.");
    }
}
