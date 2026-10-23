namespace AMES.Data.Repositories;

public static class PdaStockScope
{
    public static bool IsValid(string? kind) => kind is null or "ALL" or "GENERAL" or "FG";

    public static string Predicate(string? kind, string partColumn = "W.PartNo") => kind switch
    {
        "ALL" => "1=1",
        "FG" => $"EXISTS(SELECT 1 FROM dbo.MD_Item SI WHERE SI.ItemNo COLLATE DATABASE_DEFAULT={partColumn} COLLATE DATABASE_DEFAULT AND SI.ItemType='ASSY')",
        null or "GENERAL" => $"NOT EXISTS(SELECT 1 FROM dbo.MD_Item SI WHERE SI.ItemNo COLLATE DATABASE_DEFAULT={partColumn} COLLATE DATABASE_DEFAULT AND SI.ItemType='ASSY')",
        _ => throw new ArgumentException("Select ALL, GENERAL or FG stock.", nameof(kind))
    };

    public static string Inventory(string sql, string? kind)
    {
        var predicate = "AND " + Predicate(kind) + " AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory Child WHERE Child.ParentLotNo=W.LotNo)";
        return sql
            .Replace("AND NOT (UPPER(COALESCE(L.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')", predicate)
            .Replace("AND NOT (UPPER(COALESCE(L.AreaCode,'')) = 'FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')", predicate)
            .Replace("AND NOT (UPPER(COALESCE(L.AreaCode,N''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')", predicate)
            .Replace("AND NOT (UPPER(COALESCE(PL.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')", predicate);
    }

    public static string Transactions(string? kind) => PdaWarehouseSql.Transactions.Replace(
        "WHERE T.TransactionTime >= @From", $"WHERE {Predicate(kind, "T.PartNo")} AND T.TransactionTime >= @From");
}
