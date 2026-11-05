using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class PickingCancellationTests
{
    [Theory]
    [InlineData("", "TEST")]
    [InlineData("12345678901234567890123456789012345678901", "TEST")]
    [InlineData("PICK", "")]
    [InlineData("PICK", "123456789012345678901")]
    public void Invalid_input_is_rejected_before_connecting(string slip, string actor)
    {
        var repository = new WarehouseRepository(new AmesConnectionFactory("Server=invalid;Database=invalid;Integrated Security=true;Connect Timeout=1"));
        Assert.Throws<ArgumentException>(() => repository.CancelPickingSlip(slip, actor));
    }

    [Fact]
    public void Cancel_is_confirmed_permission_checked_and_does_not_regenerate_inside_the_cancel_handler()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        Assert.Contains("Click=\"CancelSelection\"",page);
        var cancel=page.Split("async Task CancelSelection()")[1].Split("void SelectOrder")[0];
        Assert.Contains("Dialog.Confirm",cancel);
        Assert.Contains("Permissions.RecheckEditAsync(Auth, \"wh/picking-orders\")",cancel);
        Assert.Contains("ActorCode.Of",cancel);
        Assert.Contains("WH.CancelPickingSlip(slip, actor)",cancel);
        Assert.Contains("Load();",cancel);
        Assert.DoesNotContain("Generate",cancel);
        Assert.True(cancel.IndexOf("RecheckEditAsync",StringComparison.Ordinal)<cancel.IndexOf("WH.CancelPickingSlip",StringComparison.Ordinal));
        var source=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.PickingCancellation.cs");
        Assert.Contains("LockLinePicking(connection, transaction)",source);
        Assert.Contains("WITH(UPDLOCK,HOLDLOCK)",source);
        Assert.Contains("COALESCE(DemandQty,0)>COALESCE(PickedQty,0)",source);
        Assert.Contains("Status='Cancelled'",source);
        Assert.Contains("SYS_AuditLog",source);
        Assert.DoesNotContain("UPDATE dbo.WH_Inventory",source);
        Assert.DoesNotContain("DELETE",source);
        var list=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs").Split("public List<PickingSlipHeaderRow> ListPickingSlipHeaders")[1];
        Assert.Contains("WHEN G.CLOSED_LINES = G.LINE_COUNT AND G.CANCELLED_LINES > 0 THEN N'Cancelled'",list);
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string file="")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!,"../../..",path));
}
