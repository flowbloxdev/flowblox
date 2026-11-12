using FlowBlox.Core.Authentication;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.Logging;

namespace FlowBlox.UICore.Utilities;

public sealed class ProjectSpaceUserNameCache
{
    private readonly Dictionary<(string Endpoint, int UserId), string> _names = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string?> GetDisplayNameAsync(string endpoint, int userId)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || userId <= 0)
            return null;

        var normalizedEndpoint = FlowBloxApiUrlNomalizer.Normalize(endpoint);
        var key = (normalizedEndpoint, userId);
        await _gate.WaitAsync();
        try
        {
            if (_names.TryGetValue(key, out var name))
                return name;

            var service = new FlowBloxWebApiService(normalizedEndpoint);
            var response = await service.GetUserInfoAsync(id: userId).WaitAsync(TimeSpan.FromSeconds(10));
            var user = response.ResultObject;
            if (!response.Success || user == null || string.IsNullOrWhiteSpace(user.UserName))
                return null;

            name = user.UserName.Trim();
            _names[key] = name;
            return name;
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Warn("Failed to resolve a Project Space project owner.", exception);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
