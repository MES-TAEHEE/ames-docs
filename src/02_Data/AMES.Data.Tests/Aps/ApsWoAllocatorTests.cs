using AMES.Data.Aps;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>스펙 §8.1 수주 FIFO 배정 규칙의 정본 — 납기순·수주 경계 분할·잔여 SoID NULL·잔량 이어짐.</summary>
public class ApsWoAllocatorTests
{
    const string Item = "ITEM-A";
    static readonly DateOnly D1 = new(2026, 10, 5), D2 = new(2026, 10, 6), D3 = new(2026, 10, 7);

    static ApsWoAllocator.OpenOrder So(int soId, decimal remain, DateOnly? due, string item = Item, string? soNo = null, int? line = 1)
        => new(soId, item, soNo ?? $"SO-{soId}", line, due, remain);

    [Fact]
    public void Single_order_covers_whole_qty_with_its_due_date()
    {
        var a = new ApsWoAllocator(new[] { So(1, 100, D1) });
        var pieces = a.Allocate(Item, 60);
        var p = Assert.Single(pieces);
        Assert.Equal((1, 60m, D1), (p.SoId, p.Qty, p.DueDate));
    }

    [Fact]
    public void Splits_across_two_orders_in_due_date_order_regardless_of_input_order()
    {
        var a = new ApsWoAllocator(new[] { So(2, 50, D2), So(1, 100, D1) });
        var pieces = a.Allocate(Item, 120);
        Assert.Equal(new (int?, decimal)[] { (1, 100m), (2, 20m) }, pieces.Select(p => (p.SoId, p.Qty)).ToArray());
        Assert.Equal(D2, pieces[1].DueDate);
    }

    [Fact]
    public void Remainder_beyond_all_orders_becomes_single_null_piece_without_due()
    {
        var a = new ApsWoAllocator(new[] { So(1, 100, D1), So(2, 50, D2) });
        var pieces = a.Allocate(Item, 180);
        Assert.Equal(3, pieces.Count);
        Assert.Equal((null, 30m, null), (pieces[2].SoId, pieces[2].Qty, pieces[2].DueDate));
    }

    [Fact]
    public void No_orders_at_all_gives_one_null_piece()
    {
        var a = new ApsWoAllocator(Array.Empty<ApsWoAllocator.OpenOrder>());
        var p = Assert.Single(a.Allocate(Item, 40));
        Assert.Equal((null, 40m), (p.SoId, p.Qty));
    }

    [Fact]
    public void Orders_with_zero_or_negative_remaining_are_skipped()
    {
        var a = new ApsWoAllocator(new[] { So(1, 0, D1), So(2, -5, D1), So(3, 30, D2) });
        var pieces = a.Allocate(Item, 30);
        var p = Assert.Single(pieces);
        Assert.Equal((3, 30m), (p.SoId, p.Qty));
    }

    [Fact]
    public void Consecutive_calls_for_same_item_continue_from_remaining_balance()
    {
        var a = new ApsWoAllocator(new[] { So(1, 100, D1), So(2, 50, D2) });
        Assert.Equal(new (int?, decimal)[] { (1, 70m) },              a.Allocate(Item, 70).Select(p => (p.SoId, p.Qty)).ToArray());
        Assert.Equal(new (int?, decimal)[] { (1, 30m), (2, 40m) },    a.Allocate(Item, 70).Select(p => (p.SoId, p.Qty)).ToArray());
        Assert.Equal(new (int?, decimal)[] { (2, 10m), (null, 5m) },  a.Allocate(Item, 15).Select(p => (p.SoId, p.Qty)).ToArray());
    }

    [Fact]
    public void Items_are_independent_and_matched_case_insensitively()
    {
        var a = new ApsWoAllocator(new[] { So(1, 100, D1, item: "item-a"), So(9, 100, D1, item: "ITEM-B") });
        Assert.Equal(1, a.Allocate("ITEM-A", 10).Single().SoId);
        Assert.Equal(9, a.Allocate("item-b", 10).Single().SoId);
        Assert.Null(a.Allocate("ITEM-C", 10).Single().SoId);
    }

    [Fact]
    public void Null_due_date_sorts_after_dated_orders_then_by_so_number_and_line()
    {
        var a = new ApsWoAllocator(new[]
        {
            So(3, 10, null, soNo: "SO-B", line: 1),
            So(2, 10, D3,   soNo: "SO-B", line: 2),
            So(1, 10, D3,   soNo: "SO-B", line: 1),
            So(4, 10, D3,   soNo: "SO-A", line: 9),
        });
        Assert.Equal(new int?[] { 4, 1, 2, 3 }, a.Allocate(Item, 40).Select(p => p.SoId).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_qty_returns_empty_list(decimal qty)
    {
        var a = new ApsWoAllocator(new[] { So(1, 100, D1) });
        Assert.Empty(a.Allocate(Item, qty));
    }
}
