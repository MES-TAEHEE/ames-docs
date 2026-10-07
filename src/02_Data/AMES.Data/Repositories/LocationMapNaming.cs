namespace AMES.Data.Repositories;

public static class LocationMapNaming
{
    public static bool ValidPrefix(string? value) => value is { Length: 2 } && value.All(char.IsAsciiLetterUpper);

    public static string AvailablePrefix(string areaCode, IEnumerable<string?> used)
    {
        var reserved = used.Where(x => ValidPrefix(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suggested = new string(areaCode.ToUpperInvariant().Where(char.IsAsciiLetterUpper).Take(2).ToArray());
        if (ValidPrefix(suggested) && !reserved.Contains(suggested)) return suggested;
        for (var first = 'A'; first <= 'Z'; first++)
            for (var second = 'A'; second <= 'Z'; second++)
            {
                var candidate = $"{first}{second}";
                if (!reserved.Contains(candidate)) return candidate;
            }
        throw new InvalidOperationException("No two-letter area code is available.");
    }

    public static int YOrder(string? value) => value is { Length: > 1 }
        && char.IsAsciiLetterUpper(value[0])
        && int.TryParse(value[1..], out var group) && group > 0
            ? (group - 1) * 26 + value[0] - 'A' + 1 : 0;

    public static string YCode(int index)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        return $"{(char)('A' + (index - 1) % 26)}{(index - 1) / 26 + 1}";
    }

    public static bool InGrid(string? aisle, string? bay, int aisles, int bays) =>
        YOrder(aisle) is var row && row >= 1 && row <= aisles
        && int.TryParse(bay, out var column) && column >= 1 && column <= bays;

    public static string NextY(IEnumerable<string> values)
    {
        var used = values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var number = 1; number <= 9999; number++)
            for (var letter = 'A'; letter <= 'Z'; letter++)
                if (!used.Contains($"{letter}{number}")) return $"{letter}{number}";
        throw new InvalidOperationException("No row code is available.");
    }

    public static string NextFloor(IEnumerable<string> values)
    {
        var highest = values.Select(FloorNumber).DefaultIfEmpty(0).Max();
        return $"F{highest + 1}";
    }

    public static int FloorNumber(string? value)
    {
        var text = value?.Trim() ?? "";
        var digits = text.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? text[1..]
            : text.EndsWith("F", StringComparison.OrdinalIgnoreCase) ? text[..^1] : text;
        return int.TryParse(digits, out var number) && number > 0 ? number : 0;
    }

    public static string FloorCode(string? value) => FloorNumber(value) is var number && number > 0 ? $"F{number}" : "";

    public sealed record AxisPlan(string Floor, string Code, List<(string Row, string Column, string Floor)> Cells);

    public static List<(string Row, string Column, string Floor)> MissingCells(
        IEnumerable<(string Row, string Column, string Floor)> locations, AxisPlan plan)
    {
        var existing = locations.ToList();
        var onFloor = existing.Where(x => FloorNumber(x.Floor) == FloorNumber(plan.Floor)
                && YOrder(x.Row) > 0 && int.TryParse(x.Column, out var column) && column > 0)
            .Concat(plan.Cells).ToList();
        var occupied = existing.Select(x => (x.Row, x.Column, FloorCode(x.Floor))).ToHashSet();
        return (from row in onFloor.Select(x => x.Row).Distinct(StringComparer.OrdinalIgnoreCase)
                from column in onFloor.Select(x => x.Column).Distinct(StringComparer.OrdinalIgnoreCase)
                let cell = (Row: row, Column: column, Floor: plan.Floor)
                where !occupied.Contains(cell)
                select cell).ToList();
    }

    public static AxisPlan PlanAxis(IEnumerable<(string Row, string Column, string Floor)> locations, string kind, string? selectedFloor)
    {
        if (kind is not ("Floor" or "Row" or "Column")) throw new ArgumentException("Invalid map axis.");
        var all = locations.ToList();
        var current = all.Where(x => FloorNumber(x.Floor) > 0
            && YOrder(x.Row) > 0 && int.TryParse(x.Column, out var column) && column > 0).ToList();
        var floor = kind == "Floor" ? NextFloor(all.Select(x => x.Floor))
            : FloorCode(selectedFloor) is { Length: > 0 } selected && all.Any(x => FloorNumber(x.Floor) == FloorNumber(selected)) ? selected
            : NextFloor(all.Select(x => x.Floor));
        var onFloor = current.Where(x => FloorNumber(x.Floor) == FloorNumber(floor)).ToList();
        if (kind == "Floor")
        {
            var sourceFloor = current.Select(x => FloorNumber(x.Floor)).DefaultIfEmpty(0).Max();
            var pairs = sourceFloor == 0 ? [("A1", "01")] : current.Where(x => FloorNumber(x.Floor) == sourceFloor)
                .Select(x => (x.Row, x.Column)).Distinct().ToList();
            return new AxisPlan(floor, floor, pairs.Select(x => (x.Row, x.Column, floor)).ToList());
        }
        if (onFloor.Count == 0) return new AxisPlan(floor, kind == "Row" ? "A1" : "01", [("A1", "01", floor)]);
        if (kind == "Row")
        {
            var row = NextY(onFloor.Select(x => x.Row));
            return new AxisPlan(floor, row, onFloor.Select(x => x.Column).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(column => (row, column, floor)).ToList());
        }
        var nextColumn = (onFloor.Select(x => int.Parse(x.Column)).Max() + 1).ToString("D2");
        return new AxisPlan(floor, nextColumn, onFloor.Select(x => x.Row).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(row => (row, nextColumn, floor)).ToList());
    }

    public static string LocationNo(string prefix, string x, string y, string floor)
    {
        var value = $"{prefix}{x}{y}{floor}";
        if (value.Length > 20) throw new ArgumentException("Location No exceeds 20 characters.");
        return value;
    }
}
