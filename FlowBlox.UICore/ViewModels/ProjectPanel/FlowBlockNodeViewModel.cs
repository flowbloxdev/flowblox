using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace FlowBlox.UICore.ViewModels.ProjectPanel
{
    public sealed class FlowBlockNodeViewModel : INotifyPropertyChanged, IFlowBloxUIElement, IDisposable
    {
        internal const double FixedWidth = 328d;
        private const double HeaderHeight = 26d;
        private const double NoteHeight = 150d;
        private const double RowHeight = 24d;
        private const double NotificationHeight = 30d;
        internal const double MaxBlockHeight = 300d;
        private static int _nextLayoutTraceId;

        private readonly SynchronizationContext _uiContext;
        private bool _isSelected;
        private bool _isConnectionTargetBlocked;
        private bool _isReference;
        private bool _isRuntimeFocused;
        private bool _suppressStorageLayoutPropertyChanges;
        private string _warningMessage = string.Empty;
        private string _errorMessage = string.Empty;
        private readonly FlowBlockNodeCenterPreservationGuard _centerPreservationGuard;
        private readonly FlowBlockNodePropertyRenderer _propertyRenderer;
        private readonly FlowBloxComponentChangeSubscription _componentChangeSubscription;
        private readonly FlowBloxDefinedManagedObjectsChangeSubscription _definedManagedObjectsChangeSubscription;

        public FlowBlockNodeViewModel(BaseFlowBlock flowBlock)
        {
            _uiContext = SynchronizationContext.Current;
            InternalFlowBlock = flowBlock ?? throw new ArgumentNullException(nameof(flowBlock));
            LayoutTraceId = Interlocked.Increment(ref _nextLayoutTraceId);
            _centerPreservationGuard = new FlowBlockNodeCenterPreservationGuard(this);
            _propertyRenderer = new FlowBlockNodePropertyRenderer(flowBlock);
            _componentChangeSubscription = new FlowBloxComponentChangeSubscription(
                flowBlock,
                _ => FlowBlock_OnComponentChanged());
            flowBlock.PropertyChanged += FlowBlock_PropertyChanged;
            flowBlock.OnWarn += FlowBlock_OnWarn;
            flowBlock.OnError += FlowBlock_OnError;
            flowBlock.OnUndoWarn += FlowBlock_OnUndoWarn;
            flowBlock.OnUndoError += FlowBlock_OnUndoError;
            if (flowBlock is BaseResultFlowBlock resultFlowBlock)
                resultFlowBlock.OutputDatasetProcessingChanged += ResultFlowBlock_OutputDatasetProcessingChanged;
            _definedManagedObjectsChangeSubscription = new FlowBloxDefinedManagedObjectsChangeSubscription(
                flowBlock,
                _ => DefinedManagedObjectChanged());
            flowBlock.RefreshNotExecutedState();
            RefreshRows(preserveCenter: false);
            LogLayoutTrace(
                $"Node VM created, node={FormatLayoutTraceNode()}, y={Y:0.##}, height={Height:0.##}, rows={Rows.Count}");
        }

        internal int LayoutTraceId { get; }
        public bool IsConnectionTargetBlocked
        {
            get => _isConnectionTargetBlocked;
            set
            {
                if (_isConnectionTargetBlocked == value)
                    return;
                _isConnectionTargetBlocked = value;
                OnPropertyChanged();
            }
        }
        public BaseFlowBlock InternalFlowBlock { get; }
        public ObservableCollection<FlowBlockRenderRowViewModel> Rows { get; } = new();
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ElementSelectedChangedByUser;

        public double X
        {
            get => InternalFlowBlock.Location.X;
            set
            {
                var x = Math.Max(0, (int)Math.Round(value));
                if (InternalFlowBlock.Location.X == x)
                    return;

                InternalFlowBlock.Location = new Point(x, InternalFlowBlock.Location.Y);
                OnPositionChanged();
            }
        }

        public double Y
        {
            get => InternalFlowBlock.Location.Y;
            set
            {
                var y = Math.Max(0, (int)Math.Round(value));
                if (InternalFlowBlock.Location.Y == y)
                    return;

                InternalFlowBlock.Location = new Point(InternalFlowBlock.Location.X, y);
                OnPositionChanged();
            }
        }

        public double Width => FixedWidth;
        public double Height => IsNote ? NoteHeight : Math.Min(MaxBlockHeight, DesiredHeight);
        public bool HasVerticalOverflow => !IsNote && DesiredHeight > MaxBlockHeight;
        public bool IsNote => InternalFlowBlock is NoteFlowBlock;
        public string NoteText => (InternalFlowBlock as NoteFlowBlock)?.Note ?? string.Empty;
        public string Title => FlowBloxComponentHelper.GetDisplayName(InternalFlowBlock);
        public string Name => InternalFlowBlock.Name;
        public bool HasBreakpoint => InternalFlowBlock.BreakPoint;
        public bool HasOverriddenNotifications => InternalFlowBlock.HasOverriddenNotifications;
        public bool IsNotExecuted => InternalFlowBlock.IsNotExecuted;
        public bool HasExecutionIndex => InternalFlowBlock.ExecutionIndex >= 0;
        public string ExecutionIndexText => HasExecutionIndex ? $"#{InternalFlowBlock.ExecutionIndex}" : string.Empty;
        public bool HasIterationProgress => InternalFlowBlock is BaseResultFlowBlock resultFlowBlock &&
                                            resultFlowBlock.OutputDatasets_Count > 0 &&
                                            (resultFlowBlock.OutputDataset_CurrentlyProcessingIndex > 0 ||
                                             resultFlowBlock.OutputDatasetProcessingCompleted);
        public bool IsIterationCompleted => InternalFlowBlock is BaseResultFlowBlock resultFlowBlock &&
                                            resultFlowBlock.OutputDatasetProcessingCompleted;
        public string IterationProgressText
        {
            get
            {
                if (InternalFlowBlock is not BaseResultFlowBlock resultFlowBlock)
                    return string.Empty;

                var total = resultFlowBlock.OutputDatasets_Count;
                var current = resultFlowBlock.OutputDatasetProcessingCompleted
                    ? total
                    : resultFlowBlock.OutputDataset_CurrentlyProcessingIndex;
                return total > 0 ? $"{current}/{total}" : string.Empty;
            }
        }
        public string IterationProgressToolTip
        {
            get
            {
                if (InternalFlowBlock is not BaseResultFlowBlock resultFlowBlock)
                    return string.Empty;

                var total = resultFlowBlock.OutputDatasets_Count;
                var current = resultFlowBlock.OutputDatasetProcessingCompleted
                    ? total
                    : resultFlowBlock.OutputDataset_CurrentlyProcessingIndex;
                var format = FlowBloxResourceUtil.GetLocalizedString(
                    "Tooltip_IterationProgress",
                    typeof(Resources.ProjectPanel));
                return string.Format(format, current, total);
            }
        }
        public bool HasNotification => HasWarning || HasError;
        public bool HasWarning => !string.IsNullOrWhiteSpace(_warningMessage);
        public bool HasError => !HasWarning && !string.IsNullOrWhiteSpace(_errorMessage);
        public string NotificationMessage => HasWarning ? _warningMessage : _errorMessage;
        private double RuntimeInfoHeight => HasExecutionIndex ? RowHeight : 0d;
        private double NotificationInfoHeight => HasNotification ? NotificationHeight : 0d;
        private double DesiredHeight => HeaderHeight + (Rows.Count * RowHeight) + RuntimeInfoHeight + NotificationInfoHeight + 8d;
        internal double StorageLocationYOffset => NotificationInfoHeight / 2d;

        public bool ElementSelected => IsSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMarked));
                ElementSelectedChangedByUser?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsReference
        {
            get => _isReference;
            set
            {
                if (_isReference == value)
                    return;

                _isReference = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMarked));
            }
        }

        public bool IsMarked => IsSelected || IsReference;

        public bool IsRuntimeFocused
        {
            get => _isRuntimeFocused;
            set
            {
                if (_isRuntimeFocused == value)
                    return;

                _isRuntimeFocused = value;
                OnPropertyChanged();
            }
        }

        public void RefreshRows() => RefreshRows(preserveCenter: true);

        public void RefreshRowsWithoutCenterPreservation() => RefreshRows(preserveCenter: false);

        public void SetCenterPreservationSuspended(bool suspended)
            => _centerPreservationGuard.SetSuspended(suspended);

        private void RefreshRows(bool preserveCenter)
        {
            void Refresh()
            {
                Rows.Clear();
                if (IsNote)
                {
                    OnPropertyChanged(nameof(NoteText));
                }
                else
                {
                    _propertyRenderer.Render(Rows);
                }

                OnPropertyChanged(nameof(Rows));
                NotifyLayoutChanged();
                NotifyRuntimeStateChanged(preserveCenter: false);
            }

            if (preserveCenter && !_centerPreservationGuard.IsSuspended)
                UpdatePreservingCenter(Refresh);
            else
                Refresh();
        }

        public void NotifyRuntimeStateChanged() => NotifyRuntimeStateChanged(preserveCenter: true);

        private void NotifyRuntimeStateChanged(bool preserveCenter)
        {
            if (preserveCenter && !_centerPreservationGuard.IsSuspended)
            {
                UpdatePreservingCenter(() => NotifyRuntimeStateChanged(preserveCenter: false));
                return;
            }

            OnPropertyChanged(nameof(HasBreakpoint));
            OnPropertyChanged(nameof(HasExecutionIndex));
            OnPropertyChanged(nameof(ExecutionIndexText));
            NotifyLayoutChanged();
        }

        private void FlowBlock_OnComponentChanged()
            => SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                _definedManagedObjectsChangeSubscription.Rebind();

                OnPropertyChanged(nameof(HasOverriddenNotifications));
                LogRefreshRowsRequested("ComponentChanged");
                RefreshRows();
            });

        private void DefinedManagedObjectChanged()
            => SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                LogRefreshRowsRequested("DefinedManagedObject.PropertyChanged");
                RefreshRows();
            });

        private void FlowBlock_OnWarn(BaseRuntime runtime, string message)
        {
            if (runtime?.ExecutionFlowEnabled == false)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () => SetWarning(message));
        }

        private void FlowBlock_OnError(BaseRuntime runtime, string message)
        {
            if (runtime?.ExecutionFlowEnabled == false)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () => SetError(message));
        }

        private void FlowBlock_OnUndoWarn(BaseRuntime runtime)
        {
            if (runtime?.ExecutionFlowEnabled == false)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () => SetWarning(string.Empty));
        }

        private void FlowBlock_OnUndoError(BaseRuntime runtime)
        {
            if (runtime?.ExecutionFlowEnabled == false)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () => SetError(string.Empty));
        }

        private void ResultFlowBlock_OutputDatasetProcessingChanged(BaseRuntime runtime)
        {
            if (runtime?.ExecutionFlowEnabled == false)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, NotifyIterationProgressChanged);
        }

        private void NotifyIterationProgressChanged()
        {
            OnPropertyChanged(nameof(HasIterationProgress));
            OnPropertyChanged(nameof(IsIterationCompleted));
            OnPropertyChanged(nameof(IterationProgressText));
            OnPropertyChanged(nameof(IterationProgressToolTip));
        }

        internal void SetStorageLayoutPropertyChangeSuppressed(bool suppressed)
            => _suppressStorageLayoutPropertyChanges = suppressed;

        private void FlowBlock_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suppressStorageLayoutPropertyChanges &&
                e.PropertyName == nameof(BaseFlowBlock.Location))
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () => HandleFlowBlockPropertyChanged(e));
        }

        private void HandleFlowBlockPropertyChanged(PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BaseFlowBlock.Location))
                OnPositionChanged();
            else if (e.PropertyName == nameof(BaseFlowBlock.Name))
            {
                OnPropertyChanged(nameof(Name));
                LogRefreshRowsRequested($"PropertyChanged:{e.PropertyName}");
                RefreshRows();
            }
            else if (IsNote && e.PropertyName == nameof(NoteFlowBlock.Note))
                OnPropertyChanged(nameof(NoteText));
            else if (e.PropertyName == nameof(BaseFlowBlock.BreakPoint))
                NotifyRuntimeStateChanged();
            else if (e.PropertyName == nameof(BaseFlowBlock.HasOverriddenNotifications))
                OnPropertyChanged(nameof(HasOverriddenNotifications));
            else if (e.PropertyName == nameof(BaseFlowBlock.IsNotExecuted))
                OnPropertyChanged(nameof(IsNotExecuted));
            else if (e.PropertyName == nameof(BaseFlowBlock.ExecutionIndex))
                NotifyRuntimeStateChanged();
            else if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                LogRefreshRowsRequested($"PropertyChanged:{e.PropertyName}");
                RefreshRows();
            }
        }

        private void OnPositionChanged()
        {
            OnPropertyChanged(nameof(X));
            OnPropertyChanged(nameof(Y));
        }

        private void SetWarning(string message)
        {
            if (string.Equals(_warningMessage, message ?? string.Empty, StringComparison.Ordinal))
                return;

            UpdatePreservingCenter(() =>
            {
                _warningMessage = message ?? string.Empty;
                NotifyNotificationChanged();
            });
        }

        private void SetError(string message)
        {
            if (string.Equals(_errorMessage, message ?? string.Empty, StringComparison.Ordinal))
                return;

            UpdatePreservingCenter(() =>
            {
                _errorMessage = message ?? string.Empty;
                NotifyNotificationChanged();
            });
        }

        private void NotifyNotificationChanged()
        {
            OnPropertyChanged(nameof(HasNotification));
            OnPropertyChanged(nameof(HasWarning));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(NotificationMessage));
            NotifyLayoutChanged();
        }

        private void NotifyLayoutChanged()
        {
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(HasVerticalOverflow));
        }

        private void UpdatePreservingCenter(Action update)
            => _centerPreservationGuard.PreserveCenter(update);

        private void LogRefreshRowsRequested(string reason)
            => LogLayoutTrace(
                $"Node refresh requested, reason={reason}, node={FormatLayoutTraceNode()}, y={Y:0.##}, height={Height:0.##}, rows={Rows.Count}");

        internal string FormatLayoutTraceNode()
            => $"{Name} [{InternalFlowBlock.GetType().Name}], vm={LayoutTraceId}, flowBlock={RuntimeHelpers.GetHashCode(InternalFlowBlock)}";

        private static void LogLayoutTrace(string message)
            => Trace.TraceInformation($"ProjectPanel layout trace: {message}");

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Dispose()
        {
            LogLayoutTrace(
                $"Node VM disposed, node={FormatLayoutTraceNode()}, y={Y:0.##}, height={Height:0.##}, rows={Rows.Count}");
            InternalFlowBlock.PropertyChanged -= FlowBlock_PropertyChanged;
            InternalFlowBlock.OnWarn -= FlowBlock_OnWarn;
            InternalFlowBlock.OnError -= FlowBlock_OnError;
            InternalFlowBlock.OnUndoWarn -= FlowBlock_OnUndoWarn;
            InternalFlowBlock.OnUndoError -= FlowBlock_OnUndoError;
            if (InternalFlowBlock is BaseResultFlowBlock resultFlowBlock)
                resultFlowBlock.OutputDatasetProcessingChanged -= ResultFlowBlock_OutputDatasetProcessingChanged;
            _definedManagedObjectsChangeSubscription.Dispose();
            _componentChangeSubscription.Dispose();
            _centerPreservationGuard.Dispose();
        }
    }
}
