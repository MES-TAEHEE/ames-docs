using AMES.Api.Endpoints;
using Xunit;

namespace AMES.Api.Tests;

public class FgOutboundBarcodeTests
{
    [Theory]
    [InlineData("LOT-001", "LOT-001")]
    [InlineData("\u0002LOT-001\u0003\r\n", "LOT-001")]
    [InlineData("FGLOT:LOT-001", "LOT-001")]
    [InlineData("lot: LOT-001", "LOT-001")]
    [InlineData("STOCK:BOX-001", "BOX-001")]
    public void Normalizes_inventory_barcodes(string barcode, string expected)
    {
        Assert.True(FgOutboundBarcode.TryNormalize(barcode, out var value, out var error));
        Assert.Equal(expected, value);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(null, "Scan Barcode")]
    [InlineData("EOS:EMP001", "Unsupported barcode type")]
    [InlineData("PO:001", "Unsupported barcode type")]
    [InlineData("LOT-\n001", "control characters")]
    public void Rejects_invalid_input_before_database_lookup(string? barcode, string reason)
    {
        Assert.False(FgOutboundBarcode.TryNormalize(barcode, out _, out var error));
        Assert.Contains(reason, error);
    }

    [Fact]
    public void Only_registered_FG_locations_are_eligible()
    {
        Assert.Null(FgOutboundBarcode.LocationFailure("LOT-001", "FA01A1F1", "FA01A1F1", "FG"));
        Assert.Null(FgOutboundBarcode.LocationFailure("LOT-001", "FA01A1F1", "FA01A1F1", "fg"));
        Assert.Contains("Complete FG Put-Away", FgOutboundBarcode.LocationFailure("LOT-001", null, null, null)!);
        Assert.Contains("not registered", FgOutboundBarcode.LocationFailure("LOT-001", "OLD", null, null)!);
        Assert.Contains("not an FG location", FgOutboundBarcode.LocationFailure("LOT-001", "MA01A1F1", "MA01A1F1", "MAT")!);
        Assert.Contains("type: Not set", FgOutboundBarcode.LocationFailure("LOT-001", "OLD", "OLD", null)!);
    }

    [Fact]
    public void Never_truncates_long_input_to_another_valid_barcode()
    {
        Assert.False(FgOutboundBarcode.TryNormalize(new string('A', 51), out _, out var error));
        Assert.Contains("50 characters", error);
    }

    [Theory]
    [InlineData(true, null, false, false, false, false, "unit type is missing")]
    [InlineData(true, "PART", false, false, false, true, "no linked part number")]
    [InlineData(true, "BOX", false, false, true, false, "outbound was already recorded")]
    [InlineData(true, "PALLET", false, false, false, false, "no positive-quantity")]
    [InlineData(false, null, false, true, false, false, "location barcode")]
    [InlineData(false, null, true, false, false, false, "status: CONFIRMED")]
    [InlineData(false, null, false, false, false, false, "no inventory or production LOT")]
    public void Explains_the_observed_database_state(bool stock, string? unit, bool lot, bool location,
        bool shipped, bool missingPart, string reason)
    {
        var message = FgOutboundBarcode.Failure("BARCODE-001", stock, unit, lot, "CONFIRMED", location, shipped, missingPart);
        Assert.Contains("BARCODE-001", message);
        Assert.Contains(reason, message);
        Assert.DoesNotContain("Outbound barcode was not found", message);
    }
}
