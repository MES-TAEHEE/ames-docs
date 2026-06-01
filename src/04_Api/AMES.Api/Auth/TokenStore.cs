using System.Collections.Concurrent;
using AMES.Contracts.Dto;

namespace AMES.Api.Auth;

/// <summary>
/// Process-wide bearer-token registry. Each successful /auth/login creates
/// a row whose token is the bearer the PDA sends back on subsequent calls.
/// Tokens live until ExpiresAt elapses or /auth/logout removes them.
/// Singleton — registered in Program.cs.
/// </summary>
public sealed class TokenStore
{
    private readonly ConcurrentDictionary<string, (PopSessionDto Session, DateTime Expires)> _map = new();

    public string Issue(PopSessionDto session)
    {
        var token = Guid.NewGuid().ToString("N");
        _map[token] = (session, session.ExpiresAt);
        return token;
    }

    public PopSessionDto? Resolve(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_map.TryGetValue(token, out var entry)) return null;
        if (entry.Expires < DateTime.UtcNow) { _map.TryRemove(token, out _); return null; }
        return entry.Session;
    }

    public void Revoke(string? token)
    {
        if (!string.IsNullOrEmpty(token)) _map.TryRemove(token, out _);
    }
}
