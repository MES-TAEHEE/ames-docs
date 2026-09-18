using System.Text;

namespace AMES.Pda.Services;

// 100 x 50 mm stock. DPI must match the printer's physical printhead.
public static class SparePartLabel
{
    public static string Build(string sparePartNo, string? partNo, string? maker, string? location, int dpi = 203)
    {
        if (dpi is not (203 or 300)) throw new ArgumentOutOfRangeException(nameof(dpi));
        if (string.IsNullOrWhiteSpace(sparePartNo) || sparePartNo.Length > 80
            || sparePartNo.Any(c => c < '!' || c > '~'))
            throw new ArgumentException("The spare part barcode is invalid.");
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

    // Encode every UTF-8 byte so master values cannot inject ZPL commands.
    private static string Hex(string value) => string.Concat(Encoding.UTF8.GetBytes(
        value.Replace('\r', ' ').Replace('\n', ' ')).Select(b => $"_{b:X2}"));
}
