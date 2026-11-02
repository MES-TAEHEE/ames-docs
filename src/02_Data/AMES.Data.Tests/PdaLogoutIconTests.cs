using System.Xml.Linq;
using Xunit;

namespace AMES.Data.Tests;

public sealed class PdaLogoutIconTests
{
    [Fact]
    public void Logout_uses_an_inline_vector_without_font_or_image_dependencies()
    {
        var page = Read("src/05_Pda/AMES.Pda/Components/Pages/Home.razor");
        var markup = "<button" + page.Split("<button")[1].Split("</button>")[0] + "</button>";
        var button = XElement.Parse(markup.Replace("@onclick=\"Logout\"", ""));
        Assert.Equal("pda-tbar-out", (string?)button.Attribute("class"));
        Assert.Equal("Sign out", (string?)button.Attribute("aria-label"));
        var svg = Assert.Single(button.Elements("svg"));
        Assert.Equal("0 0 24 24", (string?)svg.Attribute("viewBox"));
        Assert.Equal("currentColor", (string?)svg.Attribute("stroke"));
        Assert.Equal("true", (string?)svg.Attribute("aria-hidden"));
        Assert.NotEmpty(svg.Elements("path"));
        Assert.Empty(svg.Descendants("image"));
        Assert.DoesNotContain("⏻", button.Value);
        Assert.Contains("@onclick=\"Logout\"", page);
        Assert.Contains("await Api.LogoutAsync()", page);
        Assert.Contains("Auth.SignOut()", page);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
