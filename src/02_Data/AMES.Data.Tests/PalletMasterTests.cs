using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-17 — MD-034 팔레트 마스터(MD_Pallet)와 FG 출고의 팔레트 기록, MD-035 사출 조건 항목 저장 경로
public class PalletMasterTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test pallets and condition items";
    const string Actor  = "itest-plt";

    [Theory]
    [InlineData("EXTERNAL", "CUS-SAV", "CUS-SAV")]
    [InlineData("EXTERNAL", "  ",      null)]
    [InlineData("EXTERNAL", null,      null)]
    [InlineData("INTERNAL", "CUS-SAV", null)]
    public void Only_external_pallets_keep_a_dedicated_customer(string use, string? customer, string? expected)
        => Assert.Equal(expected, MasterDataRepository.NormalizePalletCustomer(use, customer));

    [SkippableFact]
    public void Pallet_roundtrips_numbers_in_sequence_and_deletes_when_unused()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        var customer = (string)Scalar(f!, "SELECT TOP 1 CustomerID FROM dbo.MD_Customer ORDER BY CustomerID")!;
        var created = new List<string>();
        try
        {
            // 번호는 항상 이번 달 EOS-PLT-yyMM- 다음 순번부터 이어서 발행한다. 내부용으로 고객을 넘겨도 비워 저장한다
            var first = Assert.Single(md.InsertPallets(1, MasterDataRepository.PalletInternal, customer, "itest", true, Actor));
            created.Add(first);
            Assert.Matches(@"^EOS-PLT-\d{4}-\d{4}$", first);
            Assert.StartsWith(MasterDataRepository.PalletNoPrefix(Services.DbClock.Today), first);
            var row = Assert.Single(md.ListPallets(), p => p.PalletNo == first);
            Assert.Null(row.CustomerID);
            Assert.False(row.ShippedFlag);

            // 출고됨은 출고 처리가 켠다 — 마스터 수정은 그 값을 건드리지 않는다
            Exec(f!, "UPDATE dbo.MD_Pallet SET ShippedFlag = 1 WHERE PalletNo = @N", ("@N", first));
            md.UpdatePallet(first, MasterDataRepository.PalletExternal, customer, null, false, Actor);
            row = Assert.Single(md.ListPallets(), p => p.PalletNo == first);
            Assert.Equal(customer, row.CustomerID);
            Assert.True(row.ShippedFlag);
            Assert.False(row.ActiveFlag);
            Assert.Contains(md.CustomerUsages(customer), u => u.Kind == "PALLET");

            var more = md.InsertPallets(2, MasterDataRepository.PalletExternal, null, null, true, Actor);
            created.AddRange(more);
            Assert.Equal(2, more.Count);
            Assert.Equal(int.Parse(first[^4..]) + 1, int.Parse(more[0][^4..]));
            Assert.Equal(int.Parse(first[^4..]) + 2, int.Parse(more[1][^4..]));

            Assert.Empty(md.PalletUsages(first));
            Assert.Empty(md.DeletePallet(first));
            Assert.DoesNotContain(md.ListPallets(), p => p.PalletNo == first);
        }
        finally
        {
            foreach (var n in created) Exec(f!, "DELETE dbo.MD_Pallet WHERE PalletNo = @N", ("@N", n));
        }
    }

    [SkippableFact]
    public void Fg_outbound_records_the_pallet_and_sets_the_shipped_flag()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);

        using var conn = f!.OpenConnection();
        using var tx   = conn.BeginTransaction();   // 끝에서 되돌린다 — 출고 배치의 COMMIT 은 바깥 트랜잭션 안의 중첩이라 남지 않는다
        void Run(string sql)
        {
            using var c = new SqlCommand(sql, conn, tx);
            c.ExecuteNonQuery();
        }
        object? Value(string sql)
        {
            using var c = new SqlCommand(sql, conn, tx);
            return c.ExecuteScalar();
        }

        Run("""
            DECLARE @Loc varchar(20) = (SELECT TOP 1 LocationID FROM dbo.MD_Location WHERE LocationType = 'FG' ORDER BY LocationID);
            IF @Loc IS NULL
            BEGIN
                SET @Loc = 'ITEST-PLT-FG';
                INSERT dbo.MD_Location (LocationID, LocationType, CreatedBy, CreatedTS) VALUES (@Loc, 'FG', 'itest-plt', SYSDATETIME());
            END
            DECLARE @Part varchar(20) = (SELECT TOP 1 ItemNo FROM dbo.MD_Item ORDER BY ItemNo);
            INSERT dbo.MD_Pallet (PalletNo, PalletUse, CustomerID, ShippedFlag, ActiveFlag, CreatedBy, CreatedTS)
            VALUES ('ITEST-PLT-OUT', 'EXTERNAL', NULL, 0, 1, 'itest-plt', SYSDATETIME());
            INSERT dbo.WH_Inventory (LotNo, UnitType, ParentLotNo, PartNo, PalletNo, LocationNo, Qty, ReceivedAt, CreatedAt, UpdatedAt)
            VALUES (N'ITEST-PLTLOT', 'PALLET', NULL, NULL, 'ITEST-PLT-OUT', @Loc, 0, SYSDATETIME(), SYSDATETIME(), SYSDATETIME()),
                   (N'ITEST-PLTLOT-P1', 'PART', N'ITEST-PLTLOT', @Part, NULL, @Loc, 5, SYSDATETIME(), SYSDATETIME(), SYSDATETIME());
            """);
        var customer = (string)Value("SELECT TOP 1 CustomerID FROM dbo.MD_Customer WHERE UPPER(ISNULL(Status,'ACTIVE')) NOT IN ('INACTIVE','DISABLED') ORDER BY CustomerID")!;

        using (var cmd = new SqlCommand(PdaFinishedGoodsSql.OutboundComplete, conn, tx))
        {
            cmd.Parameters.Add("@Barcode",    System.Data.SqlDbType.NVarChar, 50).Value = "ITEST-PLTLOT";
            cmd.Parameters.Add("@OperatorID", System.Data.SqlDbType.VarChar,  20).Value = Actor;
            cmd.Parameters.Add("@CustomerID", System.Data.SqlDbType.VarChar,  20).Value = customer;
            cmd.Parameters.Add("@ShippingDeliveryID",System.Data.SqlDbType.Int).Value=DBNull.Value;
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
        }

        Assert.Equal("ITEST-PLT-OUT", Value("SELECT PalletNo FROM dbo.FG_Shipment WHERE LotNo = N'ITEST-PLTLOT'"));
        Assert.True((bool)Value("SELECT ShippedFlag FROM dbo.MD_Pallet WHERE PalletNo = 'ITEST-PLT-OUT'")!);
        Assert.Equal(customer, Value("SELECT ShippedCustomerID FROM dbo.MD_Pallet WHERE PalletNo = 'ITEST-PLT-OUT'"));
        Assert.Equal(1, (int)Value("""
            SELECT COUNT(*) FROM dbo.MD_Pallet p JOIN dbo.FG_Shipment s ON s.PalletNo = p.PalletNo AND s.ShippedAt = p.ShippedAt
            WHERE p.PalletNo = 'ITEST-PLT-OUT'
            """)!);
        tx.Rollback();
    }

    [SkippableFact]
    public void Injection_condition_item_roundtrips_and_keeps_line_and_code()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        var line = (string)Scalar(f!, "SELECT TOP 1 LineID FROM dbo.MD_Line ORDER BY LineID")!;
        const string code = "ITEST-COND";
        try
        {
            Exec(f!, "DELETE dbo.MD_InjCondItem WHERE ItemCode = @C", ("@C", code));
            md.InsertInjCondItem(line, code, "itest", 100, 104, "FLOAT", true, Actor);
            Assert.True(md.InjCondItemExists(line, code));
            var row = Assert.Single(md.ListInjCondItems(), r => r.LineID == line && r.ItemCode == code);
            Assert.Equal((100, 104, "FLOAT", true), (row.SetAddress, row.ActualAddress, row.DataType, row.Enabled));

            md.UpdateInjCondItem(row.CondItemID, null, null, 204, "LONG", false, Actor);
            row = Assert.Single(md.ListInjCondItems(), r => r.CondItemID == row.CondItemID);
            Assert.Equal((line, code, (string?)null, (int?)null, 204, "LONG", false),
                (row.LineID, row.ItemCode, row.ItemName, row.SetAddress, row.ActualAddress, row.DataType, row.Enabled));

            md.DeleteInjCondItem(row.CondItemID);
            Assert.False(md.InjCondItemExists(line, code));
        }
        finally { Exec(f!, "DELETE dbo.MD_InjCondItem WHERE ItemCode = @C", ("@C", code)); }
    }
}
