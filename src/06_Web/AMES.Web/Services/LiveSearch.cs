namespace AMES.Web.Services;

/// <summary>Coalesce typing and serialize lookups so the latest query is not lost while a lookup is running.</summary>
public sealed class LiveSearch(Func<Task> search,TimeSpan? delay=null) : IDisposable
{
    readonly CancellationTokenSource _lifetime=new();
    readonly TimeSpan _delay=delay??TimeSpan.FromMilliseconds(250);
    int _revision;
    bool _running,_queued,_disposed;

    public async Task RequestAsync(bool immediate=false)
    {
        if(_disposed)return;
        var revision=++_revision;
        try { if(!immediate)await Task.Delay(_delay,_lifetime.Token); }
        catch(OperationCanceledException) when(_disposed) { return; }
        if(_disposed||revision!=_revision)return;
        _queued=true;
        if(_running)return;
        _running=true;
        try
        {
            while(_queued&&!_disposed)
            {
                _queued=false;
                await search();
            }
        }
        finally { _running=false; }
    }

    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
