using FlowBlox.Core.ExternalServices.FlowBloxWebApi;

namespace FlowBloxTest.Models.Project;

internal sealed class ProjectSpaceCacheWebApi : FlowBloxWebApiService, IDisposable
{
    internal ProjectSpaceCacheWebApi(ProjectSpaceCacheHttpHandler handler, string endpoint = "https://cache.flowblox.test/")
        : base(endpoint)
    {
        _httpClient.Dispose();
        _uploadClient.Dispose();
        _httpClient = new HttpClient(handler, disposeHandler: false);
        _uploadClient = _httpClient;
    }

    public void Dispose() => _httpClient.Dispose();
}
