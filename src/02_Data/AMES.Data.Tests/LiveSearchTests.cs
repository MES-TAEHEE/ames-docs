using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class LiveSearchTests
{
    [Fact]
    public async Task Typing_a_burst_performs_one_lookup_with_the_latest_text()
    {
        var text="";
        var results=new List<string>();
        using var search=new LiveSearch(()=>{results.Add(text);return Task.CompletedTask;},TimeSpan.FromMilliseconds(20));
        var pending=new List<Task>();
        foreach(var value in new[]{"P","PA","PART","PART-1"}) { text=value;pending.Add(search.RequestAsync()); }
        await Task.WhenAll(pending);
        Assert.Equal(new[]{"PART-1"},results);
    }

    [Fact]
    public async Task Typing_during_a_lookup_repeats_with_latest_text_without_overlapping_queries()
    {
        var release=new TaskCompletionSource();
        var results=new List<string>();
        var text="first";
        var active=0;
        using var search=new LiveSearch(async()=>
        {
            Assert.Equal(1,++active);
            results.Add(text);
            if(results.Count==1)await release.Task;
            --active;
        },TimeSpan.Zero);
        var first=search.RequestAsync(immediate:true);
        text="second";await search.RequestAsync(immediate:true);
        text="last";await search.RequestAsync(immediate:true);
        release.SetResult();await first;
        Assert.Equal(new[]{"first","last"},results);
    }

    [Fact]
    public async Task Enter_runs_immediately_without_a_duplicate_delayed_lookup()
    {
        var calls=0;
        using var search=new LiveSearch(()=>{++calls;return Task.CompletedTask;},TimeSpan.FromMilliseconds(20));
        var typing=search.RequestAsync();
        await search.RequestAsync(immediate:true);
        await typing;
        Assert.Equal(1,calls);
    }

    [Fact]
    public async Task Leaving_the_page_cancels_pending_search()
    {
        var calls=0;
        var search=new LiveSearch(()=>{++calls;return Task.CompletedTask;});
        var typing=search.RequestAsync();
        search.Dispose();await typing;
        await search.RequestAsync(immediate:true);
        Assert.Equal(0,calls);
    }
}
