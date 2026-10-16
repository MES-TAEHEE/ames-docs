using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>
/// PP-002 수주 자동 확정 — DB 경로. 플래그가 켜져 있으면 업서트의 신규 행이 Confirmed 로 들어가고,
/// 켜기는 플래그 ON + 모든 Open 일괄 확정을 한 트랜잭션으로 한다.
/// 쓰기 테스트라 AMES_TEST_CONN 을 지정했을 때만 돈다. SYS_Config 행과 다른 Open 수주는 끝에 원상 복구한다.
/// </summary>
[Collection("SoAutoConfirm")]
public class SoAutoConfirmRepositoryTests
{
    const string WriteSkip = "set AMES_TEST_CONN to run — writes PP_CustomerOrder / SYS_Config and restores them";
    const string Actor = "itest-soac";
    const string Cust  = "ITEST-AC";

    static AmesConnectionFactory? WriteFactory()
        => Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : TryFactory();

    static PpRepository.CustomerOrderImportRow Row(string so, int line = 10) =>
        new(so, line, "ITEST-ITEM", 10m, 0m, new DateTime(2026, 10, 1), new DateTime(2026, 10, 20));

    static string StatusOf(AmesConnectionFactory f, string so)
        => (string)Scalar(f, "SELECT Status FROM dbo.PP_CustomerOrder WHERE SoNumber = @S AND CustomerID = @C", ("@S", so), ("@C", Cust))!;

    /// <summary>테스트 전 상태를 기억해 두고 끝에 되돌린다 — SYS_Config 행(없었으면 삭제)과 남의 Open 수주.</summary>
    sealed class Snapshot : IDisposable
    {
        readonly AmesConnectionFactory _f;
        readonly (string? Value, bool IsActive)? _cfg;
        readonly List<(int SoId, string? ModBy, DateTime? ModTs)> _open = new();

        public Snapshot(AmesConnectionFactory f)
        {
            _f = f;
            Cleanup();
            _cfg = new SysRepository(f).GetConfigFlag(SoAutoConfirm.ConfigKey);
            using var conn = f.OpenConnection();
            using var cmd  = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT SoID, ModifiedBy, ModifiedTS FROM dbo.PP_CustomerOrder WHERE Status = 'Open'", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) _open.Add(((int)r["SoID"], r["ModifiedBy"] as string, r["ModifiedTS"] as DateTime?));
        }

        public int ForeignOpenCount => _open.Count;

        void Cleanup() => Exec(_f, "DELETE dbo.PP_CustomerOrder WHERE CustomerID = @C", ("@C", Cust));

        public void Dispose()
        {
            Cleanup();
            foreach (var (id, by, ts) in _open)
                Exec(_f, "UPDATE dbo.PP_CustomerOrder SET Status = 'Open', ModifiedBy = @B, ModifiedTS = @T WHERE SoID = @I",
                    ("@I", id), ("@B", (object?)by ?? DBNull.Value), ("@T", (object?)ts ?? DBNull.Value));
            if (_cfg is null)
                Exec(_f, "DELETE dbo.SYS_Config WHERE ConfigKey = @K", ("@K", SoAutoConfirm.ConfigKey));
            else
                // 테스트가 행을 지웠을 수도 있어 UPDATE 0행이면 다시 넣는다(마이그레이션과 같은 모양)
                Exec(_f, """
                    UPDATE dbo.SYS_Config SET ConfigValue = @V, IsActive = @A WHERE ConfigKey = @K;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.SYS_Config (ConfigKey, ConfigType, Category, ConfigValue, CodeName, SortOrder, IsActive, CreatedBy)
                        VALUES (@K, 'bool', 'PP', @V, N'Auto-confirm imported customer orders (PP-002)', 20, @A, 'migration');
                    """,
                    ("@K", SoAutoConfirm.ConfigKey), ("@V", (object?)_cfg.Value.Value ?? DBNull.Value), ("@A", _cfg.Value.IsActive));
        }
    }

    [SkippableFact]
    public void Flag_defaults_to_off_when_row_is_missing()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var snap = new Snapshot(f!);
        Exec(f!, "DELETE dbo.SYS_Config WHERE ConfigKey = @K", ("@K", SoAutoConfirm.ConfigKey));

        Assert.False(new PpRepository(f!).GetSoAutoConfirm());
    }

    [SkippableFact]
    public void Upsert_inserts_open_rows_while_flag_is_off()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var snap = new Snapshot(f!);
        var pp = new PpRepository(f!);
        pp.DisableSoAutoConfirm(Actor);

        pp.UpsertCustomerOrders(Cust, new[] { Row("ITESTAC-OFF") }, Actor);

        Assert.Equal("Open", StatusOf(f!, "ITESTAC-OFF"));
    }

    [SkippableFact]
    public void Upsert_inserts_confirmed_rows_while_flag_is_on()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var snap = new Snapshot(f!);
        var pp = new PpRepository(f!);
        pp.EnableSoAutoConfirm(Actor);

        var (ins, upd) = pp.UpsertCustomerOrders(Cust, new[] { Row("ITESTAC-ON") }, Actor);

        Assert.Equal((1, 0), (ins, upd));
        Assert.Equal("Confirmed", StatusOf(f!, "ITESTAC-ON"));
    }

    [SkippableFact]
    public void Upsert_update_keeps_existing_open_status_even_when_flag_is_on()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var snap = new Snapshot(f!);
        var pp = new PpRepository(f!);
        pp.DisableSoAutoConfirm(Actor);
        pp.UpsertCustomerOrders(Cust, new[] { Row("ITESTAC-UPD") }, Actor);
        Exec(f!, "UPDATE dbo.SYS_Config SET ConfigValue = 'true', IsActive = 1 WHERE ConfigKey = @K", ("@K", SoAutoConfirm.ConfigKey));

        var (ins, upd) = pp.UpsertCustomerOrders(Cust, new[] { Row("ITESTAC-UPD") }, Actor);

        Assert.Equal((0, 1), (ins, upd));
        Assert.Equal("Open", StatusOf(f!, "ITESTAC-UPD"));
    }

    [SkippableFact]
    public void Enable_confirms_every_open_order_and_turns_flag_on()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var snap = new Snapshot(f!);
        var pp = new PpRepository(f!);
        pp.DisableSoAutoConfirm(Actor);
        pp.UpsertCustomerOrders(Cust, new[] { Row("ITESTAC-E1"), Row("ITESTAC-E2") }, Actor);
        Assert.Equal(snap.ForeignOpenCount + 2, pp.CountOpenCustomerOrders());

        var confirmed = pp.EnableSoAutoConfirm(Actor);

        Assert.Equal(snap.ForeignOpenCount + 2, confirmed);
        Assert.Equal(0, pp.CountOpenCustomerOrders());
        Assert.Equal("Confirmed", StatusOf(f!, "ITESTAC-E1"));
        Assert.True(pp.GetSoAutoConfirm());
        Assert.Equal(Actor, Scalar(f!, "SELECT ModifiedBy FROM dbo.PP_CustomerOrder WHERE SoNumber = 'ITESTAC-E2' AND CustomerID = @C", ("@C", Cust)));
    }

    [SkippableFact]
    public void Enable_with_no_open_orders_returns_zero()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var snap = new Snapshot(f!);
        var pp = new PpRepository(f!);
        pp.EnableSoAutoConfirm(Actor);

        Assert.Equal(0, pp.EnableSoAutoConfirm(Actor));
        Assert.True(pp.GetSoAutoConfirm());
    }

    [SkippableFact]
    public void Disable_turns_flag_off_and_leaves_orders_alone()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var snap = new Snapshot(f!);
        var pp = new PpRepository(f!);
        pp.EnableSoAutoConfirm(Actor);
        pp.UpsertCustomerOrders(Cust, new[] { Row("ITESTAC-D1") }, Actor);

        pp.DisableSoAutoConfirm(Actor);

        Assert.False(pp.GetSoAutoConfirm());
        Assert.Equal("Confirmed", StatusOf(f!, "ITESTAC-D1"));
        Assert.Equal(0, pp.CountOpenCustomerOrders());
    }

    [SkippableFact]
    public void Inactive_config_row_counts_as_off()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        using var snap = new Snapshot(f!);
        var pp = new PpRepository(f!);
        pp.EnableSoAutoConfirm(Actor);
        Exec(f!, "UPDATE dbo.SYS_Config SET IsActive = 0 WHERE ConfigKey = @K", ("@K", SoAutoConfirm.ConfigKey));

        Assert.False(pp.GetSoAutoConfirm());
        pp.UpsertCustomerOrders(Cust, new[] { Row("ITESTAC-IA") }, Actor);
        Assert.Equal("Open", StatusOf(f!, "ITESTAC-IA"));
    }
}
