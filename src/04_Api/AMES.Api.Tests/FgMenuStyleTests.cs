using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgMenuStyleTests
{
    [Fact]
    public void Finished_goods_menu_reuses_five_distinct_shared_warehouse_accents()
    {
        var css = Read("src/05_Pda/AMES.Pda/wwwroot/css/pda.css");
        var page = Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/FgHome.razor");
        var accents = Regex.Matches(page, "class=\"pda-tile sm wh-home-tile ([a-z-]+)\"")
            .Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(5, accents.Length);
        Assert.Equal(5, accents.Distinct().Count());
        var colors = accents.Select(accent => Regex.Match(css,
            @"\.pda-shell\.wh\.wh-home-shell \.wh-home-tile\." + accent
            + @" \{ border-top-color: (#[0-9a-f]+) !important; \}").Groups[1].Value).ToArray();
        Assert.All(colors, color => Assert.NotEmpty(color));
        Assert.Equal(5, colors.Distinct().Count());
        Assert.DoesNotContain(".pda-shell.wh.wh-home-shell.fg-home-shell .wh-home-tile {", css);
    }

    [Fact]
    public void All_five_finished_goods_menu_actions_keep_their_existing_routes()
    {
        var page = Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/FgHome.razor");
        foreach (var route in new[] { "/fg/01", "/fg/04", "/fg/06", "/fg/pallet-return", "/fg/05" })
            Assert.Contains($"Nav.NavigateTo(\"{route}\")", page);
        Assert.Equal(5, Regex.Matches(page, "<button class=\"pda-tile").Count);
    }

    static string Read(string path, [CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path)));
}
