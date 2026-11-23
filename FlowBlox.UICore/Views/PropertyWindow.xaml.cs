using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Models.Base;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Factory;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.ViewModels.PropertyView;
using MahApps.Metro.Controls;
using System.ComponentModel;
using System.Windows.Threading;

namespace FlowBlox.UICore.Views
{
    public class PropertyWindowArgs
    {
        public object Target { get; set; }
        public object Parent { get; set; }
        public bool ReadOnly { get; set; }
        public bool DeepCopy { get; set; }
        public bool CanSave { get; set; }
        public bool Detached { get; set; }
        public bool IsNew { get; set; }

        public string PreselectedProperty { get; set; }
        public FlowBloxReactiveObject PreselectedInstance { get; set; }

        public PropertyWindowArgs()
        {
            ReadOnly = false;
            DeepCopy = true;
            CanSave = true;
            Detached = false;
            IsNew = false;
        }

        public PropertyWindowArgs(
            object target,
            object parent = null,
            bool readOnly = false,
            bool deepCopy = true,
            bool canSave = true,
            string preselectedProperty = null,
            FlowBloxReactiveObject preselectedInstance = null,
            bool detached = false,
            bool isNew = false)
        {
            Target = target;
            Parent = parent;
            ReadOnly = readOnly;
            DeepCopy = deepCopy;
            CanSave = canSave;
            PreselectedProperty = preselectedProperty;
            PreselectedInstance = preselectedInstance;
            Detached = detached;
            IsNew = isNew;
        }
    }

    public partial class PropertyWindow : MetroWindow
    {
        private PropertyWindowArgs _propertyWindowArgs;
        private bool _componentPopUpRequested;

        public PropertyWindowCommitStatus CommitStatus =>
            (DataContext as PropertyWindowViewModel)?.CommitStatus ?? PropertyWindowCommitStatus.None;

        public PropertyWindow()
        {
            InitializeComponent();
        }

        private void TransactionMonitorButton_Click(object sender, System.Windows.RoutedEventArgs e)
            => TransactionMonitorWindowFactory.Show();

        private void TransactionMenuButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button == null || button.ContextMenu == null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }

        public PropertyWindow(PropertyWindowArgs propertyWindowArgs) : this()
        {
            _propertyWindowArgs = propertyWindowArgs;
            var viewModel = new PropertyWindowViewModel(this, propertyWindowArgs);
            viewModel.PropertyViewModel.ValidationFailed += PropertyViewModel_ValidationFailed;
            DataContext = viewModel;
            ComponentAssistant.Configure(this, propertyWindowArgs, viewModel.PropertyViewModel);
            Closing += PropertyView_Closing;
            Loaded += PropertyWindow_Loaded;
        }

        private void PropertyWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_componentPopUpRequested)
                return;

            _componentPopUpRequested = true;
            Dispatcher.BeginInvoke(ShowComponentPopUpIfAvailable, DispatcherPriority.ApplicationIdle);
        }

        private void ShowComponentPopUpIfAvailable()
        {
            if (_propertyWindowArgs?.Target == null)
                return;

            var componentPopUpService = FlowBloxServiceLocator.Instance.GetService<IComponentPopupService>();
            componentPopUpService?.ShowFor(_propertyWindowArgs.Target, this);
        }

        private void PropertyView_Closing(object sender, CancelEventArgs e)
        {
            ComponentAssistant.Dispose();
            if (DataContext is PropertyWindowViewModel viewModel)
                viewModel.HandleClosing(sender, e);
        }

        private void PropertyViewModel_ValidationFailed(object? sender, EventArgs e)
        {
            if (_propertyWindowArgs?.Target == null)
                return;

            var componentPopUpService = FlowBloxServiceLocator.Instance.GetService<IComponentPopupService>();
            componentPopUpService?.ShowFor(
                _propertyWindowArgs.Target,
                ComponentPopupEvent.ValidationFailed,
                this);
        }

    }
}
