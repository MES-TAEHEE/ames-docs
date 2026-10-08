using System.Globalization;
#if ANDROID
using Android.Bluetooth;
using Android.Graphics;
using Paint = Android.Graphics.Paint;
using Color = Android.Graphics.Color;
#endif

namespace AMES.Pda.Services;

public sealed class PhomemoM220Printer
{
    public const int Width = 560;  // 70 mm at 203 dpi
    public const int Height = 640; // 80 mm at 203 dpi

    public byte[] Render(PdaApi.BoxReprintRow row)
    {
#if ANDROID
        using var bitmap = Bitmap.CreateBitmap(Width, Height, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bitmap);
        using var ink = new Paint(PaintFlags.AntiAlias) { Color = Color.Black };
        canvas.DrawColor(Color.White);

        void Rule(float y) { ink.StrokeWidth = 2; canvas.DrawLine(20, y, 540, y, ink); }
        void Line(string value, float x, float y, float size, float maxWidth, bool bold = false)
        {
            ink.SetTypeface(bold ? Typeface.Create(Typeface.Default, TypefaceStyle.Bold) : Typeface.Default);
            ink.TextSize = size;
            value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
            while (ink.MeasureText(value) > maxWidth && ink.TextSize > 15) ink.TextSize -= 1;
            canvas.DrawText(value, x, y, ink);
        }
        void Field(string title, string value, float x, float y, float width)
        {
            Line(title.ToUpperInvariant(), x, y, 16, width);
            Line(value, x, y + 28, 25, width, true);
        }

        DrawCode128(canvas, ink, row.BoxNo, 22, 69, 516, 92);
        Line(row.BoxNo, 22, 184, 24, 516, true);
        Rule(195);
        Field("Part No", row.PartNo, 22, 219, 516);
        Rule(260);
        Line("DESCRIPTION", 22, 281, 16, 516);
        var name = row.PartName.Trim();
        ink.TextSize = 22;
        var split = name.LastIndexOf(' ', Math.Min(name.Length - 1, Math.Max(0, name.Length / 2)));
        if (ink.MeasureText(name) <= 516 || split < 1)
            Line(name, 22, 309, 22, 516, true);
        else
        {
            Line(name[..split], 22, 307, 22, 516, true);
            Line(name[(split + 1)..], 22, 332, 22, 516, true);
        }
        Rule(344);
        Field("Quantity", $"{row.Qty.ToString("0.###", CultureInfo.InvariantCulture)} {row.Unit}", 22, 368, 245);
        Field("Box", $"{row.BoxSeq} / {Math.Max(row.BoxCount, 1)}", 290, 368, 245);
        Rule(411);
        Field("Delivery No", row.DeliveryNo, 22, 434, 245);
        Field("PO No", row.PoNo, 290, 434, 245);
        Rule(477);
        Field("Vendor ID", row.VendorId, 22, 500, 245);
        Field("Production Date", row.ProductionDate?.ToString("yyyy-MM-dd") ?? "", 290, 500, 245);
        Rule(543);
        Field("Destination", row.Destination, 22, 565, 516);

        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        return stream.ToArray();
#else
        throw new PlatformNotSupportedException("M220 label rendering is available on Android.");
#endif
    }

    public async Task PrintAsync(string address, byte[] png, CancellationToken cancellationToken = default)
    {
#if ANDROID
        if (!SparePartPrinter.IsValidAddress(address)) throw new InvalidOperationException("Select a paired M220 printer.");
        if (await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<SparePartPrinter.PrinterBluetoothPermission>()) != PermissionStatus.Granted)
            throw new InvalidOperationException("Allow Nearby devices permission.");
        using var bitmap = BitmapFactory.DecodeByteArray(png, 0, png.Length)
            ?? throw new InvalidOperationException("Label image could not be decoded.");
        if (bitmap.Width != Width || bitmap.Height != Height) throw new InvalidOperationException("Label size is not 70 × 80 mm.");
        var raster = new byte[(Width / 8) * Height];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var pixel = new Color(bitmap.GetPixel(x, y));
                if (pixel.R < 128 && pixel.G < 128 && pixel.B < 128)
                    raster[y * (Width / 8) + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        var manager = (BluetoothManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.BluetoothService);
        var adapter = manager?.Adapter ?? throw new InvalidOperationException("Bluetooth is unavailable.");
        if (!adapter.IsEnabled) throw new InvalidOperationException("Turn on Bluetooth.");
        if (adapter.IsDiscovering) adapter.CancelDiscovery();
        using var device = adapter.GetRemoteDevice(address);
        using var uuid = Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;
        using var socket = device?.CreateRfcommSocketToServiceRecord(uuid)
            ?? throw new InvalidOperationException("M220 Bluetooth serial service is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var cancel = timeout.Token.Register(() => { try { socket.Close(); } catch { } });
        await Task.Run(socket.Connect, timeout.Token);
        var output = socket.OutputStream ?? throw new InvalidOperationException("M220 output stream is unavailable.");
        // M110/M220 raster protocol: speed, density, gap labels, GS v 0 bitmap, status footer.
        await output.WriteAsync(new byte[] { 0x1b, 0x4e, 0x0d, 0x05, 0x1b, 0x4e, 0x04, 0x0a, 0x1f, 0x11, 0x0a }, timeout.Token);
        await output.WriteAsync(new byte[] { 0x1d, 0x76, 0x30, 0x00, 70, 0, 128, 2 }, timeout.Token);
        for (var offset = 0; offset < raster.Length; offset += 128)
        {
            await output.WriteAsync(raster.AsMemory(offset, Math.Min(128, raster.Length - offset)), timeout.Token);
            await Task.Delay(20, timeout.Token);
        }
        await output.WriteAsync(new byte[] { 0x1f, 0xf0, 0x05, 0x00, 0x1f, 0xf0, 0x03, 0x00 }, timeout.Token);
        await output.FlushAsync(timeout.Token);
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("M220 printing is available on Android.");
#endif
        // Do not auto-retry: a timed-out write may already have printed.
    }

#if ANDROID
    private static void DrawCode128(Canvas canvas, Paint ink, string value, float x, float y, float width, float height)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => c < 32 || c > 126))
            throw new InvalidOperationException("The box barcode is not valid Code128 text.");
        var codes = new List<int> { 104 };
        var checksum = 104;
        for (var i = 0; i < value.Length; i++) { var code = value[i] - 32; codes.Add(code); checksum += code * (i + 1); }
        codes.Add(checksum % 103);
        codes.Add(106);
        var modules = codes.Sum(code => Patterns[code].Sum(c => c - '0')) + 20;
        var scale = width / modules;
        var pos = x + 10 * scale;
        foreach (var code in codes)
        {
            var black = true;
            foreach (var digit in Patterns[code])
            {
                var w = (digit - '0') * scale;
                if (black) canvas.DrawRect(pos, y, pos + w, y + height, ink);
                pos += w;
                black = !black;
            }
        }
    }

    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];
#endif
}
