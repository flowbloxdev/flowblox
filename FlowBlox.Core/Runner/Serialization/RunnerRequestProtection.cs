using FlowBlox.Core.Authentication;
using FlowBlox.Core.Runner.Contracts;

namespace FlowBlox.Core.Runner.Serialization;

/// <summary>
/// Protects only password-marked input values; the rest of the request stays readable for debugging.
/// </summary>
public static class RunnerRequestProtection
{
    private const string Prefix = "flowblox-dpapi-v1:";

    public static RunnerRequest ForStorage(RunnerRequest request)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<RunnerRequest>(RunnerJson.Serialize(request))!;
        foreach (var name in copy.PasswordFieldNames ?? Enumerable.Empty<string>())
        {
            var key = copy.UserFields.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
            if (key != null && !string.IsNullOrEmpty(copy.UserFields[key]))
                copy.UserFields[key] = Prefix + FlowBloxSecureStorageManager.SetProtectedData(
                    "ProjectExecution.Input." + name.ToUpperInvariant(), copy.UserFields[key]);
        }
        return copy;
    }

    public static RunnerRequest FromStorage(RunnerRequest request)
    {
        foreach (var name in request.PasswordFieldNames ?? Enumerable.Empty<string>())
        {
            var key = request.UserFields.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
            if (key != null && request.UserFields[key]?.StartsWith(Prefix, StringComparison.Ordinal) == true)
                request.UserFields[key] = FlowBloxSecureStorageManager.GetProtectedData(
                    "ProjectExecution.Input." + name.ToUpperInvariant(), request.UserFields[key][Prefix.Length..]);
        }
        return request;
    }
}
