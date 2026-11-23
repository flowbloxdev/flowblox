using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.LocalAI;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using MahApps.Metro.IconPacks;
using Texts = FlowBlox.UICore.Resources.LocalAISetupWindow;

namespace FlowBlox.UICore.ViewModels.LocalAI;

public sealed class LocalAISetupViewModel : INotifyPropertyChanged
{
    private readonly FlowBloxProject _project;
    private readonly BaseFlowBlock _block;
    private readonly IFlowBloxMessageBoxService _messages;
    private readonly bool _requiresBlockSave;
    private LocalAIModelCard? _selectedModel;

    public ObservableCollection<LocalAIModelCard> Models { get; } = [];
    public ObservableCollection<LocalAISetupStepViewModel> Steps { get; } = [];
    public int ModelColumns => Math.Min(2, Models.Count);
    public bool IsBusy { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool CanConfigure => !IsBusy && !IsCompleted && SelectedModel != null;
    public bool CanChangeSelection => !IsBusy && !IsCompleted;
    public bool CanClose => !IsBusy;

    public LocalAIModelCard? SelectedModel
    {
        get => _selectedModel;
        set
        {
            _selectedModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConfigure));
        }
    }

    public LocalAISetupViewModel(
        FlowBloxProject project,
        BaseFlowBlock block,
        IFlowBloxMessageBoxService messages)
    {
        _project = project;
        _block = FlowBloxRegistryProvider.GetRegistry().Reload(block);
        _requiresBlockSave = !project.FlowBloxRegistry.GetFlowBlocks().Contains(_block);
        _messages = messages;
        foreach (var metadata in LocalAIScriptCatalog.ForBlock(block))
            AddModel(metadata);
        SelectedModel = Models[0];
        for (var index = 1; index <= 5; index++)
            Steps.Add(new LocalAISetupStepViewModel
            {
                Title = Text($"Step{index}Title"),
                CanOverwrite = index > 1,
                OverwriteExisting = index >= 4,
                Description = Text($"Step{index}Description") +
                    (index is 2 or 3 ? Environment.NewLine + Text("GlobalChangeNote") : string.Empty)
            });
    }

    public async Task ConfigureAsync()
    {
        if (!CanConfigure)
            return;
        var setup = new LocalAISetup(_project, _block, SelectedModel!.Model, options: new LocalAISetupOptions
        {
            OverwriteProviderInput = Steps[1].OverwriteExisting,
            OverwriteScript = Steps[2].OverwriteExisting,
            OverwriteBlockProvider = Steps[3].OverwriteExisting,
            OverwriteBlockModel = Steps[4].OverwriteExisting
        });
        IsBusy = true;
        NotifyState();
        foreach (var step in Steps)
            step.Update(string.Empty, PackIconMaterialKind.ClockOutline, Brushes.SlateGray);
        var index = 0;
        try
        {
            for (; index < Steps.Count; index++)
            {
                Steps[index].Update(Text("Running"), PackIconMaterialKind.ProgressClock, Brushes.SteelBlue);
                await Dispatcher.Yield(DispatcherPriority.Background);
                var status = Text("Done");
                switch (index)
                {
                    case 0:
                        setup.Prepare();
                        break;
                    case 1:
                        var reused = setup.ConfigureProvider();
                        status = string.Format(Text(reused ? "ProviderReused" : "ProviderCreated"), setup.ProviderField.Name);
                        break;
                    case 2:
                        var unchanged = setup.ConfigureInputFile();
                        status = string.Format(Text(unchanged ? "ScriptReused" : "ScriptConfigured"), setup.ScriptName);
                        break;
                    case 3:
                        setup.ConfigureBlockProvider();
                        break;
                    case 4:
                        setup.ConfigureBlockModel();
                        break;
                }
                Steps[index].Update(status, PackIconMaterialKind.CheckCircleOutline, Brushes.SeaGreen);
            }
            IsCompleted = true;
            var success = Text("Success");
            if (_requiresBlockSave)
                success += Environment.NewLine + Text("SaveBlockRequired");
            _messages.ShowMessageBox(success, Text("Title"), FlowBloxMessageBoxTypes.Information);
        }
        catch (Exception exception)
        {
            Steps[Math.Min(index, Steps.Count - 1)].Update(
                exception.Message, PackIconMaterialKind.AlertCircleOutline, Brushes.IndianRed);
            FlowBloxLogManager.Instance.GetLogger().Error("Local AI project setup failed.", exception);
        }
        finally
        {
            IsBusy = false;
            NotifyState();
        }
    }

    private void AddModel(LocalAIScriptMetadata metadata) => Models.Add(new LocalAIModelCard
    {
        Model = metadata.Model,
        Title = Text(metadata.Model + "Title"),
        Description = Text(metadata.Model + "Description"),
        Details = Text(metadata.Model + "Details") + Environment.NewLine + metadata.ScriptName
    });

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CanConfigure));
        OnPropertyChanged(nameof(CanChangeSelection));
        OnPropertyChanged(nameof(CanClose));
    }

    private static string Text(string key) => FlowBloxResourceUtil.GetLocalizedString(key, typeof(Texts));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
