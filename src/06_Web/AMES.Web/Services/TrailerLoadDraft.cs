using AMES.Data.Repositories;

namespace AMES.Web.Services;

// Display-only planning: never changes a customer order or reserves inventory.
public sealed class TrailerLoadDraft
{
    public const int Capacity = 18;
    public static bool IsInShipDateRange(Line line, DateTime? from, DateTime? to) =>
        from.HasValue && to.HasValue && from.Value.Date <= to.Value.Date
        && line.ShipDate is DateTime ship && ship >= from.Value.Date && ship <= to.Value.Date;
    public static bool IsShipmentCandidate(PpRepository.SoRow row) =>
        row.OrderQty > row.ShippedQty
        && !string.Equals(row.Status?.Trim(), "Shipped", StringComparison.OrdinalIgnoreCase);
    public sealed class Line(PpRepository.SoRow source, int? palletQty, bool toteFlag = false)
    {
        public PpRepository.SoRow Source { get; } = source;
        public decimal Balance => Math.Max(0, Source.OrderQty - Source.ShippedQty);
        public decimal Quantity { get; private set; } = decimal.Floor(Math.Max(0, source.OrderQty - source.ShippedQty));
        public string QuantityText { get; private set; } = decimal.Floor(Math.Max(0, source.OrderQty - source.ShippedQty)).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        public string QuantityError { get; private set; } = "";
        public decimal? PerPallet { get; } = palletQty is > 0 ? palletQty.Value : null;
        public bool IsTote { get; } = toteFlag;
        public decimal? PalletCount => QuantityError.Length > 0 ? null : Quantity == 0 ? 0
            : !IsTote && PerPallet is decimal capacity ? decimal.Ceiling(Quantity / capacity) : null;
        public DateTime? ShipDate => Source.RequestedDeliveryDate?.Date.AddDays(-1);

        public bool TryChange(string? text, out string error)
        {
            QuantityText = text ?? "";
            error = QuantityError = "";
            if (!decimal.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) || value != decimal.Truncate(value)
                || value < 0 || value > Balance)
            {
                error = QuantityError = $"Enter a whole shipment quantity between 0 and {decimal.Floor(Balance):N0}.";
                return false;
            }
            Quantity = value;
            return true;
        }
    }

    public sealed class ToteGroup(string customer, DateTime date, List<Line> lines)
    {
        public string Customer { get; } = customer;
        public DateTime Date { get; } = date;
        public List<Line> Lines { get; } = lines;
        public string CountText { get; internal set; } = "";
        public int? PalletCount { get; internal set; }
        public string Error { get; internal set; } = "";
    }
    public sealed record Pallet(int Id, Line? Line, int Sequence, decimal Quantity, ToteGroup? Tote = null);
    public sealed class Trailer(int number, string customer, DateTime date)
    {
        public int Number { get; } = number;
        public string Customer { get; } = customer;
        public DateTime Date { get; } = date;
        public List<Pallet> Pallets { get; } = [];
    }
    public List<Trailer> Trailers { get; } = [];
    public List<ToteGroup> ToteGroups { get; } = [];
    public List<Line> Unestimated { get; } = [];
    List<Line> _lines = [];
    public int AutoPallets => (int)_lines.Where(x => !x.IsTote).Sum(x => x.PalletCount ?? 0);
    public int ManualPallets => ToteGroups.Sum(x => x.PalletCount ?? 0);
    public bool IsPartial => Unestimated.Count > 0;

    public bool Build(IEnumerable<Line> source, out string error)
    {
        var candidates = source.Where(x => IsShipmentCandidate(x.Source)).ToList();
        error = candidates.FirstOrDefault(x => x.QuantityError.Length > 0)?.QuantityError ?? "";
        if (error.Length > 0) return false;
        var lines = candidates.Where(x => x.Quantity > 0).ToList();
        error = "";
        if (lines.Count == 0) error = "No remaining quantity in the displayed supply plan lines.";
        else if (lines.Any(x => x.ShipDate is null || string.IsNullOrWhiteSpace(x.Source.CustomerId)))
            error = "A customer and Requested Date are required for every included line. Set shipment quantity to 0 to exclude a line.";
        else if (lines.Sum(x => x.PalletCount) > 2000)
            error = "Preview supports up to 2,000 pallets. Narrow the supply plan filters or reduce shipment quantities.";
        if (error.Length > 0) return false;

        _lines = lines;
        ToteGroups.Clear();
        ToteGroups.AddRange(lines.Where(x => x.IsTote)
            .GroupBy(x => (Customer: x.Source.CustomerId!, Date: x.ShipDate!.Value))
            .OrderBy(x => x.Key.Date).ThenBy(x => x.Key.Customer)
            .Select(x => new ToteGroup(x.Key.Customer, x.Key.Date, x.ToList())));
        Arrange();
        return true;
    }

    public bool ChangeToteCount(ToteGroup group, string? text, out string error)
    {
        if (!ToteGroups.Contains(group)) { error = "Unknown TOTE group."; return false; }
        group.CountText = text ?? "";
        group.PalletCount = null;
        error = "";
        var available = 2000 - AutoPallets - ManualPallets;
        if (group.CountText.Length > 0)
        {
            if (!int.TryParse(text, out var count) || count < 1 || count > available)
                error = $"Enter a whole pallet count from 1 to {available:N0}, or clear it to leave this group not determined.";
            else group.PalletCount = count;
        }
        group.Error = error;
        Arrange();
        return error.Length == 0;
    }

    void Arrange()
    {
        Unestimated.Clear();
        Unestimated.AddRange(_lines.Where(x => !x.IsTote && x.PerPallet is null));
        Unestimated.AddRange(ToteGroups.Where(x => x.PalletCount is null).SelectMany(x => x.Lines));
        Trailers.Clear();
        var id = 0;
        foreach (var group in _lines.GroupBy(x => (Customer: x.Source.CustomerId!, Date: x.ShipDate!.Value))
                     .OrderBy(x => x.Key.Date).ThenBy(x => x.Key.Customer))
        {
            Trailer? trailer = null;
            void Add(Pallet pallet)
            {
                if (trailer is null || trailer.Pallets.Count == Capacity)
                {
                    trailer = new Trailer(Trailers.Count + 1, group.Key.Customer, group.Key.Date);
                    Trailers.Add(trailer);
                }
                trailer.Pallets.Add(pallet);
            }
            foreach (var line in group.Where(x => !x.IsTote && x.PerPallet.HasValue))
            {
                var perPallet = line.PerPallet!.Value;
                for (var i = 0; i < (int)line.PalletCount!.Value; i++)
                    Add(new Pallet(++id, line, i + 1, Math.Min(perPallet, line.Quantity - i * perPallet)));
            }
            var tote = ToteGroups.FirstOrDefault(x => x.Customer == group.Key.Customer && x.Date == group.Key.Date);
            if (tote?.PalletCount is int count)
            {
                // Manual slots have no invented per-pallet part/EA allocation; the group retains its source lines.
                for (var i = 0; i < count; i++) Add(new Pallet(++id, null, i + 1, 0, tote));
            }
        }
    }

    public void AddTrailer(Trailer sameRoute) =>
        Trailers.Add(new Trailer(Trailers.Count + 1, sameRoute.Customer, sameRoute.Date));

    public bool Move(int palletId, int targetNumber, out string error)
    {
        error = "";
        var source = Trailers.FirstOrDefault(x => x.Pallets.Any(p => p.Id == palletId));
        var target = Trailers.FirstOrDefault(x => x.Number == targetNumber);
        if (source is null || target is null) error = "Select a pallet and a destination trailer.";
        else if (source == target) return true;
        else if (source.Customer != target.Customer || source.Date != target.Date)
            error = "Move pallets only between trailers with the same customer and shipment date.";
        else if (target.Pallets.Count >= Capacity) error = "A trailer can hold at most 18 pallets.";
        if (error.Length > 0) return false;
        var pallet = source!.Pallets.Single(x => x.Id == palletId);
        source.Pallets.Remove(pallet);
        target!.Pallets.Add(pallet);
        return true;
    }
}
