using Xunit;

namespace AMES.Pop.Tests;

public class SmokeTest
{
    [Fact]
    public void Test_project_references_pop_assembly()
    {
        var asm = typeof(AMES.Pop.Services.AppState).Assembly;
        Assert.Equal("AMES.Pop", asm.GetName().Name);
    }

    [Fact]
    public void Test_project_can_access_internal_pop_types()
    {
        // AMES.Pop.Common.PopServices is `internal static class`. Successfully
        // referencing it here proves InternalsVisibleTo is wired correctly,
        // which Task 4's internal LabelDispatcher tests will depend on.
        var type = typeof(AMES.Pop.Common.PopServices);
        Assert.Equal("AMES.Pop", type.Assembly.GetName().Name);
        Assert.True(type.IsClass);
    }
}
