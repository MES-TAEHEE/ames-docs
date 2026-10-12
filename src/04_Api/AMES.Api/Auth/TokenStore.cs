using System.Collections.Concurrent;
using System.Diagnostics;
using AMES.Contracts.Dto;

namespace AMES.Api.Auth;

/// <summary>
/// Process-wide bearer-token registry. Each successful /auth/login creates
/// a row whose token is the bearer the PDA sends back on subsequent calls.
/// Tokens live until ExpiresAt elapses or /auth/logout removes them.
/// Singleton — registered in Program.cs.
/// </summary>
public sealed class TokenStore(Func<DateTime> databaseNow, Func<long>? timestamp = null, long? frequency = null,
    SqlTokenPersistence? persistence = null)
{
    private readonly ConcurrentDictionary<string, (PopSessionDto Session, DateTime Expires)> _map = new();
    private readonly Func<long> _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    private readonly long _frequency = frequency ?? Stopwatch.Frequency;
    private readonly object _clockGate = new();
    private DateTime _clockAt;
    private long _tickAt;
    private bool _clockLoaded;
    private readonly SqlTokenPersistence? _persistence = persistence;

    private DateTime Now()
    {
        lock (_clockGate)
        {
            var tick = _timestamp();
            var elapsed = _clockLoaded ? (double)(tick - _tickAt) / _frequency : double.MaxValue;
            if (!_clockLoaded || elapsed < 0 || elapsed >= 60)
            {
                _clockAt = databaseNow();
                _tickAt = tick;
                _clockLoaded = true;
                return _clockAt;
            }
            return _clockAt.AddSeconds(elapsed);
        }
    }

    public string Issue(PopSessionDto session)
    {
        var token = Guid.NewGuid().ToString("N");
        _persistence?.Save(token, session);
        _map[token] = (session, session.ExpiresAt);
        return token;
    }

    public PopSessionDto? Resolve(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        if (!_map.TryGetValue(token, out var entry))
        {
            var restored = _persistence?.Load(token);
            if (restored is null) return null;
            entry = (restored, restored.ExpiresAt);
            _map.TryAdd(token, entry);
        }
        if (entry.Expires <= Now()) { _map.TryRemove(token, out _); return null; }
        return entry.Session;
    }

    public void Revoke(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _persistence?.Revoke(token);
            _map.TryRemove(token, out _);
        }
    }

    public int PruneExpired()
    {
        var now = Now();
        var removed = 0;
        foreach (var (token, entry) in _map)
            if (entry.Expires <= now && _map.TryRemove(token, out _)) removed++;
        return removed;
    }
}
