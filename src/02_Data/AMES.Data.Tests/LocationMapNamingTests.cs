using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class LocationMapNamingTests
{
    [Fact]
    public void Y_axis_wraps_after_Z_and_location_number_has_no_separators()
    {
        Assert.Equal("A1", LocationMapNaming.NextY([]));
        Assert.Equal("B1", LocationMapNaming.NextY(["A1"]));
        Assert.Equal("C1", LocationMapNaming.NextY(["A1", "B1"]));
        Assert.Equal("A2", LocationMapNaming.NextY(Enumerable.Range('A', 26).Select(x => $"{(char)x}1")));
        Assert.Equal("Z1", LocationMapNaming.YCode(26));
        Assert.Equal("A2", LocationMapNaming.YCode(27));
        Assert.Equal("F3", LocationMapNaming.NextFloor(["01", "1", "02", "2"]));
        Assert.Equal("F3", LocationMapNaming.NextFloor(["01F", "02F"]));
        Assert.Equal("F4", LocationMapNaming.NextFloor(["F1", "F2", "F3"]));
        Assert.Equal("F1", LocationMapNaming.FloorCode("1F"));
        var first = LocationMapNaming.PlanAxis([("01", "A", "01"), ("09", "B", "2")], "Row", "1F");
        Assert.Equal("F1", first.Floor);
        Assert.Equal("A1", first.Code);
        Assert.Equal("B1", LocationMapNaming.PlanAxis(first.Cells, "Row", "F1").Code);
        Assert.Equal("02", LocationMapNaming.PlanAxis(first.Cells, "Column", "F1").Code);
        Assert.Equal("MA01A1F3", LocationMapNaming.LocationNo("MA", "01", "A1", "F3"));
        Assert.Equal("MA02B1F3", LocationMapNaming.LocationNo("MA", "02", "B1", "F3"));
        Assert.Equal("MA", LocationMapNaming.AvailablePrefix("MAT_AREA", ["FG"]));
        Assert.Equal("AA", LocationMapNaming.AvailablePrefix("MAT_AREA", ["MA", "FG"]));
    }

    [Fact]
    public void Adding_an_axis_fills_all_existing_grid_gaps()
    {
        (string Row, string Column, string Floor)[] existing =
            [("A1", "01", "F1"), ("B1", "01", "F1"), ("A1", "02", "F1")];
        var plan = LocationMapNaming.PlanAxis(existing, "Row", "F1");

        var missing = LocationMapNaming.MissingCells(existing, plan);

        Assert.Equal(3, missing.Count);
        Assert.Contains(("B1", "02", "F1"), missing);
        Assert.Contains(("C1", "01", "F1"), missing);
        Assert.Contains(("C1", "02", "F1"), missing);
    }

    [Fact]
    public void Grid_bounds_only_exclude_locations_outside_the_requested_size()
    {
        Assert.True(LocationMapNaming.InGrid("C1", "03", 3, 3));
        Assert.False(LocationMapNaming.InGrid("D1", "03", 3, 3));
        Assert.False(LocationMapNaming.InGrid("C1", "04", 3, 3));
    }
}
