using AMES.Data.Services.SapIf;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>
/// SAP 수신 반영 SQL(M02 품목·M03 BOM) — 한 트랜잭션 안에서 실행하고 항상 롤백한다(DB 에 흔적을 남기지 않는다).
/// <c>AMES_TEST_CONN</c> 이 있을 때만 돈다. 정의서 규칙: SAP 값 우선 · 사출품 여부는 SUB 만 · BOM 은 IF 에 없는 라인 비활성.
/// </summary>
public class SapInApplyDbTests
{
    const string Skipped = "set AMES_TEST_CONN to run — applies SAP receive SQL inside a transaction that is always rolled back";
    const string Item = "ITEST-SAPIN-I1";
    const string Ver  = "ITEST-SAPIN-V1";

    static MasterSpec Spec(string ifId) => SapInMasters.All.Single(m => m.IfId == ifId);

    static Dictionary<string, object?> ItemRow(string action, string name, string type = "SUB", bool inj = true, string uom = "EA") => new(StringComparer.OrdinalIgnoreCase)
    {
        ["IF_ACTION"] = action, ["ItemNo"] = Item, ["ItemName"] = name, ["ItemNameEN"] = null, ["ItemType"] = type,
        ["ItemCategory"] = "SUB", ["InjFlag"] = inj, ["DefaultUOM"] = uom, ["RoutingType"] = null,
        ["MinStock"] = null, ["MaxStock"] = null, ["SafetyStock"] = null, ["UnitCost"] = 1.5m, ["PalletQty"] = 8,
        ["ActiveFlag"] = null, ["LeadTimeDays"] = 3, ["ProcureType"] = "INHOUSE",
    };

    static Dictionary<string, object?> Hdr(string action, string status = "APPROVED") => new(StringComparer.OrdinalIgnoreCase)
    {
        ["IF_ACTION"] = action, ["VersionID"] = Ver, ["RootItemNo"] = Item, ["VersionNo"] = "V1.0",
        ["EffFrom"] = new DateTime(2026, 11, 1), ["EffTo"] = null, ["ChangeType"] = null, ["ChangeReason"] = null,
        ["ApprovedTS"] = new DateTime(2026, 11, 1, 9, 0, 0), ["Status"] = status,
    };

    static Dictionary<string, object?> BomLine(string id, string comp, decimal qty) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["BOMID"] = id, ["VersionID"] = Ver, ["ParentItemNo"] = Item, ["CompItemNo"] = comp, ["BOMLevel"] = 1,
        ["QtyPer"] = qty, ["UOM"] = "EA", ["ScrapPct"] = null, ["Position"] = 10, ["ActiveFlag"] = true,
    };

    static T One<T>(SqlConnection c, SqlTransaction tx, string sql, string p, object v)
    {
        using var cmd = new SqlCommand(sql, c, tx);
        cmd.Parameters.AddWithValue(p, v);
        var o = cmd.ExecuteScalar();
        return (T)Convert.ChangeType(o!, typeof(T));
    }

    [SkippableFact]
    public void Item_receive_inserts_updates_in_sap_favour_and_keeps_inj_flag_only_for_sub()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, Skipped);
        var f = TryFactory(); Skip.If(f is null, Skipped);
        using var c = f!.OpenConnection();
        using var tx = c.BeginTransaction();
        try
        {
            var s = Spec("IF-M02");

            // 신규
            var row = SapInRunner.NormalizeRow(s, ItemRow("I", "sap item"));
            Assert.Equal('N', SapInRunner.Compare(c, tx, s, row).Kind);
            SapInRunner.Apply(c, tx, s, row);
            Assert.Equal(1, One<int>(c, tx, "SELECT CAST(SAPFlag AS int) FROM dbo.MD_Item WHERE ItemNo=@i", "@i", Item));
            Assert.Equal("SAP-IF", One<string>(c, tx, "SELECT CreatedBy FROM dbo.MD_Item WHERE ItemNo=@i", "@i", Item));
            Assert.Equal(1, One<int>(c, tx, "SELECT CAST(InjFlag AS int) FROM dbo.MD_Item WHERE ItemNo=@i", "@i", Item));   // SUB → 유지
            Assert.Equal(1, One<int>(c, tx, "SELECT CAST(ActiveFlag AS int) FROM dbo.MD_Item WHERE ItemNo=@i", "@i", Item)); // NULL → 1

            // 같은 값 → 동일
            Assert.Equal('S', SapInRunner.Compare(c, tx, s, SapInRunner.NormalizeRow(s, ItemRow("U", "sap item"))).Kind);

            // 값이 다르면 SAP 값으로 덮어쓰고 유형이 SUB 가 아니면 InjFlag 를 0 으로
            var changed = SapInRunner.NormalizeRow(s, ItemRow("U", "sap item v2", type: "ASSY", inj: true));
            var (kind, note) = SapInRunner.Compare(c, tx, s, changed);
            Assert.Equal('C', kind);
            Assert.Contains("ItemName: sap item → sap item v2", note);
            Assert.Contains("InjFlag: 1 → 0", note);
            SapInRunner.Apply(c, tx, s, changed);
            Assert.Equal("sap item v2", One<string>(c, tx, "SELECT ItemName FROM dbo.MD_Item WHERE ItemNo=@i", "@i", Item));
            Assert.Equal(0, One<int>(c, tx, "SELECT CAST(InjFlag AS int) FROM dbo.MD_Item WHERE ItemNo=@i", "@i", Item));

            // D = 삭제가 아니라 비활성
            SapInRunner.Apply(c, tx, s, ItemRow("D", "ignored"));
            Assert.Equal(0, One<int>(c, tx, "SELECT CAST(ActiveFlag AS int) FROM dbo.MD_Item WHERE ItemNo=@i", "@i", Item));
            Assert.Equal("sap item v2", One<string>(c, tx, "SELECT ItemName FROM dbo.MD_Item WHERE ItemNo=@i", "@i", Item)); // 다른 값은 그대로
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Bom_receive_upserts_header_and_lines_and_deactivates_lines_missing_from_if()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, Skipped);
        var f = TryFactory(); Skip.If(f is null, Skipped);
        using var c = f!.OpenConnection();
        using var tx = c.BeginTransaction();
        try
        {
            var l1 = BomLine("ITEST-SAPIN-L1", "ITEST-SAPIN-C1", 1m);
            var l2 = BomLine("ITEST-SAPIN-L2", "ITEST-SAPIN-C2", 2.5m);

            var (a, kind, note) = SapInBom.Apply(c, tx, Hdr("I"), [l1, l2]);
            Assert.Equal('N', kind);
            Assert.Contains("라인 2건: 신규 2·변경 0·동일 0·IF 에 없어 비활성 0", note);
            Assert.True(a > 0);
            Assert.Equal(1, One<int>(c, tx, "SELECT CAST(SAPFlag AS int) FROM dbo.MD_BomVersion WHERE VersionID=@v", "@v", Ver));
            Assert.Equal(2, One<int>(c, tx, "SELECT COUNT(*) FROM dbo.MD_Bom WHERE VersionID=@v AND ActiveFlag=1", "@v", Ver));

            // 라인 하나만 오고 수량이 바뀜 → 변경 1 · IF 에 없는 L2 비활성
            var l1b = BomLine("ITEST-SAPIN-L1", "ITEST-SAPIN-C1", 3m);
            (_, kind, note) = SapInBom.Apply(c, tx, Hdr("U"), [l1b]);
            Assert.Equal('C', kind);
            Assert.Contains("신규 0·변경 1·동일 0·IF 에 없어 비활성 1", note);
            Assert.Contains("QtyPer: 1.0000 → 3", note);
            Assert.Equal(1, One<int>(c, tx, "SELECT COUNT(*) FROM dbo.MD_Bom WHERE VersionID=@v AND ActiveFlag=1", "@v", Ver));
            Assert.Equal(3m, One<decimal>(c, tx, "SELECT QtyPer FROM dbo.MD_Bom WHERE BOMID='ITEST-SAPIN-L1'", "@x", 0));

            // 같은 내용 재수신 → 동일(멱등)
            (_, kind, note) = SapInBom.Apply(c, tx, Hdr("U"), [l1b]);
            Assert.Equal('S', kind);

            // 헤더 D → 버전 만료 + 라인 전부 비활성(삭제 아님)
            SapInBom.Apply(c, tx, Hdr("D"), []);
            Assert.Equal("EXPIRED", One<string>(c, tx, "SELECT Status FROM dbo.MD_BomVersion WHERE VersionID=@v", "@v", Ver));
            Assert.Equal(0, One<int>(c, tx, "SELECT COUNT(*) FROM dbo.MD_Bom WHERE VersionID=@v AND ActiveFlag=1", "@v", Ver));
            Assert.Equal(2, One<int>(c, tx, "SELECT COUNT(*) FROM dbo.MD_Bom WHERE VersionID=@v", "@v", Ver));
        }
        finally { tx.Rollback(); }
    }
}
