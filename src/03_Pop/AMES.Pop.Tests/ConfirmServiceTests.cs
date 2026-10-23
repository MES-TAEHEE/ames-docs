using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class ConfirmServiceTests
{
    [Fact]
    public async Task Cancel_closes_a_pending_prompt_as_declined()
    {
        var c = new ConfirmService();
        var closed = 0;
        c.OnClosed += () => closed++;
        var ask = c.AskAsync("t", "b");
        c.Cancel();
        Assert.False(await ask);
        Assert.Null(c.Pending);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task A_second_prompt_declines_the_first_instead_of_orphaning_it()
    {
        var c = new ConfirmService();
        var first = c.AskAsync("a", "a");
        var second = c.AskAsync("b", "b");
        Assert.False(await first);
        c.Resolve(true);
        Assert.True(await second);
    }
}
