using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Converters;
using FlowBlox.UICore.Models;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.Views;
using MahApps.Metro.Controls;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using FlowBlox.Core.Models.Components;

namespace FlowBlox.UICore.ViewModels
{
    public class TestDefinitionViewModel : INotifyPropertyChanged
    {
        private bool _isDirty;
        private FlowBloxTestDefinition _testDefinition;
        private BaseFlowBlock _currentFlowBlock;
        private FlowBloxTestExecutor _testExecutor;
        private Window _ownerWindow;
        private bool _hasExplicitFlowBlockContext;
        private bool _hideLastLayerNeighbours;
        private bool _suppressDirtyTracking;
        private readonly HashSet<FlowBloxReactiveObject> _validationSources = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<FlowBloxReactiveObject, IReadOnlyList<string>> _validationErrors = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<FlowBloxReactiveObject, BaseFlowBlock> _validationSourceOwners = new(ReferenceEqualityComparer.Instance);
        private readonly object _validationErrorsSync = new();
        private int _validationRefreshScheduled;

        public TestDefinitionViewModel()
        {
            ExecuteTestCommand = new RelayCommand(ExecuteTestAsync);
            EditConditionsCommand = new RelayCommand(EditConditions);
            EditContentCommand = new RelayCommand(EditContent);
            OpenInEditorCommand = new RelayCommand(OpenInEditor);
            AddExpectationCommand = new RelayCommand(_ => AddExpectation(), _ => SelectedConfiguration != null);
            DeleteExpectationCommand = new RelayCommand(_ => DeleteExpectation(), _ => SelectedConfiguration?.ExpectationConditions != null && SelectedExpectation != null);
            _testDefinition = new FlowBloxTestDefinition();
            SubscribeToPropertyChangeEvents(_testDefinition);
            _testExecutor = new FlowBloxTestExecutor();
            Configurations = new ObservableCollection<FlowBloxFieldTestConfiguration>();
            SortedEntries = new ObservableCollection<FlowBlockTestDataset>();
            RuntimeLogs = new ObservableCollection<RuntimeLog>();
            ValidationMessages = new ObservableCollection<string>();
            RefreshValidationSubscriptions();
            _hideLastLayerNeighbours = ResolveHideLastLayerNeighboursOption();
            BindingOperations.EnableCollectionSynchronization(RuntimeLogs, new object());
        }

        public string HeaderDescription =>
            FlowBloxResourceUtil.GetLocalizedString("FlowBloxTestDefinition_Description", typeof(FlowBloxTexts));

        private void SubscribeToPropertyChangeEvents(FlowBloxTestDefinition testDefinition)
        {
            testDefinition.PropertyChanged += _testDefinition_PropertyChanged;
            foreach (var entry in testDefinition.Entries)
            {
                entry.PropertyChanged += Entry_PropertyChanged;
                foreach (var config in entry.FlowBloxTestConfigurations)
                {
                    config.PropertyChanged += (s, e) => Configuration_PropertyChanged(entry, config, e);
                }
            }
        }

        private void Entry_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (IsUiOnlyProperty(e.PropertyName))
                return;

            MarkDirty($"Entry property changed: {e.PropertyName}");
        }

        private void Configuration_PropertyChanged(FlowBlockTestDataset entry, FlowBloxFieldTestConfiguration config, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlowBloxFieldTestConfiguration.SelectionMode))
            {
                if (config.SelectionMode == FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue ||
                    config.SelectionMode == FlowBloxTestConfigurationSelectionMode.First ||
                    config.SelectionMode == FlowBloxTestConfigurationSelectionMode.Index ||
                    config.SelectionMode == FlowBloxTestConfigurationSelectionMode.Last)
                {
                    entry.Execute = true;
                }
            }

            if (IsUiOnlyProperty(e.PropertyName))
                return;

            MarkDirty($"Configuration property changed: {e.PropertyName}");
        }

        private static bool IsUiOnlyProperty(string propertyName)
            => propertyName == nameof(FlowBlockTestDataset.UIAssociatedFlowBlockRequiredForExecution) ||
               propertyName == nameof(FlowBlockTestDataset.UIIsTargetFlowBlock) ||
               propertyName == nameof(FlowBlockTestDataset.UIIsTargetNeighbour) ||
               propertyName == nameof(FlowBlockTestDataset.UIIsVisibleInCurrentContext) ||
               propertyName == nameof(FlowBlockTestDataset.UIHasFlowBlockValidationErrors) ||
               propertyName == nameof(FlowBloxFieldTestConfiguration.UIRequiredForExecution);

        private void LoadCurrentCondfigurations(FlowBloxTestDefinition testDefinition, BaseFlowBlock currentFlowBlock)
        {
            Configurations.Clear();

            if (testDefinition == null || currentFlowBlock == null)
            {
                SelectedConfiguration = null;
                return;
            }

            var configs = testDefinition.Entries
                .Where(e => e.FlowBlock == currentFlowBlock)
                .SelectMany(e => e.FlowBloxTestConfigurations)
                .ToList();

            Configurations.AddRange(configs);

            SelectedConfiguration = Configurations.FirstOrDefault();
        }

        private void AddExpectation()
        {
            if (SelectedConfiguration == null)
                return;

            if (SelectedConfiguration.ExpectationConditions == null)
                SelectedConfiguration.ExpectationConditions = new ObservableCollection<ExpectationCondition>();

            var defaultOp = Enum.GetValues(typeof(ComparisonOperator)).Cast<ComparisonOperator>().First();

            var ec = new ExpectationCondition
            {
                ExpectationConditionTarget = ExpectationConditionTarget.FirstValue,
                Index = 0,
                Operator = defaultOp,
                Value = string.Empty
            };

            SelectedConfiguration.ExpectationConditions.Add(ec);
            SelectedExpectation = ec;
            RefreshValidationSubscriptions();
            MarkDirty("Expectation added");
        }

        private void DeleteExpectation()
        {
            if (SelectedConfiguration?.ExpectationConditions == null || SelectedExpectation == null)
                return;

            SelectedConfiguration.ExpectationConditions.Remove(SelectedExpectation);
            SelectedExpectation = null;
            RefreshValidationSubscriptions();
            MarkDirty("Expectation deleted");
        }

        public List<ExpectationConditionTarget> ExpectationTargets => Enum.GetValues(typeof(ExpectationConditionTarget))
            .Cast<ExpectationConditionTarget>()
            .ToList();

        public List<ComparisonOperator> ComparisonOperators => Enum.GetValues(typeof(ComparisonOperator))
            .Cast<ComparisonOperator>()
            .ToList();

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(CanApply));
                }
            }
        }

        private void _testDefinition_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            MarkDirty($"Test definition property changed: {e.PropertyName}");
            if (e.PropertyName == nameof(TestDefinition.Name))
            {
                OnPropertyChanged(nameof(CanApply));
            }
        }

        public FlowBloxTestDefinition TestDefinition
        {
            get => _testDefinition;
            set
            {
                if (_testDefinition != value)
                {
                    if (_testDefinition?.Entries != null)
                        _testDefinition.Entries.CollectionChanged -= Entries_CollectionChanged;

                    _suppressDirtyTracking = true;
                    _testDefinition = value;
                    _testDefinition.RecalculateRequiredFlagsAcrossDefinition();
                    SubscribeToPropertyChangeEvents(_testDefinition);

                    if (_testDefinition?.Entries != null)
                        _testDefinition.Entries.CollectionChanged += Entries_CollectionChanged;

                    RefreshSortedEntries();
                    RefreshValidationSubscriptions();
                    _suppressDirtyTracking = false;
                    OnPropertyChanged(nameof(TestDefinition));
                    OnPropertyChanged(nameof(CanExecute));
                }
            }
        }

        private void Entries_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshSortedEntries();
            RefreshValidationSubscriptions();
            MarkDirty("Test dataset collection changed");
        }

        private void RefreshSortedEntries()
        {
            SortedEntries.Clear();
            if (_testDefinition?.Entries == null)
                return;

            foreach (var entry in _testDefinition.Entries.Where(x => x.FlowBlock == null))
                SortedEntries.Add(entry);

            foreach (var entry in _testDefinition.Entries.Where(x => x.FlowBlock != null))
                SortedEntries.Add(entry);

            OnPropertyChanged(nameof(SortedEntries));
            UpdateDatasetUiState(CurrentFlowBlock);
        }

        public ObservableCollection<FlowBlockTestDataset> SortedEntries { get; }

        public BaseFlowBlock CurrentFlowBlock
        {
            get => _currentFlowBlock;
            set
            {
                _currentFlowBlock = value;
                LoadCurrentFlowBlock();
                RefreshValidationSubscriptions();
                OnPropertyChanged(nameof(CurrentFlowBlock));
            }
        }

        private void LoadCurrentFlowBlock()
        {
            LoadCurrentCondfigurations(_testDefinition, CurrentFlowBlock);
            LoadCapturedFlowBlocks(CurrentFlowBlock);
            UpdateDatasetUiState(CurrentFlowBlock);
        }

        private void LoadCapturedFlowBlocks(BaseFlowBlock currentFlowBlock)
        {
            FlowBloxTestCapture flowBloxCapture = new FlowBloxTestCapture();
            FlowBloxRegistry registry = FlowBloxRegistryProvider.GetRegistry();
            var capturedFlowBlocks = flowBloxCapture.CreateCapture(registry.GetStartFlowBlock(), currentFlowBlock);
            flowBloxCapture.CreateCapture(registry.GetStartFlowBlock(), currentFlowBlock);
            CapturedFlowBlocks = flowBloxCapture.GetCapturedFlowBlocks();
        }

        private IEnumerable<BaseFlowBlock> _capturedFlowBlocks;
        public IEnumerable<BaseFlowBlock> CapturedFlowBlocks
        {
            get => _capturedFlowBlocks;
            set
            {
                _capturedFlowBlocks = value;
                OnPropertyChanged(nameof(CapturedFlowBlocks));
            }
        }

        public bool HasExplicitFlowBlockContext
        {
            get => _hasExplicitFlowBlockContext;
            set
            {
                if (_hasExplicitFlowBlockContext == value)
                    return;

                _hasExplicitFlowBlockContext = value;
                OnPropertyChanged(nameof(HasExplicitFlowBlockContext));
                OnPropertyChanged(nameof(CanToggleHideLastLayerNeighbours));
                UpdateDatasetUiState(CurrentFlowBlock);
            }
        }

        public bool CanToggleHideLastLayerNeighbours => HasExplicitFlowBlockContext;

        public bool HideLastLayerNeighbours
        {
            get => _hideLastLayerNeighbours;
            set
            {
                if (_hideLastLayerNeighbours == value)
                    return;

                _hideLastLayerNeighbours = value;
                OnPropertyChanged(nameof(HideLastLayerNeighbours));
                SaveHideLastLayerNeighboursOption(value);
                UpdateDatasetUiState(CurrentFlowBlock);
            }
        }

        public ObservableCollection<FlowBloxFieldTestConfiguration> Configurations { get; }

        private FlowBloxFieldTestConfiguration _selectedConfiguration;
        public FlowBloxFieldTestConfiguration SelectedConfiguration
        {
            get => _selectedConfiguration;
            set
            {
                if (_selectedConfiguration != value)
                {
                    _selectedConfiguration = value;
                    OnPropertyChanged(nameof(SelectedConfiguration));
                }
            }
        }

        private ExpectationCondition _selectedExpectation;
        public ExpectationCondition SelectedExpectation
        {
            get => _selectedExpectation;
            set
            {
                if (_selectedExpectation != value)
                {
                    _selectedExpectation = value;
                    OnPropertyChanged(nameof(SelectedExpectation));
                }
            }
        }

        public ObservableCollection<RuntimeLog> RuntimeLogs { get; set; }

        public ObservableCollection<string> ValidationMessages { get; }

        public bool HasValidationMessages => ValidationMessages.Count > 0;

        private void RefreshValidationSubscriptions()
        {
            DetachValidationSubscriptions();
            _validationSourceOwners.Clear();
            lock (_validationErrorsSync)
                _validationErrors.Clear();

            if (_testDefinition != null)
            {
                AddValidationSource(_testDefinition);
                foreach (var source in _testDefinition.GetNestedReactiveObjects())
                    AddValidationSource(source);

                var flowBlocks = _testDefinition.Entries
                    .Select(entry => entry.FlowBlock)
                    .Append(_currentFlowBlock)
                    .Concat(_capturedFlowBlocks ?? Enumerable.Empty<BaseFlowBlock>())
                    .ExceptNull()
                    .Distinct((IEqualityComparer<BaseFlowBlock>)ReferenceEqualityComparer.Instance);

                foreach (var flowBlock in flowBlocks)
                {
                    AddValidationSource(flowBlock, flowBlock);
                    foreach (var source in flowBlock.GetNestedReactiveObjects())
                        AddValidationSource(source, flowBlock);
                }
            }

            RefreshValidationMessages();
        }

        public void DetachValidationSubscriptions()
        {
            foreach (var source in _validationSources)
                source.ErrorsChanged -= ValidationSource_ErrorsChanged;

            _validationSources.Clear();
        }

        private void AddValidationSource(FlowBloxReactiveObject source, BaseFlowBlock? owner = null)
        {
            if (source == null)
                return;

            if (owner != null)
                _validationSourceOwners[source] = ResolveOriginalFlowBlock(owner);

            if (!_validationSources.Add(source))
                return;

            source.ErrorsChanged += ValidationSource_ErrorsChanged;
            UpdateValidationErrors(source);
        }

        private void ValidationSource_ErrorsChanged(object sender, DataErrorsChangedEventArgs e)
        {
            if (sender is not FlowBloxReactiveObject source)
                return;

            UpdateValidationErrors(source);
            ScheduleValidationMessagesRefresh();
        }

        private void UpdateValidationErrors(FlowBloxReactiveObject source)
        {
            var errors = source.GetErrors(null)
                .Cast<string>()
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToList();

            lock (_validationErrorsSync)
                _validationErrors[source] = errors;
        }

        private void ScheduleValidationMessagesRefresh()
        {
            if (Interlocked.Exchange(ref _validationRefreshScheduled, 1) != 0)
                return;

            var dispatcher = _ownerWindow?.Dispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                Interlocked.Exchange(ref _validationRefreshScheduled, 0);
                RefreshValidationMessages();
                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _validationRefreshScheduled, 0);
                RefreshValidationMessages();
            }), DispatcherPriority.DataBind);
        }

        public void RefreshValidationMessages()
        {
            List<KeyValuePair<FlowBloxReactiveObject, IReadOnlyList<string>>> errorsBySource;
            lock (_validationErrorsSync)
                errorsBySource = _validationErrors.ToList();

            var distinctMessages = errorsBySource
                .SelectMany(entry => entry.Value)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.CurrentCulture)
                .ToList();

            ValidationMessages.Clear();
            foreach (var message in distinctMessages)
                ValidationMessages.Add(message);

            var invalidFlowBlocks = errorsBySource
                .Where(entry => entry.Value.Count > 0 && _validationSourceOwners.ContainsKey(entry.Key))
                .Select(entry => _validationSourceOwners[entry.Key])
                .ToHashSet((IEqualityComparer<BaseFlowBlock>)ReferenceEqualityComparer.Instance);

            foreach (var entry in _testDefinition?.Entries ?? Enumerable.Empty<FlowBlockTestDataset>())
            {
                entry.UIHasFlowBlockValidationErrors = entry.FlowBlock != null &&
                    invalidFlowBlocks.Contains(ResolveOriginalFlowBlock(entry.FlowBlock));
            }

            OnPropertyChanged(nameof(HasValidationMessages));
        }

        private static BaseFlowBlock ResolveOriginalFlowBlock(BaseFlowBlock flowBlock)
            => FlowBloxRegistryProvider.GetRegistry().GetOriginalRef(flowBlock) as BaseFlowBlock ?? flowBlock;

        public RelayCommand ExecuteTestCommand { get; }
        public RelayCommand EditConditionsCommand { get; }
        public RelayCommand EditContentCommand { get; }
        public RelayCommand OpenInEditorCommand { get; }
        public RelayCommand AddExpectationCommand { get; }
        public RelayCommand DeleteExpectationCommand { get; }

        public void AcceptChanges(string reason)
        {
            if (IsDirty)
                FlowBloxLogManager.Instance.GetLogger().Info($"TestDefinitionView dirty reset: {reason}");

            IsDirty = false;
        }

        private int _selectedResultTabIndex;
        public int SelectedResultTabIndex
        {
            get => _selectedResultTabIndex;
            set
            {
                if (_selectedResultTabIndex != value)
                {
                    _selectedResultTabIndex = value;
                    OnPropertyChanged(nameof(SelectedResultTabIndex));
                }
            }
        }

        public List<string> TestResultsColumnNames { get; private set; }
        public ObservableCollection<FlowBlockOutDataset> TestResults { get; private set; }
        public ObservableCollection<DataGridColumn> TestResultsColumns { get; private set; }

        private async void ExecuteTestAsync()
        {
            RuntimeLogs.Clear();
            SelectedResultTabIndex = 1;

            var includedFlowBlocks = _testDefinition?.Entries?
                .Where(x => x.Execute)
                .Select(x => x.FlowBlock)
                .ExceptNull()
                .ToList();

            try
            {
                _testExecutor.Initialize(_testDefinition, CurrentFlowBlock, includedFlowBlocks);
            }
            catch (Exception ex)
            {
                var messageTemplate = FlowBloxResourceUtil.GetLocalizedString(
                    "Message_RuntimeInitializationFailed",
                    typeof(Resources.TestDefinitionView));
                var message = string.Format(messageTemplate, ex.Message);

                await MessageBoxHelper.ShowMessageBoxAsync(_ownerWindow as MetroWindow, MessageBoxType.Error, message);
                return;
            }

            _testExecutor.GetRuntime().LogMessageCreated += TestDefinitionViewModel_LogMessageCreated;
            await _testExecutor.ExecuteTestAsync();

            _testExecutor.Shutdown();

            if (CurrentFlowBlock is BaseResultFlowBlock resultFlowBlock && resultFlowBlock.GridElementResult?.ResultCount > 0)
            {
                TestResults = new ObservableCollection<FlowBlockOutDataset>(resultFlowBlock.GridElementResult.Results);
                TestResultsColumnNames = resultFlowBlock.GridElementResult.Results
                    .First()
                    .FieldValueMappings
                    .Select(fvm => fvm.Field.Name)
                    .ToList();

                OnPropertyChanged(nameof(TestResultsColumnNames));
                OnPropertyChanged(nameof(TestResults));
                UpdateTestResultsColumns();
            }
        }

        private void UpdateTestResultsColumns()
        {
            var columns = TestResultsColumnNames
                .Select(columnName => new DataGridTextColumn
                {
                    Header = columnName,
                    Binding = new Binding
                    {
                        Path = new PropertyPath("."),
                        Converter = new FlowBlockOutDatasetToValueConverter(),
                        ConverterParameter = columnName
                    },
                    IsReadOnly = true,
                    Width = 200
                })
                .ToList();

            TestResultsColumns = new ObservableCollection<DataGridColumn>(columns);
            OnPropertyChanged(nameof(TestResultsColumns));
        }

        public bool CanApply
        {
            get
            {
                if (!IsDirty)
                    return false;

                if (string.IsNullOrEmpty(_testDefinition.Name))
                    return false;
                
                return true;
            }
        }

        public bool CanExecute
        {
            get
            {
                return true;
            }
        }

        public Window OwnerWindow
        {
            get
            {
                return _ownerWindow;
            }
            set
            {
                _ownerWindow = value;
            }
        }

        private void TestDefinitionViewModel_LogMessageCreated(BaseRuntime runtime, string message, FlowBloxLogLevel logLevel)
        {
            var log = new RuntimeLog
            {
                Timestamp = DateTime.Now,
                Message = message,
                LogLevel = logLevel
            };
            RuntimeLogs.Add(log);
            OnPropertyChanged(nameof(RuntimeLogs));
        }

        private void EditConditions(object target)
        {
            var flowBloxTestConfiguration = target as FlowBloxFieldTestConfiguration;
            if (flowBloxTestConfiguration == null)
                return;

            var propertyView = new Views.PropertyWindow(new PropertyWindowArgs(flowBloxTestConfiguration, readOnly: false))
            {
                Owner = _ownerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (propertyView.ShowDialog() == true)
            { 
                flowBloxTestConfiguration.ExpectationConditions = flowBloxTestConfiguration.ExpectationConditions;
                OnPropertyChanged(nameof(TestDefinition));
            }
        }

        private void EditContent(object target)
        {
            var configuration = target as FlowBloxFieldTestConfiguration;
            if (configuration == null)
                return;

            var view = new EditContentView(configuration.UserInput, enableFieldSelection: true)
            {
                Owner = _ownerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            
            var result = view.ShowDialog();
            if (result == true)
            {
                configuration.UserInput = view.ContentText;
            }
        }

        private void OpenInEditor(object target)
        {
            if (target is FlowBlockOutDataset outDataset)
            {
                if (outDataset.FieldValueMappings.Count > 1)
                {
                    var multiValueSelectionDialog = new Views.MultiValueSelectionDialog("Field selection", "", new GenericSelectionHandler<FlowBlockOutDatasetFieldValueMapping>(outDataset.FieldValueMappings, t => t.Field.Name));
                    multiValueSelectionDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    multiValueSelectionDialog.Owner = _ownerWindow;
                    if (multiValueSelectionDialog.ShowDialog() == true)
                    {
                        var selectedFieldValueMapping = (FlowBlockOutDatasetFieldValueMapping)multiValueSelectionDialog.SelectedItem?.Value;
                        if (selectedFieldValueMapping != null)
                        {
                            FlowBloxEditingHelper.OpenUsingEditor(selectedFieldValueMapping.Value, selectedFieldValueMapping.Field.FullyQualifiedName);
                        }
                    }
                }
                else if (outDataset.FieldValueMappings.Count == 1)
                {
                    var selectedFieldValueMapping = outDataset.FieldValueMappings.First();
                    FlowBloxEditingHelper.OpenUsingEditor(selectedFieldValueMapping.Value, selectedFieldValueMapping.Field.FullyQualifiedName);
                }
            }
            else if (target is RuntimeLog runtimeLog)
            {
                FlowBloxEditingHelper.OpenUsingEditor(runtimeLog.Message, $"{runtimeLog.Timestamp}_{runtimeLog.LogLevel}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void UpdateDatasetUiState(BaseFlowBlock targetFlowBlock)
        {
            var entries = _testDefinition?.Entries?.ToList() ?? new List<FlowBlockTestDataset>();
            var neighbourFlowBlocks = ResolveTargetNeighbours(targetFlowBlock);
            var hiddenNeighbourTree = ResolveSuccessorTree(neighbourFlowBlocks, targetFlowBlock);
            var capturedFlowBlocks = CapturedFlowBlocks?.ToHashSet() ?? new HashSet<BaseFlowBlock>();

            foreach (var entry in entries)
            {
                bool isTarget = entry.FlowBlock != null && entry.FlowBlock == targetFlowBlock;
                bool isNeighbour = entry.FlowBlock != null && neighbourFlowBlocks.Contains(entry.FlowBlock);
                bool isCaptured = entry.FlowBlock == null || capturedFlowBlocks.Count == 0 || capturedFlowBlocks.Contains(entry.FlowBlock);
                bool hideNeighbourTree = entry.FlowBlock != null &&
                                         HasExplicitFlowBlockContext &&
                                         HideLastLayerNeighbours &&
                                         hiddenNeighbourTree.Contains(entry.FlowBlock);

                entry.UIIsTargetFlowBlock = isTarget;
                entry.UIIsTargetNeighbour = isNeighbour;
                entry.UIIsVisibleInCurrentContext = HasExplicitFlowBlockContext ? (isCaptured && !hideNeighbourTree) : true;
            }
        }

        private static HashSet<BaseFlowBlock> ResolveTargetNeighbours(BaseFlowBlock targetFlowBlock)
        {
            var neighbours = new HashSet<BaseFlowBlock>();
            if (targetFlowBlock == null)
                return neighbours;

            foreach (var predecessor in targetFlowBlock.ReferencedFlowBlocks ?? new ObservableCollection<BaseFlowBlock>())
            {
                if (predecessor == null)
                    continue;

                foreach (var nextFlowBlock in predecessor.GetNextFlowBlocks())
                {
                    if (nextFlowBlock != null && nextFlowBlock != targetFlowBlock)
                        neighbours.Add(nextFlowBlock);
                }
            }

            return neighbours;
        }

        private static HashSet<BaseFlowBlock> ResolveSuccessorTree(
            IEnumerable<BaseFlowBlock> roots,
            BaseFlowBlock targetFlowBlock)
        {
            var successors = new HashSet<BaseFlowBlock>();
            var pending = new Queue<BaseFlowBlock>(roots.Where(x => x != null && x != targetFlowBlock));

            while (pending.Count > 0)
            {
                var flowBlock = pending.Dequeue();
                if (!successors.Add(flowBlock))
                    continue;

                foreach (var nextFlowBlock in flowBlock.GetNextFlowBlocks())
                {
                    if (nextFlowBlock != null && nextFlowBlock != targetFlowBlock)
                        pending.Enqueue(nextFlowBlock);
                }
            }

            return successors;
        }

        private static bool ResolveHideLastLayerNeighboursOption()
        {
            var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.TestDefinitionViewHideLastLayerNeighbours);
            if (option?.Type == OptionElement.OptionType.Boolean && bool.TryParse(option.Value, out var parsed))
                return parsed;

            return true;
        }

        private static void SaveHideLastLayerNeighboursOption(bool value)
        {
            var options = FlowBloxOptions.GetOptionInstance();
            var option = options.GetOption(OptionsConstants.TestDefinitionViewHideLastLayerNeighbours);
            if (option?.Type != OptionElement.OptionType.Boolean)
                return;

            option.Value = value.ToString().ToLowerInvariant();
            _ = Task.Run(() =>
            {
                try
                {
                    options.Save();
                }
                catch (Exception e)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error($"Failed to persist option '{OptionsConstants.TestDefinitionViewHideLastLayerNeighbours}'.", e);
                }
            });
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void MarkDirty(string reason)
        {
            if (_suppressDirtyTracking)
            {
                FlowBloxLogManager.Instance.GetLogger().Info($"TestDefinitionView dirty suppressed: {reason}");
                return;
            }

            if (!IsDirty)
                FlowBloxLogManager.Instance.GetLogger().Info($"TestDefinitionView dirty: {reason}");

            IsDirty = true;
        }
    }
}
