using FlowBlox.UICore.Factory;
using FlowBlox.UICore.PopUp.Implementations;
using FlowBlox.UICore.ViewModels.ProjectPanel;
using FlowBlox.UICore.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlowBlox.UICore.Views
{
    public partial class ProjectPanelWpfControl : UserControl
    {
        private FlowBlockNodeViewModel _draggedNode;
        private Point _dragStart;
        private double _nodeStartX;
        private double _nodeStartY;
        private readonly Dictionary<FlowBlockNodeViewModel, Point> _draggedNodeStartPositions = new();
        private bool _isCanvasPanning;
        private Point _panStart;
        private double _panStartHorizontalOffset;
        private double _panStartVerticalOffset;
        private bool _isMarqueeSelecting;
        private Point _marqueeStart;
        private FlowBlockNodeViewModel _connectionStartNode;
        private bool _isConnectingNodes;
        private FlowBlockNodeViewModel _floatingInsertedNode;
        private double _floatingInsertedStartX;
        private double _floatingInsertedStartY;
        private Window _hostWindow;
        private readonly ProjectPanelTemporaryConnectionShortcut _temporaryConnectionShortcut;

        public ProjectPanelWpfViewModel ViewModel { get; }

        public ProjectPanelWpfControl()
        {
            InitializeComponent();
            ViewModel = new ProjectPanelWpfViewModel();
            DataContext = ViewModel;
            _temporaryConnectionShortcut = new ProjectPanelTemporaryConnectionShortcut(
                DeactivateTemporaryConnectionShortcut);
            Loaded += ProjectPanelWpfControl_Loaded;
            Unloaded += ProjectPanelWpfControl_Unloaded;
            LostKeyboardFocus += ProjectPanelWpfControl_LostKeyboardFocus;
            PreviewMouseDown += ProjectPanelWpfControl_PreviewMouseDown;
        }

        private void TransactionMonitorButton_Click(object sender, RoutedEventArgs e)
            => TransactionMonitorWindowFactory.Show();

        private void IterationContextQuickStartButton_Click(object sender, RoutedEventArgs e)
            => IterationContextQuickStart.Show(Window.GetWindow(this));

        private void TransactionMenuButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || button.ContextMenu == null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }

        public void RefreshProject() => ViewModel.Refresh();

        public void SyncNodeSizesToModel()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(SyncNodeSizesToModel);
                return;
            }

            ViewModel.SyncNodeSizesToModel();
        }

        public void PrepareNodeLocationForStorage()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(PrepareNodeLocationForStorage);
                return;
            }

            ViewModel.PrepareNodeLocationForStorage();
        }

        public void RestoreNodeLocationAfterStorage()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RestoreNodeLocationAfterStorage);
                return;
            }

            ViewModel.RestoreNodeLocationAfterStorage();
        }

        public void ResetNotifications(FlowBlox.Core.Models.Runtime.BaseRuntime runtime = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ResetNotifications(runtime));
                return;
            }

            ViewModel.ResetNotifications(runtime);
        }

        public bool CanHandleHostedShortcut => IsProjectPanelInteractionContextActive() && !IsTextInputFocusWithin();

        public bool IsTextInputFocusActive => IsTextInputFocusWithin();

        public int SelectedNodeCount => ViewModel.SelectedNodes.Count();

        public void CopySelection() => ViewModel.CopySelection();

        public void PasteSelection()
        {
            var node = ViewModel.PasteCopiedSelection(
                CanvasScrollViewer.ViewportWidth / 2d,
                CanvasScrollViewer.ViewportHeight / 2d);

            BeginFloatingInsertedNode(node, GetPasteStartPosition());
        }

        public bool ExecuteDeleteShortcut()
        {
            if (ViewModel.SelectedArrow != null && ViewModel.RemoveConnectionCommand.CanExecute(ViewModel.SelectedArrow))
            {
                ViewModel.RemoveConnectionCommand.Execute(ViewModel.SelectedArrow);
                return true;
            }

            if (ViewModel.DeleteSelectionCommand.CanExecute(null) != true)
                return false;

            ViewModel.DeleteSelectionCommand.Execute(null);
            return true;
        }

        public bool ExecuteEscapeShortcut()
        {
            if (_floatingInsertedNode == null)
                return false;

            CancelFloatingInsertedNode();
            return true;
        }

        public bool ExecuteSelectionShortcut(Key key)
        {
            var command = key switch
            {
                Key.Left => ViewModel.SelectLeftCommand,
                Key.Right => ViewModel.SelectRightCommand,
                Key.Up => ViewModel.SelectUpCommand,
                Key.Down => ViewModel.SelectDownCommand,
                Key.A => ViewModel.SelectAllCommand,
                _ => null
            };

            if (command?.CanExecute(null) != true)
                return false;

            command.Execute(null);
            return true;
        }

        public void UpdateRuntimeState(bool isRuntimeActive, bool isRuntimePaused, bool isExternalProjectEditActive)
            => ViewModel.UpdateRuntimeState(isRuntimeActive, isRuntimePaused, isExternalProjectEditActive);

        public void MarkRuntimeFocus(FlowBlox.Core.Models.FlowBlocks.Base.BaseFlowBlock flowBlock)
            => ViewModel.MarkRuntimeFocus(flowBlock);

        public void ActivateTemporaryConnectionShortcut()
        {
            UpdateTemporaryConnectionMode();
            if (ViewModel.IsConnectionMode)
                _temporaryConnectionShortcut.StartReleaseWatcher();
        }

        public void DeactivateTemporaryConnectionShortcut()
        {
            _temporaryConnectionShortcut.StopReleaseWatcher();
            ViewModel.SetTemporaryConnectionMode(false);
        }

        private void ProjectPanelWpfControl_Loaded(object sender, RoutedEventArgs e)
        {
            _hostWindow = Window.GetWindow(this);
            if (_hostWindow == null)
                return;

            _hostWindow.PreviewKeyDown -= HostWindow_PreviewKeyDown;
            _hostWindow.PreviewKeyUp -= HostWindow_PreviewKeyUp;
            _hostWindow.PreviewKeyDown += HostWindow_PreviewKeyDown;
            _hostWindow.PreviewKeyUp += HostWindow_PreviewKeyUp;
        }

        private void ProjectPanelWpfControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_hostWindow != null)
            {
                _hostWindow.PreviewKeyDown -= HostWindow_PreviewKeyDown;
                _hostWindow.PreviewKeyUp -= HostWindow_PreviewKeyUp;
                _hostWindow = null;
            }

            DeactivateTemporaryConnectionShortcut();
        }

        private void ProjectPanelWpfControl_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!IsKeyboardFocusWithin)
                DeactivateTemporaryConnectionShortcut();
        }

        private void ProjectPanelWpfControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.FocusedElement is not DependencyObject focusedElement ||
                !IsTextInput(focusedElement) ||
                e.OriginalSource is not DependencyObject clickedElement ||
                IsTextInput(clickedElement))
            {
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(() => Keyboard.Focus(this)),
                DispatcherPriority.Input);
        }

        private void HostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ExecuteEditingShortcut(e))
                return;

            ExecuteSelectionShortcut(e);
            UpdateTemporaryConnectionMode();
        }

        private void HostWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
                !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                DeactivateTemporaryConnectionShortcut();
                return;
            }

            UpdateTemporaryConnectionMode();
        }

        private void ExecuteSelectionShortcut(KeyEventArgs e)
        {
            if (e.Handled ||
                !IsProjectPanelInteractionContextActive() ||
                IsTextInputFocusWithin() ||
                Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            var command = e.Key switch
            {
                Key.Left => ViewModel.SelectLeftCommand,
                Key.Right => ViewModel.SelectRightCommand,
                Key.Up => ViewModel.SelectUpCommand,
                Key.Down => ViewModel.SelectDownCommand,
                Key.A => ViewModel.SelectAllCommand,
                _ => null
            };

            if (command?.CanExecute(null) != true)
                return;

            command.Execute(null);
            e.Handled = true;
        }

        private bool ExecuteEditingShortcut(KeyEventArgs e)
        {
            if (e.Handled ||
                !IsProjectPanelInteractionContextActive() ||
                IsTextInputFocusWithin())
            {
                return false;
            }

            if (e.Key == Key.Escape && _floatingInsertedNode != null)
            {
                ExecuteEscapeShortcut();
                e.Handled = true;
                return true;
            }

            if (Keyboard.Modifiers != ModifierKeys.None)
                return false;

            if (e.Key == Key.Delete && ExecuteDeleteShortcut())
            {
                e.Handled = true;
                return true;
            }

            return false;
        }

        private void UpdateTemporaryConnectionMode()
        {
            var isShortcutActive =
                IsProjectPanelInteractionContextActive() &&
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
                ViewModel.ConnectionModeCommand.CanExecute(null);

            ViewModel.SetTemporaryConnectionMode(isShortcutActive);
        }

        private bool IsProjectPanelInteractionContextActive()
            => IsKeyboardFocusWithin || IsMouseOver;

        private Point GetPasteStartPosition()
            => ProjectCanvas.IsMouseOver
                ? Mouse.GetPosition(ProjectCanvas)
                : new Point(
                    CanvasScrollViewer.HorizontalOffset + Math.Max(0d, CanvasScrollViewer.ViewportWidth / 2d),
                    CanvasScrollViewer.VerticalOffset + Math.Max(0d, CanvasScrollViewer.ViewportHeight / 2d));

        private static bool IsTextInputFocusWithin()
        {
            if (Keyboard.FocusedElement is not DependencyObject current)
                return false;

            while (current != null)
            {
                if (current is TextBox or PasswordBox)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static bool IsTextInput(DependencyObject current)
        {
            while (current != null)
            {
                if (current is TextBox or PasswordBox)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void Arrow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FlowBlockArrowViewModel arrow)
                return;

            ViewModel.SelectArrow(arrow);
            e.Handled = true;
        }

        private void Arrow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FlowBlockArrowViewModel arrow)
                return;

            ViewModel.SelectArrow(arrow);
        }

        private void FlowBlock_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FlowBlockNodeViewModel node)
                return;

            if (_floatingInsertedNode != null)
            {
                CommitFloatingInsertedNode();
                e.Handled = true;
                return;
            }

            if (IsScrollBarInteraction(e.OriginalSource))
                return;

            if (IsFlowBlockRowInteraction(e.OriginalSource))
                return;

            if (IsSelectableTextInteraction(e.OriginalSource))
                return;

            var isHeaderInteraction = IsFlowBlockHeaderInteraction(e.OriginalSource);

            if (ViewModel.IsConnectionMode)
            {
                if (!ViewModel.CanStartConnectionFrom(node))
                    return;

                _connectionStartNode = node;
                _isConnectingNodes = true;
                var current = e.GetPosition(ProjectCanvas);
                UpdateConnectionPreview(node, current);
                Mouse.Capture(ProjectCanvas);
                e.Handled = true;
                return;
            }

            if (e.ClickCount > 1)
            {
                ViewModel.SelectNode(node, toggle: false, extend: false);
                if (ViewModel.EditSelectionCommand.CanExecute(null))
                    ViewModel.EditSelectionCommand.Execute(null);

                e.Handled = true;
                return;
            }

            var toggle = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var keepExistingSelectionForDrag =
                !toggle &&
                !extend &&
                node.IsSelected &&
                ViewModel.SelectedNodes.Count() > 1;

            if (!keepExistingSelectionForDrag)
                ViewModel.SelectNode(node, toggle, extend);

            if (ViewModel.IsProjectEditingReadOnly)
            {
                e.Handled = true;
                return;
            }

            if (!isHeaderInteraction)
            {
                e.Handled = true;
                return;
            }

            _draggedNode = node;
            _dragStart = e.GetPosition(this);
            _nodeStartX = node.X;
            _nodeStartY = node.Y;
            CaptureDraggedNodeStartPositions(node);
            Mouse.Capture(sender as IInputElement);
            e.Handled = true;
        }

        private void FlowBlockRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FlowBlockRenderRowViewModel row)
                return;

            ViewModel.SelectRow(row);
            if (e.ClickCount > 1 && row.CanNavigate)
                ViewModel.OpenRowTarget(row);

            e.Handled = true;
        }

        private void FlowBlockRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FlowBlockRenderRowViewModel row)
                return;

            ViewModel.SelectRow(row);
        }

        private void FlowBlock_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FlowBlockNodeViewModel node)
                return;

            if (!node.IsSelected)
                ViewModel.SelectNode(node, toggle: false, extend: false);
        }

        private void FlowBlock_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_floatingInsertedNode != null)
            {
                MoveFloatingInsertedNode(e.GetPosition(ProjectCanvas));
                e.Handled = true;
                return;
            }

            if (_isConnectingNodes)
            {
                UpdateConnectionPreview(_connectionStartNode, e.GetPosition(ProjectCanvas));
                e.Handled = true;
                return;
            }

            if (_draggedNode == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(this);
            var delta = current - _dragStart;
            var snappedPosition = ViewModel.GetSnappedNodePosition(_draggedNode, _nodeStartX + delta.X, _nodeStartY + delta.Y);
            var effectiveDelta = snappedPosition - new Point(_nodeStartX, _nodeStartY);
            _draggedNode.X = snappedPosition.X;
            _draggedNode.Y = snappedPosition.Y;
            MoveSelectedNodesWithDraggedNode(effectiveDelta);
            ViewModel.EnsureCanvasContainsNode(
                _draggedNode,
                CanvasScrollViewer.ViewportWidth / 2d,
                CanvasScrollViewer.ViewportHeight / 2d);
            e.Handled = true;
        }

        private void FlowBlock_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_floatingInsertedNode != null)
            {
                e.Handled = true;
                return;
            }

            if (_isConnectingNodes)
            {
                FinishConnection(e.GetPosition(ProjectCanvas));
                e.Handled = true;
                return;
            }

            if (_draggedNode == null)
                return;

            var from = new System.Drawing.Point((int)Math.Round(_nodeStartX), (int)Math.Round(_nodeStartY));
            var to = new System.Drawing.Point((int)Math.Round(_draggedNode.X), (int)Math.Round(_draggedNode.Y));
            ViewModel.CommitNodeMove(_draggedNode, from, to, _draggedNodeStartPositions);

            Mouse.Capture(null);
            _draggedNode = null;
            _draggedNodeStartPositions.Clear();
            e.Handled = true;
        }

        private void CanvasScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle)
                return;

            _isCanvasPanning = true;
            _panStart = e.GetPosition(CanvasScrollViewer);
            _panStartHorizontalOffset = CanvasScrollViewer.HorizontalOffset;
            _panStartVerticalOffset = CanvasScrollViewer.VerticalOffset;
            CanvasScrollViewer.Cursor = Cursors.SizeAll;
            Mouse.Capture(CanvasScrollViewer);
            e.Handled = true;
        }

        private void CanvasScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isCanvasPanning)
                return;

            var current = e.GetPosition(CanvasScrollViewer);
            var delta = current - _panStart;
            CanvasScrollViewer.ScrollToHorizontalOffset(_panStartHorizontalOffset - delta.X);
            CanvasScrollViewer.ScrollToVerticalOffset(_panStartVerticalOffset - delta.Y);
            e.Handled = true;
        }

        private void CanvasScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
                EndCanvasPan();
        }

        private void CanvasScrollViewer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isCanvasPanning && e.MiddleButton != MouseButtonState.Pressed)
                EndCanvasPan();
        }

        private void ProjectCanvas_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = ViewModel.CanCreateFlowBlockFromDrop(e.Data)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void ProjectCanvas_Drop(object sender, DragEventArgs e)
        {
            if (!ViewModel.CanCreateFlowBlockFromDrop(e.Data))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var dropPosition = e.GetPosition(ProjectCanvas);
            ViewModel.CreateFlowBlockFromDrop(
                e.Data,
                dropPosition,
                CanvasScrollViewer.ViewportWidth / 2d,
                CanvasScrollViewer.ViewportHeight / 2d);

            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void ProjectCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_floatingInsertedNode != null)
            {
                CommitFloatingInsertedNode();
                e.Handled = true;
                return;
            }

            if (!ViewModel.CanStartMarqueeSelection)
                return;

            _isMarqueeSelecting = true;
            _marqueeStart = e.GetPosition(ProjectCanvas);
            UpdateMarqueeSelectionRectangle(_marqueeStart);

            ProjectCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void ProjectCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_floatingInsertedNode != null)
            {
                MoveFloatingInsertedNode(e.GetPosition(ProjectCanvas));
                e.Handled = true;
                return;
            }

            if (_isConnectingNodes)
            {
                UpdateConnectionPreview(_connectionStartNode, e.GetPosition(ProjectCanvas));
                e.Handled = true;
                return;
            }

            if (!_isMarqueeSelecting || e.LeftButton != MouseButtonState.Pressed)
                return;

            UpdateMarqueeSelectionRectangle(e.GetPosition(ProjectCanvas));
            e.Handled = true;
        }

        private void ProjectCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_floatingInsertedNode != null)
            {
                e.Handled = true;
                return;
            }

            if (_isConnectingNodes)
            {
                FinishConnection(e.GetPosition(ProjectCanvas));
                e.Handled = true;
                return;
            }

            if (!_isMarqueeSelecting)
                return;

            var selectionBounds = GetMarqueeSelectionBounds(e.GetPosition(ProjectCanvas));
            EndMarqueeSelection();
            ViewModel.SelectNodesInRectangle(selectionBounds);
            e.Handled = true;
        }

        private void EndCanvasPan()
        {
            _isCanvasPanning = false;
            CanvasScrollViewer.Cursor = Cursors.Arrow;
            Mouse.Capture(null);
        }

        private void EndMarqueeSelection()
        {
            _isMarqueeSelecting = false;
            MarqueeSelectionRectangle.Visibility = Visibility.Collapsed;
            ProjectCanvas.ReleaseMouseCapture();
        }

        private void BeginFloatingInsertedNode(FlowBlockNodeViewModel node, Point location)
        {
            if (node == null)
                return;

            _floatingInsertedNode = node;
            _floatingInsertedStartX = node.X;
            _floatingInsertedStartY = node.Y;
            MoveFloatingInsertedNode(location);
            Mouse.Capture(ProjectCanvas);
        }

        private void MoveFloatingInsertedNode(Point location)
        {
            ViewModel.MoveNodeToCanvasPosition(
                _floatingInsertedNode,
                location,
                CanvasScrollViewer.ViewportWidth / 2d,
                CanvasScrollViewer.ViewportHeight / 2d);
        }

        private void CaptureDraggedNodeStartPositions(FlowBlockNodeViewModel draggedNode)
        {
            _draggedNodeStartPositions.Clear();
            foreach (var node in ViewModel.SelectedNodes)
                _draggedNodeStartPositions[node] = new Point(node.X, node.Y);

            if (draggedNode != null && !_draggedNodeStartPositions.ContainsKey(draggedNode))
                _draggedNodeStartPositions[draggedNode] = new Point(draggedNode.X, draggedNode.Y);
        }

        private void MoveSelectedNodesWithDraggedNode(Vector effectiveDelta)
        {
            foreach (var item in _draggedNodeStartPositions)
            {
                var node = item.Key;
                if (ReferenceEquals(node, _draggedNode))
                    continue;

                node.X = item.Value.X + effectiveDelta.X;
                node.Y = item.Value.Y + effectiveDelta.Y;
                ViewModel.EnsureCanvasContainsNode(
                    node,
                    CanvasScrollViewer.ViewportWidth / 2d,
                    CanvasScrollViewer.ViewportHeight / 2d);
            }
        }

        private void CommitFloatingInsertedNode()
        {
            if (_floatingInsertedNode != null)
            {
                var from = new System.Drawing.Point(
                    (int)Math.Round(_floatingInsertedStartX),
                    (int)Math.Round(_floatingInsertedStartY));
                var to = new System.Drawing.Point(
                    (int)Math.Round(_floatingInsertedNode.X),
                    (int)Math.Round(_floatingInsertedNode.Y));
                ViewModel.CommitNodeMove(_floatingInsertedNode, from, to);
            }

            EndFloatingInsertedNode();
        }

        private void CancelFloatingInsertedNode()
        {
            ViewModel.CancelInsertedNode(_floatingInsertedNode);
            EndFloatingInsertedNode();
        }

        private void EndFloatingInsertedNode()
        {
            _floatingInsertedNode = null;
            Mouse.Capture(null);
        }

        private void FinishConnection(Point endPoint)
        {
            var startNode = _connectionStartNode;
            var endNode = ViewModel.GetNodeAt(endPoint);
            EndConnectionPreview();
            ViewModel.ConnectNodes(startNode, endNode);
        }

        private void EndConnectionPreview()
        {
            _isConnectingNodes = false;
            _connectionStartNode = null;
            ConnectionPreviewLine.Visibility = Visibility.Collapsed;
            ConnectionPreviewStartDot.Visibility = Visibility.Collapsed;
            ConnectionPreviewArrowHead.Visibility = Visibility.Collapsed;
            Mouse.Capture(null);
        }

        private void UpdateConnectionPreview(FlowBlockNodeViewModel startNode, Point rawEnd)
        {
            if (startNode == null)
                return;

            var startCenter = GetNodeCenter(startNode);
            var endNode = ViewModel.GetNodeAt(rawEnd);
            var end = endNode != null && ViewModel.CanPreviewConnection(startNode, endNode)
                ? ArrowGeometryHelper.GetEdgePoint(GetNodeBounds(endNode), GetNodeCenter(endNode), startCenter)
                : rawEnd;
            var start = ArrowGeometryHelper.GetEdgePoint(GetNodeBounds(startNode), startCenter, end);

            ConnectionPreviewLine.X1 = start.X;
            ConnectionPreviewLine.Y1 = start.Y;
            ConnectionPreviewLine.X2 = end.X;
            ConnectionPreviewLine.Y2 = end.Y;
            ConnectionPreviewLine.Visibility = Visibility.Visible;

            Canvas.SetLeft(ConnectionPreviewStartDot, start.X - (ConnectionPreviewStartDot.Width / 2d));
            Canvas.SetTop(ConnectionPreviewStartDot, start.Y - (ConnectionPreviewStartDot.Height / 2d));
            ConnectionPreviewStartDot.Visibility = Visibility.Visible;

            ConnectionPreviewArrowHead.Points = ArrowGeometryHelper.CreateArrowHeadPoints(start, end);
            ConnectionPreviewArrowHead.Visibility = Visibility.Visible;
        }

        private static Point GetNodeCenter(FlowBlockNodeViewModel node)
            => node == null
                ? new Point()
                : new Point(node.X + (node.Width / 2d), node.Y + (node.Height / 2d));

        private static Rect GetNodeBounds(FlowBlockNodeViewModel node)
            => new(node.X, node.Y, node.Width, node.Height);

        private void UpdateMarqueeSelectionRectangle(Point current)
        {
            var bounds = GetMarqueeSelectionBounds(current);
            Canvas.SetLeft(MarqueeSelectionRectangle, bounds.Left);
            Canvas.SetTop(MarqueeSelectionRectangle, bounds.Top);
            MarqueeSelectionRectangle.Width = bounds.Width;
            MarqueeSelectionRectangle.Height = bounds.Height;
            MarqueeSelectionRectangle.Visibility = Visibility.Visible;
        }

        private Rect GetMarqueeSelectionBounds(Point current)
            => new(
                Math.Min(_marqueeStart.X, current.X),
                Math.Min(_marqueeStart.Y, current.Y),
                Math.Abs(current.X - _marqueeStart.X),
                Math.Abs(current.Y - _marqueeStart.Y));

        private static bool IsScrollBarInteraction(object originalSource)
        {
            if (originalSource is not DependencyObject current)
                return false;

            while (current != null)
            {
                if (current is ScrollBar or Thumb or RepeatButton)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static bool IsFlowBlockRowInteraction(object originalSource)
        {
            if (originalSource is not DependencyObject current)
                return false;

            while (current != null)
            {
                if (current is FrameworkElement { DataContext: FlowBlockRenderRowViewModel })
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static bool IsFlowBlockHeaderInteraction(object originalSource)
        {
            if (originalSource is not DependencyObject current)
                return false;

            while (current != null)
            {
                if (current is FrameworkElement { Tag: "FlowBlockHeader" })
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static bool IsSelectableTextInteraction(object originalSource)
        {
            if (originalSource is not DependencyObject current)
                return false;

            while (current != null)
            {
                if (current is TextBox)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
