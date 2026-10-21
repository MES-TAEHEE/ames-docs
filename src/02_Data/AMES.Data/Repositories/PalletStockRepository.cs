using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// POP INJ/IMG 확정 때 OK 제품(1 LOT = 1 EA)을 스캔한 팔레트에 싣는다(2026-10-21 사용자 결정).
/// WH_Inventory 의 팔레트 묶음 규칙(SCM 케이스·PDA 출고와 같음)을 따른다 — 머리 행(LotNo = 팔레트 번호, UnitType PALLET, Qty 0)
/// 아래 제품 행(ParentLotNo = PalletNo = 팔레트 번호, UnitType PART, Qty 1, LocationNo NULL = 미배정 — PDA FG 입고가 위치를 준다).
/// 적재 판정은 확정 트랜잭션 안에서 MD_Pallet 행을 잠그고 하므로 두 터미널이 같은 팔레트에 동시에 실어도 적입수량을 넘지 않는다.
/// 실린 수 = 그 팔레트 자식 행 중 Qty &gt; 0 인 것 — 출고(OUT)되면 0 이 되고 회수 뒤 다시 쓸 수 있다.
/// </summary>
public sealed class PalletStockRepository
{
    public const string ConfirmSourceType = "PROD_CONFIRM";
    public const string CoreUseSourceType = "IMG_CORE";

    readonly AmesConnectionFactory _factory;
    public PalletStockRepository(AmesConnectionFactory factory) => _factory = factory;

    /// <param name="PalletQty">품번의 적입수량(MD_Item.PalletQty) — 품번을 안 넘겼거나 없으면 null.</param>
    public sealed record PalletStatus(string PalletNo, PalletLoadRules.PalletState State, int? PalletQty, PalletLoadOutcome Outcome)
    {
        public bool IsFull => PalletLoadRules.IsFull(State.LoadedCount, PalletQty);
        public string CapacityText => PalletLoadRules.CapacityText(State.LoadedCount, PalletQty);
    }

    /// <summary>화면 칩·팔레트 스캔용 조회(잠금 없음). itemNo 가 있으면 그 품번 기준 적재 가능 여부까지 낸다.</summary>
    public PalletStatus Inspect(string palletNo, string? itemNo)
    {
        var no = PalletLoadRules.NormalizePalletNo(palletNo) ?? string.Empty;
        using var conn = _factory.OpenConnection();
        var state = ReadState(conn, null, no);
        var qty   = string.IsNullOrEmpty(itemNo) ? null : ReadPalletQty(conn, null, itemNo);
        var outcome = string.IsNullOrEmpty(itemNo)
            ? PalletLoadRules.Check(state, state.LoadedPartNo ?? string.Empty, qty)
            : PalletLoadRules.Check(state, itemNo, qty);
        return new PalletStatus(no, state, qty, outcome);
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
        var outcome = PalletLoadRules.Check(state, itemNo, ReadPalletQty(conn, tx, itemNo));
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
    /// IMG 가 코어를 스캔해 완제품 LOT 을 만드는 순간 그 코어는 팔레트에서 나와 제품의 일부가 된다 — 코어 재고 행을 0 으로 내리고 OUT 거래를 남긴다.
    /// 재고 행이 없거나 이미 0 이면(팔레트 없이 확정된 옛 코어·배포 전 코어) 아무것도 하지 않는다. SourceID = 완제품 LotID.
    /// </summary>
    internal static void ConsumeCore(SqlConnection conn, SqlTransaction tx, int finishedLotId, string coreLotCode, string coreItemNo,
                                     string operatorId)
    {
        decimal before;
        using (var cmd = new SqlCommand("""
            UPDATE dbo.WH_Inventory SET Qty = 0, UpdatedAt = SYSDATETIME()
            OUTPUT DELETED.Qty
            WHERE  LotNo = @L AND Qty > 0;
            """, conn, tx))
        {
            ColumnParams.AddFilter(cmd, "@L", "WH_Inventory.LotNo", coreLotCode);
            if (cmd.ExecuteScalar() is not decimal q) return;
            before = q;
        }
        WriteTxn(conn, tx, "OUT", CoreUseSourceType, finishedLotId, coreLotCode, coreItemNo, before, -before, 0m,
                 $"{coreLotCode} {coreItemNo} → IMG", operatorId, operatorId);
    }

    /// <summary>팔레트 상태 — tx 가 있으면 MD_Pallet 행을 UPDLOCK·HOLDLOCK 으로 잡아 동시 적재를 줄 세운다.</summary>
    static PalletLoadRules.PalletState ReadState(SqlConnection conn, SqlTransaction? tx, string palletNo)
    {
        var hint = tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)";
        using var cmd = new SqlCommand($"""
            SELECT p.ActiveFlag, p.ShippedFlag,
                   (SELECT COUNT(*) FROM dbo.WH_Inventory c WHERE c.ParentLotNo = p.PalletNo AND c.Qty > 0)        AS Loaded,
                   (SELECT TOP 1 c.PartNo FROM dbo.WH_Inventory c WHERE c.ParentLotNo = p.PalletNo AND c.Qty > 0
                    ORDER BY c.ReceivedAt DESC)                                                                  AS LoadedPart
            FROM   dbo.MD_Pallet p {hint}
            WHERE  p.PalletNo = @P;
            """, conn, tx);
        ColumnParams.AddFilter(cmd, "@P", "MD_Pallet.PalletNo", palletNo);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return PalletLoadRules.PalletState.Missing;
        return new PalletLoadRules.PalletState(true, (bool)r["ActiveFlag"], (bool)r["ShippedFlag"],
                                               r["LoadedPart"] as string, Convert.ToInt32(r["Loaded"]));
    }

    static int? ReadPalletQty(SqlConnection conn, SqlTransaction? tx, string itemNo)
    {
        using var cmd = new SqlCommand("SELECT PalletQty FROM dbo.MD_Item WHERE ItemNo = @I;", conn, tx);
        ColumnParams.AddFilter(cmd, "@I", "MD_Item.ItemNo", itemNo);
        return cmd.ExecuteScalar() as int?;
    }

    static void WriteTxn(SqlConnection conn, SqlTransaction tx, string type, string sourceType, int sourceId, string lotNo,
                         string partNo, decimal before, decimal change, decimal after, string note, string operatorId, string createdBy)
    {
        using var cmd = new SqlCommand("""
            INSERT dbo.WH_InventoryTransaction
                (TransactionTime, TransactionType, PartNo, LocationNo, LotNo, QtyBefore, QtyChange, QtyAfter,
                 SourceType, SourceID, OperatorID, Note, CreatedBy, CreatedTS)
            VALUES
                (SYSDATETIME(), @T, @P, NULL, @L, @B, @C, @A, @S, @Src, @Op, @N, @By, SYSDATETIME());
            """, conn, tx);
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
