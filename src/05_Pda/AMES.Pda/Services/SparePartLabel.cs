using System.Text;

namespace AMES.Pda.Services;

// 100 x 50 mm stock. DPI must match the printer's physical printhead.
public static class SparePartLabel
{
    public const int LabelHeightDots = 400;
    public const int VerticalOffsetDots = 20;
    public const int PreviewVerticalOffsetPercent = VerticalOffsetDots * 100 / LabelHeightDots;

    public static IReadOnlyList<string> NextSerials(string sparePartNo, IEnumerable<string> existing, int count)
    {
        if (count is < 0 or > 50) throw new ArgumentOutOfRangeException(nameof(count));
        var key = sparePartNo.Trim().ToUpperInvariant();
        if (key.StartsWith("EOS-SP-", StringComparison.Ordinal)) key = key[7..];
        key = new(key.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (key.Length > 15) key = key[^15..];
        if (key.Length == 0) throw new ArgumentException("The Spare Part No cannot be converted to a serial prefix.");
        var prefix = $"SPI-{key}-";
        var last = existing.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(Suffix).DefaultIfEmpty().Max();
        if (last + count > 9999) throw new InvalidOperationException("The serial number range for this spare part is exhausted.");
        return Enumerable.Range(1, count).Select(i => $"{prefix}{last + i:0000}").ToList();

        static long Suffix(string value)
        {
            var start = value.Length;
            while (start > 0 && char.IsDigit(value[start - 1])) start--;
            return start < value.Length && long.TryParse(value[start..], out var number) ? number : 0;
        }
    }

    public static string BuildSerialized(string serialNo, string sparePartNo, string? partName,
        string? vendorName, string? location, string? extraLocation, int dpi = 203)
    {
        if (dpi is not (203 or 300)) throw new ArgumentOutOfRangeException(nameof(dpi));
        ValidateBarcode(serialNo);
        int D(int dots) => (int)Math.Round(dots * dpi / 203d);
        var qrScale = D(8);
        var zpl = new StringBuilder($"^XA\n^CI28\n^PW{D(800)}\n^LL{D(LabelHeightDots)}\n^LH0,0\n");
        zpl.AppendLine($"^FO{D(24)},{Y(34)}^BQN,2,{qrScale}^FH^FDLA,{Hex(serialNo)}^FS");

        var extra = string.IsNullOrWhiteSpace(extraLocation) ? null : extraLocation.Trim();
        Right(partName, 36, 48);
        Right(vendorName, 91, 48);
        Right(location, 146, 44, extra is null ? 2 : 1);
        if (extra is not null) Right($"({extra})", 197, 38, 1);
        Bottom(sparePartNo, 248, 56);
        Bottom(serialNo, 316, 52);
        zpl.AppendLine("^PQ1\n^XZ");
        return zpl.ToString();

        void Right(string? value, int y, int fontHeight, int lines = 2)
        {
            var text = Value(value);
            zpl.AppendLine($"^FO{D(244)},{Y(y)}^A0N,{D(fontHeight)},{D(Math.Max(18, fontHeight - 8))}^FB{D(536)},{lines},0,L,0^FH^FD{Hex(text)}^FS");
        }
        void Bottom(string? value, int y, int fontHeight)
        {
            var text = Value(value);
            zpl.AppendLine($"^FO{D(28)},{Y(y)}^A0N,{D(fontHeight)},{D(Math.Max(16, fontHeight - 8))}^FB{D(744)},1,0,L,0^FH^FD{Hex(text)}^FS");
        }
        static string Value(string? value)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
            if (text.Length > 100) throw new ArgumentException("A label value is too long.");
            return text;
        }
        int Y(int y) => D(y + VerticalOffsetDots);
    }

    public static string Build(string sparePartNo, string? partNo, string? maker, string? location, int dpi = 203)
    {
        if (dpi is not (203 or 300)) throw new ArgumentOutOfRangeException(nameof(dpi));
        ValidateBarcode(sparePartNo);
        int D(int dots) => (int)Math.Round(dots * dpi / 203d);
        var zpl = new StringBuilder($"^XA\n^CI28\n^PW{(int)Math.Round(100 * dpi / 25.4)}\n^LL{(int)Math.Round(50 * dpi / 25.4)}\n^LH0,0\n");
        // Byte-mode QR capacities bound the size, even for lowercase/mixed barcodes.
        // Preserve a four-module quiet zone before the text column.
        var qrScale = D(sparePartNo.Length <= 17 ? 10 : sparePartNo.Length <= 32 ? 9 : sparePartNo.Length <= 53 ? 8 : 6);
        zpl.AppendLine($"^FO{4 * qrScale},{D(60)}^BQN,2,{qrScale}^FH^FDLA,{Hex(sparePartNo)}^FS");
        Field("Spare Part No", sparePartNo, 16);
        Field("Part No", partNo, 112);
        Field("Maker", maker, 208);
        Field("Location No", location, 304);
        zpl.AppendLine("^PQ1\n^XZ");
        return zpl.ToString();

        void Field(string title, string? value, int y)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
            // Long values use two lines inside the same 96-dot row.
            var width = Math.Min(26, 456 / text.Length);
            var lines = 1;
            var height = 44;
            if (width < 16)
            {
                lines = 2;
                height = 26;
                width = Math.Min(22, 456 / (int)Math.Ceiling(text.Length / 2d));
            }
            if (width < 5) throw new ArgumentException($"{title} is too long for this label.");
            zpl.AppendLine($"^FO{D(320)},{D(y + 34)}^A0N,{D(height)},{D(width)}^FB{D(456)},{lines},{D(2)},L,0^FH^FD{Hex(text)}^FS");
        }
    }

    private static void ValidateBarcode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(c => c < '!' || c > '~'))
            throw new ArgumentException("The barcode is invalid.");
    }

    // Encode every UTF-8 byte so master values cannot inject ZPL commands.
    private static string Hex(string value) => string.Concat(Encoding.UTF8.GetBytes(
        value.Replace('\r', ' ').Replace('\n', ' ')).Select(b => $"_{b:X2}"));
}
