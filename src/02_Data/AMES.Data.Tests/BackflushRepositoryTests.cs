using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>한 트랜잭션 안에서 픽스처를 넣고 Apply 를 돌린 뒤 롤백한다 — 개발 DB 에 행이 남지 않는다.</summary>
[Collection(BackflushDbCollection.Name)]
public class BackflushRepositoryTests
{
    const string WriteSkip = "set AMES_TEST_CONN to run — writes inside a rolled-back transaction";
    const string Parent = "BF-T-PARENT", Resin = "BF-T-RESIN", Line = "LINE-INJ-01", Prefix = "I1";
    const int LotId = -990015;

    static AmesConnectionFactory? WriteFactory()
        => Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : AmesDevDb.TryFactory();

    static void Exec(SqlConnection c, SqlTransaction tx, string sql)
    { using var cmd = new SqlCommand(sql, c, tx); cmd.ExecuteNonQuery(); }

    static T Scalar<T>(SqlConnection c, SqlTransaction tx, string sql)
    { using var cmd = new SqlCommand(sql, c, tx); return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T)); }

    /// <summary>
    /// 자리표시 음수를 허용하는 CHECK(migrate_wh_inventory_backflush_placeholder.sql)를 이 트랜잭션 안에서 보장한다 —
    /// 마이그레이션 전 DB 에서도 테스트가 돌고, DDL 도 끝에 같이 롤백된다.
    /// </summary>
    static void EnsurePlaceholderCheck(SqlConnection c, SqlTransaction tx) => Exec(c, tx, """
        DECLARE @Def nvarchar(max) = (SELECT definition FROM sys.check_constraints
                                      WHERE name = N'CK_WH_Inventory_Qty' AND parent_object_id = OBJECT_ID(N'dbo.WH_Inventory'));
        IF @Def IS NULL OR @Def NOT LIKE N'%BF-%'
        BEGIN
            IF @Def IS NOT NULL ALTER TABLE dbo.WH_Inventory DROP CONSTRAINT CK_WH_Inventory_Qty;
            ALTER TABLE dbo.WH_Inventory WITH CHECK ADD CONSTRAINT CK_WH_Inventory_Qty CHECK (Qty >= 0 OR LotNo LIKE N'BF-%');
        END
        """);

    /// <summary>부모 품목 + 레진 자재 + APPROVED BOM(QtyPer 977.49 G). 라인 재고는 호출자가 넣는다.</summary>
    static void SeedBom(SqlConnection c, SqlTransaction tx, decimal qtyPer = 977.49m) => Exec(c, tx, $"""
        INSERT dbo.MD_Item (ItemNo, ItemName, ItemType, InjFlag, ToteFlag, ScanRequired, DefaultUOM, CreatedBy)
        VALUES ('{Parent}', N'bf test parent', 'SUB', 1, 0, 0, 'EA', 'BF-TEST'),
               ('{Resin}',  N'bf test resin',  'MATERIAL', 0, 0, 0, 'G', 'BF-TEST');
        INSERT dbo.MD_BomVersion (VersionID, RootItemNo, VersionNo, EffFrom, Status, CreatedBy)
        VALUES ('V-BF-T-01', '{Parent}', 'V1', '2026-01-01', 'APPROVED', 'BF-TEST');
        INSERT dbo.MD_Bom (BOMID, ParentItemNo, CompItemNo, BOMLevel, QtyPer, UOM, ScrapPct, VersionID, ActiveFlag, CreatedBy)
        VALUES ('B-BF-T-01', '{Parent}', '{Resin}', 1, {qtyPer}, 'G', 0, 'V-BF-T-01', 1, 'BF-TEST');
        """);

    static void SeedLineLot(SqlConnection c, SqlTransaction tx, string lotNo, decimal qty, string receivedAt) => Exec(c, tx, $"""
        INSERT dbo.WH_Inventory (LotNo, UnitType, PartNo, LocationNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
        VALUES (N'{lotNo}', 'BOX', '{Resin}', '{Prefix}', {qty}, '{receivedAt}', '{receivedAt}', '{receivedAt}');
        """);

    static decimal LotQty(SqlConnection c, SqlTransaction tx, string lotNo)
        => Scalar<decimal>(c, tx, $"SELECT ISNULL((SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=N'{lotNo}'), -9999)");

    static int TxnCount(SqlConnection c, SqlTransaction tx, string sourceType)
        => Scalar<int>(c, tx, $"SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE SourceType='{sourceType}' AND SourceID={LotId}");

    static BackflushResult Apply(SqlConnection c, SqlTransaction tx)
        => BackflushRepository.Apply(c, tx, LotId, "BFTESTLOT", Parent, Line, "E-BFT", "E-BFT");

    [SkippableFact]
    public void Apply_takes_fifo_from_line_lots_and_records_out_transactions()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var c = f!.OpenConnection(); using var tx = c.BeginTransaction();
        try
        {
            SeedBom(c, tx);
            SeedLineLot(c, tx, "BF-T-L2", 500m, "2026-10-02");   // 나중 입고
            SeedLineLot(c, tx, "BF-T-L1", 600m, "2026-10-01");   // 먼저 입고 → 먼저 소진

            var r = Apply(c, tx);

            Assert.Null(r.Error); Assert.False(r.Skipped); Assert.Empty(r.Shortages);
            Assert.Equal(0m,       LotQty(c, tx, "BF-T-L1"));
            Assert.Equal(122.510m, LotQty(c, tx, "BF-T-L2"));          // 500 − 377.49
            Assert.Equal(-9999m,   LotQty(c, tx, BackflushRules.PlaceholderLotNo(Prefix, Resin)));   // 자리표시 없음
            Assert.Equal(2, TxnCount(c, tx, BackflushRepository.SourceType));
            Assert.Equal(-977.490m, Scalar<decimal>(c, tx,
                $"SELECT SUM(QtyChange) FROM dbo.WH_InventoryTransaction WHERE SourceType='BACKFLUSH' AND SourceID={LotId}"));
            Assert.Equal("BFTESTLOT " + Parent, Scalar<string>(c, tx,
                $"SELECT TOP 1 Note FROM dbo.WH_InventoryTransaction WHERE SourceType='BACKFLUSH' AND SourceID={LotId}"));
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Apply_without_line_stock_creates_negative_placeholder_and_reports_shortage()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var c = f!.OpenConnection(); using var tx = c.BeginTransaction();
        try
        {
            EnsurePlaceholderCheck(c, tx);
            SeedBom(c, tx);
            var r = Apply(c, tx);
            var ph = BackflushRules.PlaceholderLotNo(Prefix, Resin);
            Assert.Null(r.Error);
            var s = Assert.Single(r.Shortages);
            Assert.Equal(Resin, s.PartNo); Assert.Equal(977.490m, s.Qty); Assert.Equal("G", s.Uom);
            Assert.Equal(-977.490m, LotQty(c, tx, ph));
            Assert.Equal("PART", Scalar<string>(c, tx, $"SELECT UnitType FROM dbo.WH_Inventory WHERE LotNo=N'{ph}'"));
            Assert.Equal(Prefix, Scalar<string>(c, tx, $"SELECT LocationNo FROM dbo.WH_Inventory WHERE LotNo=N'{ph}'"));
            Assert.Equal(1, TxnCount(c, tx, BackflushRepository.SourceType));
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Apply_is_idempotent_per_lot()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var c = f!.OpenConnection(); using var tx = c.BeginTransaction();
        try
        {
            SeedBom(c, tx);
            SeedLineLot(c, tx, "BF-T-L1", 2000m, "2026-10-01");
            Assert.False(Apply(c, tx).Skipped);
            var again = Apply(c, tx);
            Assert.True(again.Skipped);
            Assert.Equal(1022.510m, LotQty(c, tx, "BF-T-L1"));
            Assert.Equal(1, TxnCount(c, tx, BackflushRepository.SourceType));
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Apply_nets_negative_placeholder_from_newly_arrived_lot_first()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var c = f!.OpenConnection(); using var tx = c.BeginTransaction();
        try
        {
            EnsurePlaceholderCheck(c, tx);
            SeedBom(c, tx, qtyPer: 100m);
            var ph = BackflushRules.PlaceholderLotNo(Prefix, Resin);
            Exec(c, tx, $"""
                INSERT dbo.WH_Inventory (LotNo, UnitType, PartNo, LocationNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
                VALUES (N'{ph}', 'PART', '{Resin}', '{Prefix}', -30, SYSDATETIME(), SYSDATETIME(), SYSDATETIME());
                """);
            SeedLineLot(c, tx, "BF-T-L1", 500m, "2026-10-01");

            var r = Apply(c, tx);

            Assert.Empty(r.Shortages);
            Assert.Equal(0m,   LotQty(c, tx, ph));           // −30 메움
            Assert.Equal(370m, LotQty(c, tx, "BF-T-L1"));    // 500 − 30 − 100
            Assert.Equal(2, TxnCount(c, tx, BackflushRepository.NetSourceType));   // LOT −30, 자리표시 +30
            Assert.Equal(1, TxnCount(c, tx, BackflushRepository.SourceType));
        }
        finally { tx.Rollback(); }
    }

    /// <summary>
    /// 정본(10-28 사용자 결정): 재고가 모자라 자리표시에서 뺀 LOT 은, 뒤에 다른 LOT 확정이 실물 LOT 으로 메울 때
    /// 그 메우기 기록의 주인이 된다 — 자재 LOT 사용 기록(VW_MaterialLotUsage)에서 원래 LOT 이 실물 LOT 을 쓴 것으로 보인다.
    /// </summary>
    [SkippableFact]
    public void Netting_a_shortage_is_recorded_against_the_lot_that_caused_it()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        const int first = -990021, second = -990022;
        using var c = f!.OpenConnection(); using var tx = c.BeginTransaction();
        try
        {
            EnsurePlaceholderCheck(c, tx);
            SeedBom(c, tx, qtyPer: 100m);
            var ph = BackflushRules.PlaceholderLotNo(Prefix, Resin);

            Assert.Single(BackflushRepository.Apply(c, tx, first, "BFFIRST", Parent, Line, "E-BFT", "E-BFT").Shortages);
            Assert.Equal(-100m, LotQty(c, tx, ph));

            SeedLineLot(c, tx, "BF-T-L1", 500m, "2026-10-01");
            Assert.Empty(BackflushRepository.Apply(c, tx, second, "BFSECOND", Parent, Line, "E-BFT", "E-BFT").Shortages);
            Assert.Equal(0m,   LotQty(c, tx, ph));
            Assert.Equal(300m, LotQty(c, tx, "BF-T-L1"));

            int Nets(int lot) => Scalar<int>(c, tx, $"SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE SourceType='BACKFLUSH_NET' AND SourceID={lot}");
            Assert.Equal(2, Nets(first));
            Assert.Equal(0, Nets(second));
            Assert.Contains("(by BFSECOND)", Scalar<string>(c, tx,
                $"SELECT TOP 1 Note FROM dbo.WH_InventoryTransaction WHERE SourceType='BACKFLUSH_NET' AND SourceID={first} AND LotNo=N'BF-T-L1'"));

            string Usage(int lot) => Scalar<string>(c, tx, $"""
                SELECT ISNULL(STRING_AGG(CONCAT(MaterialLotNo, '=', CAST(CAST(Q AS DECIMAL(18,3)) AS FLOAT)), ',')
                              WITHIN GROUP (ORDER BY MaterialLotNo), '')
                FROM (SELECT MaterialLotNo, SUM(Qty) AS Q FROM dbo.VW_MaterialLotUsage WHERE FinishedLotID = {lot}
                      GROUP BY MaterialLotNo HAVING SUM(Qty) <> 0) x
                """);
            Assert.Equal("BF-T-L1=100", Usage(first));    // 자리표시 몫은 0 으로 사라지고 실물 LOT 이 남는다
            Assert.Equal("BF-T-L1=100", Usage(second));
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Apply_without_material_children_writes_nothing()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var c = f!.OpenConnection(); using var tx = c.BeginTransaction();
        try
        {
            Exec(c, tx, $"INSERT dbo.MD_Item (ItemNo, ItemName, ItemType, InjFlag, ToteFlag, ScanRequired, CreatedBy) VALUES ('{Parent}', N'x', 'SUB', 1, 0, 0, 'BF-TEST');");
            var r = Apply(c, tx);
            Assert.Null(r.Error); Assert.False(r.Skipped); Assert.Empty(r.Shortages);
            Assert.Equal(0, TxnCount(c, tx, BackflushRepository.SourceType));
            Assert.Equal(0, Scalar<int>(c, tx, $"SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo LIKE N'BF-{Prefix}-%' AND PartNo='{Resin}'"));
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Apply_returns_error_when_line_has_no_prefix_and_changes_nothing()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var c = f!.OpenConnection(); using var tx = c.BeginTransaction();
        try
        {
            SeedBom(c, tx);
            var r = BackflushRepository.Apply(c, tx, LotId, "BFTESTLOT", Parent, "LINE-RWK-01", "E-BFT", "E-BFT");
            Assert.NotNull(r.Error);
            Assert.Equal(0, TxnCount(c, tx, BackflushRepository.SourceType));
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Apply_rolls_back_only_its_own_savepoint_on_error()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var c = f!.OpenConnection(); using var tx = c.BeginTransaction();
        try
        {
            SeedBom(c, tx);
            // 거래 INSERT 가 실패하게 만든다 — 그 전에 만든 자리표시 행까지 차감 블록만 되돌아가야 한다
            Exec(c, tx, "ALTER TABLE dbo.WH_InventoryTransaction WITH NOCHECK ADD CONSTRAINT CK_BF_TEST CHECK (SourceType <> 'BACKFLUSH');");

            var before = Scalar<int>(c, tx, "SELECT COUNT(*) FROM dbo.WH_Inventory");
            var r = Apply(c, tx);

            Assert.NotNull(r.Error);
            var after = Scalar<int>(c, tx, "SELECT COUNT(*) FROM dbo.WH_Inventory");
            Assert.True(before == after, $"placeholder insert survived the savepoint rollback ({before} → {after}); error was: {r.Error}");
            Assert.Equal(0, TxnCount(c, tx, BackflushRepository.SourceType));
            // 트랜잭션은 살아 있어 호출자가 계속 쓸 수 있다
            Assert.Equal(1, Scalar<int>(c, tx, "SELECT 1"));
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Placeholder_read_holds_a_range_lock_so_a_concurrent_first_shortage_waits()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var missing = BackflushRules.PlaceholderLotNo("I1", "BF-T-NOPE-" + Guid.NewGuid().ToString("N")[..8]);
        using var a = f!.OpenConnection(); using var ta = a.BeginTransaction();
        try
        {
            Assert.Null(BackflushRepository.ReadLotQty(a, ta, missing));   // 첫 부족: 자리표시 없음 → 곧 INSERT 할 참

            using var b = f.OpenConnection(); using var tb = b.BeginTransaction();
            try
            {
                Exec(b, tb, "SET LOCK_TIMEOUT 800;");
                // 두 번째 터미널의 같은 읽기는 A 가 끝날 때까지 기다려야 한다 — 둘 다 INSERT 하면 한쪽이 PK 충돌로 차감을 통째로 잃는다
                var ex = Assert.Throws<SqlException>(() => BackflushRepository.ReadLotQty(b, tb, missing));
                Assert.Equal(1222, ex.Number);
            }
            finally { tb.Rollback(); }
        }
        finally { ta.Rollback(); }
    }
}
