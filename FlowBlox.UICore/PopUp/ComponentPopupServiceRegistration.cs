using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Interfaces;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Implementations;
using Microsoft.Extensions.DependencyInjection;

namespace FlowBlox.UICore.PopUp
{
    public class ComponentPopupServiceRegistration : IFlowBloxServiceRegistration
    {
        public void RegisterServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<AIPromptPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<AIPromptPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<AIPromptPopUpProvider>());

            serviceCollection.AddSingleton<SequenceDetectionPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<SequenceDetectionPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<SequenceDetectionPopUpProvider>());

            serviceCollection.AddSingleton<WebBrowserPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<WebBrowserPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<WebBrowserPopUpProvider>());

            serviceCollection.AddSingleton<OnnxGenAIPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<OnnxGenAIPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<OnnxGenAIPopUpProvider>());

            serviceCollection.AddSingleton<OnnxQAPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<OnnxQAPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<OnnxQAPopUpProvider>());

            serviceCollection.AddSingleton<ComponentValidationFailedPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<ComponentValidationFailedPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<ComponentValidationFailedPopUpProvider>());

            serviceCollection.AddSingleton<YtDlpPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<YtDlpPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<YtDlpPopUpProvider>());

            serviceCollection.AddSingleton<MediaDownloadPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<MediaDownloadPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<MediaDownloadPopUpProvider>());

            serviceCollection.AddSingleton<FFmpegMediaDownloadPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<FFmpegMediaDownloadPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<FFmpegMediaDownloadPopUpProvider>());

            serviceCollection.AddSingleton<FFmpegToolPopUpProvider>();
            serviceCollection.AddSingleton<IComponentPopupProvider>(sp =>
                sp.GetRequiredService<FFmpegToolPopUpProvider>());
            serviceCollection.AddSingleton<IOptionsRegistration>(sp =>
                sp.GetRequiredService<FFmpegToolPopUpProvider>());

            serviceCollection.AddSingleton<IComponentPopupService, ComponentPopupService>();
        }
    }
}
