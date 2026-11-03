using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaBarcodeInputStyleTests
{
    private static readonly Regex Inputs = new("""<(?:RadzenTextBox|input)\b(?:[^>"']|"[^"]*"|'[^']*')*>""", RegexOptions.Singleline);

    [Fact]
    public void Every_scan_input_uses_the_shared_placeholder_and_presentation()
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(PathFor("src/05_Pda/AMES.Pda/Components/Pages"), "*.razor", SearchOption.AllDirectories))
        {
            var markup = Regex.Replace(File.ReadAllText(file), @"@\*[\s\S]*?\*@", "");
            foreach (Match input in Inputs.Matches(markup))
            {
                var tag = input.Value;
                if (!tag.Contains("pda-scan-input") && !tag.Contains("pda-barcode-input")) continue;
                Assert.Contains("pda-barcode-input", tag);
                Assert.Contains("placeholder=\"Scan Barcode\"", tag, StringComparison.OrdinalIgnoreCase);
                count++;
            }
        }
        Assert.True(count >= 20, $"Expected the WH/FG/SP scan fields and manual barcode searches; found {count}.");
    }

    [Fact]
    public void Scan_presentation_matches_outbound_and_placeholder_inherits_the_same_font()
    {
        var css = File.ReadAllText(PathFor("src/05_Pda/AMES.Pda/wwwroot/css/pda.css"));
        var shared = css[css.IndexOf("/* Shared barcode presentation follows Outbound", StringComparison.Ordinal)..];
        foreach (var declaration in new[] {
            "height: 44px !important", "padding: 0 12px !important", "font-family: 'Inter', sans-serif !important",
            "font-size: 14px !important", "font-weight: 700 !important", "letter-spacing: 0 !important", "text-align: left !important" })
            Assert.Contains(declaration, shared);
        Assert.Contains("input.pda-barcode-input.rz-textbox", shared);
        var placeholder = shared[shared.IndexOf("::placeholder", StringComparison.Ordinal)..];
        Assert.Contains("font-family: inherit !important", placeholder);
        Assert.Contains("font-size: inherit !important", placeholder);
        Assert.Contains("text-align: left !important", placeholder);
        Assert.DoesNotContain("text-align: center", shared);
    }

    [Theory]
    [InlineData("Sp/SpMaster.razor")]
    [InlineData("Wh/WhLabelPrint.razor")]
    [InlineData("Wh/Wh08TransactionHistory.razor")]
    public void Manual_barcode_search_stays_keyboard_editable(string page)
    {
        var source = File.ReadAllText(PathFor("src/05_Pda/AMES.Pda/Components/Pages/" + page));
        var search = Assert.Single(Inputs.Matches(source).Cast<Match>(), m => m.Value.Contains("pda-barcode-input"));
        Assert.DoesNotContain("pda-scan-input", search.Value);
        Assert.DoesNotContain("readonly", search.Value, StringComparison.OrdinalIgnoreCase);
        var script = File.ReadAllText(PathFor("src/05_Pda/AMES.Pda/wwwroot/js/pda-scan.js"));
        Assert.Contains("scanInputSelector: \".pda-scan-input\"", script);
        Assert.DoesNotContain("pda-barcode-input", script);
    }

    [Fact]
    public void Maintenance_notes_printer_settings_and_quantity_fields_are_not_barcode_inputs()
    {
        var maintenance = File.ReadAllText(PathFor("src/05_Pda/AMES.Pda/Components/Pages/Maintenance.razor"));
        Assert.DoesNotContain("pda-barcode-input", maintenance);
        Assert.Contains("placeholder=\"Observation or failed-check reason\"", maintenance);
        var inventory = File.ReadAllText(PathFor("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor"));
        var quantity = Assert.Single(Inputs.Matches(inventory).Cast<Match>(), m => m.Value.Contains("wh03-qty-input"));
        Assert.DoesNotContain("pda-barcode-input", quantity.Value);
        foreach (var page in new[] { "Sp/SpMaster.razor", "Wh/WhLabelPrint.razor" })
        {
            var source = File.ReadAllText(PathFor("src/05_Pda/AMES.Pda/Components/Pages/" + page));
            foreach (var printer in Inputs.Matches(source).Cast<Match>().Where(m => m.Value.Contains("-printer-")))
                Assert.DoesNotContain("pda-barcode-input", printer.Value);
        }
    }

    private static string PathFor(string path, [CallerFilePath] string current = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path));
}
