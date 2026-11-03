using AMES.Data.Services.DemandPlan;
using AMES.Data.Services.Srm;
using Xunit;

namespace AMES.Data.Tests.Srm;

/// <summary>SRM 양식 읽기 규칙(병합 펴기·열 머리글 기준·연도 추론·업체 대조·고객 제한). 순수 함수 — DB 불필요.</summary>
public class SrmParsersTests
{
    static readonly DateOnly Today = new(2026, 11, 3);

    [Fact]
    public void Html_grid_expands_colspan_and_rowspan_into_every_covered_cell()
    {
        const string html = "<table>"
            + "<tr><td rowspan=\"2\">PART NO</td><td colspan=\"2\">11-03</td></tr>"
            + "<tr><td>발주량</td><td>납품량</td></tr>"
            + "<tr><td>A-1</td><td>40</td><td>0</td></tr>"
            + "</table>";

        var g = SrmHtmlGrid.Read(html);

        Assert.Equal(new[] { "PART NO", "11-03", "11-03" }, g[0]);
        Assert.Equal(new[] { "PART NO", "발주량", "납품량" }, g[1]);
        Assert.Equal(new[] { "A-1", "40", "0" }, g[2]);
    }

    [Fact]
    public void Po_list_reads_columns_by_name_even_when_the_order_changes()
    {
        var rows = new[]
        {
            new[] { "발주번호", "PART NO", "납기일자", "발주량", "업체", "품명", "발주단위", "납품수량" },
            new[] { "4100181400-10", "82314-PI000NNB", "2026-11-02", "600", "310471", "RAIL", "EA", "600" },
            new[] { "", "", "", "", "", "보조 표", "", "" },
        };

        var po = Assert.Single(PoListGrid.Parse(rows));

        Assert.Equal("4100181400", po.SoNumber);
        Assert.Equal(10, po.SoLineNo);
        Assert.Equal("82314-PI000NNB", po.ItemNo);
        Assert.Equal(600m, po.OrderQty);
        Assert.Equal(600m, po.ShippedQty);
        Assert.Equal(new DateTime(2026, 11, 2), po.RequestedDeliveryDate);
        Assert.Equal("310471", po.VendorCode);
    }

    [Fact]
    public void Po_list_names_every_missing_required_column_instead_of_guessing_positions()
    {
        var rows = new[] { new[] { "발주번호", "PART NO", "품명" }, new[] { "4100181400-10", "82314-PI000NNB", "RAIL" } };

        var ex = Assert.Throws<FormatException>(() => PoListGrid.Parse(rows));

        Assert.Contains("납기일자", ex.Message);
        Assert.Contains("발주량", ex.Message);
    }

    [Fact]
    public void Daily_po_pairs_each_date_with_its_order_and_delivery_columns()
    {
        var rows = new[]
        {
            new[] { "NO", "업체", "PART NO", "품명", "발주단위", "적입량", "11-03", "11-03", "11-04", "11-04" },
            new[] { "", "", "", "", "", "", "발주량", "납품량", "발주량", "납품량" },
            new[] { "1", "310471", "PN-A", "Part A", "EA", "40", "40", "0", "0", "40" },
        };

        var plan = DailyPoGrid.Parse(rows, Today);

        Assert.Equal(new DateOnly(2026, 11, 3), plan.From);
        Assert.Equal(new DateOnly(2026, 11, 4), plan.To);
        Assert.Equal("310471", plan.VendorCode);
        var item = Assert.Single(plan.Items);
        Assert.Equal("PN-A", item.PartNo);
        Assert.Equal(40m, item.PackQty);
        Assert.Equal(new[] { 40m, 0m }, item.Order);
        Assert.Equal(new[] { 0m, 40m }, item.Delivered);
    }

    [Fact]
    public void Month_day_header_takes_next_year_when_it_is_more_than_six_months_past()
    {
        // 기준일 12-20 에 "01-05" 는 지난 해가 아니라 다음 해 1월 5일이다
        Assert.True(DailyPoGrid.TryMonthDay("01-05", new DateOnly(2026, 12, 20), out var d));
        Assert.Equal(new DateOnly(2027, 1, 5), d);
    }

    [Fact]
    public void Daily_po_missing_delivery_column_is_rejected_with_the_date_position()
    {
        var rows = new[]
        {
            new[] { "PART NO", "발주단위", "적입량", "11-03" },
            new[] { "", "", "", "발주량" },
            new[] { "PN-A", "EA", "40", "40" },
        };

        var ex = Assert.Throws<FormatException>(() => DailyPoGrid.Parse(rows, Today));

        Assert.Contains("납품량", ex.Message);
    }

    [Fact]
    public void Vendor_is_read_from_the_source_description_and_judged_against_the_customer()
    {
        Assert.Equal("310471", SrmVendorCheck.VendCdOf("CORCD=7700;BIZCD=7710;VENDCD=310471;PURC_ORG=1A7700"));
        Assert.Null(SrmVendorCheck.VendCdOf("CORCD=7700"));

        var configured = SrmVendorCheck.ConfiguredVendors(new[] { "CORCD=1;VENDCD=310471", "VENDCD=310471", null });

        Assert.Equal(new[] { "310471" }, configured);
        Assert.Equal(SrmVendorCheck.Verdict.Match, SrmVendorCheck.Judge("310471", configured));
        Assert.Equal(SrmVendorCheck.Verdict.Mismatch, SrmVendorCheck.Judge("999999", configured));
        Assert.Equal(SrmVendorCheck.Verdict.NotConfigured, SrmVendorCheck.Judge("310471", Array.Empty<string>()));
    }

    [Fact]
    public void Upload_customers_are_limited_to_the_three_plants()
    {
        Assert.True(PlantCustomers.Contains("CUS-SAV"));
        Assert.True(PlantCustomers.Contains("cus-geo"));
        Assert.False(PlantCustomers.Contains("CUS-BYD"));
        Assert.False(PlantCustomers.Contains(null));
    }

    [Fact]
    public void Sample_vendor_uses_the_customer_setting_and_falls_back_to_the_placeholder()
    {
        Assert.Equal("310471", SrmVendorCheck.PickVendor(new[] { "310471" }, "SAMPLE"));
        Assert.Equal("SAMPLE", SrmVendorCheck.PickVendor(Array.Empty<string>(), "SAMPLE"));
    }
}
