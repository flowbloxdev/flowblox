using FlowBlox.Core.Actions;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Events;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.ControlFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.ObjectManager;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Events;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Models;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.Util;
using FlowBlox.UICore.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;

namespace FlowBlox.UICore.ViewModels.ProjectPanel
{
    public sealed class ProjectPanelWpfViewModel : INotifyPropertyChanged, IDisposable
    {
        private const double DefaultCanvasWidth = GlobalConstants.GridSizeX;
        private const double DefaultCanvasHeight = GlobalConstants.GridSizeY;
        private const double DefaultAutoIncreaseHorizontalReserve = 1000d;
        private const double DefaultAutoIncreaseVerticalReserve = 700d;
        private const double CenterSnapTolerance = 10d;
        private readonly Dictionary<BaseFlowBlock, FlowBlockNodeViewModel> _nodesByFlowBlock = new();
        private readonly IFlowBloxProjectComponentProvider _componentProvider;
        private readonly IDialogService _dialogService;
        private readonly IFlowBloxMessageBoxService _messageBoxService;
        private readonly IRuntimeStateService _runtimeStateService;
        private readonly SynchronizationContext _uiContext;
        private FlowBloxRegistry _registry;
        private FlowBloxProject _project;
        private FlowBlockNodeViewModel _selectedNode;
        private FlowBlockArrowViewModel _selectedArrow;
        private bool _isConnectionMode;
        private bool _isTemporaryConnectionMode;
        private bool _isRuntimeActive;
        private bool _isRuntimePaused;
        private bool _isExternalProjectEditActive;
        private readonly List<BaseFlowBlock> _copiedFlowBlocks = new();
        private readonly Dictionary<FlowBlockNodeViewModel, FlowBloxCreateAction> _pendingInsertedCreateActions = new();
        private readonly Dictionary<FlowBlockNodeViewModel, NodeLayoutSnapshot> _historyActionLayoutSnapshots = new();
        private readonly Dictionary<FlowBlockNodeViewModel, System.Drawing.Point> _storageLocationSnapshots = new();
        private ProjectChangelist _subscribedChangelist;

        public ProjectPanelWpfViewModel()
        {
            _uiContext = SynchronizationContext.Current;
            _componentProvider = FlowBloxServiceLocator.Instance.GetService<IFlowBloxProjectComponentProvider>();
            _dialogService = FlowBloxServiceLocator.Instance.GetService<IDialogService>();
            _messageBoxService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();
            _runtimeStateService = FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>();

            SelectModeCommand = new RelayCommand(() => IsConnectionMode = false, CanEditGrid);
            ConnectionModeCommand = new RelayCommand(() => IsConnectionMode = true, CanEditGrid);
            SelectAllCommand = new RelayCommand(SelectAll, CanEditGrid);
            SelectLeftCommand = new RelayCommand(() => SelectDirectional(Direction.Left), CanEditGrid);
            SelectRightCommand = new RelayCommand(() => SelectDirectional(Direction.Right), CanEditGrid);
            SelectUpCommand = new RelayCommand(() => SelectDirectional(Direction.Up), CanEditGrid);
            SelectDownCommand = new RelayCommand(() => SelectDirectional(Direction.Down), CanEditGrid);
            AutoLayoutCommand = new RelayCommand(AutoLayout, () => HasProject() && !IsProjectEditingReadOnly);
            ResetNotificationsCommand = new RelayCommand(() => ResetNotifications(), () => HasProject() && !IsProjectEditingReadOnly);
            DeleteSelectionCommand = new RelayCommand(DeleteSelection, () => HasProject() && SelectedArrow == null && SelectedNodes.Any() && !IsProjectEditingReadOnly);
            EditSelectionCommand = new RelayCommand(EditSelection, () => SelectedNode != null);
            ExecuteRuntimeCommand = new RelayCommand(() => ExecuteRuntimeRequested?.Invoke(this, EventArgs.Empty), () => CanExecuteRuntime);
            PauseRuntimeCommand = new RelayCommand(() => PauseRuntimeRequested?.Invoke(this, EventArgs.Empty), () => CanPauseRuntime);
            StopRuntimeCommand = new RelayCommand(() => StopRuntimeRequested?.Invoke(this, EventArgs.Empty), () => CanStopRuntime);
            GridSettingsCommand = new RelayCommand(ShowGridSettings, () => HasProject() && !IsExternalProjectEditActive && (!IsRuntimeActive || IsRuntimePaused));
            ManageInputFieldsCommand = new RelayCommand(() => ShowUserFields(nameof(UserFieldObjectManager.InputFields)), CanManageUserFields);
            ManageMemoryFieldsCommand = new RelayCommand(() => ShowUserFields(nameof(UserFieldObjectManager.MemoryFields)), CanManageUserFields);
            ToggleBreakpointCommand = new RelayCommand(ToggleBreakpoint, () => SelectedNode != null);
            DefineExecutionIndexCommand = new RelayCommand(DefineExecutionIndex, () => CanManageExecutionIndex);
            RemoveExecutionIndexCommand = new RelayCommand(RemoveExecutionIndex, () => CanManageExecutionIndex);
            ShowInputInsightCommand = new RelayCommand(ShowInputInsight, () => CanShowInputInsight);
            ShowOutputInsightCommand = new RelayCommand(ShowOutputInsight, () => CanShowOutputInsight);
            ManageNotificationsCommand = new RelayCommand(ManageNotifications, () => CanManageNotifications);
            CopyRowValueCommand = new RelayCommand(
                parameter => CopyRowValue(parameter as FlowBlockRenderRowViewModel),
                parameter => parameter is FlowBlockRenderRowViewModel row && row.CanCopyValue);
            RemoveConnectionCommand = new RelayCommand(
                parameter => RemoveConnection(parameter as FlowBlockArrowViewModel),
                parameter => parameter is FlowBlockArrowViewModel arrow && arrow.CanRemove && HasProject() && !IsProjectEditingReadOnly);

            FlowBloxProjectManager.Instance.ProjectChanged += ProjectManager_ProjectChanged;
            if (_runtimeStateService != null)
            {
                _runtimeStateService.StateChanged += RuntimeStateService_StateChanged;
                UpdateRuntimeState(
                    _runtimeStateService.IsRuntimeActive,
                    _runtimeStateService.IsRuntimePaused,
                    _runtimeStateService.IsExternalProjectEditActive);
            }

            Rebind(FlowBloxProjectManager.Instance.ActiveProject);
        }

        public ObservableCollection<FlowBlockNodeViewModel> Nodes { get; } = new();
        public ObservableCollection<FlowBlockArrowViewModel> Arrows { get; } = new();

        public RelayCommand SelectModeCommand { get; }
        public RelayCommand ConnectionModeCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand SelectLeftCommand { get; }
        public RelayCommand SelectRightCommand { get; }
        public RelayCommand SelectUpCommand { get; }
        public RelayCommand SelectDownCommand { get; }
        public RelayCommand AutoLayoutCommand { get; }
        public RelayCommand ResetNotificationsCommand { get; }
        public RelayCommand DeleteSelectionCommand { get; }
        public RelayCommand EditSelectionCommand { get; }
        public RelayCommand ExecuteRuntimeCommand { get; }
        public RelayCommand PauseRuntimeCommand { get; }
        public RelayCommand StopRuntimeCommand { get; }
        public RelayCommand GridSettingsCommand { get; }
        public RelayCommand ManageInputFieldsCommand { get; }
        public RelayCommand ManageMemoryFieldsCommand { get; }
        public RelayCommand ToggleBreakpointCommand { get; }
        public RelayCommand DefineExecutionIndexCommand { get; }
        public RelayCommand RemoveExecutionIndexCommand { get; }
        public RelayCommand ShowInputInsightCommand { get; }
        public RelayCommand ShowOutputInsightCommand { get; }
        public RelayCommand ManageNotificationsCommand { get; }
        public RelayCommand CopyRowValueCommand { get; }
        public RelayCommand RemoveConnectionCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ExecuteRuntimeRequested;
        public event EventHandler PauseRuntimeRequested;
        public event EventHandler StopRuntimeRequested;

        public FlowBlockNodeViewModel SelectedNode
        {
            get => _selectedNode;
            private set
            {
                if (ReferenceEquals(_selectedNode, value))
                    return;

                _selectedNode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanManageExecutionIndex));
                OnPropertyChanged(nameof(CanManageNotifications));
                OnPropertyChanged(nameof(CanShowInputInsight));
                OnPropertyChanged(nameof(CanShowOutputInsight));
                OnPropertyChanged(nameof(EditSelectionHeader));
                OnPropertyChanged(nameof(ToggleBreakpointHeader));
                EditSelectionCommand.Invalidate();
                InvalidateSelectionCommands();
            }
        }

        public FlowBlockArrowViewModel SelectedArrow
        {
            get => _selectedArrow;
            private set
            {
                if (ReferenceEquals(_selectedArrow, value))
                    return;

                if (_selectedArrow != null)
                    _selectedArrow.IsSelected = false;

                _selectedArrow = value;

                if (_selectedArrow != null)
                    _selectedArrow.IsSelected = true;

                OnPropertyChanged();
                InvalidateSelectionCommands();
            }
        }

        public bool IsConnectionMode
        {
            get => _isConnectionMode || _isTemporaryConnectionMode;
            set
            {
                var previousValue = IsConnectionMode;
                if (_isConnectionMode == value && (!_isTemporaryConnectionMode || value))
                    return;

                _isConnectionMode = value;
                if (!value)
                    _isTemporaryConnectionMode = false;

                NotifyConnectionModeChanged(previousValue);
            }
        }

        public bool IsRuntimeActive
        {
            get => _isRuntimeActive;
            private set
            {
                if (_isRuntimeActive == value)
                    return;

                _isRuntimeActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsProjectEditingReadOnly));
                OnPropertyChanged(nameof(EditSelectionHeader));
                OnPropertyChanged(nameof(CanManageExecutionIndex));
                InvalidateAllCommands();
            }
        }

        public bool IsRuntimePaused
        {
            get => _isRuntimePaused;
            private set
            {
                if (_isRuntimePaused == value)
                    return;

                _isRuntimePaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanExecuteRuntime));
                OnPropertyChanged(nameof(CanPauseRuntime));
                OnPropertyChanged(nameof(CanStopRuntime));
                InvalidateAllCommands();
            }
        }

        public bool IsExternalProjectEditActive
        {
            get => _isExternalProjectEditActive;
            private set
            {
                if (_isExternalProjectEditActive == value)
                    return;

                _isExternalProjectEditActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsProjectEditingReadOnly));
                OnPropertyChanged(nameof(EditSelectionHeader));
                OnPropertyChanged(nameof(CanManageExecutionIndex));
                OnPropertyChanged(nameof(CanExecuteRuntime));
                InvalidateAllCommands();
            }
        }

        public IEnumerable<FlowBlockNodeViewModel> SelectedNodes => Nodes.Where(x => x.IsSelected);
        public bool HasNodes => Nodes.Count > 0;
        public bool IsSelectionMode => !IsConnectionMode;
        public string ModeText => IsConnectionMode
            ? FlowBloxResourceUtil.GetLocalizedString("Toolbar_Connect", typeof(Resources.ProjectPanel))
            : FlowBloxResourceUtil.GetLocalizedString("Toolbar_Select", typeof(Resources.ProjectPanel));
        public string ToggleBreakpointHeader => SelectedNode?.InternalFlowBlock.BreakPoint == true
            ? FlowBloxResourceUtil.GetLocalizedString("ContextMenu_RemoveBreakpoint", typeof(Resources.ProjectPanel))
            : FlowBloxResourceUtil.GetLocalizedString("ContextMenu_SetBreakpoint", typeof(Resources.ProjectPanel));
        public string DeleteGestureText => GetGestureText(Key.Delete);
        public double CanvasWidth => _project?.GridSizeX > 0 ? _project.GridSizeX : DefaultCanvasWidth;
        public double CanvasHeight => _project?.GridSizeY > 0 ? _project.GridSizeY : DefaultCanvasHeight;
        public bool IsProjectEditingReadOnly => IsRuntimeActive || IsExternalProjectEditActive;
        public string EditSelectionHeader => IsProjectEditingReadOnly
            ? FlowBloxResourceUtil.GetLocalizedString("ContextMenu_ViewProperties", typeof(Resources.ProjectPanel))
            : FlowBloxResourceUtil.GetLocalizedString("ContextMenu_EditProperties", typeof(Resources.ProjectPanel));
        public bool CanManageExecutionIndex => SelectedNode != null && SelectedNode.InternalFlowBlock is not NoteFlowBlock && !IsProjectEditingReadOnly;
        public bool CanManageNotifications => SelectedNode?.InternalFlowBlock.NotificationTypes?.Any() == true;
        public bool CanShowInputInsight => SelectedNode?.InternalFlowBlock.InputDataset_CurrentlyProcessing != null;
        public bool CanShowOutputInsight => SelectedNode?.InternalFlowBlock is BaseResultFlowBlock resultFlowBlock &&
                                            resultFlowBlock.OutputDataset_CurrentlyProcessing != null;
        public bool CanStartMarqueeSelection => CanEditGrid() && !IsConnectionMode;
        public bool CanExecuteRuntime => HasProject() && !IsExternalProjectEditActive && (!IsRuntimeActive || IsRuntimePaused);
        public bool CanPauseRuntime => HasProject() && IsRuntimeActive && !IsRuntimePaused;
        public bool CanStopRuntime => HasProject() && IsRuntimeActive;
        public bool CanCopySelection => SelectedNodes.Any();
        public bool CanPasteSelection => _copiedFlowBlocks.Count > 0 && HasProject() && !IsProjectEditingReadOnly;
        public bool CanStartConnectionFrom(FlowBlockNodeViewModel node)
            => !IsProjectEditingReadOnly && node?.InternalFlowBlock is not null and not NoteFlowBlock;
        public bool CanPreviewConnection(FlowBlockNodeViewModel startNode, FlowBlockNodeViewModel endNode)
            => CanConnect(startNode, endNode);

        public void SetTemporaryConnectionMode(bool enabled)
        {
            var previousValue = IsConnectionMode;
            var temporaryValue = enabled && !_isConnectionMode;
            if (_isTemporaryConnectionMode == temporaryValue)
                return;

            _isTemporaryConnectionMode = temporaryValue;
            NotifyConnectionModeChanged(previousValue);
        }

        public void SelectNode(FlowBlockNodeViewModel node, bool toggle, bool extend)
        {
            if (node == null)
                return;

            SelectedArrow = null;

            if (!toggle && !extend)
            {
                foreach (var item in Nodes)
                    item.IsSelected = false;
            }

            node.IsSelected = toggle ? !node.IsSelected : true;
            SelectedNode = node.IsSelected ? node : SelectedNodes.LastOrDefault();
            MarkReferences();
            PublishSelectedFlowBlocks();
            InvalidateSelectionCommands();
        }

        public void SelectArrow(FlowBlockArrowViewModel arrow)
        {
            if (arrow == null)
                return;

            foreach (var node in Nodes)
                node.IsSelected = false;

            SelectedNode = null;
            SelectedArrow = arrow;
            MarkReferences();
            PublishSelectedFlowBlocks();
            InvalidateSelectionCommands();
        }

        public void SelectNodesInRectangle(Rect selectionBounds)
        {
            SelectedArrow = null;

            FlowBlockNodeViewModel lastSelected = null;
            foreach (var node in Nodes)
            {
                var isInside = node.X > selectionBounds.Left &&
                               node.X < selectionBounds.Right &&
                               node.Y > selectionBounds.Top &&
                               node.Y < selectionBounds.Bottom;

                node.IsSelected = isInside;
                if (isInside)
                    lastSelected = node;
            }

            SelectedNode = lastSelected;
            MarkReferences();
            PublishSelectedFlowBlocks();
            InvalidateSelectionCommands();
        }

        public FlowBlockNodeViewModel GetNodeAt(Point location)
            => Nodes.LastOrDefault(node =>
                location.X >= node.X &&
                location.X <= node.X + node.Width &&
                location.Y >= node.Y &&
                location.Y <= node.Y + node.Height);

        public bool ConnectNodes(FlowBlockNodeViewModel startNode, FlowBlockNodeViewModel endNode)
        {
            if (!CanConnect(startNode, endNode))
            {
                if (startNode != null && endNode != null)
                    ShowMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Message_ConnectBlocked_Description", typeof(Resources.ProjectPanel)),
                        FlowBloxResourceUtil.GetLocalizedString("Message_ConnectBlocked_Title", typeof(Resources.ProjectPanel)),
                        FlowBloxMessageBoxTypes.Information);

                return false;
            }

            FlowBloxBaseAction connectAction;
            if (startNode.InternalFlowBlock is RecursiveTargetFlowBlockBase recursiveCallFlowBlock)
            {
                connectAction = new FlowBloxInvokeAction
                {
                    From = recursiveCallFlowBlock,
                    To = endNode.InternalFlowBlock
                };
            }
            else
            {
                connectAction = new FlowBloxConnectAction
                {
                    From = startNode.InternalFlowBlock,
                    To = endNode.InternalFlowBlock
                };
            }

            connectAction.Invoke();
            _componentProvider?.GetCurrentChangelist()?.AddChange(connectAction);
            RefreshArrows();
            return true;
        }

        public Point GetSnappedNodePosition(FlowBlockNodeViewModel movedNode, double proposedX, double proposedY)
        {
            if (movedNode == null || SelectedNodes.Count() > 1)
                return new Point(proposedX, proposedY);

            var movedCenterX = proposedX + movedNode.Width / 2d;
            var movedCenterY = proposedY + movedNode.Height / 2d;
            var otherNodes = Nodes.Where(node => !ReferenceEquals(node, movedNode)).ToList();

            var horizontalCandidates = otherNodes
                .Where(node => node.Y < movedCenterY && node.Y + node.Height > movedCenterY)
                .ToList();
            var verticalCandidates = otherNodes
                .Where(node => node.X < movedCenterX && node.X + node.Width > movedCenterX)
                .ToList();

            var neighborNodes = new List<FlowBlockNodeViewModel>();
            var left = horizontalCandidates.Where(node => node.X < proposedX).OrderBy(node => node.X).LastOrDefault();
            var right = horizontalCandidates.Where(node => node.X > proposedX).OrderBy(node => node.X).FirstOrDefault();
            var up = verticalCandidates.Where(node => node.Y < proposedY).OrderBy(node => node.Y).LastOrDefault();
            var down = verticalCandidates.Where(node => node.Y > proposedY).OrderBy(node => node.Y).FirstOrDefault();

            if (left != null)
                neighborNodes.Add(left);
            if (right != null)
                neighborNodes.Add(right);
            if (up != null)
                neighborNodes.Add(up);
            if (down != null)
                neighborNodes.Add(down);

            var snappedX = proposedX;
            var snappedY = proposedY;
            var snappedHorizontal = false;
            var snappedVertical = false;

            foreach (var neighbor in neighborNodes)
            {
                var neighborCenterX = neighbor.X + neighbor.Width / 2d;
                var neighborCenterY = neighbor.Y + neighbor.Height / 2d;

                if (!snappedHorizontal && Math.Abs(movedCenterX - neighborCenterX) < CenterSnapTolerance)
                {
                    snappedHorizontal = true;
                    snappedX = neighborCenterX - movedNode.Width / 2d;
                }

                if (!snappedVertical && Math.Abs(movedCenterY - neighborCenterY) < CenterSnapTolerance)
                {
                    snappedVertical = true;
                    snappedY = neighborCenterY - movedNode.Height / 2d;
                }
            }

            return new Point(Math.Max(0d, snappedX), Math.Max(0d, snappedY));
        }

        public void NotifyNodeMoved()
        {
            RefreshArrowGeometry();
        }

        public void MarkRuntimeFocus(BaseFlowBlock flowBlock)
        {
            foreach (var node in Nodes)
                node.IsRuntimeFocused = flowBlock != null && ReferenceEquals(node.InternalFlowBlock, flowBlock);
        }

        public void ResetNotifications(BaseRuntime runtime = null)
        {
            if (!HasProject())
                return;

            foreach (var flowBlock in _registry.GetFlowBlocks().OfType<BaseFlowBlock>())
            {
                flowBlock.ResetNotifications(runtime);
                if (flowBlock is BaseResultFlowBlock resultFlowBlock)
                    resultFlowBlock.ResetOutputDatasetProcessing(runtime);
            }

            MarkRuntimeFocus(null);
        }

        public void CommitNodeMove(FlowBlockNodeViewModel node, System.Drawing.Point from, System.Drawing.Point to)
            => CommitNodeMove(node, from, to, null);

        public void CommitNodeMove(
            FlowBlockNodeViewModel node,
            System.Drawing.Point from,
            System.Drawing.Point to,
            IReadOnlyDictionary<FlowBlockNodeViewModel, Point> movedNodeStartPositions)
        {
            if (node == null)
                return;

            _pendingInsertedCreateActions.Remove(node, out var pendingCreateAction);
            if (from == to && pendingCreateAction == null)
                return;

            var moveAction = new FlowBloxMoveAction
            {
                FlowBlock = node.InternalFlowBlock,
                From = from,
                To = to
            };

            if (pendingCreateAction != null)
                moveAction.AssociatedActions.Add(pendingCreateAction);

            AddAssociatedMoveActions(moveAction, node, movedNodeStartPositions);
            _componentProvider?.GetCurrentChangelist()?.AddChange(moveAction);
        }

        private static void AddAssociatedMoveActions(
            FlowBloxMoveAction rootMoveAction,
            FlowBlockNodeViewModel rootNode,
            IReadOnlyDictionary<FlowBlockNodeViewModel, Point> movedNodeStartPositions)
        {
            if (rootMoveAction == null || movedNodeStartPositions == null)
                return;

            foreach (var item in movedNodeStartPositions)
            {
                var node = item.Key;
                if (node == null || ReferenceEquals(node, rootNode))
                    continue;

                var from = new System.Drawing.Point(
                    (int)Math.Round(item.Value.X),
                    (int)Math.Round(item.Value.Y));
                var to = new System.Drawing.Point(
                    (int)Math.Round(node.X),
                    (int)Math.Round(node.Y));
                if (from == to)
                    continue;

                rootMoveAction.AssociatedActions.Add(new FlowBloxMoveAction
                {
                    FlowBlock = node.InternalFlowBlock,
                    From = from,
                    To = to
                });
            }
        }

        public void Refresh()
        {
            if (!EnsureBoundToActiveProject())
                return;

            SyncNodesWithRegistry();

            foreach (var node in Nodes)
                node.RefreshRows();

            RefreshArrows();
        }

        public bool CanCreateFlowBlockFromDrop(IDataObject dataObject)
            => ResolveDraggedFlowBlockType(dataObject) != null && _registry != null && !IsProjectEditingReadOnly;

        public FlowBlockNodeViewModel CreateFlowBlockFromDrop(
            IDataObject dataObject,
            Point location,
            double horizontalReserve,
            double verticalReserve)
        {
            var flowBlockType = ResolveDraggedFlowBlockType(dataObject);
            if (flowBlockType == null || _registry == null)
                return null;

            var createdFlowBlock = _registry.CreateFlowBlockUnregistered(flowBlockType);
            createdFlowBlock.Location = new System.Drawing.Point(
                Math.Max(0, (int)Math.Round(location.X)),
                Math.Max(0, (int)Math.Round(location.Y)));

            if (!AssignFlowBlockName(createdFlowBlock))
                return null;

            _registry.PostProcessFlowBlockCreated(createdFlowBlock);

            var createAction = new FlowBloxCreateAction
            {
                FlowBlock = createdFlowBlock
            };
            createAction.Invoke();
            _componentProvider?.GetCurrentChangelist()?.AddChange(createAction);

            EnsureCanvasContainsBounds(
                createdFlowBlock.Location.X,
                createdFlowBlock.Location.Y,
                FlowBlockNodeViewModel.FixedWidth,
                FlowBlockNodeViewModel.MaxBlockHeight,
                horizontalReserve,
                verticalReserve);

            if (_nodesByFlowBlock.TryGetValue(createdFlowBlock, out var node))
            {
                EnsureCanvasContainsNode(node, horizontalReserve, verticalReserve);
                SelectNode(node, toggle: false, extend: false);
                RefreshArrows();
                return node;
            }

            RefreshArrows();
            return null;
        }

        public void MoveNodeToCanvasPosition(
            FlowBlockNodeViewModel node,
            Point location,
            double horizontalReserve,
            double verticalReserve)
        {
            if (node == null)
                return;

            var snappedPosition = GetSnappedNodePosition(node, location.X, location.Y);
            node.X = snappedPosition.X;
            node.Y = snappedPosition.Y;
            EnsureCanvasContainsNode(node, horizontalReserve, verticalReserve);
        }

        public void CancelInsertedNode(FlowBlockNodeViewModel node)
        {
            if (node == null || !Nodes.Contains(node) || IsProjectEditingReadOnly)
                return;

            if (_pendingInsertedCreateActions.Remove(node, out var pendingCreateAction))
            {
                pendingCreateAction.Undo();
                return;
            }

            var deleteAction = new FlowBloxDeleteAction
            {
                FlowBlock = node.InternalFlowBlock
            };

            deleteAction.Invoke();
            _componentProvider?.GetCurrentChangelist()?.AddChange(deleteAction);
        }

        public void CopySelection()
        {
            _copiedFlowBlocks.Clear();
            var node = SelectedNode ?? SelectedNodes.FirstOrDefault();
            if (node == null)
                return;

            var copier = new DynamicDeepCopier(FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(node.InternalFlowBlock));
            var copy = (BaseFlowBlock)copier.Copy(node.InternalFlowBlock);
            copy.Name = string.Format(
                FlowBloxResourceUtil.GetLocalizedString("Copy_NameFormat", typeof(Resources.ProjectPanel)),
                node.Name);
            _copiedFlowBlocks.Add(copy);
        }

        public FlowBlockNodeViewModel PasteCopiedSelection(double horizontalReserve, double verticalReserve)
        {
            if (!CanPasteSelection)
                return null;

            var pastedFlowBlocks = new List<BaseFlowBlock>();
            foreach (var copiedFlowBlock in _copiedFlowBlocks)
            {
                var copier = new DynamicDeepCopier(FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(copiedFlowBlock));
                var paste = (BaseFlowBlock)copier.Copy(copiedFlowBlock);
                paste.Location = new System.Drawing.Point(0, 0);
                pastedFlowBlocks.Add(paste);
                break;
            }

            var firstPaste = pastedFlowBlocks.FirstOrDefault();
            if (firstPaste == null)
                return null;

            var createAction = new FlowBloxCreateAction
            {
                FlowBlock = firstPaste
            };

            createAction.AssociatedActions.AddRange(pastedFlowBlocks.Skip(1).Select(flowBlock => new FlowBloxCreateAction
            {
                FlowBlock = flowBlock
            }));

            createAction.Invoke();

            foreach (var node in Nodes)
                node.IsSelected = false;

            FlowBlockNodeViewModel firstNode = null;
            foreach (var pastedFlowBlock in pastedFlowBlocks)
            {
                if (!_nodesByFlowBlock.TryGetValue(pastedFlowBlock, out var node))
                    continue;

                node.IsSelected = true;
                firstNode ??= node;
                EnsureCanvasContainsNode(node, horizontalReserve, verticalReserve);
            }

            SelectedNode = firstNode;
            SelectedArrow = null;
            MarkReferences();
            PublishSelectedFlowBlocks();
            RefreshArrows();
            InvalidateSelectionCommands();

            if (firstNode != null)
                _pendingInsertedCreateActions[firstNode] = createAction;

            return firstNode;
        }

        public void EnsureCanvasContainsNode(FlowBlockNodeViewModel node, double horizontalReserve, double verticalReserve)
        {
            if (node == null)
                return;

            EnsureCanvasContainsBounds(
                node.X,
                node.Y,
                node.Width,
                node.Height,
                horizontalReserve,
                verticalReserve);
        }

        private void EnsureCanvasContainsBounds(
            double x,
            double y,
            double width,
            double height,
            double horizontalReserve,
            double verticalReserve)
        {
            if (_project?.AutoIncreaseGridSize != true)
                return;

            var requiredWidth = (int)Math.Ceiling(x + width + Math.Max(0d, horizontalReserve));
            var requiredHeight = (int)Math.Ceiling(y + height + Math.Max(0d, verticalReserve));
            var changed = false;

            if (requiredWidth > CanvasWidth)
            {
                _project.GridSizeX = requiredWidth;
                OnPropertyChanged(nameof(CanvasWidth));
                changed = true;
            }

            if (requiredHeight > CanvasHeight)
            {
                _project.GridSizeY = requiredHeight;
                OnPropertyChanged(nameof(CanvasHeight));
                changed = true;
            }

            if (changed)
                GridSettingsCommand.Invalidate();
        }

        private void ProjectManager_ProjectChanged(object sender, ProjectChangedEventArgs e)
            => SynchronizationContextHelper.PostToUi(_uiContext, () => Rebind(e.NewProject));

        private void Rebind(FlowBloxProject project)
        {
            SubscribeCurrentChangelist();
            UnsubscribeRegistry();
            ClearNodes();

            _project = project;
            _registry = project?.FlowBloxRegistry;
            if (_registry != null)
            {
                _registry.OnFlowBlockAdded += Registry_OnFlowBlockAdded;
                _registry.OnFlowBlockRemoved += Registry_OnFlowBlockRemoved;

                foreach (var flowBlock in _registry.GetFlowBlocks())
                    AddNode(flowBlock);
            }

            RefreshArrows();
            OnPropertyChanged(nameof(HasNodes));
            OnPropertyChanged(nameof(CanvasWidth));
            OnPropertyChanged(nameof(CanvasHeight));
            AutoLayoutCommand.Invalidate();
            GridSettingsCommand.Invalidate();
            ManageInputFieldsCommand.Invalidate();
            ManageMemoryFieldsCommand.Invalidate();
        }

        private bool EnsureBoundToActiveProject()
        {
            var activeProject = FlowBloxProjectManager.Instance.ActiveProject;
            if (ReferenceEquals(_project, activeProject))
                return _registry != null;

            Rebind(activeProject);
            return _registry != null;
        }

        private void SyncNodesWithRegistry()
        {
            if (_registry == null)
            {
                ClearNodes();
                return;
            }

            var flowBlocks = _registry.GetFlowBlocks().ToList();
            var flowBlockSet = flowBlocks.ToHashSet();

            foreach (var staleFlowBlock in _nodesByFlowBlock.Keys.Where(x => !flowBlockSet.Contains(x)).ToList())
                RemoveNode(staleFlowBlock);

            foreach (var flowBlock in flowBlocks)
                AddNode(flowBlock);
        }

        private void Registry_OnFlowBlockAdded(FlowBlockAddedEventArgs eventArgs)
        {
            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                AddNode(eventArgs.AddedFlowBlock);
                RefreshArrows();
            });
        }

        private void Registry_OnFlowBlockRemoved(FlowBlockRemovedEventArgs eventArgs)
        {
            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                RemoveNode(eventArgs.RemovedFlowBlock);
                RefreshArrows();
            });
        }

        private void AddNode(BaseFlowBlock flowBlock)
        {
            if (flowBlock == null || _nodesByFlowBlock.ContainsKey(flowBlock))
                return;

            var node = new FlowBlockNodeViewModel(flowBlock);
            node.PropertyChanged += Node_PropertyChanged;
            flowBlock.ComponentChanged += FlowBlock_ComponentChanged;
            flowBlock.ReferencedFlowBlocksChanged += FlowBlock_ReferencedFlowBlocksChanged;
            _nodesByFlowBlock[flowBlock] = node;
            Nodes.Add(node);
            EnsureCanvasContainsNode(node, DefaultAutoIncreaseHorizontalReserve, DefaultAutoIncreaseVerticalReserve);
            OnPropertyChanged(nameof(HasNodes));
        }

        private void RemoveNode(BaseFlowBlock flowBlock)
        {
            if (flowBlock == null || !_nodesByFlowBlock.TryGetValue(flowBlock, out var node))
                return;

            node.PropertyChanged -= Node_PropertyChanged;
            flowBlock.ComponentChanged -= FlowBlock_ComponentChanged;
            flowBlock.ReferencedFlowBlocksChanged -= FlowBlock_ReferencedFlowBlocksChanged;
            node.Dispose();
            _pendingInsertedCreateActions.Remove(node);
            _nodesByFlowBlock.Remove(flowBlock);
            Nodes.Remove(node);
            if (ReferenceEquals(SelectedNode, node))
                SelectedNode = SelectedNodes.LastOrDefault();
            PublishSelectedFlowBlocks();
            OnPropertyChanged(nameof(HasNodes));
        }

        private void SubscribeCurrentChangelist()
        {
            var changelist = _componentProvider?.GetCurrentChangelist();
            if (ReferenceEquals(_subscribedChangelist, changelist))
                return;

            UnsubscribeCurrentChangelist();
            _subscribedChangelist = changelist;
            if (_subscribedChangelist == null)
                return;

            _subscribedChangelist.BeforeUndo += Changelist_BeforeHistoryAction;
            _subscribedChangelist.BeforeRedo += Changelist_BeforeHistoryAction;
            _subscribedChangelist.AfterUndo += Changelist_AfterHistoryAction;
            _subscribedChangelist.AfterRedo += Changelist_AfterHistoryAction;
        }

        private void UnsubscribeCurrentChangelist()
        {
            if (_subscribedChangelist == null)
                return;

            _subscribedChangelist.BeforeUndo -= Changelist_BeforeHistoryAction;
            _subscribedChangelist.BeforeRedo -= Changelist_BeforeHistoryAction;
            _subscribedChangelist.AfterUndo -= Changelist_AfterHistoryAction;
            _subscribedChangelist.AfterRedo -= Changelist_AfterHistoryAction;
            _subscribedChangelist = null;
        }

        private void Changelist_BeforeHistoryAction(object sender, ProjectChangelistActionEventArgs e)
        {
            LogLayoutTrace(
                $"Before {e.Operation}: index={e.ChangeIndex}, action={FormatAction(e.Action)}, nodes={Nodes.Count}");
            CaptureHistoryActionLayoutSnapshots(e);
        }

        private void Changelist_AfterHistoryAction(object sender, ProjectChangelistActionEventArgs e)
        {
            LogLayoutTrace(
                $"After {e.Operation}: index={e.ChangeIndex}, action={FormatAction(e.Action)}, nodes={Nodes.Count}, snapshots={_historyActionLayoutSnapshots.Count}");
            RestoreHistoryActionLayoutCenters(e);
        }

        private void CaptureHistoryActionLayoutSnapshots(ProjectChangelistActionEventArgs e)
        {
            _historyActionLayoutSnapshots.Clear();
            foreach (var node in Nodes)
            {
                node.SetCenterPreservationSuspended(true);
                _historyActionLayoutSnapshots[node] = new NodeLayoutSnapshot(
                    node.InternalFlowBlock.Location,
                    node.Y + node.Height / 2d);

                LogLayoutTrace(
                    $"Snapshot {e.Operation}: node={FormatNode(node)}, location={node.InternalFlowBlock.Location}, y={node.Y:0.##}, height={node.Height:0.##}, centerY={(node.Y + node.Height / 2d):0.##}, rows={node.Rows.Count}");
            }
        }

        private void RestoreHistoryActionLayoutCenters(ProjectChangelistActionEventArgs e)
        {
            if (_historyActionLayoutSnapshots.Count == 0)
            {
                LogLayoutTrace($"Restore {e.Operation}: no snapshots available.");
                foreach (var node in Nodes)
                    node.SetCenterPreservationSuspended(false);

                return;
            }

            try
            {
                foreach (var node in Nodes)
                {
                    var locationBeforeRefresh = node.InternalFlowBlock.Location;
                    var yBeforeRefresh = node.Y;
                    var heightBeforeRefresh = node.Height;
                    var rowsBeforeRefresh = node.Rows.Count;

                    node.RefreshRowsWithoutCenterPreservation();
                    if (!_historyActionLayoutSnapshots.TryGetValue(node, out var snapshot))
                    {
                        LogLayoutTrace(
                            $"Restore {e.Operation}: skipped no snapshot, node={FormatNode(node)}, locationBeforeRefresh={locationBeforeRefresh}, locationAfterRefresh={node.InternalFlowBlock.Location}, yBeforeRefresh={yBeforeRefresh:0.##}, yAfterRefresh={node.Y:0.##}, heightBeforeRefresh={heightBeforeRefresh:0.##}, heightAfterRefresh={node.Height:0.##}, rowsBeforeRefresh={rowsBeforeRefresh}, rowsAfterRefresh={node.Rows.Count}");
                        continue;
                    }

                    if (IsNodeMovedByAction(e.Action, node))
                    {
                        LogLayoutTrace(
                            $"Restore {e.Operation}: skipped moved by action, node={FormatNode(node)}, snapshotLocation={snapshot.Location}, currentLocation={node.InternalFlowBlock.Location}, yBeforeRefresh={yBeforeRefresh:0.##}, yAfterRefresh={node.Y:0.##}, heightBeforeRefresh={heightBeforeRefresh:0.##}, heightAfterRefresh={node.Height:0.##}, snapshotCenterY={snapshot.CenterY:0.##}, rowsBeforeRefresh={rowsBeforeRefresh}, rowsAfterRefresh={node.Rows.Count}");
                        continue;
                    }

                    var newY = Math.Max(0d, snapshot.CenterY - node.Height / 2d);
                    if (Math.Abs(node.Y - newY) > 0.1d)
                    {
                        LogLayoutTrace(
                            $"Restore {e.Operation}: applying center, node={FormatNode(node)}, oldY={node.Y:0.##}, newY={newY:0.##}, heightBeforeRefresh={heightBeforeRefresh:0.##}, heightAfterRefresh={node.Height:0.##}, snapshotCenterY={snapshot.CenterY:0.##}, rowsBeforeRefresh={rowsBeforeRefresh}, rowsAfterRefresh={node.Rows.Count}");
                        node.Y = newY;
                    }
                    else
                    {
                        LogLayoutTrace(
                            $"Restore {e.Operation}: no y change needed, node={FormatNode(node)}, y={node.Y:0.##}, heightBeforeRefresh={heightBeforeRefresh:0.##}, heightAfterRefresh={node.Height:0.##}, snapshotCenterY={snapshot.CenterY:0.##}, rowsBeforeRefresh={rowsBeforeRefresh}, rowsAfterRefresh={node.Rows.Count}");
                    }
                }
            }
            finally
            {
                foreach (var node in Nodes)
                    node.SetCenterPreservationSuspended(false);
            }

            _historyActionLayoutSnapshots.Clear();
            RefreshArrows();
        }

        private static void LogLayoutTrace(string message)
            => Trace.TraceInformation($"ProjectPanel layout trace: {message}");

        private static string FormatAction(FlowBloxBaseAction action)
            => action == null
                ? "<null>"
                : $"{action.GetType().Name}(associated={action.AssociatedActions?.Count ?? 0})";

        private static string FormatNode(FlowBlockNodeViewModel node)
            => node == null
                ? "<null>"
                : $"{node.Name} [{node.InternalFlowBlock?.GetType().Name}]";

        private static bool IsNodeMovedByAction(FlowBloxBaseAction action, FlowBlockNodeViewModel node)
        {
            if (action == null || node?.InternalFlowBlock == null)
                return false;

            if (action is FlowBloxMoveAction moveAction &&
                ReferenceEquals(moveAction.FlowBlock, node.InternalFlowBlock))
            {
                return true;
            }

            return action.AssociatedActions?.Any(associatedAction => IsNodeMovedByAction(associatedAction, node)) == true;
        }

        private void FlowBlock_ComponentChanged(object sender, EventArgs e)
        {
            if (sender is not BaseFlowBlock flowBlock)
                return;

            SynchronizationContextHelper.PostToUi(_uiContext, () => RefreshArrowsFor(flowBlock));
        }

        private void FlowBlock_ReferencedFlowBlocksChanged(ReferencedFlowBlocksChangedEventArgs eventArgs)
        {
            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                if (eventArgs == null)
                {
                    RefreshArrows();
                    return;
                }

                RefreshArrowsFor(eventArgs.AffectedFlowBlocks.ToArray());
            });
        }

        private void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlowBlockNodeViewModel.X) ||
                e.PropertyName == nameof(FlowBlockNodeViewModel.Y) ||
                e.PropertyName == nameof(FlowBlockNodeViewModel.Height))
            {
                RefreshArrowGeometry(sender as FlowBlockNodeViewModel);
            }
            else if (e.PropertyName == nameof(FlowBlockNodeViewModel.HasBreakpoint) &&
                     ReferenceEquals(sender, SelectedNode))
            {
                OnPropertyChanged(nameof(ToggleBreakpointHeader));
            }
        }

        private void RefreshArrowGeometry(FlowBlockNodeViewModel node = null)
        {
            foreach (var arrow in Arrows)
            {
                if (node == null ||
                    ReferenceEquals(arrow.From, node) ||
                    ReferenceEquals(arrow.To, node))
                {
                    arrow.NotifyGeometryChanged();
                }
            }
        }

        private void RefreshArrows()
        {
            var selectedArrow = SelectedArrow;
            var desiredArrows = BuildArrowViewModels();

            foreach (var arrow in Arrows.ToList())
                if (!desiredArrows.Any(x => x.HasSameIdentity(arrow)))
                    Arrows.Remove(arrow);

            for (var i = 0; i < desiredArrows.Count; i++)
            {
                var desiredArrow = desiredArrows[i];
                var existingArrow = Arrows.FirstOrDefault(x => desiredArrow.HasSameIdentity(x));
                if (existingArrow != null)
                {
                    var currentIndex = Arrows.IndexOf(existingArrow);
                    if (currentIndex != i)
                        Arrows.Move(currentIndex, i);

                    continue;
                }

                if (desiredArrow.HasSameIdentity(selectedArrow))
                {
                    desiredArrow.IsSelected = true;
                    _selectedArrow = desiredArrow;
                }

                Arrows.Insert(i, desiredArrow);
            }

            if (selectedArrow != null && !Arrows.Any(x => x.IsSelected))
                SelectedArrow = null;
        }

        private void RefreshArrowsFor(params BaseFlowBlock[] flowBlocks)
        {
            if (flowBlocks == null || flowBlocks.Length == 0)
            {
                RefreshArrows();
                return;
            }

            var affectedNodes = flowBlocks
                .Where(x => x != null)
                .Select(x => _nodesByFlowBlock.TryGetValue(x, out var node) ? node : null)
                .Where(x => x != null)
                .ToHashSet();

            if (affectedNodes.Count == 0)
                return;

            RefreshArrows();

            foreach (var arrow in Arrows)
            {
                if (affectedNodes.Contains(arrow.From) || affectedNodes.Contains(arrow.To))
                    arrow.NotifyGeometryChanged();
            }
        }

        private List<FlowBlockArrowViewModel> BuildArrowViewModels()
        {
            var arrows = new List<FlowBlockArrowViewModel>();
            foreach (var node in Nodes)
            {
                foreach (var reference in node.InternalFlowBlock.ReferencedFlowBlocks)
                {
                    if (_nodesByFlowBlock.TryGetValue(reference, out var from))
                        arrows.Add(new FlowBlockArrowViewModel(from, node, "invoke"));
                }

                if (node.InternalFlowBlock.HasIterationContext &&
                    node.InternalFlowBlock.IterationContext != null &&
                    _nodesByFlowBlock.TryGetValue(node.InternalFlowBlock.IterationContext, out var iterationSource))
                {
                    arrows.Add(new FlowBlockArrowViewModel(node, iterationSource, "iteration", 10d));
                }

                if (node.InternalFlowBlock is RecursiveTargetFlowBlockBase recursive &&
                    recursive.TargetFlowBlock != null &&
                    _nodesByFlowBlock.TryGetValue(recursive.TargetFlowBlock, out var recursionTarget))
                {
                    arrows.Add(new FlowBlockArrowViewModel(
                        node,
                        recursionTarget,
                        "recursive",
                        18d,
                        FlowBloxResourceUtil.GetLocalizedString("ArrowLabel_RecursiveCall", typeof(Resources.ProjectPanel))));
                }
            }

            return arrows;
        }

        private void SelectAll()
        {
            foreach (var node in Nodes)
                node.IsSelected = true;
            SelectedNode = Nodes.LastOrDefault();
            MarkReferences();
            PublishSelectedFlowBlocks();
            InvalidateSelectionCommands();
        }

        private void SelectDirectional(Direction direction)
        {
            var anchor = SelectedNode ?? SelectedNodes.LastOrDefault();
            if (anchor == null)
                return;

            foreach (var node in Nodes)
            {
                if (direction == Direction.Left && node.X < anchor.X + 20d)
                    node.IsSelected = true;
                else if (direction == Direction.Right && node.X > anchor.X - 20d)
                    node.IsSelected = true;
                else if (direction == Direction.Up && node.Y < anchor.Y + 20d)
                    node.IsSelected = true;
                else if (direction == Direction.Down && node.Y > anchor.Y - 20d)
                    node.IsSelected = true;
            }

            MarkReferences();
            PublishSelectedFlowBlocks();
            InvalidateSelectionCommands();
        }

        private void MarkReferences()
        {
            foreach (var node in Nodes)
                node.IsReference = false;

            foreach (var selected in SelectedNodes)
            {
                var context = selected.InternalFlowBlock.IterationContext;
                if (context != null && _nodesByFlowBlock.TryGetValue(context, out var referenceNode))
                    referenceNode.IsReference = true;
            }
        }

        private void AutoLayout()
        {
            if (!HasProject())
                return;

            if (_registry?.GetStartFlowBlock() == null)
            {
                ShowMessage(
                    FlowBloxResourceUtil.GetLocalizedString("Message_AutoLayoutNoStart_Description", typeof(Resources.ProjectPanel)),
                    FlowBloxResourceUtil.GetLocalizedString("Message_AutoLayoutNoStart_Title", typeof(Resources.ProjectPanel)),
                    FlowBloxMessageBoxTypes.Information);
                return;
            }

            SyncNodeSizesToModel();
            FlowBlockAutoLayoutAdjuster.Adjust(_registry.GetFlowBlocks());
            var moveActions = FlowBlockAutoLayoutAdjuster.GetRecordedMoveActions();
            FlowBloxServiceLocator.Instance
                .GetService<IFlowBloxActionHistoryService>()
                ?.RegisterAutoLayoutMoves(moveActions);
            RefreshArrows();
        }

        internal void SyncNodeSizesToModel()
            => SyncNodeSizes(node => node.Height);

        internal void PrepareNodeLocationForStorage()
        {
            RestoreNodeLocationAfterStorage();

            foreach (var node in Nodes)
            {
                var flowBlock = node.InternalFlowBlock;
                var originalLocation = flowBlock.Location;
                _storageLocationSnapshots[node] = originalLocation;
                node.SetStorageLayoutPropertyChangeSuppressed(true);

                var storageLocationYOffset = node.StorageLocationYOffset;
                if (storageLocationYOffset > 0d)
                {
                    flowBlock.Location = new System.Drawing.Point(
                        originalLocation.X,
                        Math.Max(0, originalLocation.Y + (int)Math.Round(storageLocationYOffset)));
                }
            }
        }

        private void SyncNodeSizes(Func<FlowBlockNodeViewModel, double> heightSelector)
        {
            foreach (var node in Nodes)
            {
                node.InternalFlowBlock.Size = new System.Drawing.Size(
                    (int)Math.Round(node.Width),
                    (int)Math.Round(heightSelector(node)));
            }
        }

        internal void RestoreNodeLocationAfterStorage()
        {
            if (_storageLocationSnapshots.Count == 0)
                return;

            foreach (var item in _storageLocationSnapshots)
            {
                var node = item.Key;
                var location = item.Value;
                node.InternalFlowBlock.Location = location;
                node.SetStorageLayoutPropertyChangeSuppressed(false);
            }

            _storageLocationSnapshots.Clear();
        }

        private void DeleteSelection()
        {
            if (!DeleteSelectionCommand.CanExecute(null))
                return;

            var selectedFlowBlocks = SelectedNodes.Select(x => x.InternalFlowBlock).ToList();
            var firstFlowBlock = selectedFlowBlocks.FirstOrDefault();
            if (firstFlowBlock == null)
                return;

            var report = FlowBloxDeletionHelper.Analyze(_registry, selectedFlowBlocks.Cast<object>());
            if (!DeletionConfirmationService.Confirm(_dialogService, report))
                return;

            var deleteAction = FlowBloxDeletionHelper.CreateAction(_registry, report);

            deleteAction.Invoke();
            _componentProvider?.GetCurrentChangelist()?.AddChange(deleteAction);
        }

        private void EditSelection()
        {
            if (SelectedNode == null)
                return;

            ShowPropertyWindow(SelectedNode.InternalFlowBlock);
        }

        public void OpenRowTarget(FlowBlockRenderRowViewModel row)
        {
            if (row?.CanNavigate != true)
                return;

            ShowPropertyWindow(
                row.Target,
                row.PreselectedProperty,
                row.PreselectedInstance as FlowBloxReactiveObject,
                row.Parent);
        }

        public void SelectRow(FlowBlockRenderRowViewModel row)
        {
            foreach (var item in Nodes.SelectMany(x => x.Rows))
                item.IsSelected = ReferenceEquals(item, row);

            CopyRowValueCommand.Invalidate();
        }

        private void CopyRowValue(FlowBlockRenderRowViewModel row)
        {
            if (row?.CanCopyValue != true)
                return;

            Clipboard.SetText(row.Value);
        }

        private void ShowPropertyWindow(object target, string preselectedProperty = null, FlowBloxReactiveObject preselectedInstance = null, object parent = null)
        {
            if (target == null)
                return;

            var dialog = new FlowBlox.UICore.Views.PropertyWindow(new FlowBlox.UICore.Views.PropertyWindowArgs(
                target,
                parent: parent,
                readOnly: IsProjectEditingReadOnly,
                preselectedProperty: preselectedProperty,
                preselectedInstance: preselectedInstance));
            ShowDialog(dialog);
        }

        private void ShowGridSettings()
        {
            if (_project == null)
                return;

            var dialog = new ProjectPanelGridSettingsWindow(_project);
            if (ShowDialog(dialog) == true)
            {
                OnPropertyChanged(nameof(CanvasWidth));
                OnPropertyChanged(nameof(CanvasHeight));
            }
        }

        private bool CanManageUserFields() => HasProject()
            && !FlowBlox.Core.Provider.FlowBloxRegistryProvider.IsCurrentNestedTransactionForDifferentTarget(_project);

        private void ShowUserFields(string preselectedProperty)
        {
            if (!CanManageUserFields())
                return;

            var registry = FlowBlox.Core.Provider.FlowBloxRegistryProvider.GetRegistry();
            var dialog = new FlowBlox.UICore.Views.PropertyWindow(new PropertyWindowArgs(
                new UserFieldObjectManager(registry),
                readOnly: IsProjectEditingReadOnly,
                deepCopy: false,
                canSave: false,
                preselectedProperty: preselectedProperty))
            {
                Height = 800
            };
            ShowDialog(dialog);
        }

        private void ToggleBreakpoint()
        {
            if (SelectedNode == null)
                return;

            var action = new FlowBloxPropertyChangeAction
            {
                Target = SelectedNode.InternalFlowBlock,
                PropertyName = nameof(BaseFlowBlock.BreakPoint),
                OldValue = SelectedNode.InternalFlowBlock.BreakPoint,
                NewValue = !SelectedNode.InternalFlowBlock.BreakPoint
            };
            action.Invoke();
            _componentProvider?.GetCurrentChangelist()?.AddChange(action);
            OnPropertyChanged(nameof(ToggleBreakpointHeader));
        }

        private void DefineExecutionIndex()
        {
            if (!CanManageExecutionIndex)
                return;

            var dialog = new ExecutionIndexWindow(SelectedNode.InternalFlowBlock.ExecutionIndex);
            if (ShowDialog(dialog) == true)
            {
                var action = new FlowBloxPropertyChangeAction
                {
                    Target = SelectedNode.InternalFlowBlock,
                    PropertyName = nameof(BaseFlowBlock.ExecutionIndex),
                    OldValue = SelectedNode.InternalFlowBlock.ExecutionIndex,
                    NewValue = dialog.Result
                };
                action.Invoke();
                _componentProvider?.GetCurrentChangelist()?.AddChange(action);
            }
        }

        private void RemoveExecutionIndex()
        {
            if (!CanManageExecutionIndex)
                return;

            var action = new FlowBloxPropertyChangeAction
            {
                Target = SelectedNode.InternalFlowBlock,
                PropertyName = nameof(BaseFlowBlock.ExecutionIndex),
                OldValue = SelectedNode.InternalFlowBlock.ExecutionIndex,
                NewValue = -1
            };
            action.Invoke();
            _componentProvider?.GetCurrentChangelist()?.AddChange(action);
        }

        private void ShowInputInsight()
        {
            var flowBlock = SelectedNode?.InternalFlowBlock;
            var results = flowBlock?.InputDatasets;
            var currentResult = flowBlock?.InputDataset_CurrentlyProcessing;
            if (results == null || currentResult == null)
                return;

            ShowDialog(new InsightWindow(results, currentResult));
        }

        private void ShowOutputInsight()
        {
            if (SelectedNode?.InternalFlowBlock is not BaseResultFlowBlock flowBlock)
                return;

            var results = flowBlock.GridElementResult.Results;
            var currentResult = flowBlock.OutputDataset_CurrentlyProcessing;
            if (results == null || currentResult == null)
                return;

            ShowDialog(new InsightWindow(results, currentResult));
        }

        private void ManageNotifications()
        {
            if (!CanManageNotifications)
                return;

            ShowDialog(new ManageNotificationsWindow(SelectedNode.InternalFlowBlock));
        }

        private void RemoveConnection(FlowBlockArrowViewModel arrow)
        {
            if (arrow == null || !RemoveConnectionCommand.CanExecute(arrow))
                return;

            if (arrow.Kind == "invoke")
            {
                var disconnectAction = new FlowBloxDisconnectAction
                {
                    From = arrow.From.InternalFlowBlock,
                    To = arrow.To.InternalFlowBlock
                };
                disconnectAction.Invoke();
                _componentProvider?.GetCurrentChangelist()?.AddChange(disconnectAction);
            }
            else if (arrow.Kind == "recursive" && arrow.From.InternalFlowBlock is RecursiveTargetFlowBlockBase recursive)
            {
                var disconnectAction = new FlowBloxRecursiveCallDisconnectAction
                {
                    RecursiveCallFlowBlock = recursive,
                    TargetFlowBlock = recursive.TargetFlowBlock
                };
                disconnectAction.Invoke();
                _componentProvider?.GetCurrentChangelist()?.AddChange(disconnectAction);
            }

            SelectedArrow = null;
            RefreshArrows();
        }

        private static bool CanConnect(FlowBlockNodeViewModel startNode, FlowBlockNodeViewModel endNode)
        {
            if (startNode == null || endNode == null)
                return false;

            if (ReferenceEquals(startNode, endNode))
                return false;

            var startFlowBlock = startNode.InternalFlowBlock;
            var endFlowBlock = endNode.InternalFlowBlock;
            if (startFlowBlock == null || endFlowBlock == null)
                return false;

            if (startFlowBlock is RecursiveTargetFlowBlockBase recursiveTarget)
                return recursiveTarget.GetPossibleTargetFlowBlocks().Contains(endFlowBlock);

            if (startFlowBlock is NoteFlowBlock || endFlowBlock is NoteFlowBlock)
                return false;

            if (endFlowBlock.GetInputCardinality() == FlowBlockCardinalities.None)
                return false;

            if (endFlowBlock.GetInputCardinality() == FlowBlockCardinalities.One &&
                endFlowBlock.ReferencedFlowBlocks.Count > 0)
                return false;

            if (endFlowBlock.ReferencedFlowBlocks.Contains(startFlowBlock))
                return false;

            if (startFlowBlock.ReferencedFlowBlocks.Contains(endFlowBlock))
                return false;

            return true;
        }

        private bool AssignFlowBlockName(BaseFlowBlock flowBlock)
        {
            var editValueWindow = new EditValueWindow(flowBlock.Name, false, false)
            {
                Title = string.Format(FlowBloxResourceUtil.GetLocalizedString("AssignFlowBlockName_Title", typeof(Resources.ProjectPanel)), FlowBloxComponentHelper.GetDisplayName(flowBlock)),
                Description = FlowBloxResourceUtil.GetLocalizedString("AssignFlowBlockName_Description", typeof(Resources.ProjectPanel)),
                SelectionStart = flowBlock.NamePrefix.Length,
                SelectionLength = flowBlock.Name.Length - flowBlock.NamePrefix.Length
            };

            if (ShowDialog(editValueWindow) == true)
            {
                var oldName = flowBlock.Name;
                flowBlock.Name = editValueWindow.GetValue();

                if (!ValidationUtil.ValidateProperty(flowBlock, nameof(BaseFlowBlock.Name), out var message))
                {
                    flowBlock.Name = oldName;
                    ShowMessage(
                        message,
                        FlowBloxResourceUtil.GetLocalizedString("Message_AssignFlowBlockNameInvalid_Title", typeof(Resources.ProjectPanel)),
                        FlowBloxMessageBoxTypes.Warning);
                    return AssignFlowBlockName(flowBlock);
                }

                return true;
            }

            return false;
        }

        private void ShowMessage(string message, string title, FlowBloxMessageBoxTypes messageBoxType)
        {
            _messageBoxService?.ShowMessageBox(message, title, messageBoxType);
        }

        public void UpdateRuntimeState(bool isRuntimeActive, bool isRuntimePaused, bool isExternalProjectEditActive)
        {
            IsRuntimePaused = isRuntimePaused;
            IsRuntimeActive = isRuntimeActive;
            IsExternalProjectEditActive = isExternalProjectEditActive;
            OnPropertyChanged(nameof(CanExecuteRuntime));
            OnPropertyChanged(nameof(CanPauseRuntime));
            OnPropertyChanged(nameof(CanStopRuntime));
            ExecuteRuntimeCommand.Invalidate();
            PauseRuntimeCommand.Invalidate();
            StopRuntimeCommand.Invalidate();
        }

        private void RuntimeStateService_StateChanged(object? sender, RuntimeStateChangedEventArgs e)
            => SynchronizationContextHelper.PostToUi(
                _uiContext,
                () => UpdateRuntimeState(e.IsRuntimeActive, e.IsRuntimePaused, e.IsExternalProjectEditActive));

        private static Type ResolveDraggedFlowBlockType(IDataObject dataObject)
        {
            if (dataObject?.GetDataPresent(FlowBloxFlowBlockDragDropFormats.FlowBlockType) == true &&
                dataObject.GetData(FlowBloxFlowBlockDragDropFormats.FlowBlockType) is string typeName)
                return ResolveFlowBlockType(typeName);

            return null;
        }

        private static Type ResolveFlowBlockType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName, throwOnError: false))
                .FirstOrDefault(type => type != null && typeof(BaseFlowBlock).IsAssignableFrom(type));
        }

        private static string GetGestureText(Key key)
        {
            var gestureText = new KeyGesture(key).GetDisplayStringForCulture(CultureInfo.CurrentUICulture);
            return string.IsNullOrWhiteSpace(gestureText) ? key.ToString() : gestureText;
        }

        private bool HasProject() => _registry != null && FlowBloxProjectManager.Instance.ActiveProject != null;

        private bool CanEditGrid() => HasProject() && !IsProjectEditingReadOnly;

        private void NotifyConnectionModeChanged(bool previousValue)
        {
            if (previousValue == IsConnectionMode)
                return;

            OnPropertyChanged(nameof(IsConnectionMode));
            OnPropertyChanged(nameof(IsSelectionMode));
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(CanStartMarqueeSelection));
            InvalidateSelectionCommands();
        }

        private bool? ShowDialog(Window window)
            => _dialogService?.ShowWPFDialog(window) ?? window.ShowDialog();

        private void InvalidateSelectionCommands()
        {
            DeleteSelectionCommand.Invalidate();
            EditSelectionCommand.Invalidate();
            ToggleBreakpointCommand.Invalidate();
            DefineExecutionIndexCommand.Invalidate();
            RemoveExecutionIndexCommand.Invalidate();
            ShowInputInsightCommand.Invalidate();
            ShowOutputInsightCommand.Invalidate();
            ManageNotificationsCommand.Invalidate();
            RemoveConnectionCommand.Invalidate();
        }

        private void InvalidateAllCommands()
        {
            SelectModeCommand.Invalidate();
            ConnectionModeCommand.Invalidate();
            SelectAllCommand.Invalidate();
            SelectLeftCommand.Invalidate();
            SelectRightCommand.Invalidate();
            SelectUpCommand.Invalidate();
            SelectDownCommand.Invalidate();
            AutoLayoutCommand.Invalidate();
            ResetNotificationsCommand.Invalidate();
            ExecuteRuntimeCommand.Invalidate();
            PauseRuntimeCommand.Invalidate();
            StopRuntimeCommand.Invalidate();
            GridSettingsCommand.Invalidate();
            ManageInputFieldsCommand.Invalidate();
            ManageMemoryFieldsCommand.Invalidate();
            InvalidateSelectionCommands();
        }

        private void ClearNodes()
        {
            foreach (var node in Nodes)
            {
                node.PropertyChanged -= Node_PropertyChanged;
                node.InternalFlowBlock.ComponentChanged -= FlowBlock_ComponentChanged;
                node.InternalFlowBlock.ReferencedFlowBlocksChanged -= FlowBlock_ReferencedFlowBlocksChanged;
                node.Dispose();
            }

            _nodesByFlowBlock.Clear();
            Nodes.Clear();
            Arrows.Clear();
            SelectedNode = null;
            PublishSelectedFlowBlocks();
        }

        private void PublishSelectedFlowBlocks()
            => _componentProvider?.SetSelectedFlowBlocks(SelectedNodes.Select(x => x.InternalFlowBlock));

        private void UnsubscribeRegistry()
        {
            if (_registry == null)
                return;

            _registry.OnFlowBlockAdded -= Registry_OnFlowBlockAdded;
            _registry.OnFlowBlockRemoved -= Registry_OnFlowBlockRemoved;
            _registry = null;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Dispose()
        {
            FlowBloxProjectManager.Instance.ProjectChanged -= ProjectManager_ProjectChanged;
            if (_runtimeStateService != null)
                _runtimeStateService.StateChanged -= RuntimeStateService_StateChanged;

            UnsubscribeCurrentChangelist();
            UnsubscribeRegistry();
            ClearNodes();
        }

        private readonly record struct NodeLayoutSnapshot(System.Drawing.Point Location, double CenterY);

        private enum Direction
        {
            Left,
            Right,
            Up,
            Down
        }
    }
}
