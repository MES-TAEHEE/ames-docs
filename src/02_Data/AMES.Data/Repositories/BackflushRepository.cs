using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed record BackflushShortage(string PartNo, decimal Qty, string? Uom);

/// <param name="Skipped">이미 차감된 LOT 이라 아무것도 하지 않았다.</param>
public sealed record BackflushResult(IReadOnlyList<BackflushShortage> Shortages, string? Error, bool Skipped)
{
    public static readonly BackflushResult None = new(Array.Empty<BackflushShortage>(), null, true);
    public bool HasShortage => Shortages.Count > 0;
}

/// <summary>
/// LOT 이 처음 CONFIRMED 가 될 때 유효 BOM 자식을 그 라인의 상세 위치와 기존 LotPrefix 위치 재고에서 뺀다.
/// 확정 트랜잭션 안에서 세이브포인트로 돌아 실패해도 확정을 막지 않는다(생산라인 우선, 사용자 결정 2026-10-15).
/// 멱등 판정은 WH_InventoryTransaction(SourceType BACKFLUSH, SourceID = tbl_Lot.LotID) — 새 컬럼 없음.
/// </summary>
public static class BackflushRepository
{
    public const string SourceType    = "BACKFLUSH";
    public const string NetSourceType = "BACKFLUSH_NET";
    const string Savepoint = "backflush";

    /// <param name="includeInjection">true(PNT·IMG)면 MATERIAL 자식에 더해 사출품(InjFlag 1) 자식도 뺀다 — <see cref="BackflushRules.IncludesInjection"/>.
    /// LOT 이 코어 LOT 에 이어져 있으면(tbl_Lot.ParentLotID — IMG 코어 스캔) 그 코어 품번은 빼지 않는다(ConsumeCore 가 이미 지웠다).</param>
    public static BackflushResult Apply(SqlConnection conn, SqlTransaction tx, int lotId, string lotCode, string itemNo,
                                        string lineId, string operatorId, string employeeNo, bool includeInjection = false)
    {
        tx.Save(Savepoint);
        try { return ApplyCore(conn, tx, lotId, lotCode, itemNo, lineId, operatorId, employeeNo, includeInjection); }
        catch (Exception ex)
        {
            // 교착 희생(1205)·심각도 20 이상이면 서버가 트랜잭션 전체를 이미 되돌려 세이브포인트 롤백이 또 던진다 —
            // 그때는 원인(SqlException)을 그대로 올려 호출자가 확정 실패로 처리하게 한다
            try { tx.Rollback(Savepoint); }
            catch { throw ex; }
            return new BackflushResult(Array.Empty<BackflushShortage>(), ex.Message, false);
        }
    }

    static string? ReadParentLotItem(SqlConnection conn, SqlTransaction tx, int lotId)
    {
        using var cmd = new SqlCommand("""
            SELECT p.ItemNo FROM dbo.tbl_Lot l JOIN dbo.tbl_Lot p ON p.LotID = l.ParentLotID WHERE l.LotID = @Lot;
            """, conn, tx);
        cmd.Parameters.Add("@Lot", System.Data.SqlDbType.Int).Value = lotId;
        return cmd.ExecuteScalar() as string;
    }

    static BackflushResult ApplyCore(SqlConnection conn, SqlTransaction tx, int lotId, string lotCode, string itemNo,
                                     string lineId, string operatorId, string employeeNo, bool includeInjection)
    {
        if (AlreadyApplied(conn, tx, lotId)) return BackflushResult.None;

        var prefix = LinePrefix(conn, tx, lineId);
        if (prefix is null)
            return new BackflushResult(Array.Empty<BackflushShortage>(), $"line {lineId} has no LotPrefix — no line stock location", false);

        var except = includeInjection ? ReadParentLotItem(conn, tx, lotId) is { } core ? new[] { core } : null : null;
        var requirements = BackflushRules.Requirements(ReadBomRows(conn, tx, itemNo), includeInjection, except);
        var shortages = new List<BackflushShortage>();
        var note = $"{lotCode} {itemNo}";
        foreach (var req in requirements)
        {
            var ph    = BackflushRules.PlaceholderLotNo(prefix, req.PartNo);
            var fifo  = ReadLineLots(conn, tx, lineId, prefix, req.PartNo, ph);
            var locations=fifo.ToDictionary(x=>x.LotNo,x=>x.LocationNo??prefix,StringComparer.OrdinalIgnoreCase);
            var phQty = ReadLotQty(conn, tx, ph);
            var alloc = BackflushRules.Allocate(req.Qty, fifo, phQty ?? 0m);

            foreach (var n in alloc.Nets)
            {
                var (before, after) = AdjustLot(conn, tx, n.LotNo, -n.Qty);
                WriteTxn(conn, tx, "ADJ", NetSourceType, lotId, n.LotNo, req.PartNo, locations[n.LotNo], before, -n.Qty, after, $"net {ph} ← {lotCode}", operatorId, employeeNo);
                var (pb, pa) = EnsurePlaceholder(conn, tx, ph, req.PartNo, prefix, ref phQty, +n.Qty);
                WriteTxn(conn, tx, "ADJ", NetSourceType, lotId, ph, req.PartNo, prefix, pb, +n.Qty, pa, $"net ← {n.LotNo}", operatorId, employeeNo);
            }
            foreach (var t in alloc.Takes)
            {
                var (before, after) = AdjustLot(conn, tx, t.LotNo, -t.Qty);
                WriteTxn(conn, tx, "OUT", SourceType, lotId, t.LotNo, req.PartNo, locations[t.LotNo], before, -t.Qty, after, note, operatorId, employeeNo);
            }
            if (alloc.PlaceholderTake > 0m)
            {
                var (pb, pa) = EnsurePlaceholder(conn, tx, ph, req.PartNo, prefix, ref phQty, -alloc.PlaceholderTake);
                WriteTxn(conn, tx, "OUT", SourceType, lotId, ph, req.PartNo, prefix, pb, -alloc.PlaceholderTake, pa, note, operatorId, employeeNo);
            }
            if (alloc.Shortage > 0m) shortages.Add(new BackflushShortage(req.PartNo, alloc.Shortage, req.Uom));
        }
        return new BackflushResult(shortages, null, false);
    }

    static bool AlreadyApplied(SqlConnection conn, SqlTransaction tx, int lotId)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_BACKFLUSH_AlreadyApplied", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        ColumnParams.AddFilter(cmd, "@S",   "WH_InventoryTransaction.SourceType", SourceType);
        ColumnParams.AddFilter(cmd, "@Lot", "WH_InventoryTransaction.SourceID",   lotId);
        return cmd.ExecuteScalar() is not null;
    }

    static string? LinePrefix(SqlConnection conn, SqlTransaction tx, string lineId)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_BACKFLUSH_LinePrefix", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        ColumnParams.AddFilter(cmd, "@L", "MD_Line.LineID", lineId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>유효 BOM(APPROVED·오늘 기간 내, 부모당 EffFrom 최신)의 직접 자식 — CoreItemResolver·RunMrp 와 같은 선택.</summary>
    static List<BackflushBomRow> ReadBomRows(SqlConnection conn, SqlTransaction tx, string itemNo)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_BACKFLUSH_ReadBomRows", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        ColumnParams.AddFilter(cmd, "@Item", "MD_Bom.ParentItemNo", itemNo);
        using var rdr = cmd.ExecuteReader();
        var list = new List<BackflushBomRow>();
        while (rdr.Read())
            list.Add(new BackflushBomRow((string)rdr["CompItemNo"], rdr["ItemType"] as string,
                Convert.ToDecimal(rdr["QtyPer"]), Convert.ToDecimal(rdr["ScrapPct"]), rdr["UOM"] as string,
                Convert.ToBoolean(rdr["Active"]), Convert.ToBoolean(rdr["InjFlag"])));
        return list;
    }

    /// <summary>라인 상세/기존 위치의 양수 LOT을 FIFO로 읽는다. 컨테이너 합계·자리표시는 뺀다.</summary>
    internal const string ReadLineLotsSql = """
        SELECT W.LotNo,W.Qty,W.LocationNo
        FROM dbo.WH_Inventory W WITH(UPDLOCK,ROWLOCK)
        WHERE W.PartNo=@Part AND W.Qty>0 AND W.LotNo<>@Ph
          AND (W.LocationNo=@Loc OR EXISTS(SELECT 1 FROM dbo.MD_Location L
              WHERE L.LocationID=W.LocationNo AND L.LineID=@Line AND L.LocationLevel=2))
          AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)
        ORDER BY W.ReceivedAt,W.CreatedAt,W.LotNo;
        """;

    static List<BackflushLot> ReadLineLots(SqlConnection conn, SqlTransaction tx, string lineId, string prefix, string partNo, string placeholder)
    {
        using var cmd = new SqlCommand(ReadLineLotsSql, conn, tx);
        ColumnParams.AddFilter(cmd, "@Line", "MD_Line.LineID", lineId);
        ColumnParams.AddFilter(cmd, "@Loc",  "WH_Inventory.LocationNo", prefix);
        ColumnParams.AddFilter(cmd, "@Part", "WH_Inventory.PartNo",     partNo);
        ColumnParams.AddFilter(cmd, "@Ph",   "WH_Inventory.LotNo",      placeholder);
        using var rdr = cmd.ExecuteReader();
        var list = new List<BackflushLot>();
        while (rdr.Read()) list.Add(new BackflushLot((string)rdr["LotNo"], (decimal)rdr["Qty"],rdr["LocationNo"] as string));
        return list;
    }

    /// <summary>
    /// HOLDLOCK: 없는 키에도 범위 잠금을 잡아 같은 라인의 두 확정이 첫 부족에서 자리표시를 동시에 INSERT 하지 않게 한다 —
    /// 둘째는 첫째가 끝날 때까지 기다렸다가 생긴 행을 읽어 더한다(UPDLOCK 만으로는 없는 키에 잠금이 남지 않아 PK 충돌로 차감을 통째로 잃었다).
    /// </summary>
    internal static decimal? ReadLotQty(SqlConnection conn, SqlTransaction tx, string lotNo)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_BACKFLUSH_ReadLotQty", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        ColumnParams.AddFilter(cmd, "@L", "WH_Inventory.LotNo", lotNo);
        return cmd.ExecuteScalar() as decimal?;
    }

    static (decimal Before, decimal After) AdjustLot(SqlConnection conn, SqlTransaction tx, string lotNo, decimal delta)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_BACKFLUSH_AdjustLot", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        ColumnParams.Add(cmd, "@D", "WH_Inventory.Qty", delta);
        ColumnParams.AddFilter(cmd, "@L", "WH_Inventory.LotNo", lotNo);
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) throw new InvalidOperationException($"line stock lot {lotNo} vanished during backflush");
        return ((decimal)rdr["Before"], (decimal)rdr["After"]);
    }

    /// <summary>자리표시 행을 만들거나 더한다. phQty 는 호출 사이에 바뀐 현재값을 따라간다.</summary>
    static (decimal Before, decimal After) EnsurePlaceholder(SqlConnection conn, SqlTransaction tx, string lotNo, string partNo,
                                                             string prefix, ref decimal? phQty, decimal delta)
    {
        if (phQty is not null)
        {
            var r = AdjustLot(conn, tx, lotNo, delta);
            phQty = r.After;
            return r;
        }
        using var cmd = new SqlCommand("dbo.SP_POP_BACKFLUSH_EnsurePlaceholder", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        ColumnParams.Add(cmd, "@L",   "WH_Inventory.LotNo",      lotNo);
        ColumnParams.Add(cmd, "@P",   "WH_Inventory.PartNo",     partNo);
        ColumnParams.Add(cmd, "@Loc", "WH_Inventory.LocationNo", prefix);
        ColumnParams.Add(cmd, "@Q",   "WH_Inventory.Qty",        delta);
        cmd.ExecuteNonQuery();
        phQty = delta;
        return (0m, delta);
    }

    static void WriteTxn(SqlConnection conn, SqlTransaction tx, string type, string sourceType, int lotId, string whLotNo,
                         string partNo, string location, decimal before, decimal change, decimal after, string note,
                         string operatorId, string employeeNo)
    {
        using var cmd = new SqlCommand("dbo.SP_POP_BACKFLUSH_WriteTxn", conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        ColumnParams.Add(cmd, "@T",   "WH_InventoryTransaction.TransactionType", type);
        ColumnParams.Add(cmd, "@P",   "WH_InventoryTransaction.PartNo",          partNo);
        ColumnParams.Add(cmd, "@Loc", "WH_InventoryTransaction.LocationNo",      location);
        ColumnParams.Add(cmd, "@L",   "WH_InventoryTransaction.LotNo",           whLotNo);
        ColumnParams.Add(cmd, "@B",   "WH_InventoryTransaction.QtyBefore",       before);
        ColumnParams.Add(cmd, "@C",   "WH_InventoryTransaction.QtyChange",       change);
        ColumnParams.Add(cmd, "@A",   "WH_InventoryTransaction.QtyAfter",        after);
        ColumnParams.Add(cmd, "@S",   "WH_InventoryTransaction.SourceType",      sourceType);
        ColumnParams.Add(cmd, "@Src", "WH_InventoryTransaction.SourceID",        lotId);
        ColumnParams.Add(cmd, "@Op",  "WH_InventoryTransaction.OperatorID",      operatorId);
        ColumnParams.Add(cmd, "@N",   "WH_InventoryTransaction.Note",            note);
        ColumnParams.Add(cmd, "@By",  "WH_InventoryTransaction.CreatedBy",       employeeNo.Length > 20 ? employeeNo[..20] : employeeNo);
        cmd.ExecuteNonQuery();
    }
}
