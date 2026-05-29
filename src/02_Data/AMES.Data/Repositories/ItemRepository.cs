using System.Data;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Sample repository for MD_Item showing the SP + ADO.NET pattern.
/// Per VOL01 Tech Stack: thin C# layer that invokes stored procedures.
///
/// For now this uses plain SELECT since no SPs exist yet in AMES_DEV.
/// In production, replace queries with: cmd.CommandType = StoredProcedure +
/// cmd.CommandText = "dbo.SP_Item_List".
/// </summary>
public sealed class ItemRepository
{
    private readonly AmesConnectionFactory _connFactory;

    public ItemRepository(AmesConnectionFactory connFactory)
    {
        _connFactory = connFactory ?? throw new ArgumentNullException(nameof(connFactory));
    }

    /// <summary>
    /// Returns all active items. Used by Item list / dropdowns.
    /// </summary>
    public List<ItemDto> GetAll()
    {
        using var conn = _connFactory.OpenConnection();
        using var cmd = new SqlCommand(@"
            SELECT ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory,
                   DefaultUOM, RoutingType, MinStock, SafetyStock, UnitCost,
                   CustItemNoSAV, CustItemNoGEO, ActiveFlag
            FROM dbo.MD_Item
            WHERE ActiveFlag = 1
            ORDER BY ItemNo;", conn);

        using var rdr = cmd.ExecuteReader();
        var list = new List<ItemDto>();
        while (rdr.Read())
            list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>
    /// Returns row count for the dashboard "DB connection OK" indicator.
    /// </summary>
    public int CountActive()
    {
        using var conn = _connFactory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.MD_Item WHERE ActiveFlag = 1;", conn);
        return (int)cmd.ExecuteScalar();
    }

    private static ItemDto MapToDto(IDataRecord r) => new()
    {
        ItemNo        = (string)r["ItemNo"],
        ItemName      = (string)r["ItemName"],
        ItemNameEN    = r["ItemNameEN"] as string,
        ItemType      = Enum.Parse<ItemType>((string)r["ItemType"]),
        ItemCategory  = r["ItemCategory"] as string,
        DefaultUOM    = r["DefaultUOM"] as string,
        RoutingType   = r["RoutingType"] as string,
        MinStock      = r["MinStock"] as decimal?,
        SafetyStock   = r["SafetyStock"] as decimal?,
        UnitCost      = r["UnitCost"] as decimal?,
        CustItemNoSAV = r["CustItemNoSAV"] as string,
        CustItemNoGEO = r["CustItemNoGEO"] as string,
        ActiveFlag    = (bool)r["ActiveFlag"],
    };
}
