using AMES.Data.Repositories;
using Xunit;
using Row = AMES.Data.Repositories.LineScheduleRepository.ScheduleRow;

namespace AMES.Data.Tests;

// 10-14 — PP-LSB 「적용」·「초기화」 감사 로그에 남기는 계획 요약
public class LineScheduleAuditViewTests
{
    [Fact]
    public void No_rows_means_no_saved_plan()
        => Assert.Null(LineScheduleRepository.AuditView(Array.Empty<Row>()));

    [Fact]
    public void Rows_are_summarised_in_start_order_with_readable_times()
    {
        var rows = new[]
        {
            new Row(12, "LP-CMN-001", 501, "WO-20261015-001", "Core A", 600, 690, 120m, "DRAFT", "WO", null, null, null, "M-01"),
            new Row(10, "LP-CMN-001", null, null, null, 450, 480, 0m, "DRAFT", "MC", "금형 교체", "WO", 501, "M-01"),
            new Row(11, null, null, null, null, 720, 1440, 0m, "DRAFT", "PM", "월간 PM", "PMSCH", 7, null),
            new Row(13, null, 502, null, null, 690, 690, 30m, "DRAFT", null, null, null, null, null),
        };

        var v = LineScheduleRepository.AuditView(rows)!;

        Assert.Equal("LP-CMN-001", v.PatternId);
        Assert.Equal(new[] { "MC", "WO", "WO", "PM" }, v.Rows.Select(r => r.Kind));
        Assert.Equal(new[] { "07:30–08:00", "10:00–11:30", "11:30–11:30", "12:00–24:00" }, v.Rows.Select(r => r.Time));
        Assert.Equal("WO-20261015-001", v.Rows[1].WoNumber);
        Assert.Equal("#502", v.Rows[2].WoNumber);          // WO 번호를 못 읽은 0분 형제 슬롯도 WO 를 알 수 있게
        Assert.Equal(30m, v.Rows[2].Qty);
        Assert.Equal("WO:501", v.Rows[0].Ref);
        Assert.Equal("PMSCH:7", v.Rows[3].Ref);
        Assert.Null(v.Rows[1].Ref);
    }

    [Fact]
    public void Same_rows_give_the_same_summary_regardless_of_input_order()
    {
        var a = new Row(1, "P", 1, "W1", null, 60, 120, 10m, "DRAFT", "WO", null, null, null);
        var b = new Row(2, "P", 2, "W2", null, 0, 60, 5m, "DRAFT", "WO", null, null, null);
        Assert.Equal(LineScheduleRepository.AuditView(new[] { a, b })!.Rows, LineScheduleRepository.AuditView(new[] { b, a })!.Rows);
    }
}
