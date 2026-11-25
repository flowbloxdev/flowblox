using System.Net;
using FlowBlox.AIAssistant.Services;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ModelPricingCatalogueTests
{
    [TestMethod]
    public async Task Catalogue_SharesDownloadAcrossModelsAndReloadsPersistentEntryOffline()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var handler = new CatalogueHandler();
            using var client = new HttpClient(handler);
            var service = new LiteLlmModelPricingCatalogueService(client, directory, () => TimeSpan.FromHours(72));
            var first = await service.GetPricingAsync("OpenAI", "sample", CancellationToken.None);
            var second = await service.GetPricingAsync("DeepSeek", "sample", CancellationToken.None);
            Assert.AreEqual(0.001m, first!.InputCostPerToken);
            Assert.AreEqual(0.002m, second!.InputCostPerToken);
            Assert.AreEqual(1, handler.Requests);
            Assert.IsNull(await service.GetPricingAsync("Anthropic", "sample", CancellationToken.None));
            Assert.AreEqual(1, handler.Requests);

            handler.Fail = true;
            var offline = new LiteLlmModelPricingCatalogueService(client, directory, () => TimeSpan.Zero);
            var cached = await offline.GetPricingAsync("DeepSeek", "sample", CancellationToken.None);
            Assert.AreEqual(second.InputCostPerToken, cached!.InputCostPerToken);
            await offline.GetPricingAsync("DeepSeek", "sample", CancellationToken.None);
            Assert.AreEqual(2, handler.Requests, "A failed refresh must back off instead of retrying for every completion.");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task Catalogue_UnknownModelAndUnavailableSourceReturnUnavailable()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        using var client = new HttpClient(new CatalogueHandler { Fail = true });
        var service = new LiteLlmModelPricingCatalogueService(client, directory, () => TimeSpan.FromHours(72));
        Assert.IsNull(await service.GetPricingAsync("Kimi", "unknown", CancellationToken.None));
    }

    private sealed class CatalogueHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Fail)
                throw new HttpRequestException("Offline fixture");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"sample":{"litellm_provider":"openai","input_cost_per_token":0.001},
                    "deepseek/sample":{"litellm_provider":"deepseek","input_cost_per_token":0.002}}
                    """)
            });
        }
    }
}
