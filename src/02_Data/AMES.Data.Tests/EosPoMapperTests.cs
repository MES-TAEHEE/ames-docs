using System.Text.Json;
using AMES.Data.Services.PoSync;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>Savannah EOS 조회 API(소스 TYPE=EOS) — 요청 본문, 응답 해석, 협력사 거르기, 설정 해석.</summary>
public class EosPoMapperTests
{
    // 11-09 실제 응답에서 뽑은 모양(협력사가 섞여 온다)
    const string Sample = """
        {"value":[
          {"PO_NO":"4100172428","PO_SEQ":10,"VENDOR_CODE":"701850","PART_NO":"D0131-P8010RBQ","PO_QTY":2000,"DELIVERY_DATE":"2026-11-02","PO_DATE":"2026-09-01"},
          {"PO_NO":"4100181100","PO_SEQ":20,"VENDOR_CODE":"310471","PART_NO":"82311-DW010CBK","PO_QTY":96,"DELIVERY_DATE":"2026-11-05","PO_DATE":"2026-10-20"},
          {"PO_NO":"4100181100","PO_SEQ":30,"VENDOR_CODE":"310471 ","PART_NO":"82321-DW010CBK","PO_QTY":"48","DELIVERY_DATE":"2026-11-06","PO_DATE":"2026-10-20"}
        ],"count":3}
        """;

    [Fact]
    public void Body_carries_the_query_name_and_delivery_window_in_the_remote_format()
    {
        using var doc = JsonDocument.Parse(EosPoMapper.BuildBody("SIS.EOS_GET_PO", new(2026, 11, 2), new(2026, 12, 31)));
        var root = doc.RootElement;
        Assert.Equal("SIS.EOS_GET_PO", root.GetProperty("SQL").GetString());
        Assert.False(root.GetProperty("IsPureSQL").GetBoolean());
        Assert.False(root.GetProperty("IsEncryptedSQL").GetBoolean());
        var p = root.GetProperty("Parameters");
        Assert.Equal("2026-11-02", p.GetProperty("IN_DATE_FROM").GetProperty("Value").GetString());
        Assert.Equal(16, p.GetProperty("IN_DATE_FROM").GetProperty("DbType").GetInt32());
        Assert.Equal(1, p.GetProperty("IN_DATE_FROM").GetProperty("Direction").GetInt32());
        Assert.Equal("2026-12-31", p.GetProperty("IN_DATE_TO").GetProperty("Value").GetString());
        var cur = p.GetProperty("OUT_CURSOR");
        Assert.Equal(JsonValueKind.Null, cur.GetProperty("Value").ValueKind);
        Assert.Equal(121, cur.GetProperty("DbType").GetInt32());
        Assert.Equal(2, cur.GetProperty("Direction").GetInt32());
        Assert.Equal(512, cur.GetProperty("Size").GetInt32());
    }

    [Fact]
    public void Only_our_vendor_rows_are_kept_and_map_to_customer_orders()
    {
        var rows = EosPoMapper.ToSrmRows(EosPoMapper.ParseRows(Sample), "310471");
        Assert.Equal(2, rows.Count);
        Assert.Equal("4100181100-20", rows[0].PONO);

        var mapped = SrmPoMapper.Map(rows);
        Assert.Equal(0, mapped.Skipped);
        var a = mapped.Rows[0];
        Assert.Equal("4100181100", a.SoNumber);
        Assert.Equal(20, a.SoLineNo);
        Assert.Equal("82311-DW010CBK", a.ItemNo);
        Assert.Equal(96m, a.OrderQty);
        Assert.Null(a.ShippedQty);   // EOS 는 납품수량이 없다 — 기존 값을 지우지 않는다
        Assert.Equal(new DateTime(2026, 11, 5), a.RequestedDeliveryDate);
        Assert.Equal(new DateTime(2026, 10, 20), a.OrderDate);
        Assert.Equal(48m, mapped.Rows[1].OrderQty);   // 문자열 수량도 읽는다
    }

    [Fact]
    public void Georgia_cursor_root_and_decimal_line_numbers_are_read()
    {
        // 11-16 Georgia 실제 응답 모양 — 루트가 OUT_CURSOR, 항번·수량이 소수
        const string geo = """
            {"OUT_CURSOR":[
              {"PO_NO":"4100182945","PO_SEQ":10.0,"VENDOR_CODE":"309224","PART_NO":"83335-P8000DNN","PO_QTY":66.0,"DELIVERY_DATE":"2026-11-09","PO_DATE":"2026-11-02"},
              {"PO_NO":"4100165977","PO_SEQ":40.0,"VENDOR_CODE":"309215","PART_NO":"83915-P8000RBQ","PO_QTY":2400.0,"DELIVERY_DATE":"2026-11-09","PO_DATE":"2026-07-21"}
            ]}
            """;
        var rows = EosPoMapper.ToSrmRows(EosPoMapper.ParseRows(geo), "309224");
        Assert.Single(rows);
        Assert.Equal("4100182945-10", rows[0].PONO);
        var a = SrmPoMapper.Map(rows).Rows.Single();
        Assert.Equal(10, a.SoLineNo);
        Assert.Equal(66m, a.OrderQty);
    }

    [Fact]
    public void Window_is_split_into_week_chunks_covering_every_day_once()
    {
        var c = EosPoMapper.Chunks(new(2026, 11, 2), new(2027, 1, 8), 7).ToList();
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8)), c[0]);
        Assert.Equal(new DateOnly(2027, 1, 8), c[^1].To);
        Assert.All(c.Zip(c.Skip(1)), p => Assert.Equal(p.First.To.AddDays(1), p.Second.From));
        Assert.All(c, x => Assert.InRange(x.To.DayNumber - x.From.DayNumber + 1, 1, 7));
        Assert.Single(EosPoMapper.Chunks(new(2026, 11, 9), new(2026, 11, 9), 7));
    }

    [Fact]
    public void Several_vendor_codes_separated_by_commas_are_all_kept()
    {
        var rows = EosPoMapper.ParseRows("""
            {"OUT_CURSOR":[{"PO_NO":"4100182945","PO_SEQ":10.0,"VENDOR_CODE":"309224","PART_NO":"A","PO_QTY":1.0,"DELIVERY_DATE":"2026-11-09","PO_DATE":"2026-11-02"},
                           {"PO_NO":"4100187089","PO_SEQ":20.0,"VENDOR_CODE":"312598","PART_NO":"B","PO_QTY":2.0,"DELIVERY_DATE":"2026-12-21","PO_DATE":"2026-11-25"},
                           {"PO_NO":"8700005212","PO_SEQ":10.0,"VENDOR_CODE":"100000","PART_NO":"C","PO_QTY":3.0,"DELIVERY_DATE":"2026-12-21","PO_DATE":"2026-11-25"}]}
            """);
        var kept = EosPoMapper.ToSrmRows(rows, " 309224 , 312598 ,").Select(r => r.PONO).ToList();
        Assert.Equal(["4100182945-10", "4100187089-20"], kept);
        Assert.Equal(["4100187089-20"], EosPoMapper.ToSrmRows(rows, "312598").Select(r => r.PONO));
    }

    [Fact]
    public void Upload_vendor_check_accepts_any_of_the_configured_codes()
    {
        var configured = AMES.Data.Services.Srm.SrmVendorCheck.ConfiguredVendors(["TYPE=EOS;VENDCD=309224,312598;WINDOW=-7,60", "VENDCD=312598"]);
        Assert.Equal(["309224", "312598"], configured);
        Assert.Equal(AMES.Data.Services.Srm.SrmVendorCheck.Verdict.Match, AMES.Data.Services.Srm.SrmVendorCheck.Judge("312598", configured));
    }

    [Fact]
    public void Srm_source_with_several_vendor_codes_is_a_config_error()
    {
        var r = PoSyncConfig.Resolve([
            Row(PoSyncConfig.GroupSource, "SEMS", "CUS-SAV", "CORCD=7700;BIZCD=7710;VENDCD=310471,312598;PURC_ORG=1A7700"),
            Row(PoSyncConfig.GroupUrl,    "SEMS", desc: "http://srm/x"),
        ]);
        Assert.Empty(r.Sources);
        Assert.Contains("VENDCD", Assert.Single(r.Errors).Message);
    }

    [Theory]
    [InlineData("HTTP 500 : {\"Message\":\"ORA-20001: Invalid date range. Maximum 31 days.\"}", EosPoMapper.RemoteError.RangeTooLong)]
    [InlineData("HTTP 500 : {\"Message\":\"ORA-20002: More than 1000 rows. Use a shorter date range.\"}", EosPoMapper.RemoteError.TooManyRows)]
    [InlineData("HTTP 502 Bad Gateway", EosPoMapper.RemoteError.None)]
    [InlineData(null, EosPoMapper.RemoteError.None)]
    public void Remote_limit_errors_are_recognised(string? message, EosPoMapper.RemoteError expected)
        => Assert.Equal(expected, EosPoMapper.ClassifyError(message));

    [Fact]
    public void Max_ahead_comes_from_the_source_then_global_then_default()
    {
        var r = PoSyncConfig.Resolve([
            Row(PoSyncConfig.GroupGlobal, "MAX_AHEAD", attr1: "120"),
            Row(PoSyncConfig.GroupSource, "SAV", "CUS-SAV", "TYPE=EOS;VENDCD=310471;MAX_AHEAD=90"),
            Row(PoSyncConfig.GroupUrl,    "SAV", desc: "https://x/eos"),
            Row(PoSyncConfig.GroupSource, "GEO", "CUS-GEO", "TYPE=EOS;VENDCD=309224"),
            Row(PoSyncConfig.GroupUrl,    "GEO", desc: "http://y/api"),
        ]);
        Assert.Equal(90,  r.Sources.Single(s => s.Key == "SAV").MaxAheadDays);
        Assert.Equal(120, r.Sources.Single(s => s.Key == "GEO").MaxAheadDays);
        Assert.Equal(PoSyncConfig.DefaultMaxAheadDays,
            PoSyncConfig.Resolve([Row(PoSyncConfig.GroupSource, "GEO", "CUS-GEO", "TYPE=EOS;VENDCD=309224"),
                                  Row(PoSyncConfig.GroupUrl, "GEO", desc: "http://y/api")]).Sources.Single().MaxAheadDays);
    }

    static PoSyncConfig.CodeRow Row(string group, string value, string? attr1 = null, string? desc = null)
        => new(group, value, null, attr1, desc, true);

    [Fact]
    public void Eos_source_needs_only_vendor_code_and_defaults_the_query_name()
    {
        var r = PoSyncConfig.Resolve([
            Row(PoSyncConfig.GroupSource, "SAV", "CUS-SAV", "TYPE=EOS;VENDCD=310471;WINDOW=-7,60"),
            Row(PoSyncConfig.GroupUrl, "SAV", desc: "https://exapi.example/eos/api/data/Query"),
        ]);
        Assert.Empty(r.Errors);
        var s = Assert.Single(r.Sources);
        Assert.True(s.IsEos);
        Assert.Equal("310471", s.VendCd);
        Assert.Equal(PoSyncConfig.DefaultEosSql, s.Sql);
        Assert.Equal((-7, 60), (s.WindowFrom, s.WindowTo));
        Assert.Null(s.AuthScheme);
    }

    [Fact]
    public void Srm_source_still_needs_all_four_parameters_and_unknown_type_is_an_error()
    {
        var r = PoSyncConfig.Resolve([
            Row(PoSyncConfig.GroupSource, "SRM1", "CUS-SAV", "VENDCD=310471"),
            Row(PoSyncConfig.GroupUrl, "SRM1", desc: "http://srm/x"),
            Row(PoSyncConfig.GroupSource, "BAD", "CUS-SAV", "TYPE=FTP;VENDCD=1"),
            Row(PoSyncConfig.GroupUrl, "BAD", desc: "http://x"),
        ]);
        Assert.Empty(r.Sources);
        Assert.Contains("CORCD", r.Errors.Single(e => e.Key == "SRM1").Message);
        Assert.Contains("TYPE", r.Errors.Single(e => e.Key == "BAD").Message);
    }
}
