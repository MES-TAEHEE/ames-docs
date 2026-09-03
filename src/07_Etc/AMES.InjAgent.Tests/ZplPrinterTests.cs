using AMES.Devices;
using Xunit;

namespace AMES.InjAgent.Tests;

public class ZplPrinterTests
{
    [Fact]
    public void Spooler_mode_without_printer_name_throws()
    {
        var printer = new ZplPrinter(new ZplPrinterOptions { Mode = "Spooler" });
        var ex = Assert.Throws<InvalidOperationException>(() => printer.Print("^XA^XZ", "LOT-X"));
        Assert.Contains("PrinterName", ex.Message);
    }

    [Fact]
    public void Spooler_mode_with_unknown_printer_throws_with_name_in_message()
    {
        var printer = new ZplPrinter(new ZplPrinterOptions
        {
            Mode        = "Spooler",
            PrinterName = "AMES-NO-SUCH-PRINTER-7f3a",
        });
        var ex = Assert.Throws<InvalidOperationException>(() => printer.Print("^XA^XZ", "LOT-X"));
        Assert.Contains("AMES-NO-SUCH-PRINTER-7f3a", ex.Message);
    }

    [Fact]
    public void Spooler_mode_is_case_insensitive_and_does_not_fall_back_to_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ames-spooler-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var printer = new ZplPrinter(new ZplPrinterOptions { Mode = "spooler", OutputDir = dir });
            Assert.Throws<InvalidOperationException>(() => printer.Print("^XA^XZ", "LOT-X"));
            Assert.False(Directory.Exists(dir)); // File 모드로 새지 않아야 한다
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
