using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed class MasterDataRepository
{
    private readonly AmesConnectionFactory _factory;
    public MasterDataRepository(AmesConnectionFactory f) => _factory = f;

    // ── Pop lookup helpers ───────────────────────────────────────────────
    public string? GetLineName(string lineId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT TOP 1 LineName FROM dbo.MD_Line WHERE LineID = @L;", conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        return cmd.ExecuteScalar() as string;
    }

    public int? GetRecipeCycleTime(string recipeId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT TOP 1 CycleTime FROM dbo.MD_Recipe WHERE RecipeID = @R;", conn);
        cmd.Parameters.Add("@R", SqlDbType.VarChar, 20).Value = recipeId;
        var v = cmd.ExecuteScalar();
        return v is int i ? i : null;
    }

    // ── MD-01 CodeGroup ──────────────────────────────────────────────────
    public List<CodeGroupRow> ListCodeGroups()
        => Query("""
            SELECT GroupCode, GroupName, GroupNameEn, Description,
                   ISNULL(UseFlag,1) AS UseFlag,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
            FROM   dbo.MD_CodeGroup
            ORDER  BY GroupCode
            """, r => new CodeGroupRow(
                r.GetString("GroupCode"),
                r["GroupName"]   as string,
                r["GroupNameEn"] as string,
                r["Description"] as string,
                (bool)r["UseFlag"],
                r["CreatedBy"]   as string,
                r["CreatedTS"]   is DateTime ct  ? ct  : null,
                r["ModifiedBy"]  as string,
                r["ModifiedTS"]  is DateTime mt  ? mt  : null));

    public bool CodeGroupExists(string groupCode)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.MD_CodeGroup WHERE GroupCode=@G", conn);
        cmd.Parameters.AddWithValue("@G", groupCode);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public void InsertCodeGroup(string groupCode, string? groupName, string? groupNameEn,
        string? description, bool useFlag, string createdBy)
        => Exec("""
            INSERT INTO dbo.MD_CodeGroup
                   (GroupCode,GroupName,GroupNameEn,Description,UseFlag,CreatedBy,CreatedTS)
            VALUES (@Code,@Name,@NameEn,@Desc,@UseFlag,@By,SYSDATETIME())
            """,
            ("@Code",   groupCode),
            ("@Name",   groupName),
            ("@NameEn", groupNameEn),
            ("@Desc",   description),
            ("@UseFlag",useFlag),
            ("@By",     createdBy));

    public void UpdateCodeGroup(string groupCode, string? groupName, string? groupNameEn,
        string? description, bool useFlag, string modifiedBy)
        => Exec("""
            UPDATE dbo.MD_CodeGroup
            SET    GroupName=@Name, GroupNameEn=@NameEn, Description=@Desc,
                   UseFlag=@UseFlag, ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  GroupCode=@Code
            """,
            ("@Code",   groupCode),
            ("@Name",   groupName),
            ("@NameEn", groupNameEn),
            ("@Desc",   description),
            ("@UseFlag",useFlag),
            ("@By",     modifiedBy));

    public void DeleteCodeGroup(string groupCode)
        => Exec("DELETE dbo.MD_CodeGroup WHERE GroupCode=@G", ("@G", groupCode));

    // ── MD-01 CodeItem ───────────────────────────────────────────────────
    public List<CodeItemRow> ListCodeItems(string groupCode)
        => Query("""
            SELECT CodeID, GroupCode, CodeValue, CodeName, CodeNameEn,
                   ParentCodeID, ISNULL(SortOrder,0) AS SortOrder,
                   Attribute1, ISNULL(UseFlag,1) AS UseFlag, Description,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode=@G
            ORDER  BY ISNULL(SortOrder,0), CodeValue
            """, r => new CodeItemRow(
                r.GetString("CodeID"),
                r["GroupCode"]    as string,
                r["CodeValue"]    as string,
                r["CodeName"]     as string,
                r["CodeNameEn"]   as string,
                r["ParentCodeID"] as string,
                r["SortOrder"] is int so ? so : (int?)null,
                r["Attribute1"]   as string,
                (bool)r["UseFlag"],
                r["Description"]  as string,
                r["CreatedBy"]    as string,
                r["CreatedTS"]    is DateTime ct ? ct : null,
                r["ModifiedBy"]   as string,
                r["ModifiedTS"]   is DateTime mt ? mt : null),
            ("@G", groupCode));

    public bool CodeItemExists(string codeId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.MD_CodeItem WHERE CodeID=@I", conn);
        cmd.Parameters.AddWithValue("@I", codeId);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public void InsertCodeItem(string codeId, string groupCode, string? codeValue,
        string? codeName, string? codeNameEn, int sortOrder,
        string? attribute1, bool useFlag, string? description, string createdBy,
        string? parentCodeId = null)
        => Exec("""
            INSERT INTO dbo.MD_CodeItem
                   (CodeID,GroupCode,CodeValue,CodeName,CodeNameEn,
                    SortOrder,Attribute1,UseFlag,Description,ParentCodeID,CreatedBy,CreatedTS)
            VALUES (@ID,@Group,@Val,@Name,@NameEn,
                    @Sort,@Attr,@UseFlag,@Desc,@ParentID,@By,SYSDATETIME())
            """,
            ("@ID",       codeId),
            ("@Group",    groupCode),
            ("@Val",      codeValue),
            ("@Name",     codeName),
            ("@NameEn",   codeNameEn),
            ("@Sort",     sortOrder),
            ("@Attr",     attribute1),
            ("@UseFlag",  useFlag),
            ("@Desc",     description),
            ("@ParentID", (object?)parentCodeId ?? DBNull.Value),
            ("@By",       createdBy));

    public void UpdateCodeItem(string codeId, string? codeValue, string? codeName,
        string? codeNameEn, int sortOrder, string? attribute1,
        bool useFlag, string? description, string modifiedBy,
        string? parentCodeId = null)
        => Exec("""
            UPDATE dbo.MD_CodeItem
            SET    CodeValue=@Val, CodeName=@Name, CodeNameEn=@NameEn,
                   SortOrder=@Sort, Attribute1=@Attr, UseFlag=@UseFlag,
                   Description=@Desc, ParentCodeID=@ParentID,
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  CodeID=@ID
            """,
            ("@ID",       codeId),
            ("@Val",      codeValue),
            ("@Name",     codeName),
            ("@NameEn",   codeNameEn),
            ("@Sort",     sortOrder),
            ("@Attr",     attribute1),
            ("@UseFlag",  useFlag),
            ("@Desc",     description),
            ("@ParentID", (object?)parentCodeId ?? DBNull.Value),
            ("@By",       modifiedBy));

    public void DeleteCodeItem(string codeId)
        => Exec("DELETE dbo.MD_CodeItem WHERE CodeID=@I", ("@I", codeId));

    // ── MD-02 Item ───────────────────────────────────────────────────────
    public List<ItemRow> ListItems(string? search = null)
    {
        var sql = """
            SELECT ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM,
                   RoutingType, MinStock, MaxStock, SafetyStock, UnitCost,
                   PGN, ALC, DrawingNo,
                   ISNULL(ActiveFlag,1) AS ActiveFlag,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
            FROM   dbo.MD_Item
            """ + (string.IsNullOrWhiteSpace(search) ? "" :
            " WHERE ItemNo LIKE @S OR ItemName LIKE @S OR ItemCategory LIKE @S OR CarType LIKE @S OR PGN LIKE @S OR ALC LIKE @S") +
            " ORDER BY ItemNo";
        var p = string.IsNullOrWhiteSpace(search)
            ? Array.Empty<(string, object?)>()
            : new[] { ("@S", (object?)("%" + search.Trim() + "%")) };
        return Query(sql, r => new ItemRow(
            r.GetString("ItemNo"),
            r.GetString("ItemName"),
            r["ItemType"]      as string,
            r["ItemCategory"]  as string,
            r["CarType"]       as string,
            r["DefaultUOM"]    as string,
            r["RoutingType"]   as string,
            r["MinStock"]      is decimal mn  ? mn  : null,
            r["MaxStock"]      is decimal mx  ? mx  : null,
            r["SafetyStock"]   is decimal ss  ? ss  : null,
            r["UnitCost"]      is decimal uc  ? uc  : null,
            r["PGN"]           as string,
            r["ALC"]           as string,
            r["DrawingNo"]     as string,
            (bool)r["ActiveFlag"],
            r["CreatedBy"]     as string,
            r["CreatedTS"]     is DateTime ct ? ct : null,
            r["ModifiedBy"]    as string,
            r["ModifiedTS"]    is DateTime mt ? mt : null), p);
    }

    public bool ItemExists(string itemNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.MD_Item WHERE ItemNo=@N", conn);
        cmd.Parameters.AddWithValue("@N", itemNo);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public void InsertItem(string itemNo, string itemName,
        string? itemType, string? itemCategory, string? carType, string? defaultUom,
        string? routingType, decimal? minStock, decimal? maxStock, decimal? safetyStock,
        decimal? unitCost, string? pgn, string? alc, string? drawingNo,
        bool activeFlag, string createdBy)
        => Exec("""
            INSERT INTO dbo.MD_Item
                   (ItemNo,ItemName,ItemType,ItemCategory,CarType,DefaultUOM,
                    RoutingType,MinStock,MaxStock,SafetyStock,UnitCost,
                    PGN,ALC,DrawingNo,ActiveFlag,CreatedBy,CreatedTS)
            VALUES (@No,@Name,@Type,@Cat,@Car,@Uom,
                    @Route,@Min,@Max,@Safe,@Cost,
                    @PGN,@ALC,@Draw,@Active,@By,SYSDATETIME())
            """,
            ("@No",     itemNo),   ("@Name",   itemName),
            ("@Type",   itemType), ("@Cat",    itemCategory), ("@Car", carType),
            ("@Uom",    defaultUom),
            ("@Route",  routingType), ("@Min", minStock),  ("@Max",    maxStock),
            ("@Safe",   safetyStock), ("@Cost", unitCost), ("@PGN",    pgn),
            ("@ALC",    alc),      ("@Draw",   drawingNo), ("@Active", activeFlag),
            ("@By",     createdBy));

    public void UpdateItem(string itemNo, string itemName,
        string? itemType, string? itemCategory, string? carType, string? defaultUom,
        string? routingType, decimal? minStock, decimal? maxStock, decimal? safetyStock,
        decimal? unitCost, string? pgn, string? alc, string? drawingNo,
        bool activeFlag, string modifiedBy)
        => Exec("""
            UPDATE dbo.MD_Item
            SET    ItemName=@Name, ItemType=@Type,
                   ItemCategory=@Cat, CarType=@Car, DefaultUOM=@Uom, RoutingType=@Route,
                   MinStock=@Min, MaxStock=@Max, SafetyStock=@Safe, UnitCost=@Cost,
                   PGN=@PGN, ALC=@ALC, DrawingNo=@Draw,
                   ActiveFlag=@Active, ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  ItemNo=@No
            """,
            ("@No",     itemNo),   ("@Name",   itemName),
            ("@Type",   itemType), ("@Cat",    itemCategory), ("@Car", carType),
            ("@Uom",    defaultUom),
            ("@Route",  routingType), ("@Min", minStock),  ("@Max",    maxStock),
            ("@Safe",   safetyStock), ("@Cost", unitCost), ("@PGN",    pgn),
            ("@ALC",    alc),      ("@Draw",   drawingNo), ("@Active", activeFlag),
            ("@By",     modifiedBy));

    public void DeleteItem(string itemNo)
        => Exec("DELETE dbo.MD_Item WHERE ItemNo=@N", ("@N", itemNo));

    // ── MD-03 BomVersion ─────────────────────────────────────────────────
    public List<BomVersionRow> ListBomVersions(string? statusFilter = null)
    {
        var where = statusFilter is null ? "" : " WHERE v.Status = @S";
        var p     = statusFilter is null
            ? Array.Empty<(string, object?)>()
            : new[] { ("@S", (object?)statusFilter) };
        return Query($"""
            SELECT v.VersionID, v.RootItemNo,
                   ISNULL(i.ItemName,'') AS RootItemName,
                   v.VersionNo, v.EffFrom, v.EffTo,
                   v.ChangeType, v.ChangeReason,
                   v.RequestedBy, v.ApprovedBy, v.ApprovedTS,
                   ISNULL(v.Status,'DRAFT') AS Status,
                   v.CreatedBy, v.CreatedTS, v.ModifiedBy, v.ModifiedTS
            FROM   dbo.MD_BomVersion v
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = v.RootItemNo
            {where}
            ORDER  BY v.CreatedTS DESC
            """,
            r => new BomVersionRow(
                r.GetString("VersionID"),
                r["RootItemNo"]   as string,
                r.GetString("RootItemName"),
                r["VersionNo"]    as string,
                r["EffFrom"]      is DateTime ef ? DateOnly.FromDateTime(ef) : null,
                r["EffTo"]        is DateTime et ? DateOnly.FromDateTime(et) : null,
                r["ChangeType"]   as string,
                r["ChangeReason"] as string,
                r["RequestedBy"]  as string,
                r["ApprovedBy"]   as string,
                r["ApprovedTS"]   is DateTime at ? at : null,
                r["Status"]       as string ?? "DRAFT",
                r["CreatedBy"]    as string,
                r["CreatedTS"]    is DateTime ct ? ct : null,
                r["ModifiedBy"]   as string,
                r["ModifiedTS"]   is DateTime mt ? mt : null), p);
    }

    public bool BomVersionExists(string versionId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.MD_BomVersion WHERE VersionID=@V", conn);
        cmd.Parameters.AddWithValue("@V", versionId);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public void InsertBomVersion(string versionId, string? rootItemNo, string? versionNo,
        DateOnly? effFrom, DateOnly? effTo, string? changeType, string? changeReason,
        string createdBy)
        => Exec("""
            INSERT INTO dbo.MD_BomVersion
                   (VersionID,RootItemNo,VersionNo,EffFrom,EffTo,
                    ChangeType,ChangeReason,Status,CreatedBy,CreatedTS)
            VALUES (@ID,@Root,@VNo,@EF,@ET,@CT,@CR,'DRAFT',@By,SYSDATETIME())
            """,
            ("@ID",   versionId),  ("@Root", rootItemNo), ("@VNo", versionNo),
            ("@EF",   effFrom.HasValue ? (object)effFrom.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value),
            ("@ET",   effTo.HasValue   ? (object)effTo.Value.ToDateTime(TimeOnly.MinValue)   : DBNull.Value),
            ("@CT",   changeType),  ("@CR",  changeReason), ("@By", createdBy));

    public void UpdateBomVersion(string versionId, string? rootItemNo, string? versionNo,
        DateOnly? effFrom, DateOnly? effTo, string? changeType, string? changeReason,
        string modifiedBy)
        => Exec("""
            UPDATE dbo.MD_BomVersion
            SET    RootItemNo=@Root, VersionNo=@VNo, EffFrom=@EF, EffTo=@ET,
                   ChangeType=@CT, ChangeReason=@CR,
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  VersionID=@ID AND Status='DRAFT'
            """,
            ("@ID",   versionId),  ("@Root", rootItemNo), ("@VNo", versionNo),
            ("@EF",   effFrom.HasValue ? (object)effFrom.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value),
            ("@ET",   effTo.HasValue   ? (object)effTo.Value.ToDateTime(TimeOnly.MinValue)   : DBNull.Value),
            ("@CT",   changeType), ("@CR", changeReason), ("@By", modifiedBy));

    public void DeleteBomVersion(string versionId)
    {
        Exec("DELETE dbo.MD_Bom WHERE VersionID=@V",        ("@V", versionId));
        Exec("DELETE dbo.MD_BomVersion WHERE VersionID=@V", ("@V", versionId));
    }

    public void RequestBomApproval(string versionId, string requestedBy)
        => Exec("""
            UPDATE dbo.MD_BomVersion
            SET    Status='PENDING', RequestedBy=@By,
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  VersionID=@V AND Status='DRAFT'
            """,
            ("@V", versionId), ("@By", requestedBy));

    public void ApproveBomVersion(string versionId, string approvedBy)
        => Exec("""
            UPDATE dbo.MD_BomVersion
            SET    Status='APPROVED', ApprovedBy=@By, ApprovedTS=SYSDATETIME(),
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  VersionID=@V AND Status='PENDING'
            """,
            ("@V", versionId), ("@By", approvedBy));

    public void RejectBomVersion(string versionId, string rejectedBy)
        => Exec("""
            UPDATE dbo.MD_BomVersion
            SET    Status='REJECTED',
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  VersionID=@V AND Status='PENDING'
            """,
            ("@V", versionId), ("@By", rejectedBy));

    public void ReviveBomVersion(string versionId, string modifiedBy)
        => Exec("""
            UPDATE dbo.MD_BomVersion
            SET    Status='DRAFT', ApprovedBy=NULL, ApprovedTS=NULL,
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  VersionID=@V AND Status='REJECTED'
            """,
            ("@V", versionId), ("@By", modifiedBy));

    // ── MD-03 BomLine ────────────────────────────────────────────────────
    public List<BomRow> ListBomLines(string versionId)
        => Query("""
            SELECT b.BOMID, b.ParentItemNo,
                   ISNULL(p.ItemName,'') AS ParentItemName,
                   b.CompItemNo,
                   ISNULL(c.ItemName,'') AS CompItemName,
                   b.BOMLevel, b.QtyPer, b.UOM, b.ScrapPct,
                   b.VersionID, b.Position, b.Note,
                   ISNULL(b.ActiveFlag,1) AS ActiveFlag,
                   b.CreatedBy, b.CreatedTS, b.ModifiedBy, b.ModifiedTS
            FROM   dbo.MD_Bom b
            LEFT JOIN dbo.MD_Item p ON p.ItemNo = b.ParentItemNo
            LEFT JOIN dbo.MD_Item c ON c.ItemNo = b.CompItemNo
            WHERE  b.VersionID = @V
            ORDER  BY ISNULL(b.Position,9999), b.BOMLevel, b.CompItemNo
            """,
            r => new BomRow(
                r.GetString("BOMID"),
                r["ParentItemNo"]   as string, r.GetString("ParentItemName"),
                r["CompItemNo"]     as string, r.GetString("CompItemName"),
                r["BOMLevel"]       is int lv  ? lv  : null,
                r["QtyPer"]         is decimal q ? q  : null,
                r["UOM"]            as string,
                r["ScrapPct"]       is decimal sc ? sc : null,
                r["VersionID"]      as string,
                r["Position"]       is int pos ? pos : null,
                r["Note"]           as string,
                (bool)r["ActiveFlag"],
                r["CreatedBy"]      as string,
                r["CreatedTS"]      is DateTime ct ? ct : null,
                r["ModifiedBy"]     as string,
                r["ModifiedTS"]     is DateTime mt ? mt : null),
            ("@V", (object?)versionId));

    public void InsertBomLine(string bomId, string? parentItemNo, string? compItemNo,
        int bomLevel, decimal qtyPer, string? uom, decimal? scrapPct,
        string versionId, int position, string? note, string createdBy)
        => Exec("""
            INSERT INTO dbo.MD_Bom
                   (BOMID,ParentItemNo,CompItemNo,BOMLevel,QtyPer,UOM,
                    ScrapPct,VersionID,Position,Note,ActiveFlag,CreatedBy,CreatedTS)
            VALUES (@ID,@Par,@Comp,@Lv,@Qty,@UOM,
                    @Scrap,@VID,@Pos,@Note,1,@By,SYSDATETIME())
            """,
            ("@ID",    bomId),     ("@Par",  parentItemNo), ("@Comp", compItemNo),
            ("@Lv",    bomLevel),  ("@Qty",  qtyPer),       ("@UOM",  uom),
            ("@Scrap", scrapPct),  ("@VID",  versionId),    ("@Pos",  position),
            ("@Note",  note),      ("@By",   createdBy));

    public void UpdateBomLine(string bomId, string? parentItemNo, string? compItemNo,
        int bomLevel, decimal qtyPer, string? uom, decimal? scrapPct,
        int position, string? note, string modifiedBy)
        => Exec("""
            UPDATE dbo.MD_Bom
            SET    ParentItemNo=@Par, CompItemNo=@Comp, BOMLevel=@Lv,
                   QtyPer=@Qty, UOM=@UOM, ScrapPct=@Scrap,
                   Position=@Pos, Note=@Note,
                   ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  BOMID=@ID
            """,
            ("@ID",    bomId),    ("@Par",   parentItemNo), ("@Comp", compItemNo),
            ("@Lv",    bomLevel), ("@Qty",   qtyPer),       ("@UOM",  uom),
            ("@Scrap", scrapPct), ("@Pos",   position),     ("@Note", note),
            ("@By",    modifiedBy));

    public void DeleteBomLine(string bomId)
        => Exec("DELETE dbo.MD_Bom WHERE BOMID=@I", ("@I", bomId));

    // ── Private helpers ──────────────────────────────────────────────────
    private void Exec(string sql, params (string Name, object? Val)[] p)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, Func<SqlDataReader, T> map,
        params (string Name, object? Val)[] p)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        using var rdr = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return list;
    }

    // ── Row types ────────────────────────────────────────────────────────
    public record CodeGroupRow(
        string GroupCode, string? GroupName, string? GroupNameEn,
        string? Description, bool UseFlag,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public record ItemRow(
        string ItemNo, string ItemName,
        string? ItemType, string? ItemCategory, string? CarType, string? DefaultUOM,
        string? RoutingType,
        decimal? MinStock, decimal? MaxStock, decimal? SafetyStock, decimal? UnitCost,
        string? PGN, string? ALC, string? DrawingNo,
        bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public record CodeItemRow(
        string CodeID, string? GroupCode, string? CodeValue,
        string? CodeName, string? CodeNameEn, string? ParentCodeID,
        int? SortOrder, string? Attribute1, bool UseFlag, string? Description,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    /// <summary>현재 UI 문화권(ko/en)에 맞는 CodeName을 반환합니다.</summary>
    public static string LocalName(CodeItemRow r) =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko"
            ? (r.CodeName   ?? r.CodeValue ?? "")
            : (r.CodeNameEn ?? r.CodeName  ?? r.CodeValue ?? "");

    /// <summary>현재 UI 문화권(ko/en)에 맞는 이름을 반환합니다.</summary>
    public static string LocalName(string? ko, string? en) =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko"
            ? (ko ?? en ?? "")
            : (en ?? ko ?? "");

    // ── MD-04 BOP ────────────────────────────────────────────────────────
    public List<BopItemRow> ListBopItems(string? search = null)
    {
        var where = string.IsNullOrWhiteSpace(search)
            ? "" : " WHERE i.ItemNo LIKE @S OR i.ItemName LIKE @S";
        var p = string.IsNullOrWhiteSpace(search)
            ? Array.Empty<(string, object?)>()
            : new[] { ("@S", (object?)("%" + search.Trim() + "%")) };
        return Query($"""
            SELECT i.ItemNo, ISNULL(i.ItemName,'') AS ItemName,
                   COUNT(b.BOPID) AS StepCount
            FROM   dbo.MD_Item i
            LEFT JOIN dbo.MD_Bop b ON b.ItemNo = i.ItemNo
            {where}
            GROUP  BY i.ItemNo, i.ItemName
            ORDER  BY i.ItemNo
            """,
            r => new BopItemRow(
                r.GetString("ItemNo"),
                r.GetString("ItemName"),
                (int)r["StepCount"]), p);
    }

    public List<BopRow> ListBopSteps(string itemNo, string? routingType = null)
    {
        var where2 = routingType is null ? "" : " AND b.RoutingType = @RT";
        var p2 = routingType is null
            ? new[] { ("@N", (object?)itemNo) }
            : new[] { ("@N", (object?)itemNo), ("@RT", (object?)routingType) };
        return Query($"""
            SELECT b.BOPID, b.ItemNo, b.RoutingType, b.StepSeq, b.ProcessCode,
                   b.WorkCenterID, ISNULL(w.WCName,'') AS WCName,
                   b.StdCycleTime, b.StdSetupTime,
                   ISNULL(b.QcRequiredFlag,0) AS QcRequiredFlag,
                   b.StepDescription,
                   ISNULL(b.ActiveFlag,1) AS ActiveFlag,
                   b.CreatedBy, b.CreatedTS, b.ModifiedBy, b.ModifiedTS
            FROM   dbo.MD_Bop b
            LEFT JOIN dbo.MD_WorkCenter w ON w.WCID = b.WorkCenterID
            WHERE  b.ItemNo = @N{where2}
            ORDER  BY b.RoutingType, b.StepSeq
            """,
            r => new BopRow(
                r.GetString("BOPID"),
                r["ItemNo"]         as string,
                r["RoutingType"]    as string,
                r["StepSeq"]        is int sq   ? sq   : null,
                r["ProcessCode"]    as string,
                r["WorkCenterID"]   as string,
                r.GetString("WCName"),
                r["StdCycleTime"]   is decimal ct ? ct : null,
                r["StdSetupTime"]   is decimal st ? st : null,
                (bool)r["QcRequiredFlag"],
                r["StepDescription"] as string,
                (bool)r["ActiveFlag"],
                r["CreatedBy"]      as string,
                r["CreatedTS"]      is DateTime cts ? cts : null,
                r["ModifiedBy"]     as string,
                r["ModifiedTS"]     is DateTime mts ? mts : null), p2);
    }

    public void InsertBopStep(string bopId, string itemNo, string? routingType,
        int stepSeq, string? processCode, string? workCenterId,
        decimal? stdCycleTime, decimal? stdSetupTime, bool qcRequired,
        string? stepDesc, bool activeFlag, string createdBy)
        => Exec("""
            INSERT INTO dbo.MD_Bop
                   (BOPID,ItemNo,RoutingType,StepSeq,ProcessCode,WorkCenterID,
                    StdCycleTime,StdSetupTime,QcRequiredFlag,StepDescription,
                    ActiveFlag,CreatedBy,CreatedTS)
            VALUES (@ID,@No,@RT,@Seq,@PC,@WC,
                    @CT,@ST,@QC,@Desc,
                    @Active,@By,SYSDATETIME())
            """,
            ("@ID",    bopId),       ("@No",   itemNo),      ("@RT",   routingType),
            ("@Seq",   stepSeq),     ("@PC",   processCode), ("@WC",   workCenterId),
            ("@CT",    stdCycleTime),("@ST",   stdSetupTime),("@QC",   qcRequired),
            ("@Desc",  stepDesc),    ("@Active",activeFlag), ("@By",   createdBy));

    public void UpdateBopStep(string bopId, string? routingType,
        int stepSeq, string? processCode, string? workCenterId,
        decimal? stdCycleTime, decimal? stdSetupTime, bool qcRequired,
        string? stepDesc, bool activeFlag, string modifiedBy)
        => Exec("""
            UPDATE dbo.MD_Bop
            SET    RoutingType=@RT, StepSeq=@Seq, ProcessCode=@PC,
                   WorkCenterID=@WC, StdCycleTime=@CT, StdSetupTime=@ST,
                   QcRequiredFlag=@QC, StepDescription=@Desc,
                   ActiveFlag=@Active, ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  BOPID=@ID
            """,
            ("@ID",    bopId),       ("@RT",   routingType), ("@Seq",  stepSeq),
            ("@PC",    processCode), ("@WC",   workCenterId),("@CT",   stdCycleTime),
            ("@ST",    stdSetupTime),("@QC",   qcRequired),  ("@Desc", stepDesc),
            ("@Active",activeFlag),  ("@By",   modifiedBy));

    public void DeleteBopStep(string bopId)
        => Exec("DELETE dbo.MD_Bop WHERE BOPID=@I", ("@I", bopId));

    public record BopItemRow(string ItemNo, string ItemName, int StepCount);

    public record BopRow(
        string BOPID, string? ItemNo, string? RoutingType,
        int? StepSeq, string? ProcessCode,
        string? WorkCenterID, string WCName,
        decimal? StdCycleTime, decimal? StdSetupTime,
        bool QcRequiredFlag, string? StepDescription, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public record BomVersionRow(
        string VersionID, string? RootItemNo, string RootItemName,
        string? VersionNo, DateOnly? EffFrom, DateOnly? EffTo,
        string? ChangeType, string? ChangeReason,
        string? RequestedBy, string? ApprovedBy, DateTime? ApprovedTS,
        string Status,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public record BomRow(
        string BOMID,
        string? ParentItemNo, string ParentItemName,
        string? CompItemNo,   string CompItemName,
        int? BOMLevel, decimal? QtyPer, string? UOM, decimal? ScrapPct,
        string? VersionID, int? Position, string? Note, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_Customer (MD-05)                                             ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record CustomerRow(
        string CustomerID, string? CustomerCode,
        string? CustomerName, string? CustomerNameEn,
        string? CustomerType, string? BizRegNo, string? Country,
        string? ContactPerson, string? ContactPhone, string? ContactEmail,
        bool EDIFlag, string? CurrencyCode, string? Status,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public List<CustomerRow> ListCustomers(string? search = null)
    {
        using var conn = _factory.OpenConnection();
        var sql = """
            SELECT CustomerID, CustomerCode, CustomerName, CustomerNameEn,
                   CustomerType, BizRegNo, Country,
                   ContactPerson, ContactPhone, ContactEmail,
                   ISNULL(EDIFlag,0) AS EDIFlag, CurrencyCode, Status,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
            FROM dbo.MD_Customer
            WHERE @S IS NULL
               OR CustomerID   LIKE '%'+@S+'%'
               OR CustomerCode LIKE '%'+@S+'%'
               OR CustomerName LIKE '%'+@S+'%'
            ORDER BY CustomerName;
            """;
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@S", SqlDbType.NVarChar, 80).Value = (object?)search ?? DBNull.Value;
        using var r = cmd.ExecuteReader();
        var list = new List<CustomerRow>();
        while (r.Read())
            list.Add(new CustomerRow(
                (string)r["CustomerID"],
                r["CustomerCode"] as string,
                r["CustomerName"] as string,
                r["CustomerNameEn"] as string,
                r["CustomerType"] as string,
                r["BizRegNo"] as string,
                r["Country"] as string,
                r["ContactPerson"] as string,
                r["ContactPhone"] as string,
                r["ContactEmail"] as string,
                (bool)r["EDIFlag"],
                r["CurrencyCode"] as string,
                r["Status"] as string,
                r["CreatedBy"] as string,
                r["CreatedTS"] as DateTime?,
                r["ModifiedBy"] as string,
                r["ModifiedTS"] as DateTime?));
        return list;
    }

    public bool CustomerExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Customer WHERE CustomerID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertCustomer(
        string customerId, string? customerCode, string? customerName, string? customerNameEn,
        string? customerType, string? bizRegNo, string? country,
        string? contactPerson, string? contactPhone, string? contactEmail,
        bool ediFlag, string? currencyCode, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_Customer
              (CustomerID,CustomerCode,CustomerName,CustomerNameEn,
               CustomerType,BizRegNo,Country,
               ContactPerson,ContactPhone,ContactEmail,
               EDIFlag,CurrencyCode,Status,CreatedBy,CreatedTS)
            VALUES
              (@ID,@Code,@Name,@NameEn,
               @Type,@Biz,@Cty,
               @CP,@Ph,@Em,
               @EDI,@Cur,@St,@By,SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@ID",   SqlDbType.VarChar,  20).Value = customerId;
        cmd.Parameters.Add("@Code", SqlDbType.VarChar,  20).Value = (object?)customerCode   ?? DBNull.Value;
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 80).Value = (object?)customerName   ?? DBNull.Value;
        cmd.Parameters.Add("@NameEn",SqlDbType.NVarChar,80).Value = (object?)customerNameEn ?? DBNull.Value;
        cmd.Parameters.Add("@Type", SqlDbType.VarChar,  12).Value = (object?)customerType   ?? DBNull.Value;
        cmd.Parameters.Add("@Biz",  SqlDbType.VarChar,  20).Value = (object?)bizRegNo       ?? DBNull.Value;
        cmd.Parameters.Add("@Cty",  SqlDbType.Char,      3).Value = (object?)country        ?? DBNull.Value;
        cmd.Parameters.Add("@CP",   SqlDbType.NVarChar, 40).Value = (object?)contactPerson  ?? DBNull.Value;
        cmd.Parameters.Add("@Ph",   SqlDbType.VarChar,  20).Value = (object?)contactPhone   ?? DBNull.Value;
        cmd.Parameters.Add("@Em",   SqlDbType.VarChar,  60).Value = (object?)contactEmail   ?? DBNull.Value;
        cmd.Parameters.Add("@EDI",  SqlDbType.Bit).Value          = ediFlag;
        cmd.Parameters.Add("@Cur",  SqlDbType.Char,      3).Value = (object?)currencyCode   ?? DBNull.Value;
        cmd.Parameters.Add("@St",   SqlDbType.VarChar,   8).Value = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@By",   SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateCustomer(
        string customerId, string? customerCode, string? customerName, string? customerNameEn,
        string? customerType, string? bizRegNo, string? country,
        string? contactPerson, string? contactPhone, string? contactEmail,
        bool ediFlag, string? currencyCode, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_Customer SET
              CustomerCode=@Code, CustomerName=@Name, CustomerNameEn=@NameEn,
              CustomerType=@Type, BizRegNo=@Biz, Country=@Cty,
              ContactPerson=@CP, ContactPhone=@Ph, ContactEmail=@Em,
              EDIFlag=@EDI, CurrencyCode=@Cur, Status=@St,
              ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE CustomerID=@ID;
            """, conn);
        cmd.Parameters.Add("@ID",   SqlDbType.VarChar,  20).Value = customerId;
        cmd.Parameters.Add("@Code", SqlDbType.VarChar,  20).Value = (object?)customerCode   ?? DBNull.Value;
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 80).Value = (object?)customerName   ?? DBNull.Value;
        cmd.Parameters.Add("@NameEn",SqlDbType.NVarChar,80).Value = (object?)customerNameEn ?? DBNull.Value;
        cmd.Parameters.Add("@Type", SqlDbType.VarChar,  12).Value = (object?)customerType   ?? DBNull.Value;
        cmd.Parameters.Add("@Biz",  SqlDbType.VarChar,  20).Value = (object?)bizRegNo       ?? DBNull.Value;
        cmd.Parameters.Add("@Cty",  SqlDbType.Char,      3).Value = (object?)country        ?? DBNull.Value;
        cmd.Parameters.Add("@CP",   SqlDbType.NVarChar, 40).Value = (object?)contactPerson  ?? DBNull.Value;
        cmd.Parameters.Add("@Ph",   SqlDbType.VarChar,  20).Value = (object?)contactPhone   ?? DBNull.Value;
        cmd.Parameters.Add("@Em",   SqlDbType.VarChar,  60).Value = (object?)contactEmail   ?? DBNull.Value;
        cmd.Parameters.Add("@EDI",  SqlDbType.Bit).Value          = ediFlag;
        cmd.Parameters.Add("@Cur",  SqlDbType.Char,      3).Value = (object?)currencyCode   ?? DBNull.Value;
        cmd.Parameters.Add("@St",   SqlDbType.VarChar,   8).Value = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@By",   SqlDbType.NVarChar,450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteCustomer(string customerId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Customer WHERE CustomerID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = customerId;
        cmd.ExecuteNonQuery();
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_ShipmentDest (MD-06)                                         ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record ShipmentDestRow(
        string ShipDestID, string? CustomerID, string? CustomerName,
        string? DestName, string? DestType, string? Address, string? Country,
        string? DeliveryDock, int? LeadTimeDays,
        string? DefaultCarrier, string? DeliveryWindow, string? Status,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public List<ShipmentDestRow> ListShipmentDests(string? customerId = null, string? search = null)
    {
        using var conn = _factory.OpenConnection();
        var sql = """
            SELECT d.ShipDestID, d.CustomerID, c.CustomerName,
                   d.DestName, d.DestType, d.Address, d.Country,
                   d.DeliveryDock, d.LeadTimeDays,
                   d.DefaultCarrier, d.DeliveryWindow, d.Status,
                   d.CreatedBy, d.CreatedTS, d.ModifiedBy, d.ModifiedTS
            FROM dbo.MD_ShipmentDest d
            LEFT JOIN dbo.MD_Customer c ON c.CustomerID = d.CustomerID
            WHERE (@CID IS NULL OR d.CustomerID = @CID)
              AND (@S IS NULL
                   OR d.ShipDestID LIKE '%'+@S+'%'
                   OR d.DestName   LIKE '%'+@S+'%'
                   OR d.CustomerID LIKE '%'+@S+'%')
            ORDER BY c.CustomerName, d.DestName;
            """;
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CID", SqlDbType.VarChar, 20).Value  = (object?)customerId ?? DBNull.Value;
        cmd.Parameters.Add("@S",   SqlDbType.NVarChar, 80).Value = (object?)search     ?? DBNull.Value;
        using var r = cmd.ExecuteReader();
        var list = new List<ShipmentDestRow>();
        while (r.Read())
            list.Add(new ShipmentDestRow(
                (string)r["ShipDestID"],
                r["CustomerID"] as string,
                r["CustomerName"] as string,
                r["DestName"] as string,
                r["DestType"] as string,
                r["Address"] as string,
                r["Country"] as string,
                r["DeliveryDock"] as string,
                r["LeadTimeDays"] is int lt ? lt : null,
                r["DefaultCarrier"] as string,
                r["DeliveryWindow"] as string,
                r["Status"] as string,
                r["CreatedBy"] as string,
                r["CreatedTS"] as DateTime?,
                r["ModifiedBy"] as string,
                r["ModifiedTS"] as DateTime?));
        return list;
    }

    public void InsertShipmentDest(
        string shipDestId, string? customerId, string? destName, string? destType,
        string? address, string? country, string? deliveryDock, int? leadTimeDays,
        string? defaultCarrier, string? deliveryWindow, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_ShipmentDest
              (ShipDestID,CustomerID,DestName,DestType,Address,Country,
               DeliveryDock,LeadTimeDays,DefaultCarrier,DeliveryWindow,Status,
               CreatedBy,CreatedTS)
            VALUES
              (@ID,@CID,@DN,@DT,@Addr,@Cty,
               @Dock,@LT,@Car,@DW,@St,
               @By,SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@ID",  SqlDbType.VarChar,  20).Value  = shipDestId;
        cmd.Parameters.Add("@CID", SqlDbType.VarChar,  20).Value  = (object?)customerId     ?? DBNull.Value;
        cmd.Parameters.Add("@DN",  SqlDbType.NVarChar, 80).Value  = (object?)destName       ?? DBNull.Value;
        cmd.Parameters.Add("@DT",  SqlDbType.VarChar,  10).Value  = (object?)destType       ?? DBNull.Value;
        cmd.Parameters.Add("@Addr",SqlDbType.NVarChar,200).Value  = (object?)address        ?? DBNull.Value;
        cmd.Parameters.Add("@Cty", SqlDbType.Char,      3).Value  = (object?)country        ?? DBNull.Value;
        cmd.Parameters.Add("@Dock",SqlDbType.VarChar,  20).Value  = (object?)deliveryDock   ?? DBNull.Value;
        cmd.Parameters.Add("@LT",  SqlDbType.Int).Value           = (object?)leadTimeDays   ?? DBNull.Value;
        cmd.Parameters.Add("@Car", SqlDbType.NVarChar, 40).Value  = (object?)defaultCarrier ?? DBNull.Value;
        cmd.Parameters.Add("@DW",  SqlDbType.VarChar,  40).Value  = (object?)deliveryWindow ?? DBNull.Value;
        cmd.Parameters.Add("@St",  SqlDbType.VarChar,   8).Value  = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@By",  SqlDbType.VarChar,  50).Value  = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateShipmentDest(
        string shipDestId, string? customerId, string? destName, string? destType,
        string? address, string? country, string? deliveryDock, int? leadTimeDays,
        string? defaultCarrier, string? deliveryWindow, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_ShipmentDest SET
              CustomerID=@CID, DestName=@DN, DestType=@DT,
              Address=@Addr, Country=@Cty,
              DeliveryDock=@Dock, LeadTimeDays=@LT,
              DefaultCarrier=@Car, DeliveryWindow=@DW, Status=@St,
              ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE ShipDestID=@ID;
            """, conn);
        cmd.Parameters.Add("@ID",  SqlDbType.VarChar,  20).Value  = shipDestId;
        cmd.Parameters.Add("@CID", SqlDbType.VarChar,  20).Value  = (object?)customerId     ?? DBNull.Value;
        cmd.Parameters.Add("@DN",  SqlDbType.NVarChar, 80).Value  = (object?)destName       ?? DBNull.Value;
        cmd.Parameters.Add("@DT",  SqlDbType.VarChar,  10).Value  = (object?)destType       ?? DBNull.Value;
        cmd.Parameters.Add("@Addr",SqlDbType.NVarChar,200).Value  = (object?)address        ?? DBNull.Value;
        cmd.Parameters.Add("@Cty", SqlDbType.Char,      3).Value  = (object?)country        ?? DBNull.Value;
        cmd.Parameters.Add("@Dock",SqlDbType.VarChar,  20).Value  = (object?)deliveryDock   ?? DBNull.Value;
        cmd.Parameters.Add("@LT",  SqlDbType.Int).Value           = (object?)leadTimeDays   ?? DBNull.Value;
        cmd.Parameters.Add("@Car", SqlDbType.NVarChar, 40).Value  = (object?)defaultCarrier ?? DBNull.Value;
        cmd.Parameters.Add("@DW",  SqlDbType.VarChar,  40).Value  = (object?)deliveryWindow ?? DBNull.Value;
        cmd.Parameters.Add("@St",  SqlDbType.VarChar,   8).Value  = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@By",  SqlDbType.NVarChar,450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteShipmentDest(string shipDestId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_ShipmentDest WHERE ShipDestID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = shipDestId;
        cmd.ExecuteNonQuery();
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_Vendor (MD-07)                                               ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record VendorRow(
        string VendorID, string? VendorName, string? VendorType, string? VendorCategory,
        string? BizRegNo, string? ContactPerson, string? Phone, string? Email,
        string? ScmURL, bool EdiFlag, decimal? OtdTargetRate,
        string? PaymentTerms, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public List<VendorRow> ListVendors(string? search = null)
    {
        using var conn = _factory.OpenConnection();
        var sql = """
            SELECT VendorID, VendorName, VendorType, VendorCategory,
                   BizRegNo, ContactPerson, Phone, Email, ScmURL,
                   ISNULL(EdiFlag,0) AS EdiFlag, OtdTargetRate,
                   PaymentTerms, ISNULL(ActiveFlag,1) AS ActiveFlag,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
            FROM dbo.MD_Vendor
            WHERE @S IS NULL
               OR VendorID   LIKE '%'+@S+'%'
               OR VendorName LIKE '%'+@S+'%'
            ORDER BY VendorName;
            """;
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@S", SqlDbType.NVarChar, 80).Value = (object?)search ?? DBNull.Value;
        using var r = cmd.ExecuteReader();
        var list = new List<VendorRow>();
        while (r.Read())
        {
            var otd = r["OtdTargetRate"];
            list.Add(new VendorRow(
                (string)r["VendorID"],
                r["VendorName"] as string,
                r["VendorType"] as string,
                r["VendorCategory"] as string,
                r["BizRegNo"] as string,
                r["ContactPerson"] as string,
                r["Phone"] as string,
                r["Email"] as string,
                r["ScmURL"] as string,
                (bool)r["EdiFlag"],
                otd is DBNull ? null : (decimal?)Convert.ToDecimal(otd),
                r["PaymentTerms"] as string,
                (bool)r["ActiveFlag"],
                r["CreatedBy"] as string,
                r["CreatedTS"] as DateTime?,
                r["ModifiedBy"] as string,
                r["ModifiedTS"] as DateTime?));
        }
        return list;
    }

    public bool VendorExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Vendor WHERE VendorID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertVendor(
        string vendorId, string? vendorName, string? vendorType, string? vendorCategory,
        string? bizRegNo, string? contactPerson, string? phone, string? email,
        string? scmUrl, bool ediFlag, decimal? otdTargetRate,
        string? paymentTerms, bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_Vendor
              (VendorID,VendorName,VendorType,VendorCategory,
               BizRegNo,ContactPerson,Phone,Email,ScmURL,
               EdiFlag,OtdTargetRate,PaymentTerms,ActiveFlag,
               CreatedBy,CreatedTS)
            VALUES
              (@ID,@Name,@Type,@Cat,
               @Biz,@CP,@Ph,@Em,@Url,
               @EDI,@Otd,@Pay,@Active,
               @By,SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@ID",   SqlDbType.VarChar,  20).Value = vendorId;
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 80).Value = (object?)vendorName     ?? DBNull.Value;
        cmd.Parameters.Add("@Type", SqlDbType.VarChar,  10).Value = (object?)vendorType     ?? DBNull.Value;
        cmd.Parameters.Add("@Cat",  SqlDbType.NVarChar, 30).Value = (object?)vendorCategory ?? DBNull.Value;
        cmd.Parameters.Add("@Biz",  SqlDbType.VarChar,  20).Value = (object?)bizRegNo       ?? DBNull.Value;
        cmd.Parameters.Add("@CP",   SqlDbType.NVarChar, 40).Value = (object?)contactPerson  ?? DBNull.Value;
        cmd.Parameters.Add("@Ph",   SqlDbType.VarChar,  20).Value = (object?)phone          ?? DBNull.Value;
        cmd.Parameters.Add("@Em",   SqlDbType.VarChar,  60).Value = (object?)email          ?? DBNull.Value;
        cmd.Parameters.Add("@Url",  SqlDbType.VarChar, 255).Value = (object?)scmUrl         ?? DBNull.Value;
        cmd.Parameters.Add("@EDI",  SqlDbType.Bit).Value          = ediFlag;
        cmd.Parameters.Add("@Otd",  SqlDbType.Decimal).Value      = (object?)otdTargetRate  ?? DBNull.Value;
        cmd.Parameters["@Otd"].Precision = 5; cmd.Parameters["@Otd"].Scale = 2;
        cmd.Parameters.Add("@Pay",  SqlDbType.VarChar,  30).Value = (object?)paymentTerms   ?? DBNull.Value;
        cmd.Parameters.Add("@Active",SqlDbType.Bit).Value         = activeFlag;
        cmd.Parameters.Add("@By",   SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateVendor(
        string vendorId, string? vendorName, string? vendorType, string? vendorCategory,
        string? bizRegNo, string? contactPerson, string? phone, string? email,
        string? scmUrl, bool ediFlag, decimal? otdTargetRate,
        string? paymentTerms, bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_Vendor SET
              VendorName=@Name, VendorType=@Type, VendorCategory=@Cat,
              BizRegNo=@Biz, ContactPerson=@CP, Phone=@Ph, Email=@Em, ScmURL=@Url,
              EdiFlag=@EDI, OtdTargetRate=@Otd, PaymentTerms=@Pay,
              ActiveFlag=@Active,
              ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE VendorID=@ID;
            """, conn);
        cmd.Parameters.Add("@ID",   SqlDbType.VarChar,  20).Value = vendorId;
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 80).Value = (object?)vendorName     ?? DBNull.Value;
        cmd.Parameters.Add("@Type", SqlDbType.VarChar,  10).Value = (object?)vendorType     ?? DBNull.Value;
        cmd.Parameters.Add("@Cat",  SqlDbType.NVarChar, 30).Value = (object?)vendorCategory ?? DBNull.Value;
        cmd.Parameters.Add("@Biz",  SqlDbType.VarChar,  20).Value = (object?)bizRegNo       ?? DBNull.Value;
        cmd.Parameters.Add("@CP",   SqlDbType.NVarChar, 40).Value = (object?)contactPerson  ?? DBNull.Value;
        cmd.Parameters.Add("@Ph",   SqlDbType.VarChar,  20).Value = (object?)phone          ?? DBNull.Value;
        cmd.Parameters.Add("@Em",   SqlDbType.VarChar,  60).Value = (object?)email          ?? DBNull.Value;
        cmd.Parameters.Add("@Url",  SqlDbType.VarChar, 255).Value = (object?)scmUrl         ?? DBNull.Value;
        cmd.Parameters.Add("@EDI",  SqlDbType.Bit).Value          = ediFlag;
        cmd.Parameters.Add("@Otd",  SqlDbType.Decimal).Value      = (object?)otdTargetRate  ?? DBNull.Value;
        cmd.Parameters["@Otd"].Precision = 5; cmd.Parameters["@Otd"].Scale = 2;
        cmd.Parameters.Add("@Pay",  SqlDbType.VarChar,  30).Value = (object?)paymentTerms   ?? DBNull.Value;
        cmd.Parameters.Add("@Active",SqlDbType.Bit).Value         = activeFlag;
        cmd.Parameters.Add("@By",   SqlDbType.NVarChar,450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteVendor(string vendorId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Vendor WHERE VendorID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = vendorId;
        cmd.ExecuteNonQuery();
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_Mold (MD-007)                                                ║
    // ╚══════════════════════════════════════════════════════════════════╝

    // CarType/RefCode/AssyInjResultFlag/CumulativeShots 는 SIS 정합화(migrate_mold_master.sql)
    // 추가 컬럼 — 뒤쪽 기본값 파라미터라 기존 위치 인자 호출과 호환된다.
    public record MoldRow(
        string MoldID, string? MoldName,
        int? RatedShots, int? CurrentShots, int? CavityCount, int? Tonnage,
        string? StorageLoc, DateOnly? LastMaintDate, string? Status,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS,
        string? CarType = null, string? RefCode = null,
        bool AssyInjResultFlag = false, long CumulativeShots = 0);

    public List<MoldRow> ListMolds()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT MoldID, MoldName,
                   RatedShots, CurrentShots, CavityCount, Tonnage,
                   StorageLoc, LastMaintDate, Status,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS,
                   CarType, RefCode, AssyInjResultFlag, CumulativeShots
            FROM   dbo.MD_Mold
            ORDER  BY MoldID;
            """, conn);
        using var r = cmd.ExecuteReader();
        var list = new List<MoldRow>();
        while (r.Read())
            list.Add(new MoldRow(
                (string)r["MoldID"],
                r["MoldName"]      as string,
                r["RatedShots"]    is int rs ? rs : null,
                r["CurrentShots"]  is int cs ? cs : null,
                r["CavityCount"]   is int cc ? cc : null,
                r["Tonnage"]       is int tn ? tn : null,
                r["StorageLoc"]    as string,
                r["LastMaintDate"] is DateTime lmd ? DateOnly.FromDateTime(lmd) : null,
                r["Status"]        as string,
                r["CreatedBy"]     as string,
                r["CreatedTS"]     as DateTime?,
                r["ModifiedBy"]    as string,
                r["ModifiedTS"]    as DateTime?,
                r["CarType"]       as string,
                r["RefCode"]       as string,
                r["AssyInjResultFlag"] is bool af && af,
                r["CumulativeShots"]   is long cum ? cum : 0));
        return list;
    }

    public bool MoldExists(string moldId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Mold WHERE MoldID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertMold(
        string moldId, string? moldName,
        int? ratedShots, int? currentShots, int? cavityCount, int? tonnage,
        string? storageLoc, DateOnly? lastMaintDate, string? status, string createdBy,
        string? carType = null, string? refCode = null, bool assyInjResultFlag = false)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_Mold
              (MoldID, MoldName, RatedShots, CurrentShots, CavityCount, Tonnage,
               StorageLoc, LastMaintDate, Status, CarType, RefCode, AssyInjResultFlag,
               CreatedBy, CreatedTS)
            VALUES
              (@ID, @Name, @RS, @CS, @CC, @Ton,
               @Loc, @Maint, @St, @Car, @Ref, @Assy, @By, SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@Car",  SqlDbType.VarChar, 20).Value = (object?)carType ?? DBNull.Value;
        cmd.Parameters.Add("@Ref",  SqlDbType.VarChar, 20).Value = (object?)refCode ?? DBNull.Value;
        cmd.Parameters.Add("@Assy", SqlDbType.Bit).Value         = assyInjResultFlag;
        cmd.Parameters.Add("@ID",    SqlDbType.VarChar,  20).Value = moldId;
        cmd.Parameters.Add("@Name",  SqlDbType.NVarChar, 50).Value = (object?)moldName     ?? DBNull.Value;
        cmd.Parameters.Add("@RS",    SqlDbType.Int).Value          = (object?)ratedShots   ?? DBNull.Value;
        cmd.Parameters.Add("@CS",    SqlDbType.Int).Value          = (object?)currentShots ?? DBNull.Value;
        cmd.Parameters.Add("@CC",    SqlDbType.Int).Value          = (object?)cavityCount  ?? DBNull.Value;
        cmd.Parameters.Add("@Ton",   SqlDbType.Int).Value          = (object?)tonnage      ?? DBNull.Value;
        cmd.Parameters.Add("@Loc",   SqlDbType.VarChar,  20).Value = (object?)storageLoc   ?? DBNull.Value;
        cmd.Parameters.Add("@Maint", SqlDbType.Date).Value         = lastMaintDate.HasValue
                                                                        ? lastMaintDate.Value.ToDateTime(TimeOnly.MinValue)
                                                                        : DBNull.Value;
        cmd.Parameters.Add("@St",    SqlDbType.VarChar,  10).Value = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@By",    SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateMold(
        string moldId, string? moldName,
        int? ratedShots, int? currentShots, int? cavityCount, int? tonnage,
        string? storageLoc, DateOnly? lastMaintDate, string? status, string modifiedBy,
        string? carType = null, string? refCode = null, bool assyInjResultFlag = false)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_Mold SET
              MoldName=@Name, RatedShots=@RS, CurrentShots=@CS,
              CavityCount=@CC, Tonnage=@Ton, StorageLoc=@Loc,
              LastMaintDate=@Maint, Status=@St,
              CarType=@Car, RefCode=@Ref, AssyInjResultFlag=@Assy,
              ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  MoldID=@ID;
            """, conn);
        cmd.Parameters.Add("@Car",  SqlDbType.VarChar, 20).Value = (object?)carType ?? DBNull.Value;
        cmd.Parameters.Add("@Ref",  SqlDbType.VarChar, 20).Value = (object?)refCode ?? DBNull.Value;
        cmd.Parameters.Add("@Assy", SqlDbType.Bit).Value         = assyInjResultFlag;
        cmd.Parameters.Add("@ID",    SqlDbType.VarChar,   20).Value = moldId;
        cmd.Parameters.Add("@Name",  SqlDbType.NVarChar,  50).Value = (object?)moldName     ?? DBNull.Value;
        cmd.Parameters.Add("@RS",    SqlDbType.Int).Value           = (object?)ratedShots   ?? DBNull.Value;
        cmd.Parameters.Add("@CS",    SqlDbType.Int).Value           = (object?)currentShots ?? DBNull.Value;
        cmd.Parameters.Add("@CC",    SqlDbType.Int).Value           = (object?)cavityCount  ?? DBNull.Value;
        cmd.Parameters.Add("@Ton",   SqlDbType.Int).Value           = (object?)tonnage      ?? DBNull.Value;
        cmd.Parameters.Add("@Loc",   SqlDbType.VarChar,   20).Value = (object?)storageLoc   ?? DBNull.Value;
        cmd.Parameters.Add("@Maint", SqlDbType.Date).Value          = lastMaintDate.HasValue
                                                                         ? lastMaintDate.Value.ToDateTime(TimeOnly.MinValue)
                                                                         : DBNull.Value;
        cmd.Parameters.Add("@St",    SqlDbType.VarChar,   10).Value = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@By",    SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteMold(string moldId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Mold WHERE MoldID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// ZPD20041 삭제 가드: 금형이 제품정보(MD_MoldItem)·라인배정(MD_MoldLine)에서
    /// 사용 중이면 삭제를 막기 위한 사용처 카운트.
    /// </summary>
    public (int ItemCount, int LineCount, int ColorCount) MoldUsage(string moldId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.MD_MoldItem  WHERE MoldID=@I) AS I,
              (SELECT COUNT(*) FROM dbo.MD_MoldLine  WHERE MoldID=@I) AS L,
              (SELECT COUNT(*) FROM dbo.MD_MoldColor WHERE MoldID=@I) AS C;
            """, conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? (Convert.ToInt32(r["I"]), Convert.ToInt32(r["L"]), Convert.ToInt32(r["C"]))
            : (0, 0, 0);
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_MoldColor (ZPD20041 — 금형 색상)                             ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record MoldColorRow(string MoldID, string Color, string? CreatedBy, DateTime? CreatedTS);

    public List<MoldColorRow> ListMoldColors(string moldId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT MoldID, Color, CreatedBy, CreatedTS
            FROM   dbo.MD_MoldColor
            WHERE  MoldID=@I
            ORDER  BY Color;
            """, conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        using var r = cmd.ExecuteReader();
        var list = new List<MoldColorRow>();
        while (r.Read())
            list.Add(new MoldColorRow(
                (string)r["MoldID"], (string)r["Color"],
                r["CreatedBy"] as string, r["CreatedTS"] as DateTime?));
        return list;
    }

    public void InsertMoldColor(string moldId, string color, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.MD_MoldColor WHERE MoldID=@I AND Color=@C)
              INSERT INTO dbo.MD_MoldColor (MoldID, Color, CreatedBy, CreatedTS)
              VALUES (@I, @C, @By, SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value = moldId;
        cmd.Parameters.Add("@C",  SqlDbType.VarChar, 10).Value = color;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteMoldColor(string moldId, string color)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_MoldColor WHERE MoldID=@I AND Color=@C;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 10).Value = color;
        cmd.ExecuteNonQuery();
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_MoldItem (ZPD20042 — 금형별 제품정보, SIS APM2120)           ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record MoldItemRow(
        string MoldID, string ItemNo, string? ItemName,
        string? Color, int CavitySeq, string? CavityPos,
        decimal? Usage, string? ResinItemNo, decimal? ResinUsage,
        int CavityCount, string? MoldCategory, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<MoldItemRow> ListMoldItems(string moldId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT mi.MoldID, mi.ItemNo, i.ItemName,
                   mi.Color, mi.CavitySeq, mi.CavityPos,
                   mi.[Usage], mi.ResinItemNo, mi.ResinUsage,
                   mi.CavityCount, mi.MoldCategory, mi.ActiveFlag,
                   mi.CreatedBy, mi.CreatedTS, mi.ModifiedBy, mi.ModifiedTS
            FROM   dbo.MD_MoldItem mi
            LEFT   JOIN dbo.MD_Item i ON i.ItemNo = mi.ItemNo
            WHERE  mi.MoldID=@I
            ORDER  BY mi.CavitySeq, mi.ItemNo;
            """, conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        using var r = cmd.ExecuteReader();
        var list = new List<MoldItemRow>();
        while (r.Read())
            list.Add(new MoldItemRow(
                (string)r["MoldID"], (string)r["ItemNo"], r["ItemName"] as string,
                r["Color"] as string, Convert.ToInt32(r["CavitySeq"]), r["CavityPos"] as string,
                r["Usage"]      is decimal u  ? u  : null,
                r["ResinItemNo"] as string,
                r["ResinUsage"] is decimal ru ? ru : null,
                Convert.ToInt32(r["CavityCount"]), r["MoldCategory"] as string,
                r["ActiveFlag"] is bool af && af,
                r["CreatedBy"] as string, r["CreatedTS"] as DateTime?,
                r["ModifiedBy"] as string, r["ModifiedTS"] as DateTime?));
        return list;
    }

    public bool MoldItemExists(string moldId, string itemNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_MoldItem WHERE MoldID=@I AND ItemNo=@P;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 20).Value = itemNo;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertMoldItem(
        string moldId, string itemNo, string? color, int cavitySeq, string? cavityPos,
        decimal? usage, string? resinItemNo, decimal? resinUsage,
        int cavityCount, string? moldCategory, bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_MoldItem
              (MoldID, ItemNo, Color, CavitySeq, CavityPos,
               [Usage], ResinItemNo, ResinUsage, CavityCount, MoldCategory, ActiveFlag,
               CreatedBy, CreatedTS)
            VALUES
              (@I, @P, @Col, @Seq, @Pos, @U, @RI, @RU, @CC, @Cat, @Act, @By, SYSDATETIME());
            """, conn);
        FillMoldItemParams(cmd, moldId, itemNo, color, cavitySeq, cavityPos,
                           usage, resinItemNo, resinUsage, cavityCount, moldCategory, activeFlag);
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateMoldItem(
        string moldId, string itemNo, string? color, int cavitySeq, string? cavityPos,
        decimal? usage, string? resinItemNo, decimal? resinUsage,
        int cavityCount, string? moldCategory, bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_MoldItem SET
              Color=@Col, CavitySeq=@Seq, CavityPos=@Pos,
              [Usage]=@U, ResinItemNo=@RI, ResinUsage=@RU,
              CavityCount=@CC, MoldCategory=@Cat, ActiveFlag=@Act,
              ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  MoldID=@I AND ItemNo=@P;
            """, conn);
        FillMoldItemParams(cmd, moldId, itemNo, color, cavitySeq, cavityPos,
                           usage, resinItemNo, resinUsage, cavityCount, moldCategory, activeFlag);
        cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    static void FillMoldItemParams(SqlCommand cmd,
        string moldId, string itemNo, string? color, int cavitySeq, string? cavityPos,
        decimal? usage, string? resinItemNo, decimal? resinUsage,
        int cavityCount, string? moldCategory, bool activeFlag)
    {
        cmd.Parameters.Add("@I",   SqlDbType.VarChar, 20).Value = moldId;
        cmd.Parameters.Add("@P",   SqlDbType.VarChar, 20).Value = itemNo;
        cmd.Parameters.Add("@Col", SqlDbType.VarChar, 10).Value = (object?)color        ?? DBNull.Value;
        cmd.Parameters.Add("@Seq", SqlDbType.Int).Value         = cavitySeq;
        cmd.Parameters.Add("@Pos", SqlDbType.VarChar,  4).Value = (object?)cavityPos    ?? DBNull.Value;
        cmd.Parameters.Add("@U",   SqlDbType.Decimal).Value     = (object?)usage        ?? DBNull.Value;
        cmd.Parameters.Add("@RI",  SqlDbType.VarChar, 20).Value = (object?)resinItemNo  ?? DBNull.Value;
        cmd.Parameters.Add("@RU",  SqlDbType.Decimal).Value     = (object?)resinUsage   ?? DBNull.Value;
        cmd.Parameters.Add("@CC",  SqlDbType.Int).Value         = cavityCount;
        cmd.Parameters.Add("@Cat", SqlDbType.VarChar, 20).Value = (object?)moldCategory ?? DBNull.Value;
        cmd.Parameters.Add("@Act", SqlDbType.Bit).Value         = activeFlag;
    }

    public void DeleteMoldItem(string moldId, string itemNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_MoldItem WHERE MoldID=@I AND ItemNo=@P;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 20).Value = itemNo;
        cmd.ExecuteNonQuery();
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_MoldLine (ZPD20043 — 라인별 금형, SIS APM2130)               ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record MoldLineRow(
        string LineCode, string MoldID, string? MoldName,
        decimal? UPH, decimal? PrepTime,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<MoldLineRow> ListMoldLines(string lineCode)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT ml.LineCode, ml.MoldID, m.MoldName, ml.UPH, ml.PrepTime,
                   ml.CreatedBy, ml.CreatedTS, ml.ModifiedBy, ml.ModifiedTS
            FROM   dbo.MD_MoldLine ml
            LEFT   JOIN dbo.MD_Mold m ON m.MoldID = ml.MoldID
            WHERE  ml.LineCode=@L
            ORDER  BY ml.MoldID;
            """, conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineCode;
        using var r = cmd.ExecuteReader();
        var list = new List<MoldLineRow>();
        while (r.Read())
            list.Add(new MoldLineRow(
                (string)r["LineCode"], (string)r["MoldID"], r["MoldName"] as string,
                r["UPH"]      is decimal u ? u : null,
                r["PrepTime"] is decimal p ? p : null,
                r["CreatedBy"] as string, r["CreatedTS"] as DateTime?,
                r["ModifiedBy"] as string, r["ModifiedTS"] as DateTime?));
        return list;
    }

    public bool MoldLineExists(string lineCode, string moldId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_MoldLine WHERE LineCode=@L AND MoldID=@I;", conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineCode;
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertMoldLine(string lineCode, string moldId, decimal? uph, decimal? prepTime, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy, CreatedTS)
            VALUES (@L, @I, @U, @P, @By, SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineCode;
        cmd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value = moldId;
        cmd.Parameters.Add("@U",  SqlDbType.Decimal).Value     = (object?)uph      ?? DBNull.Value;
        cmd.Parameters.Add("@P",  SqlDbType.Decimal).Value     = (object?)prepTime ?? DBNull.Value;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateMoldLine(string lineCode, string moldId, decimal? uph, decimal? prepTime, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_MoldLine SET
              UPH=@U, PrepTime=@P, ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  LineCode=@L AND MoldID=@I;
            """, conn);
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineCode;
        cmd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value = moldId;
        cmd.Parameters.Add("@U",  SqlDbType.Decimal).Value     = (object?)uph      ?? DBNull.Value;
        cmd.Parameters.Add("@P",  SqlDbType.Decimal).Value     = (object?)prepTime ?? DBNull.Value;
        cmd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteMoldLine(string lineCode, string moldId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_MoldLine WHERE LineCode=@L AND MoldID=@I;", conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineCode;
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = moldId;
        cmd.ExecuteNonQuery();
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_WorkCenter (MD-006)                                          ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record WorkCenterRow(
        string WCID, string? WCName, string? ProcessType,
        string? LineID, int? DailyCapacity, int? StdManpower,
        string? CostCenterCode, string? LocationDesc, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public List<WorkCenterRow> ListWorkCenters()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT WCID, WCName, ProcessType, LineID,
                   DailyCapacity, StdManpower, CostCenterCode, LocationDesc,
                   ISNULL(ActiveFlag,1) AS ActiveFlag,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
            FROM   dbo.MD_WorkCenter
            ORDER  BY WCID;
            """, conn);
        using var r = cmd.ExecuteReader();
        var list = new List<WorkCenterRow>();
        while (r.Read())
            list.Add(new WorkCenterRow(
                (string)r["WCID"],
                r["WCName"]         as string,
                r["ProcessType"]    as string,
                r["LineID"]         as string,
                r["DailyCapacity"]  is int dc ? dc : null,
                r["StdManpower"]    is int sm ? sm : null,
                r["CostCenterCode"] as string,
                r["LocationDesc"]   as string,
                (bool)r["ActiveFlag"],
                r["CreatedBy"]      as string,
                r["CreatedTS"]      as DateTime?,
                r["ModifiedBy"]     as string,
                r["ModifiedTS"]     as DateTime?));
        return list;
    }

    public bool WorkCenterExists(string wcid)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_WorkCenter WHERE WCID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = wcid;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertWorkCenter(
        string wcid, string? wcName, string? processType,
        string? lineId, int? dailyCapacity, int? stdManpower,
        string? costCenterCode, string? locationDesc,
        bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_WorkCenter
              (WCID, WCName, ProcessType, LineID, DailyCapacity, StdManpower,
               CostCenterCode, LocationDesc, ActiveFlag, CreatedBy, CreatedTS)
            VALUES
              (@ID, @Name, @PT, @Line, @Cap, @Man,
               @CC, @Loc, @Act, @By, SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@ID",   SqlDbType.VarChar,  20).Value = wcid;
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 50).Value = (object?)wcName         ?? DBNull.Value;
        cmd.Parameters.Add("@PT",   SqlDbType.VarChar,  16).Value = (object?)processType    ?? DBNull.Value;
        cmd.Parameters.Add("@Line", SqlDbType.VarChar,  20).Value = (object?)lineId         ?? DBNull.Value;
        cmd.Parameters.Add("@Cap",  SqlDbType.Int).Value          = (object?)dailyCapacity  ?? DBNull.Value;
        cmd.Parameters.Add("@Man",  SqlDbType.Int).Value          = (object?)stdManpower    ?? DBNull.Value;
        cmd.Parameters.Add("@CC",   SqlDbType.VarChar,  20).Value = (object?)costCenterCode ?? DBNull.Value;
        cmd.Parameters.Add("@Loc",  SqlDbType.NVarChar, 60).Value = (object?)locationDesc   ?? DBNull.Value;
        cmd.Parameters.Add("@Act",  SqlDbType.Bit).Value          = activeFlag;
        cmd.Parameters.Add("@By",   SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateWorkCenter(
        string wcid, string? wcName, string? processType,
        string? lineId, int? dailyCapacity, int? stdManpower,
        string? costCenterCode, string? locationDesc,
        bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_WorkCenter SET
              WCName=@Name, ProcessType=@PT, LineID=@Line,
              DailyCapacity=@Cap, StdManpower=@Man,
              CostCenterCode=@CC, LocationDesc=@Loc,
              ActiveFlag=@Act, ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  WCID=@ID;
            """, conn);
        cmd.Parameters.Add("@ID",   SqlDbType.VarChar,   20).Value = wcid;
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar,  50).Value = (object?)wcName         ?? DBNull.Value;
        cmd.Parameters.Add("@PT",   SqlDbType.VarChar,   16).Value = (object?)processType    ?? DBNull.Value;
        cmd.Parameters.Add("@Line", SqlDbType.VarChar,   20).Value = (object?)lineId         ?? DBNull.Value;
        cmd.Parameters.Add("@Cap",  SqlDbType.Int).Value           = (object?)dailyCapacity  ?? DBNull.Value;
        cmd.Parameters.Add("@Man",  SqlDbType.Int).Value           = (object?)stdManpower    ?? DBNull.Value;
        cmd.Parameters.Add("@CC",   SqlDbType.VarChar,   20).Value = (object?)costCenterCode ?? DBNull.Value;
        cmd.Parameters.Add("@Loc",  SqlDbType.NVarChar,  60).Value = (object?)locationDesc   ?? DBNull.Value;
        cmd.Parameters.Add("@Act",  SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@By",   SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteWorkCenter(string wcid)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_WorkCenter WHERE WCID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = wcid;
        cmd.ExecuteNonQuery();
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_Station (MD-002)                                             ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record StationRow(
        string StationCode, string? StationName, string? StationNameEn, string? LineID,
        string? StationType, string? ProcessCode,
        int? OrderSeq, string? Status,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public List<StationRow> ListStations()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT StationCode, StationName, StationNameEn, LineID,
                   StationType, ProcessCode, OrderSeq, Status,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
            FROM   dbo.MD_Station
            ORDER  BY LineID, OrderSeq, StationCode;
            """, conn);
        using var r = cmd.ExecuteReader();
        var list = new List<StationRow>();
        while (r.Read())
            list.Add(new StationRow(
                (string)r["StationCode"],
                r["StationName"]   as string,
                r["StationNameEn"] as string,
                r["LineID"]        as string,
                r["StationType"]   as string,
                r["ProcessCode"]   as string,
                r["OrderSeq"]      is int os ? os : null,
                r["Status"]        as string,
                r["CreatedBy"]     as string,
                r["CreatedTS"]     as DateTime?,
                r["ModifiedBy"]    as string,
                r["ModifiedTS"]    as DateTime?));
        return list;
    }

    public bool StationExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Station WHERE StationCode=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertStation(
        string stationId, string? stationName, string? stationNameEn, string? lineId,
        string? stationType, string? processCode,
        int? orderSeq, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_Station
              (StationCode, StationName, StationNameEn, LineID, StationType, ProcessCode, OrderSeq, Status, CreatedBy, CreatedTS)
            VALUES
              (@ID, @Name, @NameEn, @Line, @Type, @Proc, @Seq, @St, @By, SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@ID",    SqlDbType.VarChar,  20).Value = stationId;
        cmd.Parameters.Add("@Name",  SqlDbType.NVarChar, 60).Value = (object?)stationName   ?? DBNull.Value;
        cmd.Parameters.Add("@NameEn",SqlDbType.NVarChar, 60).Value = (object?)stationNameEn ?? DBNull.Value;
        cmd.Parameters.Add("@Line",  SqlDbType.VarChar,  20).Value = (object?)lineId        ?? DBNull.Value;
        cmd.Parameters.Add("@Type",  SqlDbType.VarChar,  20).Value = (object?)stationType   ?? DBNull.Value;
        cmd.Parameters.Add("@Proc",  SqlDbType.VarChar,  10).Value = (object?)processCode   ?? DBNull.Value;
        cmd.Parameters.Add("@Seq",   SqlDbType.Int).Value          = (object?)orderSeq      ?? DBNull.Value;
        cmd.Parameters.Add("@St",    SqlDbType.VarChar,  10).Value = (object?)status        ?? DBNull.Value;
        cmd.Parameters.Add("@By",    SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateStation(
        string stationId, string? stationName, string? stationNameEn, string? lineId,
        string? stationType, string? processCode,
        int? orderSeq, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_Station SET
              StationName=@Name, StationNameEn=@NameEn, LineID=@Line, StationType=@Type, ProcessCode=@Proc,
              OrderSeq=@Seq, Status=@St, ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE  StationCode=@ID;
            """, conn);
        cmd.Parameters.Add("@ID",    SqlDbType.VarChar,   20).Value = stationId;
        cmd.Parameters.Add("@Name",  SqlDbType.NVarChar,  60).Value = (object?)stationName   ?? DBNull.Value;
        cmd.Parameters.Add("@NameEn",SqlDbType.NVarChar,  60).Value = (object?)stationNameEn ?? DBNull.Value;
        cmd.Parameters.Add("@Line",  SqlDbType.VarChar,   20).Value = (object?)lineId        ?? DBNull.Value;
        cmd.Parameters.Add("@Type",  SqlDbType.VarChar,   20).Value = (object?)stationType   ?? DBNull.Value;
        cmd.Parameters.Add("@Proc",  SqlDbType.VarChar,   10).Value = (object?)processCode   ?? DBNull.Value;
        cmd.Parameters.Add("@Seq",   SqlDbType.Int).Value           = (object?)orderSeq      ?? DBNull.Value;
        cmd.Parameters.Add("@St",    SqlDbType.VarChar,   10).Value = (object?)status        ?? DBNull.Value;
        cmd.Parameters.Add("@By",    SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteStation(string stationId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Station WHERE StationCode=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = stationId;
        cmd.ExecuteNonQuery();
    }

    // ╔══════════════════════════════════════════════════════════════════╗
    // ║  MD_Line (MD-08)                                                 ║
    // ╚══════════════════════════════════════════════════════════════════╝

    public record LineRow(
        string LineID, string? LineName, string? LineNameEn, string? LineType,
        string? PlantCode, string? DefaultWCID,
        int? DailyCap, string? ShiftPattern,
        bool RfidEnabledFlag, string? Status,
        string? CreatedBy, DateTime? CreatedTS,
        string? ModifiedBy, DateTime? ModifiedTS);

    public List<LineRow> ListLines(string? search = null)
    {
        using var conn = _factory.OpenConnection();
        var sql = """
            SELECT LineID, LineName, LineNameEn, LineType, PlantCode, DefaultWCID,
                   DailyCap, ShiftPattern,
                   ISNULL(RfidEnabledFlag,0) AS RfidEnabledFlag, Status,
                   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
            FROM dbo.MD_Line
            WHERE @S IS NULL
               OR LineID   LIKE '%'+@S+'%'
               OR LineName LIKE '%'+@S+'%'
            ORDER BY LineID;
            """;
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@S", SqlDbType.NVarChar, 50).Value = (object?)search ?? DBNull.Value;
        using var r = cmd.ExecuteReader();
        var list = new List<LineRow>();
        while (r.Read())
            list.Add(new LineRow(
                (string)r["LineID"],
                r["LineName"]   as string,
                r["LineNameEn"] as string,
                r["LineType"]   as string,
                r["PlantCode"]  as string,
                r["DefaultWCID"] as string,
                r["DailyCap"] is int dc ? dc : null,
                r["ShiftPattern"] as string,
                (bool)r["RfidEnabledFlag"],
                r["Status"]     as string,
                r["CreatedBy"]  as string,
                r["CreatedTS"]  as DateTime?,
                r["ModifiedBy"] as string,
                r["ModifiedTS"] as DateTime?));
        return list;
    }

    public bool LineExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Line WHERE LineID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertLine(
        string lineId, string? lineName, string? lineNameEn, string? lineType,
        string? plantCode, string? defaultWcId,
        int? dailyCap, string? shiftPattern,
        bool rfidEnabled, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.MD_Line
              (LineID,LineName,LineNameEn,LineType,PlantCode,DefaultWCID,
               DailyCap,ShiftPattern,RfidEnabledFlag,Status,
               CreatedBy,CreatedTS)
            VALUES
              (@ID,@Name,@NameEn,@Type,@Plant,@WC,
               @Cap,@Shift,@Rfid,@St,
               @By,SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@ID",    SqlDbType.VarChar,  20).Value = lineId;
        cmd.Parameters.Add("@Name",  SqlDbType.NVarChar, 50).Value = (object?)lineName    ?? DBNull.Value;
        cmd.Parameters.Add("@NameEn",SqlDbType.NVarChar, 50).Value = (object?)lineNameEn  ?? DBNull.Value;
        cmd.Parameters.Add("@Type",  SqlDbType.VarChar,  16).Value = (object?)lineType    ?? DBNull.Value;
        cmd.Parameters.Add("@Plant", SqlDbType.VarChar,  20).Value = (object?)plantCode   ?? DBNull.Value;
        cmd.Parameters.Add("@WC",    SqlDbType.VarChar,  20).Value = (object?)defaultWcId ?? DBNull.Value;
        cmd.Parameters.Add("@Cap",   SqlDbType.Int).Value          = (object?)dailyCap    ?? DBNull.Value;
        cmd.Parameters.Add("@Shift", SqlDbType.VarChar,  20).Value = (object?)shiftPattern ?? DBNull.Value;
        cmd.Parameters.Add("@Rfid",  SqlDbType.Bit).Value          = rfidEnabled;
        cmd.Parameters.Add("@St",    SqlDbType.VarChar,  10).Value = (object?)status      ?? DBNull.Value;
        cmd.Parameters.Add("@By",    SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateLine(
        string lineId, string? lineName, string? lineNameEn, string? lineType,
        string? plantCode, string? defaultWcId,
        int? dailyCap, string? shiftPattern,
        bool rfidEnabled, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_Line SET
              LineName=@Name, LineNameEn=@NameEn, LineType=@Type,
              PlantCode=@Plant, DefaultWCID=@WC,
              DailyCap=@Cap, ShiftPattern=@Shift,
              RfidEnabledFlag=@Rfid, Status=@St,
              ModifiedBy=@By, ModifiedTS=SYSDATETIME()
            WHERE LineID=@ID;
            """, conn);
        cmd.Parameters.Add("@ID",    SqlDbType.VarChar,  20).Value = lineId;
        cmd.Parameters.Add("@Name",  SqlDbType.NVarChar, 50).Value = (object?)lineName    ?? DBNull.Value;
        cmd.Parameters.Add("@NameEn",SqlDbType.NVarChar, 50).Value = (object?)lineNameEn  ?? DBNull.Value;
        cmd.Parameters.Add("@Type",  SqlDbType.VarChar,  16).Value = (object?)lineType    ?? DBNull.Value;
        cmd.Parameters.Add("@Plant", SqlDbType.VarChar,  20).Value = (object?)plantCode   ?? DBNull.Value;
        cmd.Parameters.Add("@WC",    SqlDbType.VarChar,  20).Value = (object?)defaultWcId ?? DBNull.Value;
        cmd.Parameters.Add("@Cap",   SqlDbType.Int).Value          = (object?)dailyCap    ?? DBNull.Value;
        cmd.Parameters.Add("@Shift", SqlDbType.VarChar,  20).Value = (object?)shiftPattern ?? DBNull.Value;
        cmd.Parameters.Add("@Rfid",  SqlDbType.Bit).Value          = rfidEnabled;
        cmd.Parameters.Add("@St",    SqlDbType.VarChar,  10).Value = (object?)status      ?? DBNull.Value;
        cmd.Parameters.Add("@By",    SqlDbType.NVarChar,450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteLine(string lineId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Line WHERE LineID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = lineId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_DefectCode ────────────────────────────────────────────────
    public record DefectCodeRow(
        string DefectCode, string? DefectName, string? DefectNameEn,
        string? ProcessCode, string? DefectCategory, string? SeverityLevel,
        string? DispositionDefault, string? DefaultCauseCode,
        bool ParetoFlag, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<DefectCodeRow> ListDefectCodes()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT DefectCode,DefectName,DefectNameEn,ProcessCode,DefectCategory," +
            "SeverityLevel,DispositionDefault,DefaultCauseCode,ISNULL(ParetoFlag,0)," +
            "Status,CreatedBy,CreatedTS,ModifiedBy,ModifiedTS " +
            "FROM dbo.MD_DefectCode ORDER BY DefectCode;", conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<DefectCodeRow>();
        while (rdr.Read())
            list.Add(new DefectCodeRow(
                rdr.GetString(0),
                rdr.IsDBNull(1)  ? null : rdr.GetString(1),
                rdr.IsDBNull(2)  ? null : rdr.GetString(2),
                rdr.IsDBNull(3)  ? null : rdr.GetString(3),
                rdr.IsDBNull(4)  ? null : rdr.GetString(4),
                rdr.IsDBNull(5)  ? null : rdr.GetString(5),
                rdr.IsDBNull(6)  ? null : rdr.GetString(6),
                rdr.IsDBNull(7)  ? null : rdr.GetString(7),
                rdr.GetBoolean(8),
                rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                rdr.IsDBNull(10) ? null : rdr.GetString(10),
                rdr.IsDBNull(11) ? null : rdr.GetDateTime(11),
                rdr.IsDBNull(12) ? null : rdr.GetString(12),
                rdr.IsDBNull(13) ? null : rdr.GetDateTime(13)));
        return list;
    }

    public bool DefectCodeExists(string code)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_DefectCode WHERE DefectCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 16).Value = code;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertDefectCode(
        string code, string? name, string? nameEn,
        string? processCode, string? category, string? severity,
        string? disposition, string? causeCode, bool paretoFlag,
        string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_DefectCode" +
            "(DefectCode,DefectName,DefectNameEn,ProcessCode,DefectCategory," +
            "SeverityLevel,DispositionDefault,DefaultCauseCode,ParetoFlag,Status,CreatedBy)" +
            " VALUES(@C,@N,@NE,@PC,@CAT,@SEV,@DIS,@CC,@PF,@ST,@CB);", conn);
        cmd.Parameters.Add("@C",   SqlDbType.VarChar,  16).Value = code;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar, 60).Value = (object?)name   ?? DBNull.Value;
        cmd.Parameters.Add("@NE",  SqlDbType.NVarChar, 60).Value = (object?)nameEn ?? DBNull.Value;
        cmd.Parameters.Add("@PC",  SqlDbType.VarChar,  10).Value = (object?)processCode  ?? DBNull.Value;
        cmd.Parameters.Add("@CAT", SqlDbType.VarChar,  14).Value = (object?)category     ?? DBNull.Value;
        cmd.Parameters.Add("@SEV", SqlDbType.VarChar,   8).Value = (object?)severity     ?? DBNull.Value;
        cmd.Parameters.Add("@DIS", SqlDbType.VarChar,  10).Value = (object?)disposition  ?? DBNull.Value;
        cmd.Parameters.Add("@CC",  SqlDbType.VarChar,  16).Value = (object?)causeCode    ?? DBNull.Value;
        cmd.Parameters.Add("@PF",  SqlDbType.Bit).Value          = paretoFlag;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   8).Value = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateDefectCode(
        string code, string? name, string? nameEn,
        string? processCode, string? category, string? severity,
        string? disposition, string? causeCode, bool paretoFlag,
        string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_DefectCode SET " +
            "DefectName=@N,DefectNameEn=@NE,ProcessCode=@PC,DefectCategory=@CAT," +
            "SeverityLevel=@SEV,DispositionDefault=@DIS,DefaultCauseCode=@CC," +
            "ParetoFlag=@PF,Status=@ST,ModifiedTS=SYSDATETIME(),ModifiedBy=@MB " +
            "WHERE DefectCode=@C;", conn);
        cmd.Parameters.Add("@C",   SqlDbType.VarChar,  16).Value = code;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar, 60).Value = (object?)name   ?? DBNull.Value;
        cmd.Parameters.Add("@NE",  SqlDbType.NVarChar, 60).Value = (object?)nameEn ?? DBNull.Value;
        cmd.Parameters.Add("@PC",  SqlDbType.VarChar,  10).Value = (object?)processCode  ?? DBNull.Value;
        cmd.Parameters.Add("@CAT", SqlDbType.VarChar,  14).Value = (object?)category     ?? DBNull.Value;
        cmd.Parameters.Add("@SEV", SqlDbType.VarChar,   8).Value = (object?)severity     ?? DBNull.Value;
        cmd.Parameters.Add("@DIS", SqlDbType.VarChar,  10).Value = (object?)disposition  ?? DBNull.Value;
        cmd.Parameters.Add("@CC",  SqlDbType.VarChar,  16).Value = (object?)causeCode    ?? DBNull.Value;
        cmd.Parameters.Add("@PF",  SqlDbType.Bit).Value          = paretoFlag;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   8).Value = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteDefectCode(string code)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_DefectCode WHERE DefectCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 16).Value = code;
        cmd.ExecuteNonQuery();
    }

    // ── MD_DefectCause ───────────────────────────────────────────────
    public record DefectCauseRow(
        string CauseCode, string? CauseName, string? CauseCategory,
        string? ParentCauseCode, string? ProcessCode,
        bool RootCauseFlag, string? CorrectiveGuide, string? ResponsibleDept,
        int? SortOrder, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<DefectCauseRow> ListDefectCauses()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT CauseCode,CauseName,CauseCategory,ParentCauseCode,ProcessCode," +
            "ISNULL(RootCauseFlag,0),CorrectiveGuide,ResponsibleDept,SortOrder," +
            "Status,CreatedBy,CreatedTS,ModifiedBy,ModifiedTS " +
            "FROM dbo.MD_DefectCause ORDER BY ISNULL(SortOrder,9999),CauseCode;", conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<DefectCauseRow>();
        while (rdr.Read())
            list.Add(new DefectCauseRow(
                rdr.GetString(0),
                rdr.IsDBNull(1)  ? null : rdr.GetString(1),
                rdr.IsDBNull(2)  ? null : rdr.GetString(2),
                rdr.IsDBNull(3)  ? null : rdr.GetString(3),
                rdr.IsDBNull(4)  ? null : rdr.GetString(4),
                rdr.GetBoolean(5),
                rdr.IsDBNull(6)  ? null : rdr.GetString(6),
                rdr.IsDBNull(7)  ? null : rdr.GetString(7),
                rdr.IsDBNull(8)  ? null : rdr.GetInt32(8),
                rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                rdr.IsDBNull(10) ? null : rdr.GetString(10),
                rdr.IsDBNull(11) ? null : rdr.GetDateTime(11),
                rdr.IsDBNull(12) ? null : rdr.GetString(12),
                rdr.IsDBNull(13) ? null : rdr.GetDateTime(13)));
        return list;
    }

    public bool DefectCauseExists(string code)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_DefectCause WHERE CauseCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 16).Value = code;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertDefectCause(
        string code, string? name, string? category,
        string? parentCode, string? processCode,
        bool rootCauseFlag, string? correctiveGuide, string? responsibleDept,
        int? sortOrder, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_DefectCause" +
            "(CauseCode,CauseName,CauseCategory,ParentCauseCode,ProcessCode," +
            "RootCauseFlag,CorrectiveGuide,ResponsibleDept,SortOrder,Status,CreatedBy)" +
            " VALUES(@C,@N,@CAT,@PC2,@PC,@RF,@CG,@RD,@SO,@ST,@CB);", conn);
        cmd.Parameters.Add("@C",   SqlDbType.VarChar,   16).Value = code;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar,  60).Value = (object?)name            ?? DBNull.Value;
        cmd.Parameters.Add("@CAT", SqlDbType.VarChar,    9).Value = (object?)category        ?? DBNull.Value;
        cmd.Parameters.Add("@PC2", SqlDbType.VarChar,   16).Value = (object?)parentCode      ?? DBNull.Value;
        cmd.Parameters.Add("@PC",  SqlDbType.VarChar,   10).Value = (object?)processCode     ?? DBNull.Value;
        cmd.Parameters.Add("@RF",  SqlDbType.Bit).Value           = rootCauseFlag;
        cmd.Parameters.Add("@CG",  SqlDbType.NVarChar, 200).Value = (object?)correctiveGuide ?? DBNull.Value;
        cmd.Parameters.Add("@RD",  SqlDbType.NVarChar,  30).Value = (object?)responsibleDept ?? DBNull.Value;
        cmd.Parameters.Add("@SO",  SqlDbType.Int).Value           = (object?)sortOrder       ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,    8).Value = (object?)status          ?? DBNull.Value;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateDefectCause(
        string code, string? name, string? category,
        string? parentCode, string? processCode,
        bool rootCauseFlag, string? correctiveGuide, string? responsibleDept,
        int? sortOrder, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_DefectCause SET " +
            "CauseName=@N,CauseCategory=@CAT,ParentCauseCode=@PC2,ProcessCode=@PC," +
            "RootCauseFlag=@RF,CorrectiveGuide=@CG,ResponsibleDept=@RD," +
            "SortOrder=@SO,Status=@ST,ModifiedTS=SYSDATETIME(),ModifiedBy=@MB " +
            "WHERE CauseCode=@C;", conn);
        cmd.Parameters.Add("@C",   SqlDbType.VarChar,   16).Value = code;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar,  60).Value = (object?)name            ?? DBNull.Value;
        cmd.Parameters.Add("@CAT", SqlDbType.VarChar,    9).Value = (object?)category        ?? DBNull.Value;
        cmd.Parameters.Add("@PC2", SqlDbType.VarChar,   16).Value = (object?)parentCode      ?? DBNull.Value;
        cmd.Parameters.Add("@PC",  SqlDbType.VarChar,   10).Value = (object?)processCode     ?? DBNull.Value;
        cmd.Parameters.Add("@RF",  SqlDbType.Bit).Value           = rootCauseFlag;
        cmd.Parameters.Add("@CG",  SqlDbType.NVarChar, 200).Value = (object?)correctiveGuide ?? DBNull.Value;
        cmd.Parameters.Add("@RD",  SqlDbType.NVarChar,  30).Value = (object?)responsibleDept ?? DBNull.Value;
        cmd.Parameters.Add("@SO",  SqlDbType.Int).Value           = (object?)sortOrder       ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,    8).Value = (object?)status          ?? DBNull.Value;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteDefectCause(string code)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_DefectCause WHERE CauseCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 16).Value = code;
        cmd.ExecuteNonQuery();
    }

    // ── MD_Equipment ─────────────────────────────────────────────────
    public record EquipmentRow(
        string EquipID, string? EquipName, string? EquipType,
        string? LineID, string? WCID, string? MakerModel,
        DateOnly? InstallDate, decimal? TheoreticalCycle, decimal? TargetOEE,
        string? PlcAddress, string? Status, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<EquipmentRow> ListEquipments()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT EquipID,EquipName,EquipType,LineID,WCID,MakerModel," +
            "InstallDate,TheoreticalCycle,TargetOEE,PlcAddress,Status," +
            "ISNULL(ActiveFlag,1),CreatedBy,CreatedTS,ModifiedBy,ModifiedTS " +
            "FROM dbo.MD_Equipment ORDER BY EquipID;", conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<EquipmentRow>();
        while (rdr.Read())
            list.Add(new EquipmentRow(
                rdr.GetString(0),
                rdr.IsDBNull(1)  ? null : rdr.GetString(1),
                rdr.IsDBNull(2)  ? null : rdr.GetString(2),
                rdr.IsDBNull(3)  ? null : rdr.GetString(3),
                rdr.IsDBNull(4)  ? null : rdr.GetString(4),
                rdr.IsDBNull(5)  ? null : rdr.GetString(5),
                rdr.IsDBNull(6)  ? null : DateOnly.FromDateTime(rdr.GetDateTime(6)),
                rdr.IsDBNull(7)  ? null : rdr.GetDecimal(7),
                rdr.IsDBNull(8)  ? null : rdr.GetDecimal(8),
                rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                rdr.IsDBNull(10) ? null : rdr.GetString(10),
                rdr.GetBoolean(11),
                rdr.IsDBNull(12) ? null : rdr.GetString(12),
                rdr.IsDBNull(13) ? null : rdr.GetDateTime(13),
                rdr.IsDBNull(14) ? null : rdr.GetString(14),
                rdr.IsDBNull(15) ? null : rdr.GetDateTime(15)));
        return list;
    }

    public bool EquipmentExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Equipment WHERE EquipID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertEquipment(
        string equipId, string? name, string? type,
        string? lineId, string? wcid, string? makerModel,
        DateOnly? installDate, decimal? theoreticalCycle, decimal? targetOee,
        string? plcAddress, string? status, bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_Equipment" +
            "(EquipID,EquipName,EquipType,LineID,WCID,MakerModel," +
            "InstallDate,TheoreticalCycle,TargetOEE,PlcAddress,Status,ActiveFlag,CreatedBy)" +
            " VALUES(@I,@N,@T,@L,@W,@M,@ID,@TC,@OEE,@PLC,@ST,@AF,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value = equipId;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar,  50).Value = (object?)name          ?? DBNull.Value;
        cmd.Parameters.Add("@T",   SqlDbType.VarChar,   16).Value = (object?)type           ?? DBNull.Value;
        cmd.Parameters.Add("@L",   SqlDbType.VarChar,   20).Value = (object?)lineId         ?? DBNull.Value;
        cmd.Parameters.Add("@W",   SqlDbType.VarChar,   20).Value = (object?)wcid           ?? DBNull.Value;
        cmd.Parameters.Add("@M",   SqlDbType.NVarChar,  60).Value = (object?)makerModel     ?? DBNull.Value;
        cmd.Parameters.Add("@ID",  SqlDbType.Date).Value          = installDate.HasValue ? (object)installDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@TC",  SqlDbType.Decimal).Value       = (object?)theoreticalCycle ?? DBNull.Value;
        cmd.Parameters["@TC"].Precision = 8; cmd.Parameters["@TC"].Scale = 2;
        cmd.Parameters.Add("@OEE", SqlDbType.Decimal).Value       = (object?)targetOee      ?? DBNull.Value;
        cmd.Parameters["@OEE"].Precision = 5; cmd.Parameters["@OEE"].Scale = 2;
        cmd.Parameters.Add("@PLC", SqlDbType.VarChar,   40).Value = (object?)plcAddress     ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,    8).Value = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateEquipment(
        string equipId, string? name, string? type,
        string? lineId, string? wcid, string? makerModel,
        DateOnly? installDate, decimal? theoreticalCycle, decimal? targetOee,
        string? plcAddress, string? status, bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_Equipment SET " +
            "EquipName=@N,EquipType=@T,LineID=@L,WCID=@W,MakerModel=@M," +
            "InstallDate=@ID,TheoreticalCycle=@TC,TargetOEE=@OEE," +
            "PlcAddress=@PLC,Status=@ST,ActiveFlag=@AF," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB " +
            "WHERE EquipID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value = equipId;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar,  50).Value = (object?)name          ?? DBNull.Value;
        cmd.Parameters.Add("@T",   SqlDbType.VarChar,   16).Value = (object?)type           ?? DBNull.Value;
        cmd.Parameters.Add("@L",   SqlDbType.VarChar,   20).Value = (object?)lineId         ?? DBNull.Value;
        cmd.Parameters.Add("@W",   SqlDbType.VarChar,   20).Value = (object?)wcid           ?? DBNull.Value;
        cmd.Parameters.Add("@M",   SqlDbType.NVarChar,  60).Value = (object?)makerModel     ?? DBNull.Value;
        cmd.Parameters.Add("@ID",  SqlDbType.Date).Value          = installDate.HasValue ? (object)installDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@TC",  SqlDbType.Decimal).Value       = (object?)theoreticalCycle ?? DBNull.Value;
        cmd.Parameters["@TC"].Precision = 8; cmd.Parameters["@TC"].Scale = 2;
        cmd.Parameters.Add("@OEE", SqlDbType.Decimal).Value       = (object?)targetOee      ?? DBNull.Value;
        cmd.Parameters["@OEE"].Precision = 5; cmd.Parameters["@OEE"].Scale = 2;
        cmd.Parameters.Add("@PLC", SqlDbType.VarChar,   40).Value = (object?)plcAddress     ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,    8).Value = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteEquipment(string equipId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Equipment WHERE EquipID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = equipId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_Oven ──────────────────────────────────────────────────────
    public record OvenRow(
        string OvenID, string? OvenName, string? LineID,
        int? ZoneCount, int? TargetTemp, int? Tolerance, int? DwellSec,
        decimal? ConveyorSpeed, decimal? MaxLoadKg, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<OvenRow> ListOvens()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT OvenID,OvenName,LineID,ZoneCount,TargetTemp,Tolerance,DwellSec," +
            "ConveyorSpeed,MaxLoadKg,Status,CreatedBy,CreatedTS,ModifiedBy,ModifiedTS " +
            "FROM dbo.MD_Oven ORDER BY OvenID;", conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<OvenRow>();
        while (rdr.Read())
            list.Add(new OvenRow(
                rdr.GetString(0),
                rdr.IsDBNull(1)  ? null : rdr.GetString(1),
                rdr.IsDBNull(2)  ? null : rdr.GetString(2),
                rdr.IsDBNull(3)  ? null : rdr.GetInt32(3),
                rdr.IsDBNull(4)  ? null : rdr.GetInt32(4),
                rdr.IsDBNull(5)  ? null : rdr.GetInt32(5),
                rdr.IsDBNull(6)  ? null : rdr.GetInt32(6),
                rdr.IsDBNull(7)  ? null : rdr.GetDecimal(7),
                rdr.IsDBNull(8)  ? null : rdr.GetDecimal(8),
                rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                rdr.IsDBNull(10) ? null : rdr.GetString(10),
                rdr.IsDBNull(11) ? null : rdr.GetDateTime(11),
                rdr.IsDBNull(12) ? null : rdr.GetString(12),
                rdr.IsDBNull(13) ? null : rdr.GetDateTime(13)));
        return list;
    }

    public bool OvenExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Oven WHERE OvenID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertOven(
        string ovenId, string? name, string? lineId,
        int? zoneCount, int? targetTemp, int? tolerance, int? dwellSec,
        decimal? conveyorSpeed, decimal? maxLoadKg, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_Oven" +
            "(OvenID,OvenName,LineID,ZoneCount,TargetTemp,Tolerance,DwellSec," +
            "ConveyorSpeed,MaxLoadKg,Status,CreatedBy)" +
            " VALUES(@I,@N,@L,@ZC,@TT,@TOL,@DS,@CS,@ML,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,  20).Value = ovenId;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar, 50).Value = (object?)name         ?? DBNull.Value;
        cmd.Parameters.Add("@L",   SqlDbType.VarChar,  20).Value = (object?)lineId        ?? DBNull.Value;
        cmd.Parameters.Add("@ZC",  SqlDbType.Int).Value          = (object?)zoneCount     ?? DBNull.Value;
        cmd.Parameters.Add("@TT",  SqlDbType.Int).Value          = (object?)targetTemp    ?? DBNull.Value;
        cmd.Parameters.Add("@TOL", SqlDbType.Int).Value          = (object?)tolerance     ?? DBNull.Value;
        cmd.Parameters.Add("@DS",  SqlDbType.Int).Value          = (object?)dwellSec      ?? DBNull.Value;
        cmd.Parameters.Add("@CS",  SqlDbType.Decimal).Value      = (object?)conveyorSpeed ?? DBNull.Value;
        cmd.Parameters["@CS"].Precision = 6; cmd.Parameters["@CS"].Scale = 2;
        cmd.Parameters.Add("@ML",  SqlDbType.Decimal).Value      = (object?)maxLoadKg     ?? DBNull.Value;
        cmd.Parameters["@ML"].Precision = 8; cmd.Parameters["@ML"].Scale = 1;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   8).Value = (object?)status        ?? DBNull.Value;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateOven(
        string ovenId, string? name, string? lineId,
        int? zoneCount, int? targetTemp, int? tolerance, int? dwellSec,
        decimal? conveyorSpeed, decimal? maxLoadKg, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_Oven SET " +
            "OvenName=@N,LineID=@L,ZoneCount=@ZC,TargetTemp=@TT,Tolerance=@TOL," +
            "DwellSec=@DS,ConveyorSpeed=@CS,MaxLoadKg=@ML,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB " +
            "WHERE OvenID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,  20).Value = ovenId;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar, 50).Value = (object?)name         ?? DBNull.Value;
        cmd.Parameters.Add("@L",   SqlDbType.VarChar,  20).Value = (object?)lineId        ?? DBNull.Value;
        cmd.Parameters.Add("@ZC",  SqlDbType.Int).Value          = (object?)zoneCount     ?? DBNull.Value;
        cmd.Parameters.Add("@TT",  SqlDbType.Int).Value          = (object?)targetTemp    ?? DBNull.Value;
        cmd.Parameters.Add("@TOL", SqlDbType.Int).Value          = (object?)tolerance     ?? DBNull.Value;
        cmd.Parameters.Add("@DS",  SqlDbType.Int).Value          = (object?)dwellSec      ?? DBNull.Value;
        cmd.Parameters.Add("@CS",  SqlDbType.Decimal).Value      = (object?)conveyorSpeed ?? DBNull.Value;
        cmd.Parameters["@CS"].Precision = 6; cmd.Parameters["@CS"].Scale = 2;
        cmd.Parameters.Add("@ML",  SqlDbType.Decimal).Value      = (object?)maxLoadKg     ?? DBNull.Value;
        cmd.Parameters["@ML"].Precision = 8; cmd.Parameters["@ML"].Scale = 1;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   8).Value = (object?)status        ?? DBNull.Value;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteOven(string ovenId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Oven WHERE OvenID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = ovenId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_Jig ───────────────────────────────────────────────────────
    public record JigRow(
        string JigID, string? JigName, int? HangerCount,
        int? RatedCycle, int? CycleCount, decimal? ReadFailRate,
        string? HealthStatus, DateOnly? LastServiceDate, DateTime? LastUsedTS,
        bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<JigRow> ListJigs()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT JigID,JigName,HangerCount,RatedCycle,CycleCount,ReadFailRate," +
            "HealthStatus,LastServiceDate,LastUsedTS,ISNULL(ActiveFlag,1)," +
            "CreatedBy,CreatedTS,ModifiedBy,ModifiedTS " +
            "FROM dbo.MD_Jig ORDER BY JigID;", conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<JigRow>();
        while (rdr.Read())
            list.Add(new JigRow(
                rdr.GetString(0),
                rdr.IsDBNull(1)  ? null : rdr.GetString(1),
                rdr.IsDBNull(2)  ? null : rdr.GetInt32(2),
                rdr.IsDBNull(3)  ? null : rdr.GetInt32(3),
                rdr.IsDBNull(4)  ? null : rdr.GetInt32(4),
                rdr.IsDBNull(5)  ? null : rdr.GetDecimal(5),
                rdr.IsDBNull(6)  ? null : rdr.GetString(6),
                rdr.IsDBNull(7)  ? null : DateOnly.FromDateTime(rdr.GetDateTime(7)),
                rdr.IsDBNull(8)  ? null : rdr.GetDateTime(8),
                rdr.GetBoolean(9),
                rdr.IsDBNull(10) ? null : rdr.GetString(10),
                rdr.IsDBNull(11) ? null : rdr.GetDateTime(11),
                rdr.IsDBNull(12) ? null : rdr.GetString(12),
                rdr.IsDBNull(13) ? null : rdr.GetDateTime(13)));
        return list;
    }

    public bool JigExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Jig WHERE JigID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertJig(
        string jigId, string? name, int? hangerCount,
        int? ratedCycle, int? cycleCount, decimal? readFailRate,
        string? healthStatus, DateOnly? lastServiceDate,
        bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_Jig" +
            "(JigID,JigName,HangerCount,RatedCycle,CycleCount,ReadFailRate," +
            "HealthStatus,LastServiceDate,ActiveFlag,CreatedBy)" +
            " VALUES(@I,@N,@HC,@RC,@CC,@RF,@HS,@LS,@AF,@CB);", conn);
        cmd.Parameters.Add("@I",  SqlDbType.VarChar,  20).Value = jigId;
        cmd.Parameters.Add("@N",  SqlDbType.NVarChar, 50).Value = (object?)name          ?? DBNull.Value;
        cmd.Parameters.Add("@HC", SqlDbType.Int).Value          = (object?)hangerCount    ?? DBNull.Value;
        cmd.Parameters.Add("@RC", SqlDbType.Int).Value          = (object?)ratedCycle     ?? DBNull.Value;
        cmd.Parameters.Add("@CC", SqlDbType.Int).Value          = (object?)cycleCount     ?? DBNull.Value;
        cmd.Parameters.Add("@RF", SqlDbType.Decimal).Value      = (object?)readFailRate   ?? DBNull.Value;
        cmd.Parameters["@RF"].Precision = 5; cmd.Parameters["@RF"].Scale = 2;
        cmd.Parameters.Add("@HS", SqlDbType.VarChar,   8).Value = (object?)healthStatus   ?? DBNull.Value;
        cmd.Parameters.Add("@LS", SqlDbType.Date).Value         = lastServiceDate.HasValue ? (object)lastServiceDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@AF", SqlDbType.Bit).Value          = activeFlag;
        cmd.Parameters.Add("@CB", SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateJig(
        string jigId, string? name, int? hangerCount,
        int? ratedCycle, int? cycleCount, decimal? readFailRate,
        string? healthStatus, DateOnly? lastServiceDate,
        bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_Jig SET " +
            "JigName=@N,HangerCount=@HC,RatedCycle=@RC,CycleCount=@CC," +
            "ReadFailRate=@RF,HealthStatus=@HS,LastServiceDate=@LS," +
            "ActiveFlag=@AF,ModifiedTS=SYSDATETIME(),ModifiedBy=@MB " +
            "WHERE JigID=@I;", conn);
        cmd.Parameters.Add("@I",  SqlDbType.VarChar,   20).Value = jigId;
        cmd.Parameters.Add("@N",  SqlDbType.NVarChar,  50).Value = (object?)name          ?? DBNull.Value;
        cmd.Parameters.Add("@HC", SqlDbType.Int).Value           = (object?)hangerCount    ?? DBNull.Value;
        cmd.Parameters.Add("@RC", SqlDbType.Int).Value           = (object?)ratedCycle     ?? DBNull.Value;
        cmd.Parameters.Add("@CC", SqlDbType.Int).Value           = (object?)cycleCount     ?? DBNull.Value;
        cmd.Parameters.Add("@RF", SqlDbType.Decimal).Value       = (object?)readFailRate   ?? DBNull.Value;
        cmd.Parameters["@RF"].Precision = 5; cmd.Parameters["@RF"].Scale = 2;
        cmd.Parameters.Add("@HS", SqlDbType.VarChar,    8).Value = (object?)healthStatus   ?? DBNull.Value;
        cmd.Parameters.Add("@LS", SqlDbType.Date).Value          = lastServiceDate.HasValue ? (object)lastServiceDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@AF", SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@MB", SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteJig(string jigId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Jig WHERE JigID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = jigId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_InspectionStandard ────────────────────────────────────────
    public record InspectionStandardRow(
        string InspStdID, string? ItemID, string? ProcessCode, string? InspType,
        string? CharName, decimal? SpecNominal, decimal? SpecLSL, decimal? SpecUSL,
        string? UOM, string? SamplingPlan, string? InspMethod, bool IsCTQ,
        DateOnly? EffectiveDate, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<InspectionStandardRow> ListInspectionStandards()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT InspStdID,ItemID,ProcessCode,InspType,CharName," +
            "SpecNominal,SpecLSL,SpecUSL,UOM,SamplingPlan,InspMethod," +
            "ISNULL(IsCTQ,0),EffectiveDate,Status," +
            "CreatedBy,CreatedTS,ModifiedBy,ModifiedTS " +
            "FROM dbo.MD_InspectionStandard ORDER BY InspStdID;", conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<InspectionStandardRow>();
        while (rdr.Read())
            list.Add(new InspectionStandardRow(
                rdr.GetString(0),
                rdr.IsDBNull(1)  ? null : rdr.GetString(1),
                rdr.IsDBNull(2)  ? null : rdr.GetString(2),
                rdr.IsDBNull(3)  ? null : rdr.GetString(3),
                rdr.IsDBNull(4)  ? null : rdr.GetString(4),
                rdr.IsDBNull(5)  ? null : rdr.GetDecimal(5),
                rdr.IsDBNull(6)  ? null : rdr.GetDecimal(6),
                rdr.IsDBNull(7)  ? null : rdr.GetDecimal(7),
                rdr.IsDBNull(8)  ? null : rdr.GetString(8),
                rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                rdr.IsDBNull(10) ? null : rdr.GetString(10),
                rdr.GetBoolean(11),
                rdr.IsDBNull(12) ? null : DateOnly.FromDateTime(rdr.GetDateTime(12)),
                rdr.IsDBNull(13) ? null : rdr.GetString(13),
                rdr.IsDBNull(14) ? null : rdr.GetString(14),
                rdr.IsDBNull(15) ? null : rdr.GetDateTime(15),
                rdr.IsDBNull(16) ? null : rdr.GetString(16),
                rdr.IsDBNull(17) ? null : rdr.GetDateTime(17)));
        return list;
    }

    public bool InspectionStandardExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_InspectionStandard WHERE InspStdID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertInspectionStandard(
        string id, string? itemId, string? processCode, string? inspType,
        string? charName, decimal? specNominal, decimal? specLsl, decimal? specUsl,
        string? uom, string? samplingPlan, string? inspMethod, bool isCTQ,
        DateOnly? effectiveDate, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_InspectionStandard" +
            "(InspStdID,ItemID,ProcessCode,InspType,CharName," +
            "SpecNominal,SpecLSL,SpecUSL,UOM,SamplingPlan,InspMethod," +
            "IsCTQ,EffectiveDate,Status,CreatedBy)" +
            " VALUES(@I,@ITEM,@PC,@IT,@CN,@SN,@LSL,@USL,@UOM,@SP,@IM,@CTQ,@ED,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",    SqlDbType.VarChar,   20).Value = id;
        cmd.Parameters.Add("@ITEM", SqlDbType.VarChar,   20).Value = (object?)itemId       ?? DBNull.Value;
        cmd.Parameters.Add("@PC",   SqlDbType.VarChar,   10).Value = (object?)processCode  ?? DBNull.Value;
        cmd.Parameters.Add("@IT",   SqlDbType.VarChar,   10).Value = (object?)inspType     ?? DBNull.Value;
        cmd.Parameters.Add("@CN",   SqlDbType.NVarChar,  60).Value = (object?)charName     ?? DBNull.Value;
        cmd.Parameters.Add("@SN",   SqlDbType.Decimal).Value       = (object?)specNominal  ?? DBNull.Value;
        cmd.Parameters["@SN"].Precision = 12; cmd.Parameters["@SN"].Scale = 4;
        cmd.Parameters.Add("@LSL",  SqlDbType.Decimal).Value       = (object?)specLsl      ?? DBNull.Value;
        cmd.Parameters["@LSL"].Precision = 12; cmd.Parameters["@LSL"].Scale = 4;
        cmd.Parameters.Add("@USL",  SqlDbType.Decimal).Value       = (object?)specUsl      ?? DBNull.Value;
        cmd.Parameters["@USL"].Precision = 12; cmd.Parameters["@USL"].Scale = 4;
        cmd.Parameters.Add("@UOM",  SqlDbType.VarChar,   10).Value = (object?)uom          ?? DBNull.Value;
        cmd.Parameters.Add("@SP",   SqlDbType.VarChar,   20).Value = (object?)samplingPlan ?? DBNull.Value;
        cmd.Parameters.Add("@IM",   SqlDbType.NVarChar,  40).Value = (object?)inspMethod   ?? DBNull.Value;
        cmd.Parameters.Add("@CTQ",  SqlDbType.Bit).Value           = isCTQ;
        cmd.Parameters.Add("@ED",   SqlDbType.Date).Value          = effectiveDate.HasValue ? (object)effectiveDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ST",   SqlDbType.VarChar,    8).Value = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@CB",   SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateInspectionStandard(
        string id, string? itemId, string? processCode, string? inspType,
        string? charName, decimal? specNominal, decimal? specLsl, decimal? specUsl,
        string? uom, string? samplingPlan, string? inspMethod, bool isCTQ,
        DateOnly? effectiveDate, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_InspectionStandard SET " +
            "ItemID=@ITEM,ProcessCode=@PC,InspType=@IT,CharName=@CN," +
            "SpecNominal=@SN,SpecLSL=@LSL,SpecUSL=@USL,UOM=@UOM," +
            "SamplingPlan=@SP,InspMethod=@IM,IsCTQ=@CTQ," +
            "EffectiveDate=@ED,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB " +
            "WHERE InspStdID=@I;", conn);
        cmd.Parameters.Add("@I",    SqlDbType.VarChar,   20).Value = id;
        cmd.Parameters.Add("@ITEM", SqlDbType.VarChar,   20).Value = (object?)itemId       ?? DBNull.Value;
        cmd.Parameters.Add("@PC",   SqlDbType.VarChar,   10).Value = (object?)processCode  ?? DBNull.Value;
        cmd.Parameters.Add("@IT",   SqlDbType.VarChar,   10).Value = (object?)inspType     ?? DBNull.Value;
        cmd.Parameters.Add("@CN",   SqlDbType.NVarChar,  60).Value = (object?)charName     ?? DBNull.Value;
        cmd.Parameters.Add("@SN",   SqlDbType.Decimal).Value       = (object?)specNominal  ?? DBNull.Value;
        cmd.Parameters["@SN"].Precision = 12; cmd.Parameters["@SN"].Scale = 4;
        cmd.Parameters.Add("@LSL",  SqlDbType.Decimal).Value       = (object?)specLsl      ?? DBNull.Value;
        cmd.Parameters["@LSL"].Precision = 12; cmd.Parameters["@LSL"].Scale = 4;
        cmd.Parameters.Add("@USL",  SqlDbType.Decimal).Value       = (object?)specUsl      ?? DBNull.Value;
        cmd.Parameters["@USL"].Precision = 12; cmd.Parameters["@USL"].Scale = 4;
        cmd.Parameters.Add("@UOM",  SqlDbType.VarChar,   10).Value = (object?)uom          ?? DBNull.Value;
        cmd.Parameters.Add("@SP",   SqlDbType.VarChar,   20).Value = (object?)samplingPlan ?? DBNull.Value;
        cmd.Parameters.Add("@IM",   SqlDbType.NVarChar,  40).Value = (object?)inspMethod   ?? DBNull.Value;
        cmd.Parameters.Add("@CTQ",  SqlDbType.Bit).Value           = isCTQ;
        cmd.Parameters.Add("@ED",   SqlDbType.Date).Value          = effectiveDate.HasValue ? (object)effectiveDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ST",   SqlDbType.VarChar,    8).Value = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@MB",   SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteInspectionStandard(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_InspectionStandard WHERE InspStdID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        cmd.ExecuteNonQuery();
    }

    // ── MD_Location ──────────────────────────────────────────────────
    public record LocationRow(
        string LocationID, string? LocationName, string? ZoneCode,
        string? Aisle, string? Bay, string? Slot,
        decimal? Capacity, string? LocationType, string? PlantCode, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<LocationRow> ListLocations()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT LocationID,LocationName,ZoneCode,Aisle,Bay,Slot," +
            "Capacity,LocationType,PlantCode,ISNULL(ActiveFlag,1)," +
            "CreatedBy,CreatedTS,ModifiedBy,ModifiedTS " +
            "FROM dbo.MD_Location ORDER BY LocationID;", conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<LocationRow>();
        while (rdr.Read())
            list.Add(new LocationRow(
                rdr.GetString(0),
                rdr.IsDBNull(1)  ? null : rdr.GetString(1),
                rdr.IsDBNull(2)  ? null : rdr.GetString(2),
                rdr.IsDBNull(3)  ? null : rdr.GetString(3),
                rdr.IsDBNull(4)  ? null : rdr.GetString(4),
                rdr.IsDBNull(5)  ? null : rdr.GetString(5),
                rdr.IsDBNull(6)  ? null : rdr.GetDecimal(6),
                rdr.IsDBNull(7)  ? null : rdr.GetString(7),
                rdr.IsDBNull(8)  ? null : rdr.GetString(8),
                rdr.GetBoolean(9),
                rdr.IsDBNull(10) ? null : rdr.GetString(10),
                rdr.IsDBNull(11) ? null : rdr.GetDateTime(11),
                rdr.IsDBNull(12) ? null : rdr.GetString(12),
                rdr.IsDBNull(13) ? null : rdr.GetDateTime(13)));
        return list;
    }

    public bool LocationExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_Location WHERE LocationID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertLocation(
        string locationId, string? name, string? zoneCode,
        string? aisle, string? bay, string? slot,
        decimal? capacity, string? locationType, string? plantCode,
        bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_Location" +
            "(LocationID,LocationName,ZoneCode,Aisle,Bay,Slot," +
            "Capacity,LocationType,PlantCode,ActiveFlag,CreatedBy)" +
            " VALUES(@I,@N,@Z,@A,@B,@S,@CAP,@LT,@PL,@AF,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value = locationId;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar,  60).Value = (object?)name         ?? DBNull.Value;
        cmd.Parameters.Add("@Z",   SqlDbType.VarChar,   10).Value = (object?)zoneCode      ?? DBNull.Value;
        cmd.Parameters.Add("@A",   SqlDbType.VarChar,    5).Value = (object?)aisle         ?? DBNull.Value;
        cmd.Parameters.Add("@B",   SqlDbType.VarChar,    5).Value = (object?)bay           ?? DBNull.Value;
        cmd.Parameters.Add("@S",   SqlDbType.VarChar,    5).Value = (object?)slot          ?? DBNull.Value;
        cmd.Parameters.Add("@CAP", SqlDbType.Decimal).Value       = (object?)capacity      ?? DBNull.Value;
        cmd.Parameters["@CAP"].Precision = 10; cmd.Parameters["@CAP"].Scale = 3;
        cmd.Parameters.Add("@LT",  SqlDbType.VarChar,   20).Value = (object?)locationType  ?? DBNull.Value;
        cmd.Parameters.Add("@PL",  SqlDbType.VarChar,   20).Value = (object?)plantCode     ?? DBNull.Value;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateLocation(
        string locationId, string? name, string? zoneCode,
        string? aisle, string? bay, string? slot,
        decimal? capacity, string? locationType, string? plantCode,
        bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_Location SET " +
            "LocationName=@N,ZoneCode=@Z,Aisle=@A,Bay=@B,Slot=@S," +
            "Capacity=@CAP,LocationType=@LT,PlantCode=@PL,ActiveFlag=@AF," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB " +
            "WHERE LocationID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value = locationId;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar,  60).Value = (object?)name         ?? DBNull.Value;
        cmd.Parameters.Add("@Z",   SqlDbType.VarChar,   10).Value = (object?)zoneCode      ?? DBNull.Value;
        cmd.Parameters.Add("@A",   SqlDbType.VarChar,    5).Value = (object?)aisle         ?? DBNull.Value;
        cmd.Parameters.Add("@B",   SqlDbType.VarChar,    5).Value = (object?)bay           ?? DBNull.Value;
        cmd.Parameters.Add("@S",   SqlDbType.VarChar,    5).Value = (object?)slot          ?? DBNull.Value;
        cmd.Parameters.Add("@CAP", SqlDbType.Decimal).Value       = (object?)capacity      ?? DBNull.Value;
        cmd.Parameters["@CAP"].Precision = 10; cmd.Parameters["@CAP"].Scale = 3;
        cmd.Parameters.Add("@LT",  SqlDbType.VarChar,   20).Value = (object?)locationType  ?? DBNull.Value;
        cmd.Parameters.Add("@PL",  SqlDbType.VarChar,   20).Value = (object?)plantCode     ?? DBNull.Value;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteLocation(string locationId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_Location WHERE LocationID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = locationId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_PaintFabric ───────────────────────────────────────────────
    public record PaintFabricRow(
        string MatLotID, string? MatCode, string? MatName, string? MatType,
        string? LotNo, string? SupplierID, string? UOM,
        decimal? QtyOnHand, DateOnly? ReceiptDate, DateOnly? ExpDate,
        string? StorageReq, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<PaintFabricRow> ListPaintFabrics()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT MatLotID,MatCode,MatName,MatType,LotNo,SupplierID,UOM," +
            "QtyOnHand,ReceiptDate,ExpDate,StorageReq,Status," +
            "CreatedBy,CreatedTS,ModifiedBy,ModifiedTS " +
            "FROM dbo.MD_PaintFabric ORDER BY MatLotID;", conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<PaintFabricRow>();
        while (rdr.Read())
            list.Add(new PaintFabricRow(
                rdr.GetString(0),
                rdr.IsDBNull(1)  ? null : rdr.GetString(1),
                rdr.IsDBNull(2)  ? null : rdr.GetString(2),
                rdr.IsDBNull(3)  ? null : rdr.GetString(3),
                rdr.IsDBNull(4)  ? null : rdr.GetString(4),
                rdr.IsDBNull(5)  ? null : rdr.GetString(5),
                rdr.IsDBNull(6)  ? null : rdr.GetString(6),
                rdr.IsDBNull(7)  ? null : rdr.GetDecimal(7),
                rdr.IsDBNull(8)  ? null : DateOnly.FromDateTime(rdr.GetDateTime(8)),
                rdr.IsDBNull(9)  ? null : DateOnly.FromDateTime(rdr.GetDateTime(9)),
                rdr.IsDBNull(10) ? null : rdr.GetString(10),
                rdr.IsDBNull(11) ? null : rdr.GetString(11),
                rdr.IsDBNull(12) ? null : rdr.GetString(12),
                rdr.IsDBNull(13) ? null : rdr.GetDateTime(13),
                rdr.IsDBNull(14) ? null : rdr.GetString(14),
                rdr.IsDBNull(15) ? null : rdr.GetDateTime(15)));
        return list;
    }

    public bool PaintFabricExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "SELECT 1 FROM dbo.MD_PaintFabric WHERE MatLotID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 24).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertPaintFabric(
        string matLotId, string? matCode, string? matName, string? matType,
        string? lotNo, string? supplierId, string? uom,
        decimal? qtyOnHand, DateOnly? receiptDate, DateOnly? expDate,
        string? storageReq, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_PaintFabric" +
            "(MatLotID,MatCode,MatName,MatType,LotNo,SupplierID,UOM," +
            "QtyOnHand,ReceiptDate,ExpDate,StorageReq,Status,CreatedBy)" +
            " VALUES(@I,@MC,@MN,@MT,@LN,@SID,@UOM,@QTY,@RD,@ED,@SR,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   24).Value = matLotId;
        cmd.Parameters.Add("@MC",  SqlDbType.VarChar,   20).Value = (object?)matCode    ?? DBNull.Value;
        cmd.Parameters.Add("@MN",  SqlDbType.NVarChar,  60).Value = (object?)matName    ?? DBNull.Value;
        cmd.Parameters.Add("@MT",  SqlDbType.VarChar,   14).Value = (object?)matType    ?? DBNull.Value;
        cmd.Parameters.Add("@LN",  SqlDbType.VarChar,   24).Value = (object?)lotNo      ?? DBNull.Value;
        cmd.Parameters.Add("@SID", SqlDbType.VarChar,   20).Value = (object?)supplierId ?? DBNull.Value;
        cmd.Parameters.Add("@UOM", SqlDbType.VarChar,   10).Value = (object?)uom        ?? DBNull.Value;
        cmd.Parameters.Add("@QTY", SqlDbType.Decimal).Value       = (object?)qtyOnHand  ?? DBNull.Value;
        cmd.Parameters["@QTY"].Precision = 12; cmd.Parameters["@QTY"].Scale = 3;
        cmd.Parameters.Add("@RD",  SqlDbType.Date).Value          = receiptDate.HasValue ? (object)receiptDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ED",  SqlDbType.Date).Value          = expDate.HasValue     ? (object)expDate.Value.ToDateTime(TimeOnly.MinValue)     : DBNull.Value;
        cmd.Parameters.Add("@SR",  SqlDbType.NVarChar,  40).Value = (object?)storageReq ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value = (object?)status     ?? DBNull.Value;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdatePaintFabric(
        string matLotId, string? matCode, string? matName, string? matType,
        string? lotNo, string? supplierId, string? uom,
        decimal? qtyOnHand, DateOnly? receiptDate, DateOnly? expDate,
        string? storageReq, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_PaintFabric SET " +
            "MatCode=@MC,MatName=@MN,MatType=@MT,LotNo=@LN,SupplierID=@SID,UOM=@UOM," +
            "QtyOnHand=@QTY,ReceiptDate=@RD,ExpDate=@ED,StorageReq=@SR,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB " +
            "WHERE MatLotID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   24).Value = matLotId;
        cmd.Parameters.Add("@MC",  SqlDbType.VarChar,   20).Value = (object?)matCode    ?? DBNull.Value;
        cmd.Parameters.Add("@MN",  SqlDbType.NVarChar,  60).Value = (object?)matName    ?? DBNull.Value;
        cmd.Parameters.Add("@MT",  SqlDbType.VarChar,   14).Value = (object?)matType    ?? DBNull.Value;
        cmd.Parameters.Add("@LN",  SqlDbType.VarChar,   24).Value = (object?)lotNo      ?? DBNull.Value;
        cmd.Parameters.Add("@SID", SqlDbType.VarChar,   20).Value = (object?)supplierId ?? DBNull.Value;
        cmd.Parameters.Add("@UOM", SqlDbType.VarChar,   10).Value = (object?)uom        ?? DBNull.Value;
        cmd.Parameters.Add("@QTY", SqlDbType.Decimal).Value       = (object?)qtyOnHand  ?? DBNull.Value;
        cmd.Parameters["@QTY"].Precision = 12; cmd.Parameters["@QTY"].Scale = 3;
        cmd.Parameters.Add("@RD",  SqlDbType.Date).Value          = receiptDate.HasValue ? (object)receiptDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ED",  SqlDbType.Date).Value          = expDate.HasValue     ? (object)expDate.Value.ToDateTime(TimeOnly.MinValue)     : DBNull.Value;
        cmd.Parameters.Add("@SR",  SqlDbType.NVarChar,  40).Value = (object?)storageReq ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value = (object?)status     ?? DBNull.Value;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeletePaintFabric(string matLotId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "DELETE FROM dbo.MD_PaintFabric WHERE MatLotID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 24).Value = matLotId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_Uom ───────────────────────────────────────────────────────
    public record UomRow(
        string UOMCode, string? UOMName, string? UOMCategory,
        bool BaseFlag, string? BaseUOM, decimal? ConvFactor,
        int? DecimalPrec, string? Symbol, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<UomRow> ListUoms() => Query("""
        SELECT UOMCode, UOMName, UOMCategory,
               ISNULL(BaseFlag,0) AS BaseFlag, BaseUOM, ConvFactor,
               DecimalPrec, Symbol, ISNULL(ActiveFlag,1) AS ActiveFlag,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_Uom ORDER BY UOMCode
        """, r => new UomRow(
            r.GetString("UOMCode"),
            r["UOMName"]     as string,
            r["UOMCategory"] as string,
            (bool)r["BaseFlag"],
            r["BaseUOM"]     as string,
            r["ConvFactor"]  is decimal cf  ? cf  : null,
            r["DecimalPrec"] is int    dp   ? dp  : null,
            r["Symbol"]      as string,
            (bool)r["ActiveFlag"],
            r["CreatedBy"]   as string,
            r["CreatedTS"]   is DateTime ct ? ct  : null,
            r["ModifiedBy"]  as string,
            r["ModifiedTS"]  is DateTime mt ? mt  : null));

    // ── MD_RfidTag ───────────────────────────────────────────────────
    public record RfidTagRow(
        string TagID, string? EPC, string? JigID, string? TagRole,
        int? HeatRating, string? MountPos,
        DateOnly? InstallDate, int? CycleCount, DateOnly? ReplaceSchedule, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<RfidTagRow> ListRfidTags() => Query("""
        SELECT TagID, EPC, JigID, TagRole,
               HeatRating, MountPos, InstallDate,
               CycleCount, ReplaceSchedule, Status,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_RfidTag ORDER BY TagID
        """, r => new RfidTagRow(
            r.GetString("TagID"),
            r["EPC"]             as string,
            r["JigID"]           as string,
            r["TagRole"]         as string,
            r["HeatRating"]      is int    hr  ? hr  : null,
            r["MountPos"]        as string,
            r["InstallDate"]     is DateTime id1 ? DateOnly.FromDateTime(id1) : null,
            r["CycleCount"]      is int    cc  ? cc  : null,
            r["ReplaceSchedule"] is DateTime rs  ? DateOnly.FromDateTime(rs) : null,
            r["Status"]          as string,
            r["CreatedBy"]       as string,
            r["CreatedTS"]       is DateTime ct  ? ct  : null,
            r["ModifiedBy"]      as string,
            r["ModifiedTS"]      is DateTime mt  ? mt  : null));

    // ── MD_RalColor ──────────────────────────────────────────────────
    public record RalColorRow(
        string RALCode, string? ColorName, string? HexValue,
        string? CurrentPowderLot, int? CureTemp, int? CureDuration,
        int? ElectroV, decimal? ParticleUm, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<RalColorRow> ListRalColors() => Query("""
        SELECT RALCode, ColorName, HexValue,
               CurrentPowderLot, CureTemp, CureDuration,
               ElectroV, ParticleUm, ISNULL(ActiveFlag,1) AS ActiveFlag,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_RalColor ORDER BY RALCode
        """, r => new RalColorRow(
            r.GetString("RALCode"),
            r["ColorName"]        as string,
            r["HexValue"]         as string,
            r["CurrentPowderLot"] as string,
            r["CureTemp"]         is int     tmp ? tmp : null,
            r["CureDuration"]     is int     cdu ? cdu : null,
            r["ElectroV"]         is int     ev  ? ev  : null,
            r["ParticleUm"]       is decimal pu  ? pu  : null,
            (bool)r["ActiveFlag"],
            r["CreatedBy"]        as string,
            r["CreatedTS"]        is DateTime ct  ? ct  : null,
            r["ModifiedBy"]       as string,
            r["ModifiedTS"]       is DateTime mt  ? mt  : null));

    // ── MD_RfidReader ────────────────────────────────────────────────
    public record RfidReaderRow(
        string ReaderID, string? ReaderName, string? GateLocation, string? LineID,
        int? AntennaCount, int? PowerDbm, bool PeTriggerFlag, int? WindowMs,
        string? IpAddress, string? FirmwareVer, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<RfidReaderRow> ListRfidReaders() => Query("""
        SELECT ReaderID, ReaderName, GateLocation, LineID,
               AntennaCount, PowerDbm, ISNULL(PeTriggerFlag,0) AS PeTriggerFlag,
               WindowMs, IpAddress, FirmwareVer, Status,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_RfidReader ORDER BY ReaderID
        """, r => new RfidReaderRow(
            r.GetString("ReaderID"),
            r["ReaderName"]    as string,
            r["GateLocation"]  as string,
            r["LineID"]        as string,
            r["AntennaCount"]  is int ac  ? ac  : null,
            r["PowerDbm"]      is int pdb ? pdb : null,
            (bool)r["PeTriggerFlag"],
            r["WindowMs"]      is int wm  ? wm  : null,
            r["IpAddress"]     as string,
            r["FirmwareVer"]   as string,
            r["Status"]        as string,
            r["CreatedBy"]     as string,
            r["CreatedTS"]     is DateTime ct ? ct : null,
            r["ModifiedBy"]    as string,
            r["ModifiedTS"]    is DateTime mt ? mt : null));

    // ── MD_PackagingSpec ─────────────────────────────────────────────
    public record PackagingSpecRow(
        string PackSpecID, string? ItemID, string? PackType,
        int? QtyPerInner, int? InnerPerOuter, int? OuterPerPallet,
        decimal? NetWeightKg, decimal? GrossWeightKg, string? DimLxWxH,
        bool ReturnableFlag, string? LabelTemplateID, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<PackagingSpecRow> ListPackagingSpecs() => Query("""
        SELECT PackSpecID, ItemID, PackType,
               QtyPerInner, InnerPerOuter, OuterPerPallet,
               NetWeightKg, GrossWeightKg, DimLxWxH,
               ISNULL(ReturnableFlag,0) AS ReturnableFlag,
               LabelTemplateID, Status,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_PackagingSpec ORDER BY PackSpecID
        """, r => new PackagingSpecRow(
            r.GetString("PackSpecID"),
            r["ItemID"]          as string,
            r["PackType"]        as string,
            r["QtyPerInner"]     is int     qi  ? qi  : null,
            r["InnerPerOuter"]   is int     io2 ? io2 : null,
            r["OuterPerPallet"]  is int     op2 ? op2 : null,
            r["NetWeightKg"]     is decimal nw  ? nw  : null,
            r["GrossWeightKg"]   is decimal gw  ? gw  : null,
            r["DimLxWxH"]        as string,
            (bool)r["ReturnableFlag"],
            r["LabelTemplateID"] as string,
            r["Status"]          as string,
            r["CreatedBy"]       as string,
            r["CreatedTS"]       is DateTime ct ? ct : null,
            r["ModifiedBy"]      as string,
            r["ModifiedTS"]      is DateTime mt ? mt : null));

    // ── MD_LabelTemplate ─────────────────────────────────────────────
    public record LabelTemplateRow(
        string LabelTemplateID, string? TemplateName, string? LabelType,
        string? PaperSize, string? BarcodeType, string? CustomerID,
        int? Version, string? PrinterModel, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<LabelTemplateRow> ListLabelTemplates() => Query("""
        SELECT LabelTemplateID, TemplateName, LabelType,
               PaperSize, BarcodeType, CustomerID,
               Version, PrinterModel, Status,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_LabelTemplate ORDER BY LabelTemplateID
        """, r => new LabelTemplateRow(
            r.GetString("LabelTemplateID"),
            r["TemplateName"]  as string,
            r["LabelType"]     as string,
            r["PaperSize"]     as string,
            r["BarcodeType"]   as string,
            r["CustomerID"]    as string,
            r["Version"]       is int lv ? lv : null,
            r["PrinterModel"]  as string,
            r["Status"]        as string,
            r["CreatedBy"]     as string,
            r["CreatedTS"]     is DateTime ct ? ct : null,
            r["ModifiedBy"]    as string,
            r["ModifiedTS"]    is DateTime mt ? mt : null));

    // ── MD_ReasonCode ────────────────────────────────────────────────
    public record ReasonCodeRow(
        string ReasonCode, string? ReasonName, string? ReasonType,
        string? AppliesToModule, bool RequiresComment, bool PlannedFlag,
        int? DisplayOrder, string? Description, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<ReasonCodeRow> ListReasonCodes() => Query("""
        SELECT ReasonCode, ReasonName, ReasonType,
               AppliesToModule, ISNULL(RequiresComment,0) AS RequiresComment,
               ISNULL(PlannedFlag,0) AS PlannedFlag,
               DisplayOrder, Description, Status,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_ReasonCode ORDER BY ISNULL(DisplayOrder,9999), ReasonCode
        """, r => new ReasonCodeRow(
            r.GetString("ReasonCode"),
            r["ReasonName"]       as string,
            r["ReasonType"]       as string,
            r["AppliesToModule"]  as string,
            (bool)r["RequiresComment"],
            (bool)r["PlannedFlag"],
            r["DisplayOrder"]     is int dord ? dord : null,
            r["Description"]      as string,
            r["Status"]           as string,
            r["CreatedBy"]        as string,
            r["CreatedTS"]        is DateTime ct ? ct : null,
            r["ModifiedBy"]       as string,
            r["ModifiedTS"]       is DateTime mt ? mt : null));

    // ── MD_SparePart (마스터) ────────────────────────────────────────
    public record SparePartMasterRow(
        string PartNo, string? PartName, string? Category,
        decimal? UnitCost, string? UOM,
        int? SafetyStock, int? ReorderPoint, int? ReorderQty, int? LeadTimeDays,
        string? SupplierID, string? StorageLoc, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<SparePartMasterRow> ListSparePartMasters() => Query("""
        SELECT PartNo, PartName, Category,
               UnitCost, UOM,
               SafetyStock, ReorderPoint, ReorderQty, LeadTimeDays,
               SupplierID, StorageLoc, ISNULL(ActiveFlag,1) AS ActiveFlag,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_SparePart ORDER BY PartNo
        """, r => new SparePartMasterRow(
            r.GetString("PartNo"),
            r["PartName"]     as string,
            r["Category"]     as string,
            r["UnitCost"]     is decimal uc  ? uc  : null,
            r["UOM"]          as string,
            r["SafetyStock"]  is int     ss2 ? ss2 : null,
            r["ReorderPoint"] is int     rpo ? rpo : null,
            r["ReorderQty"]   is int     rq2 ? rq2 : null,
            r["LeadTimeDays"] is int     ltd ? ltd : null,
            r["SupplierID"]   as string,
            r["StorageLoc"]   as string,
            (bool)r["ActiveFlag"],
            r["CreatedBy"]    as string,
            r["CreatedTS"]    is DateTime ct ? ct : null,
            r["ModifiedBy"]   as string,
            r["ModifiedTS"]   is DateTime mt ? mt : null));

    // ── MD_PmTemplate ────────────────────────────────────────────────
    public record PmTemplateRow(
        string PMTemplateID, string? TemplateName, string? EquipType,
        string? CycleBasis, int? IntervalValue, string? IntervalUnit,
        int? StdDurationMin, bool SafetyLOTOFlag, bool ActiveFlag,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<PmTemplateRow> ListPmTemplates() => Query("""
        SELECT PMTemplateID, TemplateName, EquipType,
               CycleBasis, IntervalValue, IntervalUnit,
               StdDurationMin, ISNULL(SafetyLOTOFlag,0) AS SafetyLOTOFlag,
               ISNULL(ActiveFlag,1) AS ActiveFlag,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_PmTemplate ORDER BY PMTemplateID
        """, r => new PmTemplateRow(
            r.GetString("PMTemplateID"),
            r["TemplateName"]    as string,
            r["EquipType"]       as string,
            r["CycleBasis"]      as string,
            r["IntervalValue"]   is int ivl ? ivl : null,
            r["IntervalUnit"]    as string,
            r["StdDurationMin"]  is int sdm ? sdm : null,
            (bool)r["SafetyLOTOFlag"],
            (bool)r["ActiveFlag"],
            r["CreatedBy"]       as string,
            r["CreatedTS"]       is DateTime ct ? ct : null,
            r["ModifiedBy"]      as string,
            r["ModifiedTS"]      is DateTime mt ? mt : null));

    public record PmTemplateStepRow(
        string PMStepID, string? PMTemplateID, int? StepSeq,
        string? StepDescription, string? AcceptanceCriteria,
        string? RequiredPartNo, decimal? RequiredQty, int? StepDurationMin,
        string? CreatedBy, DateTime? CreatedTS);

    public List<PmTemplateStepRow> ListPmTemplateSteps(string templateId) => Query("""
        SELECT PMStepID, PMTemplateID, StepSeq,
               StepDescription, AcceptanceCriteria,
               RequiredPartNo, RequiredQty, StepDurationMin,
               CreatedBy, CreatedTS
        FROM dbo.MD_PmTemplateStep WHERE PMTemplateID = @T ORDER BY ISNULL(StepSeq,9999)
        """, r => new PmTemplateStepRow(
            r.GetString("PMStepID"),
            r["PMTemplateID"]       as string,
            r["StepSeq"]            is int     sq2 ? sq2 : null,
            r["StepDescription"]    as string,
            r["AcceptanceCriteria"] as string,
            r["RequiredPartNo"]     as string,
            r["RequiredQty"]        is decimal rq3 ? rq3 : null,
            r["StepDurationMin"]    is int     sds ? sds : null,
            r["CreatedBy"]          as string,
            r["CreatedTS"]          is DateTime ct  ? ct  : null),
        ("@T", (object?)templateId));

    // ── MD_LineTimePattern ───────────────────────────────────────────
    public record LineTimePatternRow(
        string PatternID, string? LineID, string? PatternName,
        string? DayType, string? ShiftModel,
        DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
        int? TotalOperatingMin, int? TotalPlannedDownMin,
        string? TimeZone, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<LineTimePatternRow> ListLineTimePatterns() => Query("""
        SELECT PatternID, LineID, PatternName,
               DayType, ShiftModel, EffectiveFrom, EffectiveTo,
               TotalOperatingMin, TotalPlannedDownMin,
               TimeZone, Status,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_LineTimePattern ORDER BY LineID, PatternID
        """, r => new LineTimePatternRow(
            r.GetString("PatternID"),
            r["LineID"]              as string,
            r["PatternName"]         as string,
            r["DayType"]             as string,
            r["ShiftModel"]          as string,
            r["EffectiveFrom"]       is DateTime ef  ? DateOnly.FromDateTime(ef)  : null,
            r["EffectiveTo"]         is DateTime eto ? DateOnly.FromDateTime(eto) : null,
            r["TotalOperatingMin"]   is int     tom ? tom : null,
            r["TotalPlannedDownMin"] is int     tpd ? tpd : null,
            r["TimeZone"]            as string,
            r["Status"]              as string,
            r["CreatedBy"]           as string,
            r["CreatedTS"]           is DateTime ct  ? ct  : null,
            r["ModifiedBy"]          as string,
            r["ModifiedTS"]          is DateTime mt  ? mt  : null));

    public record LineTimeSegmentRow(
        string SegmentID, string? PatternID, int? SeqNo,
        int? StartMin, int? EndMin, string? SegmentState,
        string? ReasonCode, string? ShiftCode, string? Description,
        string? CreatedBy, DateTime? CreatedTS);

    public List<LineTimeSegmentRow> ListLineTimeSegments(string patternId) => Query("""
        SELECT SegmentID, PatternID, SeqNo,
               StartMin, EndMin, SegmentState,
               ReasonCode, ShiftCode, Description,
               CreatedBy, CreatedTS
        FROM dbo.MD_LineTimeSegment WHERE PatternID = @P ORDER BY ISNULL(SeqNo,9999)
        """, r => new LineTimeSegmentRow(
            r.GetString("SegmentID"),
            r["PatternID"]    as string,
            r["SeqNo"]        is int     sno ? sno : null,
            r["StartMin"]     is short   sm1 ? (int)sm1 : (int?)null,
            r["EndMin"]       is short   em1 ? (int)em1 : (int?)null,
            r["SegmentState"] as string,
            r["ReasonCode"]   as string,
            r["ShiftCode"]    as string,
            r["Description"]  as string,
            r["CreatedBy"]    as string,
            r["CreatedTS"]    is DateTime ct ? ct : null),
        ("@P", (object?)patternId));

    // ── MD_Recipe ────────────────────────────────────────────────────
    public record RecipeRow(
        string RecipeID, string? RecipeName, string? RecipeType,
        string? ItemNo, int? CycleTime,
        string? Version, DateOnly? EffectiveDate, string? Status,
        string? CreatedBy, DateTime? CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public List<RecipeRow> ListRecipes() => Query("""
        SELECT RecipeID, RecipeName, RecipeType,
               ItemNo, CycleTime,
               Version, EffectiveDate, Status,
               CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.MD_Recipe ORDER BY RecipeID
        """, r2 => new RecipeRow(
            r2.GetString("RecipeID"),
            r2["RecipeName"]    as string,
            r2["RecipeType"]    as string,
            r2["ItemNo"]        as string,
            r2["CycleTime"]     is int     ctm ? ctm : null,
            r2["Version"]       as string,
            r2["EffectiveDate"] is DateTime erd ? DateOnly.FromDateTime(erd) : null,
            r2["Status"]        as string,
            r2["CreatedBy"]     as string,
            r2["CreatedTS"]     is DateTime ct  ? ct  : null,
            r2["ModifiedBy"]    as string,
            r2["ModifiedTS"]    is DateTime mt  ? mt  : null));

    // ── MD_Uom CRUD ──────────────────────────────────────────────────
    public bool UomExists(string code)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_Uom WHERE UOMCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 20).Value = code;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertUom(string uomCode, string? uomName, string? uomCategory,
        bool baseFlag, string? baseUOM, decimal? convFactor,
        int? decimalPrec, string? symbol, bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_Uom(UOMCode,UOMName,UOMCategory,BaseFlag,BaseUOM," +
            "ConvFactor,DecimalPrec,Symbol,ActiveFlag,CreatedBy)" +
            " VALUES(@C,@N,@CAT,@BF,@BU,@CF,@DP,@SY,@AF,@CB);", conn);
        cmd.Parameters.Add("@C",   SqlDbType.VarChar,  20).Value = uomCode;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar, 50).Value = (object?)uomName      ?? DBNull.Value;
        cmd.Parameters.Add("@CAT", SqlDbType.VarChar,  20).Value = (object?)uomCategory  ?? DBNull.Value;
        cmd.Parameters.Add("@BF",  SqlDbType.Bit).Value          = baseFlag;
        cmd.Parameters.Add("@BU",  SqlDbType.VarChar,  20).Value = (object?)baseUOM      ?? DBNull.Value;
        cmd.Parameters.Add("@CF",  SqlDbType.Decimal).Value      = (object?)convFactor   ?? DBNull.Value;
        if (convFactor.HasValue) { cmd.Parameters["@CF"].Precision = 18; cmd.Parameters["@CF"].Scale = 6; }
        cmd.Parameters.Add("@DP",  SqlDbType.Int).Value          = (object?)decimalPrec  ?? DBNull.Value;
        cmd.Parameters.Add("@SY",  SqlDbType.NVarChar, 10).Value = (object?)symbol       ?? DBNull.Value;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value          = activeFlag;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateUom(string uomCode, string? uomName, string? uomCategory,
        bool baseFlag, string? baseUOM, decimal? convFactor,
        int? decimalPrec, string? symbol, bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_Uom SET UOMName=@N,UOMCategory=@CAT,BaseFlag=@BF,BaseUOM=@BU," +
            "ConvFactor=@CF,DecimalPrec=@DP,Symbol=@SY,ActiveFlag=@AF," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE UOMCode=@C;", conn);
        cmd.Parameters.Add("@C",   SqlDbType.VarChar,  20).Value  = uomCode;
        cmd.Parameters.Add("@N",   SqlDbType.NVarChar, 50).Value  = (object?)uomName     ?? DBNull.Value;
        cmd.Parameters.Add("@CAT", SqlDbType.VarChar,  20).Value  = (object?)uomCategory ?? DBNull.Value;
        cmd.Parameters.Add("@BF",  SqlDbType.Bit).Value           = baseFlag;
        cmd.Parameters.Add("@BU",  SqlDbType.VarChar,  20).Value  = (object?)baseUOM     ?? DBNull.Value;
        cmd.Parameters.Add("@CF",  SqlDbType.Decimal).Value       = (object?)convFactor  ?? DBNull.Value;
        if (convFactor.HasValue) { cmd.Parameters["@CF"].Precision = 18; cmd.Parameters["@CF"].Scale = 6; }
        cmd.Parameters.Add("@DP",  SqlDbType.Int).Value           = (object?)decimalPrec ?? DBNull.Value;
        cmd.Parameters.Add("@SY",  SqlDbType.NVarChar, 10).Value  = (object?)symbol      ?? DBNull.Value;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteUom(string uomCode)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_Uom WHERE UOMCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 20).Value = uomCode;
        cmd.ExecuteNonQuery();
    }

    // ── MD_RfidTag CRUD ──────────────────────────────────────────────
    public bool RfidTagExists(string tagId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_RfidTag WHERE TagID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 30).Value = tagId;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertRfidTag(string tagId, string? epc, string? jigId, string? tagRole,
        int? heatRating, string? mountPos,
        DateOnly? installDate, int? cycleCount, DateOnly? replaceSchedule,
        string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_RfidTag(TagID,EPC,JigID,TagRole,HeatRating,MountPos," +
            "InstallDate,CycleCount,ReplaceSchedule,Status,CreatedBy)" +
            " VALUES(@I,@EP,@JI,@TR,@HR,@MP,@ID,@CC,@RS,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",  SqlDbType.VarChar,  30).Value = tagId;
        cmd.Parameters.Add("@EP", SqlDbType.VarChar,  64).Value = (object?)epc            ?? DBNull.Value;
        cmd.Parameters.Add("@JI", SqlDbType.VarChar,  20).Value = (object?)jigId          ?? DBNull.Value;
        cmd.Parameters.Add("@TR", SqlDbType.VarChar,  20).Value = (object?)tagRole        ?? DBNull.Value;
        cmd.Parameters.Add("@HR", SqlDbType.Int).Value          = (object?)heatRating     ?? DBNull.Value;
        cmd.Parameters.Add("@MP", SqlDbType.VarChar,  20).Value = (object?)mountPos       ?? DBNull.Value;
        cmd.Parameters.Add("@ID", SqlDbType.Date).Value         = installDate.HasValue    ? (object)installDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@CC", SqlDbType.Int).Value          = (object?)cycleCount     ?? DBNull.Value;
        cmd.Parameters.Add("@RS", SqlDbType.Date).Value         = replaceSchedule.HasValue ? (object)replaceSchedule.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ST", SqlDbType.VarChar,  10).Value = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@CB", SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateRfidTag(string tagId, string? epc, string? jigId, string? tagRole,
        int? heatRating, string? mountPos,
        DateOnly? installDate, int? cycleCount, DateOnly? replaceSchedule,
        string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_RfidTag SET EPC=@EP,JigID=@JI,TagRole=@TR,HeatRating=@HR," +
            "MountPos=@MP,InstallDate=@ID,CycleCount=@CC,ReplaceSchedule=@RS,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE TagID=@I;", conn);
        cmd.Parameters.Add("@I",  SqlDbType.VarChar,  30).Value  = tagId;
        cmd.Parameters.Add("@EP", SqlDbType.VarChar,  64).Value  = (object?)epc            ?? DBNull.Value;
        cmd.Parameters.Add("@JI", SqlDbType.VarChar,  20).Value  = (object?)jigId          ?? DBNull.Value;
        cmd.Parameters.Add("@TR", SqlDbType.VarChar,  20).Value  = (object?)tagRole        ?? DBNull.Value;
        cmd.Parameters.Add("@HR", SqlDbType.Int).Value           = (object?)heatRating     ?? DBNull.Value;
        cmd.Parameters.Add("@MP", SqlDbType.VarChar,  20).Value  = (object?)mountPos       ?? DBNull.Value;
        cmd.Parameters.Add("@ID", SqlDbType.Date).Value          = installDate.HasValue    ? (object)installDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@CC", SqlDbType.Int).Value           = (object?)cycleCount     ?? DBNull.Value;
        cmd.Parameters.Add("@RS", SqlDbType.Date).Value          = replaceSchedule.HasValue ? (object)replaceSchedule.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ST", SqlDbType.VarChar,  10).Value  = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@MB", SqlDbType.NVarChar, 450).Value = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteRfidTag(string tagId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_RfidTag WHERE TagID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 30).Value = tagId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_RalColor CRUD ─────────────────────────────────────────────
    public bool RalColorExists(string ralCode)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_RalColor WHERE RALCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 10).Value = ralCode;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertRalColor(string ralCode, string? colorName, string? hexValue,
        string? currentPowderLot, int? cureTemp, int? cureDuration,
        int? electroV, decimal? particleUm, bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_RalColor(RALCode,ColorName,HexValue,CurrentPowderLot," +
            "CureTemp,CureDuration,ElectroV,ParticleUm,ActiveFlag,CreatedBy)" +
            " VALUES(@C,@CN,@HV,@PL,@CT,@CD,@EV,@PU,@AF,@CB);", conn);
        cmd.Parameters.Add("@C",  SqlDbType.VarChar,   10).Value = ralCode;
        cmd.Parameters.Add("@CN", SqlDbType.NVarChar,  50).Value = (object?)colorName        ?? DBNull.Value;
        cmd.Parameters.Add("@HV", SqlDbType.VarChar,   7).Value  = (object?)hexValue         ?? DBNull.Value;
        cmd.Parameters.Add("@PL", SqlDbType.VarChar,  30).Value  = (object?)currentPowderLot ?? DBNull.Value;
        cmd.Parameters.Add("@CT", SqlDbType.Int).Value           = (object?)cureTemp         ?? DBNull.Value;
        cmd.Parameters.Add("@CD", SqlDbType.Int).Value           = (object?)cureDuration     ?? DBNull.Value;
        cmd.Parameters.Add("@EV", SqlDbType.Int).Value           = (object?)electroV         ?? DBNull.Value;
        cmd.Parameters.Add("@PU", SqlDbType.Decimal).Value       = (object?)particleUm       ?? DBNull.Value;
        if (particleUm.HasValue) { cmd.Parameters["@PU"].Precision = 6; cmd.Parameters["@PU"].Scale = 2; }
        cmd.Parameters.Add("@AF", SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@CB", SqlDbType.VarChar,  50).Value  = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateRalColor(string ralCode, string? colorName, string? hexValue,
        string? currentPowderLot, int? cureTemp, int? cureDuration,
        int? electroV, decimal? particleUm, bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_RalColor SET ColorName=@CN,HexValue=@HV,CurrentPowderLot=@PL," +
            "CureTemp=@CT,CureDuration=@CD,ElectroV=@EV,ParticleUm=@PU,ActiveFlag=@AF," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE RALCode=@C;", conn);
        cmd.Parameters.Add("@C",  SqlDbType.VarChar,   10).Value  = ralCode;
        cmd.Parameters.Add("@CN", SqlDbType.NVarChar,  50).Value  = (object?)colorName        ?? DBNull.Value;
        cmd.Parameters.Add("@HV", SqlDbType.VarChar,   7).Value   = (object?)hexValue         ?? DBNull.Value;
        cmd.Parameters.Add("@PL", SqlDbType.VarChar,  30).Value   = (object?)currentPowderLot ?? DBNull.Value;
        cmd.Parameters.Add("@CT", SqlDbType.Int).Value            = (object?)cureTemp         ?? DBNull.Value;
        cmd.Parameters.Add("@CD", SqlDbType.Int).Value            = (object?)cureDuration     ?? DBNull.Value;
        cmd.Parameters.Add("@EV", SqlDbType.Int).Value            = (object?)electroV         ?? DBNull.Value;
        cmd.Parameters.Add("@PU", SqlDbType.Decimal).Value        = (object?)particleUm       ?? DBNull.Value;
        if (particleUm.HasValue) { cmd.Parameters["@PU"].Precision = 6; cmd.Parameters["@PU"].Scale = 2; }
        cmd.Parameters.Add("@AF", SqlDbType.Bit).Value            = activeFlag;
        cmd.Parameters.Add("@MB", SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteRalColor(string ralCode)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_RalColor WHERE RALCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 10).Value = ralCode;
        cmd.ExecuteNonQuery();
    }

    // ── MD_RfidReader CRUD ───────────────────────────────────────────
    public bool RfidReaderExists(string readerId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_RfidReader WHERE ReaderID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = readerId;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertRfidReader(string readerId, string? readerName, string? gateLocation,
        string? lineId, int? antennaCount, int? powerDbm, bool peTrigger,
        int? windowMs, string? ipAddress, string? firmwareVer, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_RfidReader(ReaderID,ReaderName,GateLocation,LineID," +
            "AntennaCount,PowerDbm,PeTriggerFlag,WindowMs,IpAddress,FirmwareVer,Status,CreatedBy)" +
            " VALUES(@I,@RN,@GL,@LI,@AC,@PD,@PT,@WM,@IP,@FW,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",  SqlDbType.VarChar,  20).Value = readerId;
        cmd.Parameters.Add("@RN", SqlDbType.NVarChar, 50).Value = (object?)readerName   ?? DBNull.Value;
        cmd.Parameters.Add("@GL", SqlDbType.NVarChar, 50).Value = (object?)gateLocation ?? DBNull.Value;
        cmd.Parameters.Add("@LI", SqlDbType.VarChar,  20).Value = (object?)lineId       ?? DBNull.Value;
        cmd.Parameters.Add("@AC", SqlDbType.Int).Value          = (object?)antennaCount ?? DBNull.Value;
        cmd.Parameters.Add("@PD", SqlDbType.Int).Value          = (object?)powerDbm     ?? DBNull.Value;
        cmd.Parameters.Add("@PT", SqlDbType.Bit).Value          = peTrigger;
        cmd.Parameters.Add("@WM", SqlDbType.Int).Value          = (object?)windowMs     ?? DBNull.Value;
        cmd.Parameters.Add("@IP", SqlDbType.VarChar,  20).Value = (object?)ipAddress    ?? DBNull.Value;
        cmd.Parameters.Add("@FW", SqlDbType.VarChar,  20).Value = (object?)firmwareVer  ?? DBNull.Value;
        cmd.Parameters.Add("@ST", SqlDbType.VarChar,  10).Value = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@CB", SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateRfidReader(string readerId, string? readerName, string? gateLocation,
        string? lineId, int? antennaCount, int? powerDbm, bool peTrigger,
        int? windowMs, string? ipAddress, string? firmwareVer, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_RfidReader SET ReaderName=@RN,GateLocation=@GL,LineID=@LI," +
            "AntennaCount=@AC,PowerDbm=@PD,PeTriggerFlag=@PT,WindowMs=@WM," +
            "IpAddress=@IP,FirmwareVer=@FW,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE ReaderID=@I;", conn);
        cmd.Parameters.Add("@I",  SqlDbType.VarChar,   20).Value  = readerId;
        cmd.Parameters.Add("@RN", SqlDbType.NVarChar,  50).Value  = (object?)readerName   ?? DBNull.Value;
        cmd.Parameters.Add("@GL", SqlDbType.NVarChar,  50).Value  = (object?)gateLocation ?? DBNull.Value;
        cmd.Parameters.Add("@LI", SqlDbType.VarChar,   20).Value  = (object?)lineId       ?? DBNull.Value;
        cmd.Parameters.Add("@AC", SqlDbType.Int).Value            = (object?)antennaCount ?? DBNull.Value;
        cmd.Parameters.Add("@PD", SqlDbType.Int).Value            = (object?)powerDbm     ?? DBNull.Value;
        cmd.Parameters.Add("@PT", SqlDbType.Bit).Value            = peTrigger;
        cmd.Parameters.Add("@WM", SqlDbType.Int).Value            = (object?)windowMs     ?? DBNull.Value;
        cmd.Parameters.Add("@IP", SqlDbType.VarChar,   20).Value  = (object?)ipAddress    ?? DBNull.Value;
        cmd.Parameters.Add("@FW", SqlDbType.VarChar,   20).Value  = (object?)firmwareVer  ?? DBNull.Value;
        cmd.Parameters.Add("@ST", SqlDbType.VarChar,   10).Value  = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@MB", SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteRfidReader(string readerId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_RfidReader WHERE ReaderID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = readerId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_PackagingSpec CRUD ────────────────────────────────────────
    public bool PackagingSpecExists(string packSpecId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_PackagingSpec WHERE PackSpecID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = packSpecId;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertPackagingSpec(string packSpecId, string? itemId, string? packType,
        int? qtyPerInner, int? innerPerOuter, int? outerPerPallet,
        decimal? netWeightKg, decimal? grossWeightKg, string? dimLxWxH,
        bool returnableFlag, string? labelTemplateId, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_PackagingSpec(PackSpecID,ItemID,PackType," +
            "QtyPerInner,InnerPerOuter,OuterPerPallet,NetWeightKg,GrossWeightKg," +
            "DimLxWxH,ReturnableFlag,LabelTemplateID,Status,CreatedBy)" +
            " VALUES(@I,@II,@PT,@QI,@IO,@OP,@NW,@GW,@DIM,@RF,@LT,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,  20).Value = packSpecId;
        cmd.Parameters.Add("@II",  SqlDbType.VarChar,  30).Value = (object?)itemId          ?? DBNull.Value;
        cmd.Parameters.Add("@PT",  SqlDbType.VarChar,  20).Value = (object?)packType        ?? DBNull.Value;
        cmd.Parameters.Add("@QI",  SqlDbType.Int).Value          = (object?)qtyPerInner     ?? DBNull.Value;
        cmd.Parameters.Add("@IO",  SqlDbType.Int).Value          = (object?)innerPerOuter   ?? DBNull.Value;
        cmd.Parameters.Add("@OP",  SqlDbType.Int).Value          = (object?)outerPerPallet  ?? DBNull.Value;
        cmd.Parameters.Add("@NW",  SqlDbType.Decimal).Value      = (object?)netWeightKg     ?? DBNull.Value;
        if (netWeightKg.HasValue)   { cmd.Parameters["@NW"].Precision = 8; cmd.Parameters["@NW"].Scale = 3; }
        cmd.Parameters.Add("@GW",  SqlDbType.Decimal).Value      = (object?)grossWeightKg  ?? DBNull.Value;
        if (grossWeightKg.HasValue) { cmd.Parameters["@GW"].Precision = 8; cmd.Parameters["@GW"].Scale = 3; }
        cmd.Parameters.Add("@DIM", SqlDbType.VarChar,  30).Value = (object?)dimLxWxH       ?? DBNull.Value;
        cmd.Parameters.Add("@RF",  SqlDbType.Bit).Value          = returnableFlag;
        cmd.Parameters.Add("@LT",  SqlDbType.VarChar,  20).Value = (object?)labelTemplateId ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,  10).Value = (object?)status          ?? DBNull.Value;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,  50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdatePackagingSpec(string packSpecId, string? itemId, string? packType,
        int? qtyPerInner, int? innerPerOuter, int? outerPerPallet,
        decimal? netWeightKg, decimal? grossWeightKg, string? dimLxWxH,
        bool returnableFlag, string? labelTemplateId, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_PackagingSpec SET ItemID=@II,PackType=@PT," +
            "QtyPerInner=@QI,InnerPerOuter=@IO,OuterPerPallet=@OP," +
            "NetWeightKg=@NW,GrossWeightKg=@GW,DimLxWxH=@DIM," +
            "ReturnableFlag=@RF,LabelTemplateID=@LT,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE PackSpecID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value  = packSpecId;
        cmd.Parameters.Add("@II",  SqlDbType.VarChar,   30).Value  = (object?)itemId          ?? DBNull.Value;
        cmd.Parameters.Add("@PT",  SqlDbType.VarChar,   20).Value  = (object?)packType        ?? DBNull.Value;
        cmd.Parameters.Add("@QI",  SqlDbType.Int).Value            = (object?)qtyPerInner     ?? DBNull.Value;
        cmd.Parameters.Add("@IO",  SqlDbType.Int).Value            = (object?)innerPerOuter   ?? DBNull.Value;
        cmd.Parameters.Add("@OP",  SqlDbType.Int).Value            = (object?)outerPerPallet  ?? DBNull.Value;
        cmd.Parameters.Add("@NW",  SqlDbType.Decimal).Value        = (object?)netWeightKg     ?? DBNull.Value;
        if (netWeightKg.HasValue)   { cmd.Parameters["@NW"].Precision = 8; cmd.Parameters["@NW"].Scale = 3; }
        cmd.Parameters.Add("@GW",  SqlDbType.Decimal).Value        = (object?)grossWeightKg  ?? DBNull.Value;
        if (grossWeightKg.HasValue) { cmd.Parameters["@GW"].Precision = 8; cmd.Parameters["@GW"].Scale = 3; }
        cmd.Parameters.Add("@DIM", SqlDbType.VarChar,   30).Value  = (object?)dimLxWxH       ?? DBNull.Value;
        cmd.Parameters.Add("@RF",  SqlDbType.Bit).Value            = returnableFlag;
        cmd.Parameters.Add("@LT",  SqlDbType.VarChar,   20).Value  = (object?)labelTemplateId ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value  = (object?)status          ?? DBNull.Value;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeletePackagingSpec(string packSpecId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_PackagingSpec WHERE PackSpecID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = packSpecId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_LabelTemplate CRUD ────────────────────────────────────────
    public bool LabelTemplateExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_LabelTemplate WHERE LabelTemplateID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertLabelTemplate(string id, string? templateName, string? labelType,
        string? paperSize, string? barcodeType, string? customerId,
        int? version, string? printerModel, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_LabelTemplate(LabelTemplateID,TemplateName,LabelType," +
            "PaperSize,BarcodeType,CustomerID,Version,PrinterModel,Status,CreatedBy)" +
            " VALUES(@I,@TN,@LT,@PS,@BT,@CI,@VER,@PM,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value = id;
        cmd.Parameters.Add("@TN",  SqlDbType.NVarChar, 100).Value = (object?)templateName ?? DBNull.Value;
        cmd.Parameters.Add("@LT",  SqlDbType.VarChar,   20).Value = (object?)labelType    ?? DBNull.Value;
        cmd.Parameters.Add("@PS",  SqlDbType.VarChar,   20).Value = (object?)paperSize    ?? DBNull.Value;
        cmd.Parameters.Add("@BT",  SqlDbType.VarChar,   20).Value = (object?)barcodeType  ?? DBNull.Value;
        cmd.Parameters.Add("@CI",  SqlDbType.VarChar,   20).Value = (object?)customerId   ?? DBNull.Value;
        cmd.Parameters.Add("@VER", SqlDbType.Int).Value           = (object?)version      ?? DBNull.Value;
        cmd.Parameters.Add("@PM",  SqlDbType.VarChar,   50).Value = (object?)printerModel ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateLabelTemplate(string id, string? templateName, string? labelType,
        string? paperSize, string? barcodeType, string? customerId,
        int? version, string? printerModel, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_LabelTemplate SET TemplateName=@TN,LabelType=@LT," +
            "PaperSize=@PS,BarcodeType=@BT,CustomerID=@CI,Version=@VER," +
            "PrinterModel=@PM,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE LabelTemplateID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value  = id;
        cmd.Parameters.Add("@TN",  SqlDbType.NVarChar, 100).Value  = (object?)templateName ?? DBNull.Value;
        cmd.Parameters.Add("@LT",  SqlDbType.VarChar,   20).Value  = (object?)labelType    ?? DBNull.Value;
        cmd.Parameters.Add("@PS",  SqlDbType.VarChar,   20).Value  = (object?)paperSize    ?? DBNull.Value;
        cmd.Parameters.Add("@BT",  SqlDbType.VarChar,   20).Value  = (object?)barcodeType  ?? DBNull.Value;
        cmd.Parameters.Add("@CI",  SqlDbType.VarChar,   20).Value  = (object?)customerId   ?? DBNull.Value;
        cmd.Parameters.Add("@VER", SqlDbType.Int).Value            = (object?)version      ?? DBNull.Value;
        cmd.Parameters.Add("@PM",  SqlDbType.VarChar,   50).Value  = (object?)printerModel ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value  = (object?)status       ?? DBNull.Value;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteLabelTemplate(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_LabelTemplate WHERE LabelTemplateID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        cmd.ExecuteNonQuery();
    }

    // ── MD_ReasonCode CRUD ───────────────────────────────────────────
    public bool ReasonCodeExists(string code)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_ReasonCode WHERE ReasonCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 20).Value = code;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertReasonCode(string code, string? reasonName, string? reasonType,
        string? appliesToModule, bool requiresComment, bool plannedFlag,
        int? displayOrder, string? description, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_ReasonCode(ReasonCode,ReasonName,ReasonType," +
            "AppliesToModule,RequiresComment,PlannedFlag,DisplayOrder,Description,Status,CreatedBy)" +
            " VALUES(@C,@RN,@RT,@AM,@RC2,@PF,@DO,@DESC,@ST,@CB);", conn);
        cmd.Parameters.Add("@C",    SqlDbType.VarChar,   20).Value = code;
        cmd.Parameters.Add("@RN",   SqlDbType.NVarChar, 100).Value = (object?)reasonName      ?? DBNull.Value;
        cmd.Parameters.Add("@RT",   SqlDbType.VarChar,   20).Value = (object?)reasonType      ?? DBNull.Value;
        cmd.Parameters.Add("@AM",   SqlDbType.VarChar,   20).Value = (object?)appliesToModule ?? DBNull.Value;
        cmd.Parameters.Add("@RC2",  SqlDbType.Bit).Value           = requiresComment;
        cmd.Parameters.Add("@PF",   SqlDbType.Bit).Value           = plannedFlag;
        cmd.Parameters.Add("@DO",   SqlDbType.Int).Value           = (object?)displayOrder   ?? DBNull.Value;
        cmd.Parameters.Add("@DESC", SqlDbType.NVarChar, 500).Value = (object?)description    ?? DBNull.Value;
        cmd.Parameters.Add("@ST",   SqlDbType.VarChar,   10).Value = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@CB",   SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateReasonCode(string code, string? reasonName, string? reasonType,
        string? appliesToModule, bool requiresComment, bool plannedFlag,
        int? displayOrder, string? description, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_ReasonCode SET ReasonName=@RN,ReasonType=@RT," +
            "AppliesToModule=@AM,RequiresComment=@RC2,PlannedFlag=@PF," +
            "DisplayOrder=@DO,Description=@DESC,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE ReasonCode=@C;", conn);
        cmd.Parameters.Add("@C",    SqlDbType.VarChar,   20).Value  = code;
        cmd.Parameters.Add("@RN",   SqlDbType.NVarChar, 100).Value  = (object?)reasonName      ?? DBNull.Value;
        cmd.Parameters.Add("@RT",   SqlDbType.VarChar,   20).Value  = (object?)reasonType      ?? DBNull.Value;
        cmd.Parameters.Add("@AM",   SqlDbType.VarChar,   20).Value  = (object?)appliesToModule ?? DBNull.Value;
        cmd.Parameters.Add("@RC2",  SqlDbType.Bit).Value            = requiresComment;
        cmd.Parameters.Add("@PF",   SqlDbType.Bit).Value            = plannedFlag;
        cmd.Parameters.Add("@DO",   SqlDbType.Int).Value            = (object?)displayOrder   ?? DBNull.Value;
        cmd.Parameters.Add("@DESC", SqlDbType.NVarChar, 500).Value  = (object?)description    ?? DBNull.Value;
        cmd.Parameters.Add("@ST",   SqlDbType.VarChar,   10).Value  = (object?)status         ?? DBNull.Value;
        cmd.Parameters.Add("@MB",   SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteReasonCode(string code)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_ReasonCode WHERE ReasonCode=@C;", conn);
        cmd.Parameters.Add("@C", SqlDbType.VarChar, 20).Value = code;
        cmd.ExecuteNonQuery();
    }

    // ── MD_SparePart CRUD ────────────────────────────────────────────
    public bool SparePartExists(string partNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_SparePart WHERE PartNo=@P;", conn);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 30).Value = partNo;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertSparePart(string partNo, string? partName, string? category,
        decimal? unitCost, string? uom,
        int? safetyStock, int? reorderPoint, int? reorderQty, int? leadTimeDays,
        string? supplierId, string? storageLoc, bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_SparePart(PartNo,PartName,Category,UnitCost,UOM," +
            "SafetyStock,ReorderPoint,ReorderQty,LeadTimeDays,SupplierID,StorageLoc,ActiveFlag,CreatedBy)" +
            " VALUES(@P,@PN,@CAT,@UC,@UOM,@SS,@RP,@RQ,@LT,@SI,@SL,@AF,@CB);", conn);
        cmd.Parameters.Add("@P",   SqlDbType.VarChar,   30).Value = partNo;
        cmd.Parameters.Add("@PN",  SqlDbType.NVarChar, 100).Value = (object?)partName    ?? DBNull.Value;
        cmd.Parameters.Add("@CAT", SqlDbType.VarChar,   20).Value = (object?)category    ?? DBNull.Value;
        cmd.Parameters.Add("@UC",  SqlDbType.Decimal).Value       = (object?)unitCost    ?? DBNull.Value;
        if (unitCost.HasValue) { cmd.Parameters["@UC"].Precision = 12; cmd.Parameters["@UC"].Scale = 2; }
        cmd.Parameters.Add("@UOM", SqlDbType.VarChar,   10).Value = (object?)uom         ?? DBNull.Value;
        cmd.Parameters.Add("@SS",  SqlDbType.Int).Value           = (object?)safetyStock ?? DBNull.Value;
        cmd.Parameters.Add("@RP",  SqlDbType.Int).Value           = (object?)reorderPoint ?? DBNull.Value;
        cmd.Parameters.Add("@RQ",  SqlDbType.Int).Value           = (object?)reorderQty  ?? DBNull.Value;
        cmd.Parameters.Add("@LT",  SqlDbType.Int).Value           = (object?)leadTimeDays ?? DBNull.Value;
        cmd.Parameters.Add("@SI",  SqlDbType.VarChar,   20).Value = (object?)supplierId  ?? DBNull.Value;
        cmd.Parameters.Add("@SL",  SqlDbType.VarChar,   30).Value = (object?)storageLoc  ?? DBNull.Value;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateSparePart(string partNo, string? partName, string? category,
        decimal? unitCost, string? uom,
        int? safetyStock, int? reorderPoint, int? reorderQty, int? leadTimeDays,
        string? supplierId, string? storageLoc, bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_SparePart SET PartName=@PN,Category=@CAT,UnitCost=@UC,UOM=@UOM," +
            "SafetyStock=@SS,ReorderPoint=@RP,ReorderQty=@RQ,LeadTimeDays=@LT," +
            "SupplierID=@SI,StorageLoc=@SL,ActiveFlag=@AF," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE PartNo=@P;", conn);
        cmd.Parameters.Add("@P",   SqlDbType.VarChar,   30).Value  = partNo;
        cmd.Parameters.Add("@PN",  SqlDbType.NVarChar, 100).Value  = (object?)partName    ?? DBNull.Value;
        cmd.Parameters.Add("@CAT", SqlDbType.VarChar,   20).Value  = (object?)category    ?? DBNull.Value;
        cmd.Parameters.Add("@UC",  SqlDbType.Decimal).Value        = (object?)unitCost    ?? DBNull.Value;
        if (unitCost.HasValue) { cmd.Parameters["@UC"].Precision = 12; cmd.Parameters["@UC"].Scale = 2; }
        cmd.Parameters.Add("@UOM", SqlDbType.VarChar,   10).Value  = (object?)uom         ?? DBNull.Value;
        cmd.Parameters.Add("@SS",  SqlDbType.Int).Value            = (object?)safetyStock ?? DBNull.Value;
        cmd.Parameters.Add("@RP",  SqlDbType.Int).Value            = (object?)reorderPoint ?? DBNull.Value;
        cmd.Parameters.Add("@RQ",  SqlDbType.Int).Value            = (object?)reorderQty  ?? DBNull.Value;
        cmd.Parameters.Add("@LT",  SqlDbType.Int).Value            = (object?)leadTimeDays ?? DBNull.Value;
        cmd.Parameters.Add("@SI",  SqlDbType.VarChar,   20).Value  = (object?)supplierId  ?? DBNull.Value;
        cmd.Parameters.Add("@SL",  SqlDbType.VarChar,   30).Value  = (object?)storageLoc  ?? DBNull.Value;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value            = activeFlag;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteSparePart(string partNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_SparePart WHERE PartNo=@P;", conn);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 30).Value = partNo;
        cmd.ExecuteNonQuery();
    }

    // ── MD_PmTemplate CRUD ───────────────────────────────────────────
    public bool PmTemplateExists(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_PmTemplate WHERE PMTemplateID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertPmTemplate(string id, string? templateName, string? equipType,
        string? cycleBasis, int? intervalValue, string? intervalUnit,
        int? stdDurationMin, bool safetyLoto, bool activeFlag, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_PmTemplate(PMTemplateID,TemplateName,EquipType," +
            "CycleBasis,IntervalValue,IntervalUnit,StdDurationMin,SafetyLOTOFlag,ActiveFlag,CreatedBy)" +
            " VALUES(@I,@TN,@ET,@CB2,@IV,@IU,@SD,@SL,@AF,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value = id;
        cmd.Parameters.Add("@TN",  SqlDbType.NVarChar, 100).Value = (object?)templateName  ?? DBNull.Value;
        cmd.Parameters.Add("@ET",  SqlDbType.VarChar,   20).Value = (object?)equipType     ?? DBNull.Value;
        cmd.Parameters.Add("@CB2", SqlDbType.VarChar,   10).Value = (object?)cycleBasis    ?? DBNull.Value;
        cmd.Parameters.Add("@IV",  SqlDbType.Int).Value           = (object?)intervalValue ?? DBNull.Value;
        cmd.Parameters.Add("@IU",  SqlDbType.VarChar,   10).Value = (object?)intervalUnit  ?? DBNull.Value;
        cmd.Parameters.Add("@SD",  SqlDbType.Int).Value           = (object?)stdDurationMin ?? DBNull.Value;
        cmd.Parameters.Add("@SL",  SqlDbType.Bit).Value           = safetyLoto;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value           = activeFlag;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdatePmTemplate(string id, string? templateName, string? equipType,
        string? cycleBasis, int? intervalValue, string? intervalUnit,
        int? stdDurationMin, bool safetyLoto, bool activeFlag, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_PmTemplate SET TemplateName=@TN,EquipType=@ET," +
            "CycleBasis=@CB2,IntervalValue=@IV,IntervalUnit=@IU," +
            "StdDurationMin=@SD,SafetyLOTOFlag=@SL,ActiveFlag=@AF," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE PMTemplateID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value  = id;
        cmd.Parameters.Add("@TN",  SqlDbType.NVarChar, 100).Value  = (object?)templateName  ?? DBNull.Value;
        cmd.Parameters.Add("@ET",  SqlDbType.VarChar,   20).Value  = (object?)equipType     ?? DBNull.Value;
        cmd.Parameters.Add("@CB2", SqlDbType.VarChar,   10).Value  = (object?)cycleBasis    ?? DBNull.Value;
        cmd.Parameters.Add("@IV",  SqlDbType.Int).Value            = (object?)intervalValue ?? DBNull.Value;
        cmd.Parameters.Add("@IU",  SqlDbType.VarChar,   10).Value  = (object?)intervalUnit  ?? DBNull.Value;
        cmd.Parameters.Add("@SD",  SqlDbType.Int).Value            = (object?)stdDurationMin ?? DBNull.Value;
        cmd.Parameters.Add("@SL",  SqlDbType.Bit).Value            = safetyLoto;
        cmd.Parameters.Add("@AF",  SqlDbType.Bit).Value            = activeFlag;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeletePmTemplate(string id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_PmTemplate WHERE PMTemplateID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = id;
        cmd.ExecuteNonQuery();
    }

    // ── MD_LineTimePattern CRUD ──────────────────────────────────────
    public bool LineTimePatternExists(string patternId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_LineTimePattern WHERE PatternID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = patternId;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertLineTimePattern(string patternId, string? lineId, string? patternName,
        string? dayType, string? shiftModel,
        DateOnly? effectiveFrom, DateOnly? effectiveTo,
        int? totalOperatingMin, int? totalPlannedDownMin,
        string? timeZone, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_LineTimePattern(PatternID,LineID,PatternName," +
            "DayType,ShiftModel,EffectiveFrom,EffectiveTo," +
            "TotalOperatingMin,TotalPlannedDownMin,TimeZone,Status,CreatedBy)" +
            " VALUES(@I,@LI,@PN,@DT,@SM,@EF,@ET,@TOM,@TPD,@TZ,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value = patternId;
        cmd.Parameters.Add("@LI",  SqlDbType.VarChar,   20).Value = (object?)lineId           ?? DBNull.Value;
        cmd.Parameters.Add("@PN",  SqlDbType.NVarChar, 100).Value = (object?)patternName      ?? DBNull.Value;
        cmd.Parameters.Add("@DT",  SqlDbType.VarChar,   10).Value = (object?)dayType          ?? DBNull.Value;
        cmd.Parameters.Add("@SM",  SqlDbType.VarChar,   20).Value = (object?)shiftModel       ?? DBNull.Value;
        cmd.Parameters.Add("@EF",  SqlDbType.Date).Value          = effectiveFrom.HasValue    ? (object)effectiveFrom.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ET",  SqlDbType.Date).Value          = effectiveTo.HasValue      ? (object)effectiveTo.Value.ToDateTime(TimeOnly.MinValue)   : DBNull.Value;
        cmd.Parameters.Add("@TOM", SqlDbType.Int).Value           = (object?)totalOperatingMin   ?? DBNull.Value;
        cmd.Parameters.Add("@TPD", SqlDbType.Int).Value           = (object?)totalPlannedDownMin  ?? DBNull.Value;
        cmd.Parameters.Add("@TZ",  SqlDbType.VarChar,   50).Value = (object?)timeZone         ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value = (object?)status           ?? DBNull.Value;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateLineTimePattern(string patternId, string? lineId, string? patternName,
        string? dayType, string? shiftModel,
        DateOnly? effectiveFrom, DateOnly? effectiveTo,
        int? totalOperatingMin, int? totalPlannedDownMin,
        string? timeZone, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_LineTimePattern SET LineID=@LI,PatternName=@PN," +
            "DayType=@DT,ShiftModel=@SM,EffectiveFrom=@EF,EffectiveTo=@ET," +
            "TotalOperatingMin=@TOM,TotalPlannedDownMin=@TPD,TimeZone=@TZ,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE PatternID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value  = patternId;
        cmd.Parameters.Add("@LI",  SqlDbType.VarChar,   20).Value  = (object?)lineId           ?? DBNull.Value;
        cmd.Parameters.Add("@PN",  SqlDbType.NVarChar, 100).Value  = (object?)patternName      ?? DBNull.Value;
        cmd.Parameters.Add("@DT",  SqlDbType.VarChar,   10).Value  = (object?)dayType          ?? DBNull.Value;
        cmd.Parameters.Add("@SM",  SqlDbType.VarChar,   20).Value  = (object?)shiftModel       ?? DBNull.Value;
        cmd.Parameters.Add("@EF",  SqlDbType.Date).Value           = effectiveFrom.HasValue    ? (object)effectiveFrom.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ET",  SqlDbType.Date).Value           = effectiveTo.HasValue      ? (object)effectiveTo.Value.ToDateTime(TimeOnly.MinValue)   : DBNull.Value;
        cmd.Parameters.Add("@TOM", SqlDbType.Int).Value            = (object?)totalOperatingMin   ?? DBNull.Value;
        cmd.Parameters.Add("@TPD", SqlDbType.Int).Value            = (object?)totalPlannedDownMin  ?? DBNull.Value;
        cmd.Parameters.Add("@TZ",  SqlDbType.VarChar,   50).Value  = (object?)timeZone         ?? DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value  = (object?)status           ?? DBNull.Value;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteLineTimePattern(string patternId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_LineTimePattern WHERE PatternID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = patternId;
        cmd.ExecuteNonQuery();
    }

    // ── MD_Recipe CRUD ───────────────────────────────────────────────
    public bool RecipeExists(string recipeId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT 1 FROM dbo.MD_Recipe WHERE RecipeID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = recipeId;
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertRecipe(string recipeId, string? recipeName, string? recipeType,
        string? itemNo, int? cycleTime, string? version,
        DateOnly? effectiveDate, string? status, string createdBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.MD_Recipe(RecipeID,RecipeName,RecipeType," +
            "ItemNo,CycleTime,Version,EffectiveDate,Status,CreatedBy)" +
            " VALUES(@I,@RN,@RT,@IN,@CT,@VER,@ED,@ST,@CB);", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value = recipeId;
        cmd.Parameters.Add("@RN",  SqlDbType.NVarChar, 100).Value = (object?)recipeName    ?? DBNull.Value;
        cmd.Parameters.Add("@RT",  SqlDbType.VarChar,   20).Value = (object?)recipeType    ?? DBNull.Value;
        cmd.Parameters.Add("@IN",  SqlDbType.VarChar,   30).Value = (object?)itemNo        ?? DBNull.Value;
        cmd.Parameters.Add("@CT",  SqlDbType.Int).Value           = (object?)cycleTime     ?? DBNull.Value;
        cmd.Parameters.Add("@VER", SqlDbType.VarChar,   10).Value = (object?)version       ?? DBNull.Value;
        cmd.Parameters.Add("@ED",  SqlDbType.Date).Value          = effectiveDate.HasValue  ? (object)effectiveDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value = (object?)status        ?? DBNull.Value;
        cmd.Parameters.Add("@CB",  SqlDbType.VarChar,   50).Value = createdBy;
        cmd.ExecuteNonQuery();
    }

    public void UpdateRecipe(string recipeId, string? recipeName, string? recipeType,
        string? itemNo, int? cycleTime, string? version,
        DateOnly? effectiveDate, string? status, string modifiedBy)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(
            "UPDATE dbo.MD_Recipe SET RecipeName=@RN,RecipeType=@RT," +
            "ItemNo=@IN,CycleTime=@CT,Version=@VER,EffectiveDate=@ED,Status=@ST," +
            "ModifiedTS=SYSDATETIME(),ModifiedBy=@MB WHERE RecipeID=@I;", conn);
        cmd.Parameters.Add("@I",   SqlDbType.VarChar,   20).Value  = recipeId;
        cmd.Parameters.Add("@RN",  SqlDbType.NVarChar, 100).Value  = (object?)recipeName    ?? DBNull.Value;
        cmd.Parameters.Add("@RT",  SqlDbType.VarChar,   20).Value  = (object?)recipeType    ?? DBNull.Value;
        cmd.Parameters.Add("@IN",  SqlDbType.VarChar,   30).Value  = (object?)itemNo        ?? DBNull.Value;
        cmd.Parameters.Add("@CT",  SqlDbType.Int).Value            = (object?)cycleTime     ?? DBNull.Value;
        cmd.Parameters.Add("@VER", SqlDbType.VarChar,   10).Value  = (object?)version       ?? DBNull.Value;
        cmd.Parameters.Add("@ED",  SqlDbType.Date).Value           = effectiveDate.HasValue  ? (object)effectiveDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        cmd.Parameters.Add("@ST",  SqlDbType.VarChar,   10).Value  = (object?)status        ?? DBNull.Value;
        cmd.Parameters.Add("@MB",  SqlDbType.NVarChar, 450).Value  = modifiedBy;
        cmd.ExecuteNonQuery();
    }

    public void DeleteRecipe(string recipeId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE FROM dbo.MD_Recipe WHERE RecipeID=@I;", conn);
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = recipeId;
        cmd.ExecuteNonQuery();
    }
}
