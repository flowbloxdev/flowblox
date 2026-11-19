using FlowBlox.Core.Constants;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Util;
using FlowBloxTest.FlowBlocks.Knowledge;
using Newtonsoft.Json;

namespace FlowBloxTest.FlowBlocks;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ExecuteProjectNameTests
{
    [TestMethod]
    public void UnresponsiveServiceReturnsFallbackWithinRemotePropertyTimeout()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.ApiProjectServiceBaseUrl);
        var previousEndpoint = endpoint.Value;
        try
        {
            endpoint.Value = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            var block = new ExecuteProjectFlowBlock { ProjectSpaceGuid = Guid.NewGuid().ToString("N") };
            var elapsed = Stopwatch.StartNew();
            var name = block.ProjectName;

            Assert.IsFalse(string.IsNullOrWhiteSpace(name));
            Assert.IsTrue(elapsed.Elapsed.TotalSeconds < GlobalConstants.RemotePropertyLoadTimeoutSeconds + 1);
            elapsed.Restart();
            Assert.AreEqual(name, block.ProjectName);
            Assert.IsTrue(elapsed.Elapsed.TotalSeconds < 1);
        }
        finally
        {
            endpoint.Value = previousEndpoint;
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ConcurrentBlocksShareMetadataRequestAndChangingGuidLoadsTheNewName(bool success)
    {
        var guid = Guid.NewGuid().ToString("N");
        using var server = new KnowledgeHttpServer(request =>
        {
            var initial = request.Path.Contains(guid, StringComparison.OrdinalIgnoreCase);
            return new KnowledgeHttpResponse
            {
                Status = initial && !success ? 404 : 200,
                Body = JsonConvert.SerializeObject(new { Name = initial ? "Shared project" : "Other project" })
            };
        });
        var endpoint = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.ApiProjectServiceBaseUrl);
        var previousEndpoint = endpoint.Value;
        try
        {
            endpoint.Value = server.BaseUrl;
            var blocks = Enumerable.Range(0, 8)
                .Select(_ => new ExecuteProjectFlowBlock { ProjectSpaceGuid = guid })
                .ToArray();
            var names = await Task.WhenAll(blocks.Select(block => Task.Run(() => block.ProjectName)));

            Assert.HasCount(1, server.Requests);
            StringAssert.StartsWith(server.Requests[0].Path, "/project.php?guid=");
            Assert.IsFalse(string.IsNullOrWhiteSpace(names[0]));
            Assert.IsTrue(names.All(name => name == names[0]));
            if (success)
                Assert.AreEqual("Shared project", names[0]);
            Assert.AreEqual(names[0], blocks[0].ProjectName);
            Assert.HasCount(1, server.Requests);

            blocks[0].ProjectSpaceGuid = Guid.NewGuid().ToString("N");
            Assert.AreEqual("Other project", blocks[0].ProjectName);
            Assert.HasCount(2, server.Requests);
        }
        finally
        {
            endpoint.Value = previousEndpoint;
        }
    }
}
