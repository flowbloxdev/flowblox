using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Interceptors;
using FlowBlox.Core.Interfaces;
using FlowBlox.UICore.Factory;
using Microsoft.Extensions.DependencyInjection;
using FlowBlox.UICore.Interfaces;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.Services;
using FlowBlox.AvaloniaDesktop.Services.Updates;
using FlowBlox.Core.TaskManagement;
using FlowBlox.UICore.Services;
using FlowBlox.AvaloniaDesktop.Providers;

namespace FlowBlox.AvaloniaDesktop
{
    internal class ServiceRegistration : IFlowBloxServiceRegistration
    {
        public void RegisterServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<IRuntimeInterceptor, RuntimeBacktraceInterceptor>();
            var componentProvider = new FlowBloxProjectComponentProvider();
            serviceCollection.AddSingleton(componentProvider);
            serviceCollection.AddSingleton<IFlowBloxProjectComponentProvider>(componentProvider);
            serviceCollection.AddSingleton<IPropertyWindowViewFactory, TestDefinitionViewFactory>();
            serviceCollection.AddSingleton<FlowBloxMessageBoxService>();
            serviceCollection.AddSingleton<IFlowBloxMessageBoxService>(services => services.GetRequiredService<FlowBloxMessageBoxService>());
            serviceCollection.AddSingleton<IMessageBoxService>(services => services.GetRequiredService<FlowBloxMessageBoxService>());
            serviceCollection.AddSingleton<IFilePickerService, FilePickerService>();
            serviceCollection.AddSingleton<IDialogService, DialogService>();
            serviceCollection.AddSingleton<IOwnerService, OwnerService>();
            serviceCollection.AddSingleton<IAssemblyPreloader, AssemblyPreloader>();
            serviceCollection.AddSingleton<IFlowBloxUIEvaluationService, FlowBloxUIEvaluationService>();
            serviceCollection.AddSingleton<IAppNotificationService, AppNotificationService>();
            serviceCollection.AddSingleton<IFlowBloxInstallerService, FlowBloxInstallerService>();
            serviceCollection.AddSingleton<IFlowBloxActionHistoryService, FlowBloxActionHistoryService>();
            serviceCollection.AddSingleton<IRuntimeStateService>(RuntimeStateService.Shared);
            serviceCollection.AddSingleton<ITaskManagementService, WindowsTaskManagementService>();
        }
    }
}
