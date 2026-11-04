using System.Runtime.CompilerServices;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PmChecklistTemplateShortcutTests
{
    [Fact]
    public void Management_button_is_next_to_the_checklist_picker_and_uses_standard_controls()
    {
        var source = Board();
        var picker = source.IndexOf("Name=\"PmDropDownChecklist\"", StringComparison.Ordinal);
        var nextField = source.IndexOf("Component=\"PmDropDownTech\"", picker, StringComparison.Ordinal);
        var row = source[picker..nextField];
        Assert.Contains("<RadzenButton Text=\"@L[\"MD.PmTemplate.Title\"]\"", row);
        Assert.Contains("Icon=\"open_in_new\"", row);
        Assert.Contains("ButtonStyle=\"ButtonStyle.Base\" Size=\"ButtonSize.Small\"", row);
        Assert.Contains("Click=\"@OpenTemplateManagement\"", row);
        Assert.Contains("!PermSvc.IsVisible(\"md/rp/pm-template\")", row);
        Assert.Contains("class=\"ames-rz-btn\"", row);
        Assert.Contains("if (!PermSvc.IsVisible(\"md/rp/pm-template\")) return;", source);
        Assert.Contains("JS.InvokeVoidAsync(\"open\", Nav.ToAbsoluteUri(\"md/rp/pm-template\").ToString(), \"_blank\", \"noopener,noreferrer\")", source);
    }

    [Fact]
    public void Returning_from_management_refreshes_templates_without_clearing_the_schedule_form()
    {
        var source = Board();
        Assert.Contains("Open=\"@RefreshChecklistTemplates\"", source);
        Assert.Contains("_refreshChecklistTemplates = true;", source);
        var start = source.IndexOf("async Task RefreshChecklistTemplates()", StringComparison.Ordinal);
        var end = source.IndexOf("void OnPmEquipChanged", start, StringComparison.Ordinal);
        var handler = source[start..end];
        Assert.Contains("if (!_refreshChecklistTemplates) return;", handler);
        Assert.Contains("_templates = await Task.Run(MD.ListPmTemplates);", handler);
        Assert.Contains("_refreshChecklistTemplates = false;", handler);
        Assert.DoesNotContain("_f =", handler);
        Assert.DoesNotContain("CloseModal", handler);
        Assert.Contains("t.ActiveFlag && MntRepository.TemplateMatchesEquipment(t.EquipType, SelectedEquipType)", source);
    }

    [Theory]
    [InlineData("EquipPmSchedule", "EQUIP")]
    [InlineData("MaintPmSchedule", "MAINT")]
    public void Both_schedule_screens_use_the_shared_board(string page, string pmClass)
        => Assert.Contains($"<PmScheduleBoard PmClass=\"{pmClass}\"", Read($"src/06_Web/AMES.Web/Components/Pages/Mnt/{page}.razor"));

    static string Board() => Read("src/06_Web/AMES.Web/Components/Pages/Mnt/PmScheduleBoard.razor");
    static string Read(string path, [CallerFilePath] string source = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../..", path)));
}
