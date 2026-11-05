using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Repositories.WarehouseRepository;

namespace AMES.Data.Tests;

public sealed class PickingOrderDateTests
{
    private static readonly DateTime Today = new(2026, 11, 5);
    private static PickingSlipHeaderRow Row(string number, DateTime? created, DateTime? due)
        => new(number, "legacy-required-date", null, null, null, null, "Open", 1, 1, 0, null, null, "G", created, due);

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Overdue_compares_calendar_dates_not_the_time_of_day(int days, bool expected)
    {
        var row = Row("PICK", Today, Today.AddDays(days).AddHours(23));
        Assert.Equal(expected, row.IsOverdue(Today.AddHours(12)));
    }

    [Fact]
    public void Minimum_without_a_due_date_is_not_overdue_and_sorts_after_dated_orders()
    {
        var minimum = Row("MINIMUM", Today, null);
        var early = Row("EARLY", Today.AddDays(1), Today.AddDays(-1));
        var late = Row("LATE", Today, Today.AddDays(14));
        Assert.False(minimum.IsOverdue(Today));
        Assert.Equal(new[] { "EARLY", "LATE", "MINIMUM" },
            new[] { minimum, late, early }.OrderBy(r => r.DueSortDate).Select(r => r.PickSlipNo));
        Assert.Equal(new[] { "LATE", "MINIMUM", "EARLY" },
            new[] { late, minimum, early }.OrderBy(r => r.CreatedAt).Select(r => r.PickSlipNo));
        Assert.Equal(new[] { "MINIMUM", "LATE" },
            new[] { minimum, early, late }.Where(r => !r.IsOverdue(Today)).Select(r => r.PickSlipNo));
    }

    [Fact]
    public void Filtering_rows_keeps_explicit_column_widths_and_reserves_the_scrollbar_space()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        var list = page.Split("Data=\"VisibleOrders\"")[1].Split("</RadzenDataGrid>")[0];
        Assert.Contains("wh-picking-orders-grid", list);
        var columns = System.Text.RegularExpressions.Regex.Matches(list, "<RadzenDataGridColumn[^>]+>");
        Assert.Equal(9, columns.Count);
        var total = 0;
        foreach (System.Text.RegularExpressions.Match column in columns)
        {
            var width = System.Text.RegularExpressions.Regex.Match(column.Value, " Width=\"([0-9]+)px\"");
            Assert.True(width.Success, column.Value);
            Assert.Contains($"MinWidth=\"{width.Groups[1].Value}px\"", column.Value);
            total += int.Parse(width.Groups[1].Value);
        }
        var css = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor.css");
        Assert.Contains(".wh-pick-list ::deep .wh-picking-orders-grid .rz-data-grid-data > table", css);
        Assert.Contains("table-layout: fixed !important", css);
        Assert.Contains($"min-width: {total}px", css);
        Assert.Contains("scrollbar-gutter: stable", css);
    }

    [Fact]
    public void Web_exposes_both_sortable_dates_and_scm_style_overdue_filter_without_changing_legacy_api_dates()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        Assert.Contains("Property=\"CreatedAt\"", page);
        Assert.Contains("Property=\"DueAt\" SortProperty=\"DueSortDate\" SortOrder=\"SortOrder.Ascending\"", page);
        Assert.Contains("AllowMultiColumnSorting=\"false\"", page);
        Assert.Contains("_includeOverdue = true", page);
        Assert.Contains("RadzenCheckBox TValue=\"bool\" Value=\"_includeOverdue\" Change=\"ToggleOverdue\"", page);
        Assert.Contains("!row.IsOverdue(DbClock.Today)", page);
        Assert.Contains("row.IsOverdue(DbClock.Today) ? \"picking-overdue\"", page);
        Assert.Contains("row.CreatedAt?.ToString(DateFmt.Date)", page);
        Assert.Contains("row.DueAt?.ToString(DateFmt.Date)", page);
        var toggle = page.Split("async Task ToggleOverdue(bool value)")[1].Split("async Task Refresh()")[0];
        Assert.Contains("_ordersGrid.FirstPage()", toggle);
        Assert.DoesNotContain("Generate", toggle);
        var css = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor.css");
        Assert.Contains("color: var(--ames-tx-red-400)", css);
        var query = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs")
            .Split("public List<PickingSlipHeaderRow> ListPickingSlipHeaders")[1].Split("public List<PickingSlipLineRow>")[0];
        Assert.Contains("MIN(CREATED_AT) AS CREATED_AT", query);
        Assert.Contains("MIN(DUE_AT) AS DUE_AT", query);
        Assert.Contains("CASE WHEN O.CreatedBy='WH-AUTO' THEN W.DueDate ELSE O.RequiredAt END AS DUE_AT", query);
        Assert.Contains("CONVERT(varchar(10), O.RequiredAt, 23) AS REQ_DATE", query);
    }

    [SkippableFact]
    public void Read_only_header_dates_match_the_local_clone_without_altering_inventory_or_orders()
    {
        var cs = Environment.GetEnvironmentVariable("AMES_LINE_PICKING_READONLY_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(cs), "Read-only connection not configured.");
        var factory = new AmesConnectionFactory(cs!);
        var rows = new WarehouseRepository(factory).ListPickingSlipHeaders().ToDictionary(r => r.PickSlipNo);
        using var connection = factory.OpenConnection();
        using var command = new SqlCommand("""
            SELECT COALESCE(NULLIF(P.PickSlipNo,N''),CONCAT(N'RS-',P.PickSlipID)),MIN(P.CreatedTS),
                MIN(CASE WHEN P.CreatedBy='WH-AUTO' THEN W.DueDate ELSE P.RequiredAt END),
                MAX(CASE WHEN P.WoID IS NOT NULL THEN 1 ELSE 0 END),
                MAX(CASE WHEN P.CreatedBy='WH-AUTO' AND P.WoID IS NULL THEN 1 ELSE 0 END),
                MAX(CASE WHEN P.CreatedBy='WH-REQUEST' THEN 1 ELSE 0 END)
            FROM dbo.WH_PickSlip P LEFT JOIN dbo.PP_WorkOrder W ON W.WoID=P.WoID
            GROUP BY COALESCE(NULLIF(P.PickSlipNo,N''),CONCAT(N'RS-',P.PickSlipID));
            """, connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var actual = rows[reader.GetString(0)];
            Assert.Equal(reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1), actual.CreatedAt);
            Assert.Equal(reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2), actual.DueAt);
            var hasWork = reader.GetInt32(3)==1;
            var hasMinimum = reader.GetInt32(4)==1;
            Assert.Equal(hasWork && hasMinimum ? "BOTH" : hasWork ? "WORK_ORDER" : hasMinimum ? "MINIMUM" : reader.GetInt32(5)==1 ? "REQUEST" : "OTHER", actual.GenerationSource);
        }
    }

    [Fact]
    public void Cancelled_orders_have_their_own_default_hidden_filter()
    {
        foreach(var status in new[]{"Cancelled","Canceled","CANCELLED"})
            Assert.True((Row("PICK",Today,null) with{Status=status}).IsCancelled);
        foreach(var status in new[]{"Open","Partial","Closed","Picked"})
            Assert.False((Row("PICK",Today,null) with{Status=status}).IsCancelled);
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        Assert.Contains("_includeCancelled = false",page);
        Assert.Contains("_includeCancelled || !row.IsCancelled",page);
        Assert.Contains("RadzenCheckBox TValue=\"bool\" Value=\"_includeCancelled\" Change=\"ToggleCancelled\"",page);
        Assert.Contains("SCM.Preview.Lbl.IncludeCancelled",page);
        var toggle=page.Split("async Task ToggleCancelled(bool value)")[1].Split("async Task Refresh()")[0];
        Assert.Contains("_ordersGrid.FirstPage()",toggle);
        Assert.DoesNotContain("Generate",toggle);
    }

    [Theory]
    [InlineData(false, false, "Open,Partial")]
    [InlineData(true, false, "Open,Partial,Cancelled")]
    [InlineData(false, true, "Open,Partial,Closed")]
    [InlineData(true, true, "Open,Partial,Cancelled,Closed")]
    public void Closed_and_cancelled_filters_are_independent(bool cancelled, bool closed, string expected)
    {
        var rows = new[] { "Open", "Partial", "Cancelled", "Closed" }
            .Select(status => Row(status, Today, null) with { Status = status });
        Assert.Equal(expected, string.Join(",", rows.Where(row => cancelled || !row.IsCancelled)
            .Where(row => closed || !row.IsClosed).Select(row => row.Status)));
        Assert.True((Row("C", Today, null) with { Status = "CLOSED" }).IsClosed);
        Assert.False((Row("C", Today, null) with { Status = "Cancelled" }).IsClosed);
    }

    [Fact]
    public void Status_checkboxes_share_a_second_toolbar_row_and_closed_is_hidden_by_default()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        Assert.Contains("_includeClosed = false", page);
        Assert.Contains("_includeClosed || !row.IsClosed", page);
        var filters = page.Split("class=\"picking-status-filters\"")[1].Split("<RadzenDataGrid")[0];
        Assert.True(page.IndexOf("Click=\"Load\"", StringComparison.Ordinal) < page.IndexOf("class=\"picking-status-filters\"", StringComparison.Ordinal));
        Assert.True(filters.IndexOf("PickingIncludeOverdue", StringComparison.Ordinal) < filters.IndexOf("PickingIncludeCancelled", StringComparison.Ordinal));
        Assert.True(filters.IndexOf("PickingIncludeCancelled", StringComparison.Ordinal) < filters.IndexOf("PickingIncludeClosed", StringComparison.Ordinal));
        Assert.Contains("WH.Picking.IncludeClosed", filters);
        var toggle = page.Split("async Task ToggleClosed(bool value)")[1].Split("async Task Refresh()")[0];
        Assert.Contains("_ordersGrid.FirstPage()", toggle);
        Assert.DoesNotContain("Generate", toggle);
        var css = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor.css");
        Assert.Contains(".picking-orders-toolbar ::deep .picking-status-filters", css);
        Assert.Contains("flex-basis: 100%", css);
    }

    [Fact]
    public void Generation_source_shows_work_orders_minimum_and_mixed_without_labelling_legacy_manual_orders_as_minimum()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        Assert.Contains("CASE WHEN O.WoID IS NOT NULL THEN 1 ELSE 0 END AS HAS_WORK_ORDER",source);
        Assert.Contains("CASE WHEN O.CreatedBy='WH-AUTO' AND O.WoID IS NULL THEN 1 ELSE 0 END AS HAS_MINIMUM",source);
        Assert.Contains("G.HAS_WORK_ORDER=1 AND G.HAS_MINIMUM=1 THEN 'BOTH'",source);
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        Assert.Contains("Property=\"GenerationSource\"",page);
        Assert.Contains("@GenerationSourceText(row.GenerationSource)",page);
        Assert.Contains("\"WORK_ORDER\" => L[\"WH.Picking.Source.WorkOrder\"]",page);
        Assert.Contains("\"MINIMUM\" => L[\"WH.Picking.Source.Minimum\"]",page);
        Assert.Contains("\"BOTH\" => L[\"WH.Picking.Source.Both\"]",page);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path));
}
