using System.Runtime.CompilerServices;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaScannerIntegrationTests
{
    [Theory]
    [InlineData("Honeywell", "Honeywell", true)]
    [InlineData("HONEYWELL INTERNATIONAL INC.", "CT45P", true)]
    [InlineData("OEM", "honeywell", true)]
    [InlineData("Zebra Technologies", "Zebra", false)]
    [InlineData("Symbol", "Zebra", false)]
    [InlineData("Datalogic", "Datalogic", false)]
    [InlineData("Urovo", "Urovo", false)]
    [InlineData("Newland", "Newland", false)]
    [InlineData("Point Mobile", "Point Mobile", false)]
    [InlineData(null, null, false)]
    public void Non_honeywell_devices_use_common_input_without_claiming_honeywell_scanners(
        string? manufacturer, string? brand, bool expected)
        => Assert.Equal(expected, PdaScannerIntegration.UsesHoneywellIntents(manufacturer, brand));

    [Fact]
    public void Honeywell_receiver_is_conditional_and_keeps_the_sender_permission_check()
    {
        var code = Read("src/05_Pda/AMES.Pda/Platforms/Android/MainActivity.cs");
        Assert.Contains("PdaScannerIntegration.UsesHoneywellIntents(Build.Manufacturer, Build.Brand)", code);
        Assert.Matches(@"if \(UsesHoneywellScanner\)\s*\{\s*RegisterBarcodeBroadcastReceiver\(\);\s*ClaimHoneywellScanner\(\);", code);
        Assert.Matches(@"if \(UsesHoneywellScanner\)\s*ReleaseHoneywellScanner\(\);", code);
        Assert.Contains("RegisterReceiver(_barcodeReceiver, filter, HoneywellDecodePermission, null, ReceiverFlags.Exported)", code);
        Assert.Contains("RegisterReceiver(_barcodeReceiver, filter, HoneywellDecodePermission, null)", code);
        Assert.Contains("intent?.Action != ClaimedBarcodeAction", code);
        Assert.DoesNotContain("DispatchKeyEvent", code);
    }

    static string Read(string path, [CallerFilePath] string source = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../..", path)));
}
