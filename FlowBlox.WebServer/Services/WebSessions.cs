using System.Collections.Concurrent;
using System.Security.Claims;

namespace FlowBlox.WebServer.Services;

/// <summary>Revokes open circuits on logout and invalidates all previous sessions after a configuration restart.</summary>
public sealed class WebSessions(FlowBlox.Core.Models.WebServer.WebServerConfiguration configuration)
{
    public const string ClaimName = "flowblox-session";
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();

    private readonly string _passwordHash = string.IsNullOrEmpty(configuration.AppPassword)
        ? string.Empty : FlowBlox.Core.Authentication.WebServerPasswordHasher.Hash(configuration.AppPassword);
    public bool PasswordRequired => _passwordHash.Length > 0;
    public bool VerifyPassword(string password) => !PasswordRequired || FlowBlox.Core.Authentication.WebServerPasswordHasher.Verify(password, _passwordHash);

    public string Create(DateTimeOffset expires)
    {
        foreach (var session in _sessions.Where(pair => pair.Value <= DateTimeOffset.UtcNow))
            _sessions.TryRemove(session.Key, out _);
        var id = Guid.NewGuid().ToString("N");
        _sessions[id] = expires;
        return id;
    }

    public bool IsValid(ClaimsPrincipal principal) => principal.Identity?.IsAuthenticated == true
        && (!PasswordRequired || (principal.FindFirstValue(ClaimName) is { } id
        && _sessions.TryGetValue(id, out var expires) && expires > DateTimeOffset.UtcNow));

    public void Remove(ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue(ClaimName) is { } id)
            _sessions.TryRemove(id, out _);
    }
}
