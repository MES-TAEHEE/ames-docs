using AMES.Data.Services.SapIf;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>SAP B1 인터페이스(EOS_IF) 공통 규칙·수신 대상 정의 — 순수 함수, DB 불필요.</summary>
public class SapIfRulesTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]    // 32 → 상한 30
    [InlineData(50, 30)]
    [InlineData(-3, 1)]
    public void Backoff_doubles_up_to_30_minutes(int retry, int expected)
        => Assert.Equal(expected, SapIfRules.BackoffMinutes(retry));

    [Fact]
    public void Truncate_cuts_only_when_longer_than_max()
    {
        Assert.Equal("abc", SapIfRules.Truncate("abcdef", 3));
        Assert.Equal("abc", SapIfRules.Truncate("abc", 3));
        Assert.Null(SapIfRules.Truncate(null, 3));
    }

    [Fact]
    public void ErrorMessage_is_single_line_and_within_if_msg_length()
    {
        var ex = new InvalidOperationException(new string('x', 3000) + "\nsecond line");
        var m = SapIfRules.ErrorMessage(ex);
        Assert.True(m.Length <= SapIfRules.MsgMax);
        Assert.DoesNotContain('\n', m);
        Assert.StartsWith("InvalidOperationException: ", m);
    }

    [Theory]
    [InlineData("D", true)]
    [InlineData("U", false)]
    [InlineData("I", false)]
    [InlineData("d", false)]   // 대소문자 구분 — EOS_IF 는 대문자 한 글자
    [InlineData(null, false)]
    public void Only_D_deactivates(string? action, bool expected)
        => Assert.Equal(expected, SapIfRules.IsDeactivate(action));

    // ── 비교 정책: SAP 우선 덮어쓰기 / SAP 단독 신규 / AMES 단독 유지 ─────────────────────

    private static Dictionary<string, object?> Row(params (string, object?)[] kv)
        => kv.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Diff_lists_only_changed_columns_with_old_and_new()
    {
        var existing = Row(("UOMName", "Kilo"), ("UOMCategory", "WEIGHT"), ("ConvFactor", 1000m), ("BaseFlag", false), ("ActiveFlag", true));
        var incoming = Row(("UOMName", "Kilogram"), ("UOMCategory", "WEIGHT"), ("ConvFactor", 1000.00000000m), ("BaseFlag", false), ("ActiveFlag", true));
        var d = SapIfRules.Diff(existing, incoming, ["UOMName", "UOMCategory", "ConvFactor", "BaseFlag", "ActiveFlag"], ["ActiveFlag"]);
        var only = Assert.Single(d);
        Assert.Equal(("UOMName", (object?)"Kilo", (object?)"Kilogram"), only);
    }

    [Fact]
    public void Diff_treats_numeric_types_and_scales_as_equal()
    {
        var d = SapIfRules.Diff(Row(("ConvFactor", 1m)), Row(("ConvFactor", 1.00000000m)), ["ConvFactor"], []);
        Assert.Empty(d);
        d = SapIfRules.Diff(Row(("PalletQty", 8)), Row(("PalletQty", 8L)), ["PalletQty"], []);
        Assert.Empty(d);
    }

    [Fact]
    public void Diff_reports_sap_null_as_a_change_because_sap_wins()
    {
        var d = SapIfRules.Diff(Row(("BaseUOM", "G")), Row(("BaseUOM", null)), ["BaseUOM"], []);
        var c = Assert.Single(d);
        Assert.Equal("BaseUOM", c.Col);
        Assert.Null(c.New);
    }

    [Fact]
    public void Diff_applies_the_active_flag_default_like_the_merge_does()
    {
        // 적용 시 ActiveFlag NULL → 1 이므로 기존이 1 이면 변경이 아니다
        Assert.Empty(SapIfRules.Diff(Row(("ActiveFlag", true)), Row(("ActiveFlag", null)), ["ActiveFlag"], ["ActiveFlag"]));
        Assert.Single(SapIfRules.Diff(Row(("ActiveFlag", false)), Row(("ActiveFlag", null)), ["ActiveFlag"], ["ActiveFlag"]));
    }

    [Fact]
    public void FormatDiff_is_readable_and_bounded()
    {
        var one = SapIfRules.FormatDiff([("UOMName", "Kilo", "Kilogram"), ("BaseUOM", null, "G"), ("ActiveFlag", false, true)]);
        Assert.Equal("UOMName: Kilo → Kilogram; BaseUOM: (null) → G; ActiveFlag: 0 → 1", one);
        var many = SapIfRules.FormatDiff(Enumerable.Range(0, 500).Select(i => ($"Col{i}", (object?)"a", (object?)"b")));
        Assert.True(many.Length <= 900);
    }

    [Fact]
    public void AmesOnlyKeys_are_those_missing_from_sap_ignoring_case_and_sorted()
    {
        var only = SapIfRules.AmesOnlyKeys(
            ["PCS", "EA", "ea2", "kg", "BOX", "m"], ["EA", "KG", "G"]);
        Assert.Equal(["BOX", "ea2", "m", "PCS"], only);
    }

    [Theory]
    [InlineData("O", "O", 100, 100, "Open")]
    [InlineData("O", "O", 100, 40, "Partial")]
    [InlineData("O", "O", 100, 0, "Received")]
    [InlineData("O", "C", 100, 30, "Complete")]
    [InlineData("C", "C", 100, 30, "Complete")]
    [InlineData("C", "C", 100, 0, "Received")]
    [InlineData("X", "O", 100, 100, "Cancelled")]
    public void Po_status_maps_b1_state_to_existing_vocabulary(string doc, string line, int order, int open, string expected)
        => Assert.Equal(expected, SapIfRules.PoStatus(doc, line, order, open));

    [Fact]
    public void Master_targets_follow_the_spec_order_and_have_unique_ids()
    {
        var ids = SapInMasters.All.Select(m => m.IfId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        // 정의서: UoM → (Item → BOM) → 고객 → 공급사 → 로케이션
        Assert.Equal(["IF-M01", "IF-M02", "IF-M03", "IF-M04", "IF-M05", "IF-M06", "IF-T04", "IF-T08"], ids);
    }

    [Theory]
    [InlineData("SUB", true)]
    [InlineData("sub", true)]
    [InlineData("ASSY", false)]
    [InlineData("MATERIAL", false)]
    [InlineData(null, false)]
    public void Item_inj_flag_survives_only_for_sub_type(string? type, bool keeps)
    {
        var item = SapInMasters.All.Single(m => m.IfId == "IF-M02");
        var row = new Dictionary<string, object?> { ["ItemNo"] = "X", ["ItemType"] = type, ["InjFlag"] = true };
        var norm = SapInRunner.NormalizeRow(item, row);
        Assert.Equal(keeps, (bool)norm["InjFlag"]!);
        Assert.True((bool)row["InjFlag"]!);                       // 원본 행은 바꾸지 않는다
    }

    [Fact]
    public void Normalize_leaves_other_masters_untouched()
    {
        var uom = SapInMasters.All.Single(m => m.IfId == "IF-M01");
        var row = new Dictionary<string, object?> { ["InjFlag"] = true };
        Assert.Same(row, SapInRunner.NormalizeRow(uom, row));
    }

    [Fact]
    public void Bom_header_and_line_specs_are_consistent_with_the_ames_tables()
    {
        var bom = SapInMasters.All.Single(m => m.IfId == "IF-M03");
        Assert.Equal(MasterKind.Bom, bom.Kind);
        Assert.Equal("MD_BomVersion", bom.AmesTable);
        Assert.Equal("VersionID", bom.Key);
        Assert.Equal("IF_MD_BOM_HDR", bom.EosTable);
        // 라인 테이블 MD_Bom 에는 SAPFlag 컬럼이 없다 — 헤더(MD_BomVersion)에만 있다
        Assert.False(SapInBom.Line.SapFlag);
        Assert.True(SapInBom.Version.SapFlag);
        Assert.Equal("BOMID", SapInBom.Line.Key);
        Assert.Contains("VersionID", SapInBom.Line.Cols);        // 라인의 소속 버전도 SAP 값 그대로
        Assert.Equal("IF_MD_BOM_LINE", SapInBom.LineEosTable);
    }

    [Fact]
    public void Master_specs_never_list_control_or_audit_columns()
    {
        var banned = new[] { "IF_SEQ", "IF_ACTION", "IF_STATUS", "IF_MSG", "SRC_TS", "CREATED_TS", "PROC_TS",
                             "SAPFlag", "CreatedBy", "CreatedTS", "ModifiedBy", "ModifiedTS" };
        // 컬럼 매핑 방식(Simple·Bom)만 해당 — PO·MRP 는 전용 처리라 Cols 를 쓰지 않는다
        foreach (var m in SapInMasters.All.Where(x => x.Kind is MasterKind.Simple or MasterKind.Bom))
        {
            Assert.DoesNotContain(m.Key, banned);
            Assert.All(m.Cols, c => Assert.DoesNotContain(c, banned));
            Assert.DoesNotContain(m.Key, m.Cols);                 // 키는 MERGE 조건 — SET 에 넣지 않는다
            Assert.Contains(m.DeactivateCol, m.Cols);            // 비활성 컬럼은 반드시 반영 대상 컬럼
        }
    }

    [Fact]
    public void Po_and_mrp_use_header_and_line_tables()
    {
        Assert.Equal("IF_TX_PO_LINE", SapInRunner.ChildTable(MasterKind.Po));
        Assert.Equal("IF_TX_MRP_PLAN_LINE", SapInRunner.ChildTable(MasterKind.Mrp));
        Assert.Equal("IF_MD_BOM_LINE", SapInRunner.ChildTable(MasterKind.Bom));
        Assert.Null(SapInRunner.ChildTable(MasterKind.Simple));
        var po = SapInMasters.All.Single(m => m.IfId == "IF-T04");
        Assert.Equal("IF_TX_PO_HDR", po.EosTable);
        Assert.Equal("WH_PurchaseOrder", po.AmesTable);
        Assert.Equal("IF_TX_MRP_PLAN_HDR", SapInMasters.All.Single(m => m.IfId == "IF-T08").EosTable);
    }

    [Fact]
    public void Mrp_log_key_is_plan_date_and_bucket()
    {
        var mrp = SapInMasters.All.Single(m => m.IfId == "IF-T08");
        var row = new Dictionary<string, object?> { ["PLAN_DATE"] = new DateTime(2026, 11, 13), ["BUCKET"] = "W" };
        Assert.Equal("2026-11-13/W", SapInRunner.LogKey(mrp, row));
        var uom = SapInMasters.All.Single(m => m.IfId == "IF-M01");
        Assert.Equal("EA", SapInRunner.LogKey(uom, new Dictionary<string, object?> { ["UOMCode"] = "EA" }));
    }

    [Fact]
    public void Po_incoming_values_follow_b1_with_received_qty_from_open_qty()
    {
        var h = new Dictionary<string, object?> { ["B1_DOCNUM"] = 4500001, ["VendorID"] = "V1007", ["DocStatus"] = "O",
            ["CurrencyCode"] = "USD", ["DocDate"] = new DateTime(2026, 11, 1), ["DueDate"] = new DateTime(2026, 11, 20) };
        var l = new Dictionary<string, object?> { ["LINENUM"] = 2, ["ItemNo"] = "RM-1", ["OrderQty"] = 100m, ["OpenQty"] = 40m,
            ["LineStatus"] = "O", ["UOM"] = "KG", ["Price"] = 1.25m, ["ShipDate"] = null };
        var v = SapInPo.Incoming(h, l);
        Assert.Equal("4500001", v["PoNumber"]);
        Assert.Equal(60m, v["ReceivedQty"]);                         // 100 − 40
        Assert.Equal("Partial", v["Status"]);
        Assert.Equal(new DateTime(2026, 11, 20), v["DueDate"]);      // 라인 납기 없으면 헤더 납기
        l["ShipDate"] = new DateTime(2026, 11, 18);
        Assert.Equal(new DateTime(2026, 11, 18), SapInPo.Incoming(h, l)["DueDate"]);   // 라인 납기 우선
        l["OpenQty"] = 150m;                                         // 잔량이 발주량보다 크면 입고 누계는 0 으로 막는다
        Assert.Equal(0m, SapInPo.Incoming(h, l)["ReceivedQty"]);
    }

    [Fact]
    public void Customer_deactivates_through_status_and_others_through_active_flag()
    {
        var by = SapInMasters.All.ToDictionary(m => m.IfId);
        Assert.Equal("Status", by["IF-M04"].DeactivateCol);
        Assert.Equal("CLOSED", by["IF-M04"].DeactivateValue);
        foreach (var id in new[] { "IF-M01", "IF-M05", "IF-M06" })
        {
            Assert.Equal("ActiveFlag", by[id].DeactivateCol);
            Assert.Equal(false, by[id].DeactivateValue);
        }
    }
}
