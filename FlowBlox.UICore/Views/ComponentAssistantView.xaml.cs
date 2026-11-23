using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.ViewModels.PropertyView;

namespace FlowBlox.UICore.Views
{
    public partial class ComponentAssistantView : UserControl, IDisposable
    {
        public static readonly DependencyProperty EditorEnabledProperty = DependencyProperty.Register(
            nameof(EditorEnabled), typeof(bool), typeof(ComponentAssistantView), new PropertyMetadata(true));

        private PropertyWindow _window;
        private PropertyViewModel _properties;
        private AIAssistantViewModel _assistant;
        private AssistantComponentContext _context;
        private AiAssistantDebugView _debugWindow;
        private double _collapsedWidth;
        private bool _disposed;

        public string ToggleTooltip => FlowBloxResourceUtil.GetLocalizedString(
            "ComponentAssistant_Toggle", typeof(Resources.PropertyWindow));

        public bool EditorEnabled
        {
            get => (bool)GetValue(EditorEnabledProperty);
            private set => SetValue(EditorEnabledProperty, value);
        }

        public ComponentAssistantView()
        {
            InitializeComponent();
        }

        public void Configure(PropertyWindow window, PropertyWindowArgs args, PropertyViewModel properties)
        {
            if (FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(properties.WorkingCopy))
                return;

            if (args.ReadOnly || !args.DeepCopy || args.Detached || properties.WorkingCopy is not FlowBloxComponent)
                return;

            _window = window;
            _properties = properties;
            _properties.WorkingCopyChanged += Properties_WorkingCopyChanged;
            Visibility = Visibility.Visible;
        }

        private void Properties_WorkingCopyChanged(object sender, EventArgs e)
        {
            ReleaseAssistant();
            if (AssistantToggle.IsChecked == true)
                CreateAssistant();
        }

        private void CreateAssistant()
        {
            if (_assistant != null || _properties.WorkingCopy is not FlowBloxComponent component)
                return;

            var timer = Stopwatch.StartNew();
            var registry = FlowBloxRegistryProvider.GetRegistry();
            _context = new AssistantComponentContext(
                component,
                registry,
                () => !_disposed && _properties.HasActiveTransaction &&
                    ReferenceEquals(_properties.WorkingCopy, component) && FlowBloxRegistryProvider.IsCurrentTransaction(registry),
                _properties.AppendWorkingCopy)
            {
                DispatchAsync = operation => Dispatcher.InvokeAsync(operation).Task.Unwrap(),
                ChangedAsync = () => _properties.RefreshAfterAssistantAsync()
            };

            var contextMs = timer.Elapsed.TotalMilliseconds;
            var control = new AIAssistantControl();
            var controlMs = timer.Elapsed.TotalMilliseconds;
            _assistant = (AIAssistantViewModel)control.DataContext;
            _assistant.ChatViewModel.ConfigureComponentContext(_context);
            var projectGuid = FlowBloxProjectManager.Instance.ActiveProject?.ProjectGuid ?? Guid.Empty;
            var historyScope = _context.HistoryScope;
            _assistant.ConfigureProjectStateAccess(
                () => new AIAssistantProjectStateSnapshot { ProjectGuid = projectGuid, ComponentScope = historyScope },
                _ => Task.FromResult(false));
            var configurationMs = timer.Elapsed.TotalMilliseconds;
            _assistant.ChatViewModel.PropertyChanged += Chat_PropertyChanged;
            _assistant.ChatViewModel.DebugToolsRequested += Chat_DebugToolsRequested;
            control.ConfigurationRequested += (_, _) => ShowConfiguration();
            AssistantHost.Content = control;
            _assistant.NewChatCommand.Execute(null);
            FlowBlox.Core.Logging.FlowBloxLogManager.Instance.GetLogger().Info(
                $"Component assistant opening: context={contextMs:F1} ms, control={controlMs - contextMs:F1} ms, " +
                $"scope/history={configurationMs - controlMs:F1} ms, chat={timer.Elapsed.TotalMilliseconds - configurationMs:F1} ms.");
        }

        private void Chat_DebugToolsRequested(object sender, EventArgs e)
        {
            if (_debugWindow != null)
            {
                _debugWindow.Activate();
                return;
            }
            _debugWindow = new AiAssistantDebugView(_assistant.ChatViewModel.CreateDebugViewModel()) { Owner = _window };
            _debugWindow.Closed += (_, _) => _debugWindow = null;
            _debugWindow.Show();
        }

        private void ShowConfiguration()
        {
            var configuration = _assistant.GetConfiguration(out var error);
            if (!string.IsNullOrWhiteSpace(error))
                return;

            var dialog = new PropertyWindow(new PropertyWindowArgs(configuration, detached: true))
            {
                Owner = _window,
                Width = 980,
                Height = 760
            };
            if (dialog.ShowDialog() == true)
                _assistant.SaveConfiguration(configuration, out _);
        }

        private void Chat_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AiAssistantChatViewModel.IsBusy))
                EditorEnabled = !_assistant.ChatViewModel.IsBusy;
        }

        private void AssistantToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_window == null)
                return;

            var expanded = AssistantToggle.IsChecked == true;
            if (expanded)
            {
                CreateAssistant();
                _collapsedWidth = _window.ActualWidth;
                _window.Width = Math.Min(SystemParameters.WorkArea.Width, _collapsedWidth + AssistantHost.Width + ResizeHandle.Width);
                if (_window.Left + _window.Width > SystemParameters.WorkArea.Right)
                    _window.Left = SystemParameters.WorkArea.Right - _window.Width;
            }
            else if (_collapsedWidth > 0)
            {
                _window.Width = _collapsedWidth;
            }

            AssistantHost.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ResizeHandle.Visibility = AssistantHost.Visibility;
        }

        private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            AssistantHost.Width = Math.Clamp(AssistantHost.Width - e.HorizontalChange, 360, Math.Max(360, _window.ActualWidth - 380));
        }

        private void ReleaseAssistant()
        {
            if (_context != null)
                _context.IsClosed = true;
            if (_assistant != null)
            {
                _debugWindow?.Close();
                _assistant.ChatViewModel.DebugToolsRequested -= Chat_DebugToolsRequested;
                _assistant.ChatViewModel.PropertyChanged -= Chat_PropertyChanged;
                _assistant.Dispose();
            }
            _assistant = null;
            AssistantHost.Content = null;
            EditorEnabled = true;
        }

        public void Dispose()
        {
            _disposed = true;
            if (_properties != null)
                _properties.WorkingCopyChanged -= Properties_WorkingCopyChanged;
            ReleaseAssistant();
        }
    }
}
