using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FlowBlox.AIAssistant
{
    public class ServiceRegistration : IFlowBloxServiceRegistration
    {
        public void RegisterServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<Services.LiteLlmModelPricingCatalogueService>();
            serviceCollection.AddSingleton<Services.IModelPricingCatalogueProvider, Services.ModelPricingCatalogueProvider>();
            serviceCollection.AddSingleton<Services.IModelPricingCatalogueService>(provider =>
                provider.GetRequiredService<Services.IModelPricingCatalogueProvider>().GetService());
            serviceCollection.AddSingleton<IOptionsRegistration, AssistantOptionsRegistration>();
        }
    }
}