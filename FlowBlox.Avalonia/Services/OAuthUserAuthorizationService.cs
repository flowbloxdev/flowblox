using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using FlowBlox.Core.Authentication.OAuth;
using FlowBlox.Core.Interfaces;

namespace FlowBlox.AvaloniaDesktop.Services;

public sealed class OAuthUserAuthorizationService : IOAuthUserAuthorizationService
{
    public async Task<bool> AuthorizeAsync(OAuthConnectionSettings settings, CancellationToken cancellationToken)
    {
        if (!Environment.UserInteractive)
            return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var session = new OAuthAuthorizationSession(settings);
        try
        {
            Process.Start(new ProcessStartInfo(session.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });
            var code = await session.WaitForCodeAsync(timeout.Token).ConfigureAwait(false);
            await new OAuthTokenService().CompleteAuthorizationAsync(
                settings, code, session.CodeVerifier, session.RedirectUri, settings.ClientSecret, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OAuthInteractionRequiredException();
        }
    }
}
