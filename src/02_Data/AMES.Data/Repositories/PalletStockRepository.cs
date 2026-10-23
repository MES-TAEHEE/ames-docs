using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// POP INJ/IMG 확정 때 OK 제품(1 LOT = 1 EA)을 스캔한 팔레트에 싣는다(2026-10-21 사용자 결정).
/// WH_Inventory 의 팔레트 묶음 규칙(SCM 케이스·PDA 출고와 같음)을 따른다 — 머리 행(LotNo = 팔레트 번호, UnitType PALLET, Qty 0)
/// 아래 제품 행(ParentLotNo = PalletNo = 팔레트 번호, UnitType PART, Qty 1, LocationNo NULL = 미배정 — PDA FG 입고가 위치를 준다).
/// 적재 판정은 확정 트랜잭션 안에서 MD_Pallet 행을 잠그고 하므로 두 터미널이 같은 팔레트에 동시에 실어도 적입수량을 넘지 않는다.
/// 실린 수 = 그 팔레트 자식 행 중 Qty &gt; 0 인 것을 품번별로 센 것 — 출고(OUT)되면 0 이 되고 회수 뒤 다시 쓸 수 있다.
/// 형제 품번(같은 활성 금형·색상의 MD_MoldItem 품번)은 함께 실릴 수 있고 적입수량은 품번마다 따로 센다(<see cref="PalletLoadRules"/>).
/// </summary>
public sealed class PalletStockRepository
{
    public const string ConfirmSourceType = "PROD_CONFIRM";
    public const string CoreUseSourceType = "IMG_CORE";
    /// <summary>라인 불량 팝업 등록이 팔레트에서 뺀 LOT 의 OUT 거래 SourceType(SourceID = 그 LotID).</summary>
    public const string DefectSourceType  = "POP_DEFECT";
    /// <summary>재작업 스테이션 폐기가 팔레트에서 뺀 LOT 의 OUT 거래 SourceType — 불량 등록이 이미 뺐으면 아무것도 쓰지 않는 안전망.</summary>
    public const string ScrapSourceType   = "POP_SCRAP";

    /// <summary>
    /// <see cref="Unload"/> 결과 — <paramref name="Removed"/> 면 재고 행이 있어서 지웠고, <paramref name="LocationNo"/> 가 있으면
    /// 그 행이 이미 PDA FG 입고로 위치를 받은 뒤였다(창고 재고에서 빠졌으니 화면이 경고한다).
    /// </summary>
    public sealed record PalletUnloadResult(bool Removed, string? PalletNo, string? LocationNo)
    {
        public static readonly PalletUnloadResult None = new(false, null, null);
        public bool WasLocated => Removed && !string.IsNullOrEmpty(LocationNo);
    }

    readonly AmesConnectionFactory _factory;
    public PalletStockRepository(AmesConnectionFactory factory) => _factory = factory;

    /// <param name="ItemNo">판정 기준 품번(화면이 고른 품번) — 적입수량·칩 문구가 이 품번 기준. 없으면 null.</param>
    /// <param name="PalletQty">그 품번의 적입수량(MD_Item.PalletQty) — 품번을 안 넘겼거나 없으면 null.</param>
    public sealed record PalletStatus(string PalletNo, PalletLoadRules.PalletState State, string? ItemNo, int? PalletQty, PalletLoadOutcome Outcome)
    {
        public bool IsFull => PalletLoadRules.IsFull(ItemNo is null ? State.TotalCount : State.CountOf(ItemNo), PalletQty);
        public string CapacityText => PalletLoadRules.CapacityText(State, ItemNo ?? string.Empty, PalletQty);
    }

    /// <summary>화면 칩·팔레트 스캔용 조회(잠금 없음). itemNo 가 있으면 그 품번 기준 적재 가능 여부까지 낸다.</summary>
    public PalletStatus Inspect(string palletNo, string? itemNo)
    {
        var no = PalletLoadRules.NormalizePalletNo(palletNo) ?? string.Empty;
        using var conn = _factory.OpenConnection();
        var state = ReadState(conn, null, no);
        if (string.IsNullOrEmpty(itemNo))
        {
            // 고른 품번이 없으면 실린 첫 품번 기준으로 보여 준다(칩 전용 — 적재 판정은 LOT 품번으로 다시 한다)
            var first = state.Loaded.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                    .Select(kv => kv.Key).FirstOrDefault();
            var q0 = first is null ? null : ReadPalletQty(conn, null, first);
            var o0 = PalletLoadRules.Check(state, first ?? string.Empty, q0, first is null ? EmptySet : ReadSiblings(conn, null, first));
            return new PalletStatus(no, state, first, q0, o0);
        }
        var qty = ReadPalletQty(conn, null, itemNo);
        var outcome = PalletLoadRules.Check(state, itemNo, qty, ReadSiblings(conn, null, itemNo));
        return new PalletStatus(no, state, itemNo, qty, outcome);
    }

    /// <summary>오프라인 사본용 팔레트 한 줄 — 사용 중이고 출고되지 않은 팔레트와 품번별 실린 수.</summary>
    public sealed record OfflinePalletRow(string PalletNo, bool Active, bool Shipped, Dictionary<string, int> Loaded);

    /// <summary>오프라인 사본용 품번 규칙 — 적입수량과 함께 실릴 수 있는 형제 품번(자기 자신 포함).</summary>
    public sealed record OfflineItemRule(int? PalletQty, string[] Siblings);

    /// <summary>
    /// 오프라인 사본용: 사용 중(ActiveFlag 1)이고 출고되지 않은(ShippedFlag 0) 팔레트 전부와 품번별 실린 수.
    /// 오프라인에서 새로 발행된 팔레트는 여기에 없다 — 화면은 모르는 팔레트를 받아들이고 복구 뒤 서버가 검증한다.
    /// </summary>
    public List<OfflinePalletRow> ListForOffline()
    {
        using var conn = _factory.OpenConnection();
        var rows = new Dictionary<string, OfflinePalletRow>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = new SqlCommand("""
            SELECT p.PalletNo, p.ActiveFlag, p.ShippedFlag
            FROM   dbo.MD_Pallet p
            WHERE  p.ActiveFlag = 1 AND p.ShippedFlag = 0;
            """, conn) { CommandTimeout = 120 })
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var no = (string)r["PalletNo"];
                rows[no] = new OfflinePalletRow(no, (bool)r["ActiveFlag"], (bool)r["ShippedFlag"],
                                                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
            }
        }
        using (var cmd = new SqlCommand("""
            SELECT c.ParentLotNo, ISNULL(c.PartNo, '') AS PartNo, COUNT(*) AS Loaded
            FROM   dbo.WH_Inventory c
            WHERE  c.ParentLotNo IS NOT NULL AND c.Qty > 0
              AND  c.ParentLotNo IN (SELECT PalletNo FROM dbo.MD_Pallet WHERE ActiveFlag = 1 AND ShippedFlag = 0)
            GROUP  BY c.ParentLotNo, c.PartNo;
            """, conn) { CommandTimeout = 120 })
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (rows.TryGetValue((string)r["ParentLotNo"], out var row))
                    row.Loaded[(string)r["PartNo"]] = Convert.ToInt32(r["Loaded"]);
        }
        return rows.Values.ToList();
    }

    /// <summary>오프라인 사본용: 품번마다 적입수량·형제 품번을 읽는다(<see cref="Load"/> 판정에 쓰는 것과 같은 값).</summary>
    public Dictionary<string, OfflineItemRule> ReadItemRules(IEnumerable<string> itemNos)
    {
        var map = new Dictionary<string, OfflineItemRule>(StringComparer.OrdinalIgnoreCase);
        using var conn = _factory.OpenConnection();
        foreach (var item in itemNos.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.OrdinalIgnoreCase))
            map[item] = new OfflineItemRule(ReadPalletQty(conn, null, item), ReadSiblings(conn, null, item).ToArray());
        return map;
    }

    /// <summary>
    /// 확정 트랜잭션 안에서: 팔레트를 잠그고 판정 → Ok 면 머리 행(없을 때)·제품 행·IN 거래를 쓴다.
    /// Ok 가 아니면 아무것도 쓰지 않는다 — 호출자가 트랜잭션을 되돌려 확정도 취소한다.
    /// </summary>
    internal static PalletLoadOutcome Load(SqlConnection conn, SqlTransaction tx, int lotId, string lotCode, string itemNo,
                                           string palletNo, string operatorId, string employeeNo)
    {
        var no = PalletLoadRules.NormalizePalletNo(palletNo) ?? string.Empty;
        var state   = ReadState(conn, tx, no);
        var outcome = PalletLoadRules.Check(state, itemNo, ReadPalletQty(conn, tx, itemNo), ReadSiblings(conn, tx, itemNo));
        if (outcome != PalletLoadOutcome.Ok) return outcome;

        using (var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.WH_Inventory WITH (UPDLOCK, HOLDLOCK) WHERE LotNo = @P)
                INSERT dbo.WH_Inventory (LotNo, UnitType, PalletNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
                VALUES (@P, 'PALLET', @P, 0, SYSDATETIME(), SYSDATETIME(), SYSDATETIME());
            INSERT dbo.WH_Inventory (LotNo, UnitType, ParentLotNo, PartNo, PartName, PalletNo, LocationNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
            SELECT @L, 'PART', @P, @I, (SELECT TOP 1 ItemName FROM dbo.MD_Item WHERE ItemNo = @I), @P, NULL, 1,
                   SYSDATETIME(), SYSDATETIME(), SYSDATETIME();
            """, conn, tx))
        {
            ColumnParams.Add(cmd, "@P", "WH_Inventory.PalletNo", no);
            ColumnParams.Add(cmd, "@L", "WH_Inventory.LotNo",    lotCode);
            ColumnParams.Add(cmd, "@I", "WH_Inventory.PartNo",   itemNo);
            cmd.ExecuteNonQuery();
        }
        WriteTxn(conn, tx, "IN", ConfirmSourceType, lotId, lotCode, itemNo, 0m, 1m, 1m,
                 $"{lotCode} {itemNo} → {no}", operatorId, employeeNo);
        return PalletLoadOutcome.Ok;
    }

    /// <summary>
    /// IMG 가 코어를 스캔해 완제품 LOT 을 만드는 순간 그 코어는 팔레트에서 나와 제품의 일부가 된다 — 코어 재고 행을 **지우고** OUT 거래만 남긴다
    /// (2026-10-22 사용자 결정: 0 으로 남기지 않는다. 이력은 WH_InventoryTransaction 이 가진다).
    /// 재고 행이 없으면(팔레트 없이 확정된 옛 코어·배포 전 코어) 아무것도 하지 않고, 수량 0 으로 남아 있던 옛 행은 거래 없이 지운다. SourceID = 완제품 LotID.
    /// </summary>
    internal static void ConsumeCore(SqlConnection conn, SqlTransaction tx, int finishedLotId, string coreLotCode, string coreItemNo,
                                     string operatorId)
    {
        decimal before;
        using (var cmd = new SqlCommand("""
            DELETE dbo.WH_Inventory
            OUTPUT DELETED.Qty
            WHERE  LotNo = @L;
            """, conn, tx))
        {
            ColumnParams.AddFilter(cmd, "@L", "WH_Inventory.LotNo", coreLotCode);
            if (cmd.ExecuteScalar() is not decimal q || q <= 0m) return;
            before = q;
        }
        WriteTxn(conn, tx, "OUT", CoreUseSourceType, finishedLotId, coreLotCode, coreItemNo, before, -before, 0m,
                 $"{coreLotCode} {coreItemNo} → IMG", operatorId, operatorId);
    }

    /// <summary>
    /// 불량 등록·폐기 트랜잭션 안에서: 그 LOT 의 재고 행(팔레트 아래 제품 행 — 1 LOT = 1 EA)을 **지우고** OUT 거래만 남긴다
    /// (<see cref="ConsumeCore"/> 와 같은 10-22 규칙 — 0 으로 남기지 않는다). 불량품은 팔레트에서 빠져 재작업으로 가므로
    /// 팔레트 적재 수·APS 가용재고·FG 출고 대상에서 사라져야 한다. 재고 행이 없으면(RAW·NG_BLOCKED 등 실린 적 없는 LOT,
    /// 팔레트 없이 확정된 옛 LOT, 이미 뺀 LOT) 아무것도 하지 않고, 수량 0 으로 남아 있던 행은 거래 없이 지운다.
    /// 팔레트 머리 행(UnitType PALLET)은 LotNo 가 팔레트 번호라 이 조건에 걸리지 않는다.
    /// 이미 위치를 받은 행(PDA FG 입고 뒤)도 지운다 — 물건이 라인으로 돌아왔으니 창고 재고에서도 빠져야 한다; 결과의 LocationNo 로 화면이 경고한다.
    /// </summary>
    internal static PalletUnloadResult Unload(SqlConnection conn, SqlTransaction tx, int lotId, string lotCode, string itemNo,
                                              string sourceType, string reason, string operatorId, string employeeNo)
    {
        decimal before; string? palletNo, locationNo;
        using (var cmd = new SqlCommand("""
            DELETE dbo.WH_Inventory
            OUTPUT DELETED.Qty, DELETED.PalletNo, DELETED.LocationNo
            WHERE  LotNo = @L AND ISNULL(UnitType, '') <> 'PALLET';
            """, conn, tx))
        {
            ColumnParams.AddFilter(cmd, "@L", "WH_Inventory.LotNo", lotCode);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return PalletUnloadResult.None;
            before     = r["Qty"] is decimal q ? q : 0m;
            palletNo   = r["PalletNo"] as string;
            locationNo = r["LocationNo"] as string;
        }
        if (before > 0m)
            WriteTxn(conn, tx, "OUT", sourceType, lotId, lotCode, itemNo, before, -before, 0m,
                     $"{lotCode} {itemNo} {reason}{(palletNo is null ? "" : $" ← {palletNo}")}", operatorId, employeeNo, locationNo);
        return new PalletUnloadResult(true, palletNo, locationNo);
    }

    static readonly IReadOnlySet<string> EmptySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>팔레트 상태 — tx 가 있으면 MD_Pallet 행을 UPDLOCK·HOLDLOCK 으로 잡아 동시 적재를 줄 세운다. 실린 수는 품번별.</summary>
    static PalletLoadRules.PalletState ReadState(SqlConnection conn, SqlTransaction? tx, string palletNo)
    {
        var hint = tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)";
        bool active, shipped;
        using (var cmd = new SqlCommand($"SELECT p.ActiveFlag, p.ShippedFlag FROM dbo.MD_Pallet p {hint} WHERE p.PalletNo = @P;", conn, tx))
        {
            ColumnParams.AddFilter(cmd, "@P", "MD_Pallet.PalletNo", palletNo);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return PalletLoadRules.PalletState.Missing;
            active = (bool)r["ActiveFlag"]; shipped = (bool)r["ShippedFlag"];
        }
        var loaded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = new SqlCommand("""
            SELECT ISNULL(c.PartNo, '') AS PartNo, COUNT(*) AS Loaded
            FROM   dbo.WH_Inventory c
            WHERE  c.ParentLotNo = @P AND c.Qty > 0
            GROUP  BY c.PartNo;
            """, conn, tx))
        {
            ColumnParams.AddFilter(cmd, "@P", "WH_Inventory.ParentLotNo", palletNo);
            using var r = cmd.ExecuteReader();
            while (r.Read()) loaded[(string)r["PartNo"]] = Convert.ToInt32(r["Loaded"]);
        }
        return new PalletLoadRules.PalletState(true, active, shipped, loaded);
    }

    static int? ReadPalletQty(SqlConnection conn, SqlTransaction? tx, string itemNo)
    {
        using var cmd = new SqlCommand("SELECT PalletQty FROM dbo.MD_Item WHERE ItemNo = @I;", conn, tx);
        ColumnParams.AddFilter(cmd, "@I", "MD_Item.ItemNo", itemNo);
        return cmd.ExecuteScalar() as int?;
    }

    /// <summary>itemNo 와 같은 활성 금형·색상에 묶인 품번(형제) — APS 가 형제를 묶는 기준(금형 × 색상)과 같다. itemNo 자신도 들어 있다.</summary>
    static IReadOnlySet<string> ReadSiblings(SqlConnection conn, SqlTransaction? tx, string itemNo)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand("""
            SELECT DISTINCT s.ItemNo
            FROM   dbo.MD_MoldItem m
            JOIN   dbo.MD_MoldItem s ON s.MoldID = m.MoldID
                                    AND ISNULL(s.Color, '') = ISNULL(m.Color, '')
                                    AND s.ActiveFlag = 1
            WHERE  m.ItemNo = @I AND m.ActiveFlag = 1;
            """, conn, tx);
        ColumnParams.AddFilter(cmd, "@I", "MD_MoldItem.ItemNo", itemNo);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add((string)r["ItemNo"]);
        return set;
    }

    static void WriteTxn(SqlConnection conn, SqlTransaction tx, string type, string sourceType, int sourceId, string lotNo,
                         string partNo, decimal before, decimal change, decimal after, string note, string operatorId, string createdBy,
                         string? locationNo = null)
    {
        using var cmd = new SqlCommand("""
            INSERT dbo.WH_InventoryTransaction
                (TransactionTime, TransactionType, PartNo, LocationNo, LotNo, QtyBefore, QtyChange, QtyAfter,
                 SourceType, SourceID, OperatorID, Note, CreatedBy, CreatedTS)
            VALUES
                (SYSDATETIME(), @T, @P, @Loc, @L, @B, @C, @A, @S, @Src, @Op, @N, @By, SYSDATETIME());
            """, conn, tx);
        ColumnParams.Add(cmd, "@Loc", "WH_InventoryTransaction.LocationNo",      (object?)locationNo ?? DBNull.Value);
        ColumnParams.Add(cmd, "@T",   "WH_InventoryTransaction.TransactionType", type);
        ColumnParams.Add(cmd, "@P",   "WH_InventoryTransaction.PartNo",          partNo);
        ColumnParams.Add(cmd, "@L",   "WH_InventoryTransaction.LotNo",           lotNo);
        ColumnParams.Add(cmd, "@B",   "WH_InventoryTransaction.QtyBefore",       before);
        ColumnParams.Add(cmd, "@C",   "WH_InventoryTransaction.QtyChange",       change);
        ColumnParams.Add(cmd, "@A",   "WH_InventoryTransaction.QtyAfter",        after);
        ColumnParams.Add(cmd, "@S",   "WH_InventoryTransaction.SourceType",      sourceType);
        ColumnParams.Add(cmd, "@Src", "WH_InventoryTransaction.SourceID",        sourceId);
        ColumnParams.Add(cmd, "@Op",  "WH_InventoryTransaction.OperatorID",      operatorId.Length > 20 ? operatorId[..20] : operatorId);
        ColumnParams.Add(cmd, "@N",   "WH_InventoryTransaction.Note",            note);
        ColumnParams.Add(cmd, "@By",  "WH_InventoryTransaction.CreatedBy",       createdBy.Length > 20 ? createdBy[..20] : createdBy);
        cmd.ExecuteNonQuery();
    }
}
