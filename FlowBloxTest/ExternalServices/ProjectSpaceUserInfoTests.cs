using System.Net;
using System.Net.Sockets;
using System.Text;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.UICore.Utilities;

namespace FlowBloxTest.ExternalServices;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class ProjectSpaceUserInfoTests
{
    [TestMethod]
    [DataRow("", 4, "GET /userinfo.php?id=4 HTTP/1.1")]
    [DataRow("session+&?", 0, "GET /userinfo.php?token=session%2B%26%3F HTTP/1.1")]
    [DataRow("session+&?", 4, "GET /userinfo.php?token=session%2B%26%3F&id=4 HTTP/1.1")]
    public async Task UserInfo_SupportsIdAndTokenQueries(string token, int userId, string expectedRequest)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var serve = SendUserResponseAsync(listener, 200,
            "{\"username\":\"project-owner\",\"firstName\":null,\"lastName\":null,\"email\":null,\"products\":null}");
        var response = await new FlowBloxWebApiService(endpoint)
            .GetUserInfoAsync(token, userId == 0 ? null : userId)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(expectedRequest, await serve);
        Assert.IsTrue(response.Success);
        Assert.AreEqual("project-owner", response.ResultObject.UserName);
    }

    [TestMethod]
    public async Task OwnerNames_ReuseOneRequestForConcurrentLookups()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var cache = new ProjectSpaceUserNameCache();
        var serve = SendUserResponseAsync(listener, 200, "{\"username\":\"owner\"}");
        var lookups = Task.WhenAll(cache.GetDisplayNameAsync(endpoint, 4), cache.GetDisplayNameAsync(endpoint + "/", 4));
        var names = await lookups.WaitAsync(TimeSpan.FromSeconds(5));
        await serve;
        CollectionAssert.AreEqual(new[] { "owner", "owner" }, names);
        Assert.IsFalse(listener.Pending());
    }

    [TestMethod]
    public async Task OwnerNames_KeepDifferentEndpointsSeparate()
    {
        using var first = new TcpListener(IPAddress.Loopback, 0);
        using var second = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        second.Start();
        var firstEndpoint = $"http://127.0.0.1:{((IPEndPoint)first.LocalEndpoint).Port}/";
        var secondEndpoint = $"http://127.0.0.1:{((IPEndPoint)second.LocalEndpoint).Port}/";
        var firstResponse = SendUserResponseAsync(first, 200, "{\"username\":\"first-owner\"}");
        var secondResponse = SendUserResponseAsync(second, 200, "{\"username\":\"second-owner\"}");
        var cache = new ProjectSpaceUserNameCache();
        Assert.AreEqual("first-owner", await cache.GetDisplayNameAsync(firstEndpoint, 4).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("second-owner", await cache.GetDisplayNameAsync(secondEndpoint, 4).WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.WhenAll(firstResponse, secondResponse);
    }

    [TestMethod]
    [DataRow(404, "{\"error\":\"User not found\"}")]
    [DataRow(500, "{\"error\":\"Unavailable\"}")]
    [DataRow(200, "{\"username\":null}")]
    public async Task OwnerNames_RetryFailedLookupsAndCacheOnlySuccessfulNames(int status, string body)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var cache = new ProjectSpaceUserNameCache();
        var failed = SendUserResponseAsync(listener, status, body);
        Assert.IsNull(await cache.GetDisplayNameAsync(endpoint, 4).WaitAsync(TimeSpan.FromSeconds(5)));
        await failed;
        var retry = SendUserResponseAsync(listener, 200, "{\"username\":\"restored-owner\"}");
        Assert.AreEqual("restored-owner", await cache.GetDisplayNameAsync(endpoint, 4).WaitAsync(TimeSpan.FromSeconds(5)));
        await retry;
        Assert.AreEqual("restored-owner", await cache.GetDisplayNameAsync(endpoint, 4).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(listener.Pending());
    }

    private static async Task<string?> SendUserResponseAsync(TcpListener listener, int status, string body)
    {
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var request = await reader.ReadLineAsync();
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }
        var bytes = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Response\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(bytes);
        return request;
    }
}
