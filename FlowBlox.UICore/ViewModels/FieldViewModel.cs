using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Events;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Models;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.ViewModels.FieldView;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace FlowBlox.UICore.ViewModels
{
    public sealed class FieldViewModel : INotifyPropertyChanged, IDisposable
    {
        private const int DefaultFieldViewMaxDisplayLength = 4000;

        private readonly IFlowBloxProjectComponentProvider _componentProvider;
        private readonly IFlowBloxMessageBoxService _messageBoxService;
        private readonly SynchronizationContext? _uiContext;

        private readonly Dictionary<FieldElement, FieldEntryViewModel> _rowsByField = new();
        private readonly List<FieldEntryViewModel> _allRows = new();

        private readonly List<FieldEntryViewModel> _selectedRows = new();

        private FlowBloxRegistry? _registry;
        private FlowBloxProject? _project;

        private string _filterText = string.Empty;
        private bool _showFlowBlock;
        private bool _isSingleLineFieldValues;
        private int _maxDisplayLength = DefaultFieldViewMaxDisplayLength;

        public ObservableCollection<FieldEntryViewModel> Fields { get; } = new();

        public RelayCommand CopyCommand { get; }
        public RelayCommand OpenFieldValueCommand { get; }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText == value)
                    return;

                _filterText = value ?? string.Empty;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public bool ShowFlowBlock
        {
            get => _showFlowBlock;
            set
            {
                if (_showFlowBlock == value)
                    return;

                _showFlowBlock = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FlowBlockColumnWidth));
                SaveFieldViewBooleanOption(OptionsConstants.FieldViewShowFlowBlock, value);
            }
        }

        public bool IsSingleLineFieldValues
        {
            get => _isSingleLineFieldValues;
            set
            {
                if (_isSingleLineFieldValues == value)
                    return;

                _isSingleLineFieldValues = value;
                OnPropertyChanged();
                UpdateFieldValuePresentation();
                SaveFieldViewBooleanOption(OptionsConstants.FieldViewSingleLineFieldValues, value);
            }
        }

        public double FlowBlockColumnWidth => ShowFlowBlock ? 180d : 0d;

        public bool HasFields => _allRows.Count > 0;

        public FieldViewModel()
        {
            _uiContext = SynchronizationContext.Current;
            _componentProvider = FlowBloxServiceLocator.Instance.GetService<IFlowBloxProjectComponentProvider>();
            _messageBoxService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();
            _showFlowBlock = ResolveFieldViewShowFlowBlock();
            _isSingleLineFieldValues = ResolveFieldViewSingleLineFieldValues();

            CopyCommand = new RelayCommand(CopySelection, () => _selectedRows.Count > 0);
            OpenFieldValueCommand = new RelayCommand(OpenFieldValue, () => _selectedRows.Count == 1);

            RebindAndRefresh();
        }

        public void AfterProjectFullyInitialized() => RebindAndRefresh();

        public void UpdateSelection(IEnumerable<FieldEntryViewModel> selectedRows)
        {
            _selectedRows.Clear();
            if (selectedRows != null)
                _selectedRows.AddRange(selectedRows.Where(x => x?.FieldElement != null));

            CopyCommand.Invalidate();
            OpenFieldValueCommand.Invalidate();
        }

        private void Refresh()
        {
            if (!EnsureBoundToActiveProject())
                return;

            ReloadFields();
            UpdateFlowBlockSelectionState();
        }

        private void RebindAndRefresh()
        {
            UnsubscribeAll();

            if (_componentProvider != null)
                _componentProvider.SelectedFlowBlocksChanged += ComponentProvider_SelectedFlowBlocksChanged;

            _project = FlowBloxProjectManager.Instance.ActiveProject;
            _registry = _project?.FlowBloxRegistry;
            if (_registry != null)
            {
                _registry.OnManagedObjectAdded += Registry_OnManagedObjectAdded;
                _registry.OnManagedObjectRemoved += Registry_OnManagedObjectRemoved;
            }

            ReloadFields();
            UpdateFlowBlockSelectionState();
        }

        private void ComponentProvider_SelectedFlowBlocksChanged(object? sender, EventArgs e)
            => SynchronizationContextHelper.PostToUi(_uiContext, UpdateFlowBlockSelectionState);

        private bool EnsureBoundToActiveProject()
        {
            var activeProject = FlowBloxProjectManager.Instance.ActiveProject;
            if (ReferenceEquals(_project, activeProject) &&
                ReferenceEquals(_registry, activeProject?.FlowBloxRegistry))
            {
                return true;
            }

            RebindAndRefresh();
            return false;
        }

        private void ReloadFields()
        {
            _maxDisplayLength = ResolveFieldViewMaxDisplayLength();

            foreach (var field in _rowsByField.Keys.ToList())
            {
                UnsubscribeFieldEvents(field);
            }

            _rowsByField.Clear();
            _allRows.Clear();

            if (_registry != null)
            {
                var fields = new List<FieldElement>();
                fields.AddRange(_registry.GetUserFields());
                fields.AddRange(_registry.GetRuntimeFields(true));

                foreach (var field in fields)
                {
                    AddFieldRow(field);
                }
            }

            ApplyFilter();
        }

        private void Registry_OnManagedObjectAdded(ManagedObjectAddedEventArgs eventArgs)
        {
            if (eventArgs?.AddedObject is not FieldElement fieldElement)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                AddFieldRow(fieldElement);
                ApplyFilter();
                UpdateFlowBlockSelectionState();
            });
        }

        private void Registry_OnManagedObjectRemoved(ManagedObjectRemovedEventArgs eventArgs)
        {
            if (eventArgs?.RemovedObject is not FieldElement fieldElement)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                RemoveFieldRow(fieldElement);
                ApplyFilter();
            });
        }

        private void AddFieldRow(FieldElement fieldElement)
        {
            if (fieldElement == null || _rowsByField.ContainsKey(fieldElement))
                return;

            SubscribeFieldEvents(fieldElement);

            var row = new FieldEntryViewModel(fieldElement);
            row.SetSingleLineFieldValues(IsSingleLineFieldValues);
            row.SetMaxDisplayLength(_maxDisplayLength);
            _rowsByField[fieldElement] = row;
            _allRows.Add(row);
        }

        private void RemoveFieldRow(FieldElement fieldElement)
        {
            if (fieldElement == null)
                return;

            UnsubscribeFieldEvents(fieldElement);

            if (_rowsByField.TryGetValue(fieldElement, out var row))
            {
                _rowsByField.Remove(fieldElement);
                _allRows.Remove(row);
            }
        }

        private void SubscribeFieldEvents(FieldElement fieldElement)
        {
            fieldElement.ComponentChanged -= FieldElement_ComponentChanged;
            fieldElement.ComponentChanged += FieldElement_ComponentChanged;

            fieldElement.OnNameChanged -= FieldElement_OnNameChanged;
            fieldElement.OnNameChanged += FieldElement_OnNameChanged;

            fieldElement.OnValueChanged -= FieldElement_OnValueChanged;
            fieldElement.OnValueChanged += FieldElement_OnValueChanged;

            fieldElement.PropertyChanged -= FieldElement_PropertyChanged;
            fieldElement.PropertyChanged += FieldElement_PropertyChanged;
        }

        private void UnsubscribeFieldEvents(FieldElement fieldElement)
        {
            fieldElement.ComponentChanged -= FieldElement_ComponentChanged;
            fieldElement.OnNameChanged -= FieldElement_OnNameChanged;
            fieldElement.OnValueChanged -= FieldElement_OnValueChanged;
            fieldElement.PropertyChanged -= FieldElement_PropertyChanged;
        }

        private void FieldElement_ComponentChanged(object? sender, EventArgs e)
        {
            if (sender is not FieldElement field)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                if (_rowsByField.TryGetValue(field, out var row))
                {
                    row.UpdateName();
                    row.UpdateValue(field.StringValue, field.Pending);
                    ApplyFilter();
                }
            });
        }

        private void FieldElement_OnNameChanged(FieldElement field, string oldName, string newName)
        {
            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                if (_rowsByField.TryGetValue(field, out var row))
                {
                    row.UpdateName();
                    ApplyFilter();
                }
            });
        }

        private void FieldElement_OnValueChanged(FieldElement field, string oldValue, string newValue)
        {
            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                if (_rowsByField.TryGetValue(field, out var row))
                {
                    row.UpdateValue(newValue, field.Pending);
                }
            });
        }

        private void FieldElement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not FieldElement field)
                return;

            if (e.PropertyName != nameof(FieldElement.IsPassword))
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                if (_rowsByField.TryGetValue(field, out var row))
                    row.UpdateValue(field.StringValue, field.Pending);
            });
        }

        private void UpdateFlowBlockSelectionState()
        {
            var selectedFlowBlocks = _componentProvider?.GetSelectedFlowBlocks();
            var selectedFields = selectedFlowBlocks?
                .SelectMany(x => (x as BaseResultFlowBlock)?.Fields ?? Enumerable.Empty<FieldElement>())
                .ToHashSet()
                ?? new HashSet<FieldElement>();

            foreach (var row in _allRows)
            {
                bool isSelectedByFlowBlock = selectedFields.Contains(row.FieldElement);
                row.IsFlowBlockSelected = isSelectedByFlowBlock;
                row.IsAutoSelected = isSelectedByFlowBlock;
            }
        }

        private void UpdateFieldValuePresentation()
        {
            foreach (var row in _allRows)
            {
                row.SetSingleLineFieldValues(IsSingleLineFieldValues);
            }
        }

        private void ApplyFilter()
        {
            var filter = FilterText?.Trim() ?? string.Empty;

            IEnumerable<FieldEntryViewModel> filtered = _allRows;
            if (!string.IsNullOrEmpty(filter))
            {
                filtered = filtered.Where(x =>
                    x.FullyQualifiedName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            var snapshot = filtered.ToList();

            Fields.Clear();
            foreach (var row in snapshot)
            {
                Fields.Add(row);
            }

            OnPropertyChanged(nameof(HasFields));
            CopyCommand.Invalidate();
            OpenFieldValueCommand.Invalidate();
        }

        private void CopySelection()
        {
            if (_selectedRows.Count == 0)
                return;

            try
            {
                var builder = new StringBuilder();
                foreach (var row in _selectedRows)
                {
                    builder.AppendLine(row.FieldElement?.StringValue ?? string.Empty);
                }

                Clipboard.SetText(builder.ToString());
            }
            catch (Exception ex)
            {
                _messageBoxService?.ShowMessageBox(
                    string.Format(FlowBlox.UICore.Resources.FieldView.Message_CopyFailed_Description, ex.Message),
                    FlowBlox.UICore.Resources.FieldView.Message_CopyFailed_Title,
                    FlowBloxMessageBoxTypes.Error);
            }
        }

        private void OpenFieldValue()
        {
            var selectedRow = _selectedRows.Count == 1 ? _selectedRows[0] : null;
            if (selectedRow?.FieldElement == null)
                return;

            FlowBloxEditingHelper.OpenUsingEditor(
                selectedRow.FieldElement.StringValue,
                selectedRow.FieldElement.FullyQualifiedName);
        }

        private int ResolveFieldViewMaxDisplayLength()
        {
            try
            {
                var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.FieldViewMaxDisplayLength);
                if (option?.Type == OptionElement.OptionType.Integer)
                {
                    var value = option.GetValueInt();
                    if (value > 0)
                        return value;
                }
            }
            catch (Exception e)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to resolve FieldView max display length option.", e);
            }

            return DefaultFieldViewMaxDisplayLength;
        }

        private bool ResolveFieldViewShowFlowBlock()
            => ResolveFieldViewBooleanOption(OptionsConstants.FieldViewShowFlowBlock, defaultValue: false);

        private bool ResolveFieldViewSingleLineFieldValues()
            => ResolveFieldViewBooleanOption(OptionsConstants.FieldViewSingleLineFieldValues, defaultValue: true);

        private static bool ResolveFieldViewBooleanOption(string optionName, bool defaultValue)
        {
            var option = FlowBloxOptions.GetOptionInstance().GetOption(optionName);
            if (option?.Type == OptionElement.OptionType.Boolean && bool.TryParse(option.Value, out var parsed))
                return parsed;

            return defaultValue;
        }

        private static void SaveFieldViewBooleanOption(string optionName, bool value)
        {
            var options = FlowBloxOptions.GetOptionInstance();
            var option = options.GetOption(optionName);
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
                    FlowBloxLogManager.Instance.GetLogger().Error($"Failed to persist FieldView option '{optionName}'.", e);
                }
            });
        }

        private void UnsubscribeAll()
        {
            if (_componentProvider != null)
                _componentProvider.SelectedFlowBlocksChanged -= ComponentProvider_SelectedFlowBlocksChanged;

            if (_registry != null)
            {
                _registry.OnManagedObjectAdded -= Registry_OnManagedObjectAdded;
                _registry.OnManagedObjectRemoved -= Registry_OnManagedObjectRemoved;
            }

        }

        public void Dispose()
        {
            UnsubscribeAll();

            foreach (var fieldElement in _rowsByField.Keys.ToList())
            {
                UnsubscribeFieldEvents(fieldElement);
            }

            _rowsByField.Clear();
            _allRows.Clear();
            Fields.Clear();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
