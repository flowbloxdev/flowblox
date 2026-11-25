namespace FlowBlox.AIAssistant.Services;

public sealed class ModelPricingCatalogueProvider : IModelPricingCatalogueProvider
{
    private readonly LiteLlmModelPricingCatalogueService _service;

    public ModelPricingCatalogueProvider(LiteLlmModelPricingCatalogueService service)
    {
        _service = service;
    }

    public IModelPricingCatalogueService GetService() => _service;
}
