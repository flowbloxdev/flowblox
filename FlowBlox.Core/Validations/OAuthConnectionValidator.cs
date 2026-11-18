using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.FlowBlocks.Web.InternalWebRequest;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Validations;

public static class OAuthConnectionValidator
{
    public static ValidationResult? Validate(object? value, ValidationContext context)
    {
        if (value == null)
            return ValidationResult.Success;

        var bearer = string.Empty;
        var userName = string.Empty;
        var password = string.Empty;
        IEnumerable<string> headers = Enumerable.Empty<string>();
        if (context.ObjectInstance is WebRequestFlowBlock web)
        {
            userName = web.UserName;
            password = web.Password;
            headers = web.HeaderParameters.Select(header => header.Key);
        }
        else if (context.ObjectInstance is OpenApiRestFlowBlock rest)
        {
            bearer = rest.BearerToken;
            userName = rest.UserName;
            password = rest.Password;
            headers = rest.HeaderParameters.Select(header => header.Name);
        }
        else if (context.ObjectInstance is ConfigurableWebRequest request)
        {
            userName = request.UserName;
            password = request.Password;
            headers = request.Headers?.Keys ?? Enumerable.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(bearer) && string.IsNullOrWhiteSpace(userName) && string.IsNullOrWhiteSpace(password)
            && !headers.Any(header => string.Equals(header?.Trim(), "Authorization", StringComparison.OrdinalIgnoreCase)))
            return ValidationResult.Success;

        return new ValidationResult(
            FlowBloxResourceUtil.GetLocalizedString("OAuthConnectionValidator_ConflictingAuthentication", typeof(FlowBloxTexts)),
            [context.MemberName ?? "OAuthConnection"]);
    }
}
