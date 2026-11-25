using FlowBlox.AIAssistant.Models;

namespace FlowBlox.AIAssistant.Services;

public interface IModelPricingCatalogueService
{
    Task<ModelPricing?> GetPricingAsync(string provider, string model, CancellationToken cancellationToken);
}
