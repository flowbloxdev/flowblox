using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Authentication.OAuth;

public sealed class OAuthInteractionRequiredException : ValidationException
{
    public OAuthInteractionRequiredException() : base(
        "OAuth user sign-in is required. Authorize this managed object in a browser-capable FlowBlox desktop session "
        + "and retry. Background execution can reuse or refresh locally stored credentials, but cannot ask the user "
        + "to sign in without an interactive authorization service.")
    {
    }
}
