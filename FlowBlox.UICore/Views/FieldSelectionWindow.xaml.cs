using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.UICore.Models.FieldSelection;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.ViewModels.FieldSelection;
using MahApps.Metro.Controls;
using System.Windows.Controls;
using System.Windows.Input;

namespace FlowBlox.UICore.Views
{
    public partial class FieldSelectionWindow : MetroWindow
    {
        private readonly FieldSelectionWindowArgs _args;
        private readonly FieldSelectionWindowViewModel _vm;

        public FieldSelectionWindowResult Result { get; private set; }

        public FieldSelectionWindow(FieldSelectionWindowArgs args)
        {
            InitializeComponent();

            _args = args ?? new FieldSelectionWindowArgs();
            _vm = new FieldSelectionWindowViewModel(this, _args);
            DataContext = _vm;

            // Apply selection modes based on MultiSelect.
            FieldsListView.SelectionMode = _args.MultiSelect ? SelectionMode.Extended : SelectionMode.Single;
            ProjectPropertiesListView.SelectionMode = _args.MultiSelect ? SelectionMode.Extended : SelectionMode.Single;
            OptionsListView.SelectionMode = _args.MultiSelect ? SelectionMode.Extended : SelectionMode.Single;
            InputFilesListView.SelectionMode = _args.MultiSelect ? SelectionMode.Extended : SelectionMode.Single;
            GenerationStrategyDataListView.SelectionMode = _args.MultiSelect ? SelectionMode.Extended : SelectionMode.Single;

            FieldsListView.SelectionChanged += (_, __) => RefreshOkEnabled();
            ProjectPropertiesListView.SelectionChanged += (_, __) => RefreshOkEnabled();
            OptionsListView.SelectionChanged += (_, __) => RefreshOkEnabled();
            InputFilesListView.SelectionChanged += (_, __) => RefreshOkEnabled();
            GenerationStrategyDataListView.SelectionChanged += (_, __) => RefreshOkEnabled();

            VaultListView.SelectionMode = _args.MultiSelect ? SelectionMode.Extended : SelectionMode.Single;
            VaultListView.SelectionChanged += (_, __) => RefreshOkEnabled();
            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_vm.SelectionMode))
                    RefreshOkEnabled();
            };
            RefreshOkEnabled();
        }

        private void RefreshOkEnabled()
        {
            if (_vm.IsVaultMode)
            {
                OkButton.IsEnabled = VaultListView.SelectedItems.Count > 0;
            }
            else if (_vm.IsFieldsMode)
            {
                OkButton.IsEnabled = FieldsListView.SelectedItems != null && FieldsListView.SelectedItems.Count > 0;
            }
            else if (_vm.IsProjectPropertiesMode)
            {
                OkButton.IsEnabled = ProjectPropertiesListView.SelectedItems != null && ProjectPropertiesListView.SelectedItems.Count > 0;
            }
            else if (_vm.IsOptionsMode)
            {
                OkButton.IsEnabled = OptionsListView.SelectedItems != null && OptionsListView.SelectedItems.Count > 0;
            }
            else if (_vm.IsInputFilesMode)
            {
                OkButton.IsEnabled = InputFilesListView.SelectedItems != null && InputFilesListView.SelectedItems.Count > 0;
            }
            else
            {
                OkButton.IsEnabled = GenerationStrategyDataListView.SelectedItems != null && GenerationStrategyDataListView.SelectedItems.Count > 0;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            if (DialogResult != true)
                return;

            var res = new FieldSelectionWindowResult
            {
                SelectionMode = _vm.SelectionMode,
                IsRequired = _vm.IsRequired
            };

            if (_vm.IsVaultMode)
            {
                res.SelectedVaultReferences = VaultListView.SelectedItems.Cast<FlowBlox.Core.Util.Vault.VaultReference>()
                    .Select(item => item.Reference).ToList();
                res.IsRequired = false;
            }
            else if (_vm.IsFieldsMode)
            {
                var selected = FieldsListView.SelectedItems.Cast<object>()
                    .Select(x => x as FieldRowViewModel)
                    .Where(x => x?.FieldElement != null)
                    .Select(x => x.FieldElement)
                    .ToList();

                res.SelectedFields = selected;
                res.SelectedOptions = new List<OptionElement>();
                res.SelectedProjectProperties = new List<FlowBloxProjectPropertyElement>();
                res.SelectedInputFiles = new List<FlowBloxInputFilePlaceholderElement>();
                res.SelectedGenerationStrategyData = new List<FlowBloxGenerationStrategyPlaceholderElement>();
            }
            else if (_vm.IsProjectPropertiesMode)
            {
                var selected = ProjectPropertiesListView.SelectedItems.Cast<object>()
                    .Select(x => x as ProjectPropertyRowViewModel)
                    .Where(x => x?.ProjectPropertyElement != null)
                    .Select(x => x.ProjectPropertyElement)
                    .ToList();

                res.SelectedProjectProperties = selected;
                res.SelectedFields = new List<FieldElement>();
                res.SelectedOptions = new List<OptionElement>();
                res.SelectedInputFiles = new List<FlowBloxInputFilePlaceholderElement>();
                res.SelectedGenerationStrategyData = new List<FlowBloxGenerationStrategyPlaceholderElement>();
                res.IsRequired = false;
            }
            else if (_vm.IsOptionsMode)
            {
                var selected = OptionsListView.SelectedItems.Cast<object>()
                    .Select(x => x as OptionRowViewModel)
                    .Where(x => x?.OptionElement != null)
                    .Select(x => x.OptionElement)
                    .ToList();

                res.SelectedOptions = selected;
                res.SelectedFields = new List<FieldElement>();
                res.SelectedProjectProperties = new List<FlowBloxProjectPropertyElement>();
                res.SelectedInputFiles = new List<FlowBloxInputFilePlaceholderElement>();
                res.SelectedGenerationStrategyData = new List<FlowBloxGenerationStrategyPlaceholderElement>();
                res.IsRequired = false;
            }
            else if (_vm.IsInputFilesMode)
            {
                var selected = InputFilesListView.SelectedItems.Cast<object>()
                    .Select(x => x as InputFileRowViewModel)
                    .Where(x => x?.InputFileElement != null)
                    .Select(x => x.InputFileElement)
                    .ToList();

                res.SelectedInputFiles = selected;
                res.SelectedFields = new List<FieldElement>();
                res.SelectedProjectProperties = new List<FlowBloxProjectPropertyElement>();
                res.SelectedOptions = new List<OptionElement>();
                res.SelectedGenerationStrategyData = new List<FlowBloxGenerationStrategyPlaceholderElement>();
                res.IsRequired = false;
            }
            else
            {
                var selected = GenerationStrategyDataListView.SelectedItems.Cast<object>()
                    .Select(x => x as GenerationStrategyDataRowViewModel)
                    .Where(x => x?.GenerationStrategyDataElement != null)
                    .Select(x => x.GenerationStrategyDataElement)
                    .ToList();

                res.SelectedGenerationStrategyData = selected;
                res.SelectedFields = new List<FieldElement>();
                res.SelectedProjectProperties = new List<FlowBloxProjectPropertyElement>();
                res.SelectedOptions = new List<OptionElement>();
                res.SelectedInputFiles = new List<FlowBloxInputFilePlaceholderElement>();
                res.IsRequired = false;
            }

            Result = res;
        }

        private void FieldsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (OkButton.IsEnabled)
                _vm.OkCommand.Execute(null);
        }

        private void ProjectPropertiesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (OkButton.IsEnabled)
                _vm.OkCommand.Execute(null);
        }

        private void OptionsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (OkButton.IsEnabled)
                _vm.OkCommand.Execute(null);
        }

        private void InputFilesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (OkButton.IsEnabled)
                _vm.OkCommand.Execute(null);
        }

        private void GenerationStrategyDataListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (OkButton.IsEnabled)
                _vm.OkCommand.Execute(null);
        }
    }
}
