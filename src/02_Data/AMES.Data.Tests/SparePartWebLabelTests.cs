using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class SparePartWebLabelTests
{
    [Fact]
    public void Web_and_PDA_use_the_same_serial_allocator_and_only_differ_in_history_source()
    {
        var pda = PdaSparePartsSql.SerialCreate;
        var web = PdaSparePartsSql.WebSerialCreate;
        const string options = "SET QUOTED_IDENTIFIER ON;\nSET ANSI_NULLS ON;\n";
        Assert.StartsWith(options + "DECLARE @LabelRefType varchar(20)='PDA_LABEL';", pda);
        Assert.StartsWith(options + "DECLARE @LabelRefType varchar(20)='WEB_LABEL';", web);
        Assert.Equal(pda.Replace("'PDA_LABEL'", "'WEB_LABEL'"), web);
        Assert.Equal(1, web.Split("SET QUOTED_IDENTIFIER ON;").Length - 1);
        Assert.Equal(1, web.Split("SET ANSI_NULLS ON;").Length - 1);
        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)", web);
        Assert.Contains("'LABEL',0,@Balance,@Balance,@LabelRefType", web);
        Assert.Contains("'CREATED'", web);
        Assert.Contains("COMMIT;", web);
        Assert.DoesNotContain("UPDATE dbo.MD_SparePart", web);
    }

    [Theory]
    [InlineData("", "user")]
    [InlineData(" ", "user")]
    [InlineData("EOS-SP-A1-260001", "")]
    public void Creation_rejects_missing_identity_before_opening_the_database(string part, string actor)
    {
        var repo = new MntRepository(new AmesConnectionFactory("Server=invalid;Database=unused;Integrated Security=true"));
        Assert.Throws<ArgumentException>(() => repo.CreateSparePartLabel(part, actor));
    }

    [Fact]
    public void Label_list_is_scoped_to_the_selected_active_master_and_preserves_serial_locations()
    {
        var sql = MntRepository.SparePartLabelsSql;
        Assert.Contains("P.SparePartNo=@Part", sql);
        Assert.Contains("COALESCE(P.ActiveFlag,1)=1", sql);
        Assert.Contains("COALESCE(I.LocationID", sql);
        Assert.Contains("P.ExtraLocation", sql);
        Assert.Contains("V.VendorName", sql);
        foreach (var write in new[] { "INSERT ", "UPDATE ", "DELETE ", "EXEC " })
            Assert.DoesNotContain(write, sql);
    }

    [Fact]
    public void Detail_print_uses_the_existing_label_dialog_and_creation_rechecks_master_edit_permission()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Mnt/SpareParts.razor");
        Assert.Contains("Click=\"OpenPartLabels\"", page);
        Assert.Contains("Labels=\"@_partLabels\"", page);
        var create = page[page.IndexOf("async Task CreatePartLabel()", StringComparison.Ordinal)..];
        var permission = create.IndexOf("RecheckEditAsync(AuthPrvd, \"md/re/spare-part\")", StringComparison.Ordinal);
        var allocate = create.IndexOf("Mnt.CreateSparePartLabel(partNo, _actor)", StringComparison.Ordinal);
        Assert.True(permission >= 0 && allocate > permission);
        Assert.Contains("Audit.Denied", create[..allocate]);
        Assert.Contains("_labelCreateBlocked = true;", create[..allocate]);
        Assert.Contains("_labelSerial = created;", create);
        Assert.DoesNotContain("InvokeVoidAsync(\"window.print\")", create);
        var dialog = Read("src/06_Web/AMES.Web/Components/Shared/SparePartLabelDialog.razor");
        Assert.Contains("RadzenQRCode Value=\"@label.SerialNo\"", dialog);
        Assert.Contains("Labels.FirstOrDefault(row => row.SerialNo == SerialNo)", dialog);
        Assert.Contains("Busy || CurrentLabel is null", dialog);
        Assert.Contains("class=\"spare-label-paper\"", dialog);
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
