#:project ../src/06_Web/AMES.Web/AMES.Web.csproj
#:property PublishAot=false

// dotnet run --file tools/check_trailer_load_draft.cs
// Pure UI model; no web host, database connection, or seed execution.
using AMES.Data.Repositories;
using AMES.Web.Services;

static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
static TrailerLoadDraft.Line Line(int id, decimal qty, string customer = "A", DateTime? requested = null) =>
    new(new PpRepository.SoRow(id, "PO-" + id, id, customer, "PART-" + id, "Part", qty, 0,
        null, requested ?? new DateTime(2026, 10, 5), null, "Confirmed", null, null, true), 60);

var line = Line(1, 1141);
Check(line.PalletCount == 20, "Partial pallets round up.");
Check(line.ShipDate == new DateTime(2026, 10, 4), "Ship on requested date minus one day.");
var rangeFrom = new DateTime(2026, 10, 1);
var rangeTo = rangeFrom.AddDays(6);
Check((rangeTo - rangeFrom).Days + 1 == 7, "The default range covers seven calendar days including today.");
Check(TrailerLoadDraft.IsInShipDateRange(Line(90, 60, requested: rangeFrom.AddDays(1)), rangeFrom, rangeTo), "Include first ship date.");
Check(TrailerLoadDraft.IsInShipDateRange(Line(91, 60, requested: rangeTo.AddDays(1)), rangeFrom, rangeTo), "Include last ship date using requested date minus one.");
Check(!TrailerLoadDraft.IsInShipDateRange(Line(92, 60, requested: rangeFrom), rangeFrom, rangeTo), "Exclude shipments before the range.");
Check(!TrailerLoadDraft.IsInShipDateRange(Line(93, 60, requested: rangeTo.AddDays(2)), rangeFrom, rangeTo), "Exclude shipments after the range.");
Check(!TrailerLoadDraft.IsInShipDateRange(line, rangeTo, rangeFrom)
    && !TrailerLoadDraft.IsInShipDateRange(line, null, rangeTo), "Incomplete or reversed ranges never show all dates.");
foreach (var text in new[] { "-1", "1.5", "abc", "", "1142" })
    Check(!line.TryChange(text, out _), "Reject invalid quantity: " + text);
Check(line.Quantity == 1141, "Invalid edits must not mutate the draft.");
Check(line.PalletCount is null && !new TrailerLoadDraft().Build([line], out _), "Invalid input must not preview a stale quantity.");
Check(!line.TryChange("", out _) && line.QuantityText == "", "Keep an empty edit buffer while replacing a quantity.");
foreach (var (input, pallets) in new[] { ("1", 1m), ("12", 1m), ("121", 3m) })
    Check(line.TryChange(input, out _) && line.QuantityText == input && line.PalletCount == pallets, "Recalculate on every input event.");
Check(line.TryChange("1141", out _), "A corrected value clears the input error.");
Check(new TrailerLoadDraft.Line(line.Source, 55).PalletCount == 21, "Calculate using the matched master packing quantity.");
Check(typeof(TrailerLoadDraft.Line).GetProperty("PerPallet")!.SetMethod is null, "Master packing quantity is read-only.");
Check(typeof(TrailerLoadDraft.Line).GetProperty("IsTote")!.SetMethod is null, "TOTE mode comes from Item Master and cannot be overridden in the draft.");
foreach (int? missingPacking in new int?[] { null, 0, -1 })
{
    var invalid = new TrailerLoadDraft.Line(line.Source, missingPacking);
    Check(invalid.PerPallet is null && invalid.PalletCount is null, "Never replace missing master data with a default.");
    var incomplete = new TrailerLoadDraft();
    Check(incomplete.Build([invalid], out _) && incomplete.IsPartial && incomplete.Unestimated.Count == 1
        && incomplete.Trailers.Count == 0, "Missing packing data opens an unestimated preview without invented trailers.");
    Check(invalid.TryChange("0", out _) && invalid.PalletCount == 0, "A missing-packing line can be excluded with zero shipment quantity.");
}
var plan = new TrailerLoadDraft();
Check(plan.Build([line, Line(2, 60, "B"), Line(3, 60, "A", new DateTime(2026, 10, 6))], out _), "Build.");
Check(plan.Trailers.Count == 4, "18 per trailer, grouped by customer and date.");
Check(plan.Trailers.All(x => x.Pallets.Count <= 18), "Respect capacity.");
var first = plan.Trailers[0];
var second = plan.Trailers[1];
Check(second.Pallets.Last().Quantity == 1, "Preserve the final partial pallet.");
Check(!plan.Move(second.Pallets[0].Id, first.Number, out _), "Reject full trailer.");
var idToMove = first.Pallets[0].Id;
Check(!plan.Move(idToMove, plan.Trailers[2].Number, out _), "Reject other customer.");
Check(!plan.Move(idToMove, plan.Trailers[3].Number, out _), "Reject other date.");
var total = plan.Trailers.SelectMany(x => x.Pallets).Sum(x => x.Quantity);
plan.AddTrailer(first);
Check(plan.Move(idToMove, plan.Trailers.Last().Number, out _), "Move into empty same-route trailer.");
Check(plan.Trailers.SelectMany(x => x.Pallets).Count(x => x.Id == idToMove) == 1, "No duplicated pallet.");
Check(plan.Trailers.SelectMany(x => x.Pallets).Sum(x => x.Quantity) == total, "Quantity conserved.");
Check(!plan.Move(-1, first.Number, out _), "Unknown pallet rejected.");
Check(!plan.Build([Line(9, 0)], out _), "Empty loads rejected.");
Check(!plan.Build([Line(9, 60, "")], out _), "Missing customer rejected.");
Check(!plan.Build([Line(9, 2001 * 60)], out _), "Bound preview size.");
var missingDate = new TrailerLoadDraft.Line(new PpRepository.SoRow(10, "PO", 1, "A", "PART", "Part", 60, 0,
    null, null, null, "Open", null, null, true), 60);
Check(!plan.Build([missingDate], out _), "Missing requested date rejected.");
Check(!TrailerLoadDraft.IsInShipDateRange(missingDate, rangeFrom, rangeTo), "Missing dates are not silently placed in the selected week.");
Check(line.TryChange("0", out _) && line.PalletCount == 0, "Zero quantity excludes the line.");
var original = Line(20, 100).Source;
var partial = new TrailerLoadDraft.Line(original with { ShippedQty = 40 }, 60);
Check(TrailerLoadDraft.IsShipmentCandidate(partial.Source) && partial.Balance == 60 && partial.Quantity == 60,
    "Partial shipments retain only their unshipped balance.");
Check(!partial.TryChange("61", out _), "A partial shipment cannot exceed its remaining quantity.");
Check(partial.TryChange("60", out _), "Correct invalid input before previewing.");
foreach (var completed in new[] {
    original with { ShippedQty = 100 }, original with { ShippedQty = 110 },
    original with { Status = "Shipped" }, original with { Status = " shipped " } })
    Check(!TrailerLoadDraft.IsShipmentCandidate(completed), "Exclude completed quantities and shipped status.");
Check(plan.Build([partial, new(original with { Status = "Shipped" }, 60)], out _)
    && plan.Trailers.SelectMany(x => x.Pallets).Sum(x => x.Quantity) == 60,
    "Completed shipments cannot enter trailer loads even if passed to the builder.");
Console.WriteLine("PASS: quantities, rounding, partial pallets, dates, customer isolation, capacity, movement, conservation, and validation.");

var tote1 = new TrailerLoadDraft.Line(Line(101, 300).Source, 60, true);
var tote2 = new TrailerLoadDraft.Line(Line(102, 120).Source, null, true);
var unknown = new TrailerLoadDraft.Line(Line(103, 50).Source, null);
var totePlan = new TrailerLoadDraft();
Check(tote1.PalletCount is null, "TOTE never uses per-item packing to invent a pallet estimate.");
Check(totePlan.Build([Line(100, 17 * 60), tote1, tote2, unknown,
    new(Line(104, 20, "B").Source, null, true), new(Line(105, 20, "A", new DateTime(2026, 10, 6)).Source, null, true)], out _), "Mixed preview opens.");
Check(totePlan.ToteGroups.Count == 3, "TOTE grouping isolates customers and dates.");
var mixed = totePlan.ToteGroups.Single(x => x.Lines.Count == 2);
Check(totePlan.AutoPallets == 17 && totePlan.ManualPallets == 0 && totePlan.Unestimated.Count == 5, "Unknown and unentered TOTE lines are excluded and counted.");
Check(totePlan.ChangeToteCount(mixed, "2", out _) && totePlan.ManualPallets == 2 && totePlan.Unestimated.Count == 3,
    "Two mixed-part TOTE pallets counted once, not per item.");
Check(totePlan.Trailers.Count == 2 && totePlan.Trailers[0].Pallets.Count == 18, "Manual and auto pallets share trailer capacity.");
Check(totePlan.Trailers.SelectMany(x => x.Pallets).Count(x => x.Tote == mixed) == 2
    && totePlan.Trailers.SelectMany(x => x.Pallets).Where(x => x.Tote != null).All(x => x.Line is null && x.Quantity == 0),
    "Manual pallets retain group identity without inventing per-pallet part quantities.");
foreach (var text in new[] { "0", "-1", "1.5", "abc", "1984" })
    Check(!totePlan.ChangeToteCount(mixed, text, out _) && mixed.PalletCount is null && totePlan.ManualPallets == 0,
        "Invalid TOTE count excludes its old estimate: " + text);
Check(totePlan.ChangeToteCount(mixed, "", out _) && mixed.CountText == "" && mixed.Error == "", "Cleared count is unestimated, not zero pallets.");
Check(totePlan.ChangeToteCount(mixed, "3", out _) && mixed.PalletCount == 3, "Count updates on input.");
var manualId = totePlan.Trailers[0].Pallets.First(x => x.Tote != null).Id;
Check(totePlan.Move(manualId, totePlan.Trailers[1].Number, out _) && totePlan.ManualPallets == 3,
    "Manual pallet movement preserves the group estimate.");
Check(totePlan.Build([tote1, tote2], out _) && totePlan.ManualPallets == 0 && totePlan.IsPartial,
    "A rebuilt quantity/filter scope resets estimates instead of reusing stale group totals.");
Check(totePlan.ChangeToteCount(totePlan.ToteGroups[0], "1", out _) && !totePlan.IsPartial,
    "All groups estimated removes the partial warning.");
Check(!totePlan.ChangeToteCount(mixed, "1", out _), "Reject groups outside the current scope.");
Console.WriteLine("PASS: partial previews, manual mixed TOTE groups, isolation, capacity, validation, and estimate reset.");

// Shipment Plan must use the same route policy as MainLayout, not menu visibility.
var permissions = new PermissionService(null!, new ScreenCatalogNotifier());
void SetPermissionField(string name, object value) => typeof(PermissionService)
    .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .SetValue(permissions, value);
var levels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
SetPermissionField("_hrefLevel", levels);
Check(permissions.CanOpen("fg/shipment-plan") && !permissions.IsVisible("fg/shipment-plan"),
    "An unregistered preview route follows MainLayout policy, not menu visibility.");
SetPermissionField("_screenHrefs", new List<string> { "fg/shipment-plan" });
Check(!permissions.CanOpen("fg/shipment-plan"), "Registered screens still require read permission.");
levels["fg/shipment-plan"] = "REA";
Check(permissions.CanOpen("fg/shipment-plan"), "Registered screen with read permission opens.");
SetPermissionField("_loadFailed", true);
Check(!permissions.CanOpen("fg/shipment-plan"), "Permission lookup failure stays closed.");
Console.WriteLine("PASS: Shipment Plan route permissions match the shared layout policy.");
