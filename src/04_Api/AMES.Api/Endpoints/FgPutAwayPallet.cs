using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static partial class FgEndpoints
{
    private static PutAwayScanRow? FindPutAwayScanRow(SqlConnection conn, SqlTransaction? tx, string barcode)
    {
        using var check = new SqlCommand("SELECT UnitType FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode", conn, tx);
        check.Parameters.Add("@Barcode", SqlDbType.NVarChar, 50).Value = barcode;
        if (check.ExecuteScalar() as string != "PALLET") return FindPutAwayLotRow(conn, tx, barcode);

        using var contents = new SqlCommand("""
            ;WITH Contents AS (
                SELECT LotNo,ParentLotNo,UnitType,Qty,PartNo FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode
                UNION ALL
                SELECT C.LotNo,C.ParentLotNo,C.UnitType,C.Qty,C.PartNo FROM dbo.WH_Inventory C WITH(UPDLOCK,HOLDLOCK)
                JOIN Contents P ON C.ParentLotNo=P.LotNo
            )
            SELECT TOP(201) P.LotNo,P.PartNo,I.ItemType FROM Contents P
            LEFT JOIN dbo.MD_Item I WITH(HOLDLOCK) ON I.ItemNo COLLATE DATABASE_DEFAULT=P.PartNo COLLATE DATABASE_DEFAULT
            WHERE P.LotNo<>@Barcode AND NOT EXISTS(SELECT 1 FROM Contents C WHERE C.ParentLotNo=P.LotNo)
            ORDER BY P.LotNo OPTION(MAXRECURSION 100);
            """, conn, tx);
        contents.Parameters.Add("@Barcode", SqlDbType.NVarChar, 50).Value = barcode;
        var lots = new List<(string LotNo,string? PartNo,string? ItemType)>();
        using (var reader = contents.ExecuteReader()) while (reader.Read())
            lots.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        if (lots.Count == 0 || lots.Count > 200)
            throw new InvalidOperationException("Scan a pallet containing 1-200 finished-goods LOTs.");
        EnsureAssyPutAwayContents(lots);
        var items = lots.Select(lot => FindPutAwayLotRow(conn, tx, lot.LotNo)
            ?? throw new InvalidOperationException($"{lot.LotNo}: a completed production LOT was not found.")).ToList();
        var invalid = items.FirstOrDefault(item => !item.IsProductionCompleted || item.Qty <= 0 || item.AlreadyStocked);
        var mixedUnit = items.Select(x => x.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var message = invalid is not null ? $"{invalid.LotNo}: {invalid.Message}"
            : mixedUnit ? "All pallet LOTs must use the same quantity unit."
            : $"{items.Count} ASSY LOTs matched. Scan Location Barcode.";
        return items[0] with {
            LotId = null, LotNo = barcode, WoId = null,
            ItemNo = items.Select(x => x.ItemNo).Distinct().Count() == 1 ? items[0].ItemNo : "MIXED",
            ItemName = items.Select(x => x.ItemNo).Distinct().Count() == 1 ? items[0].ItemName : "Multiple parts",
            CustomerCode = items.Select(x => x.CustomerCode).Distinct().Count() == 1 ? items[0].CustomerCode : null,
            Qty = items.Sum(x => x.Qty), IsProductionCompleted = invalid is null && !mixedUnit,
            AlreadyStocked = items.All(x => x.AlreadyStocked), ExistingLotNo = barcode,
            ExistingLocation = null, ExistingStatus = null, BarcodeType = BarcodePallet, Items = items, Message = message
        };
    }

    private static string SavePutAwayUnit(SqlConnection conn, SqlTransaction tx, PutAwayScanRow row,
        string location, string actor, Dictionary<string,decimal>? expectedItems)
    {
        if (row.Items is null) return SavePutAwayStock(conn, tx, row.LotNo, location, actor);
        if (!MatchesPutAwayContents(row.Items.Select(x => (x.LotNo,x.Qty)).ToList(), expectedItems))
            throw new InvalidOperationException("Pallet contents or quantities changed. Scan the pallet again and review the LOT list.");
        foreach (var item in row.Items) SavePutAwayStock(conn, tx, item.LotNo, location, actor);
        using var parent = new SqlCommand("""
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
            SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
            UPDATE dbo.WH_Inventory SET LocationNo=@Location,UpdatedAt=SYSDATETIME()
            WHERE LotNo=@Pallet AND UnitType='PALLET' AND Qty=0;
            """, conn, tx);
        parent.Parameters.Add("@Pallet",SqlDbType.NVarChar,50).Value=row.LotNo;
        parent.Parameters.Add("@Location",SqlDbType.VarChar,50).Value=location;
        if (parent.ExecuteNonQuery()!=1) throw new InvalidOperationException("The pallet changed. Scan it again.");
        return row.LotNo;
    }

    internal static bool MatchesPutAwayContents(IReadOnlyList<(string LotNo,decimal Qty)> items, Dictionary<string,decimal>? expected)
        => expected is not null && items.Count == expected.Count && items.All(x => expected.TryGetValue(x.LotNo,out var qty) && qty == x.Qty);

    internal static void EnsureAssyPutAwayContents(IEnumerable<(string LotNo,string? PartNo,string? ItemType)> items)
    {
        foreach (var item in items)
            if (string.IsNullOrWhiteSpace(item.PartNo) || !string.Equals(item.ItemType?.Trim(), "ASSY", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{item.LotNo} ({item.PartNo ?? "no part number"}): this part is not registered as ASSY in Item Master. Only ASSY finished goods can be assigned an FG location. Nothing on this pallet was moved.");
    }
}
