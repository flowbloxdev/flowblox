using FlowBlox.Core.Authentication.OAuth;

namespace FlowBlox.Core.Interfaces;

public interface IOAuthUserAuthorizationService
{
    Task<bool> AuthorizeAsync(OAuthConnectionSettings settings, CancellationToken cancellationToken);
}
