using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FlowBlox.UICore.Views;

public partial class ProjectPanelWpfControl
{
    private const double AutoScrollEdgeWidth = 48;
    private const double AutoScrollMaxSpeed = 480;
    private bool _isEdgeAutoScrollActive;
    private bool _isExternalDragOver;
    private Point _externalDragPosition;
    private TimeSpan? _lastAutoScrollFrame;

    private void StartEdgeAutoScroll()
    {
        if (_isEdgeAutoScrollActive)
            return;

        _isEdgeAutoScrollActive = true;
        _lastAutoScrollFrame = null;
        CompositionTarget.Rendering += EdgeAutoScroll_Rendering;
    }

    private void StopEdgeAutoScroll()
    {
        CompositionTarget.Rendering -= EdgeAutoScroll_Rendering;
        _isEdgeAutoScrollActive = false;
        _isExternalDragOver = false;
        _lastAutoScrollFrame = null;
    }

    private void EdgeAutoScroll_Rendering(object? sender, EventArgs e)
    {
        var isInternalDrag = _floatingInsertedNode != null ||
            (Mouse.LeftButton == MouseButtonState.Pressed && (_isConnectingNodes || _draggedNode != null));
        if (!IsLoaded || !IsVisible || ViewModel.IsProjectEditingReadOnly ||
            (!_isExternalDragOver && (!isInternalDrag || !ProjectCanvas.IsMouseCaptureWithin)))
        {
            StopEdgeAutoScroll();
            return;
        }

        var frameTime = ((RenderingEventArgs)e).RenderingTime;
        var elapsedSeconds = _lastAutoScrollFrame.HasValue
            ? Math.Clamp((frameTime - _lastAutoScrollFrame.Value).TotalSeconds, 0, 0.05)
            : 0;
        _lastAutoScrollFrame = frameTime;

        var pointer = _isExternalDragOver ? _externalDragPosition : Mouse.GetPosition(CanvasScrollViewer);
        if (!new Rect(CanvasScrollViewer.RenderSize).Contains(pointer))
            return;

        var velocity = new Vector(
            GetEdgeAutoScrollSpeed(pointer.X, CanvasScrollViewer.ViewportWidth),
            GetEdgeAutoScrollSpeed(pointer.Y, CanvasScrollViewer.ViewportHeight));
        if (velocity.Length > AutoScrollMaxSpeed)
            velocity *= AutoScrollMaxSpeed / velocity.Length;

        var horizontalOffset = Math.Clamp(
            CanvasScrollViewer.HorizontalOffset + velocity.X * elapsedSeconds,
            0, CanvasScrollViewer.ScrollableWidth);
        var verticalOffset = Math.Clamp(
            CanvasScrollViewer.VerticalOffset + velocity.Y * elapsedSeconds,
            0, CanvasScrollViewer.ScrollableHeight);

        if (horizontalOffset != CanvasScrollViewer.HorizontalOffset)
            CanvasScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
        if (verticalOffset != CanvasScrollViewer.VerticalOffset)
            CanvasScrollViewer.ScrollToVerticalOffset(verticalOffset);
    }

    private static double GetEdgeAutoScrollSpeed(double position, double viewportSize)
    {
        var edgeWidth = Math.Min(AutoScrollEdgeWidth, viewportSize / 2);
        if (edgeWidth <= 0)
            return 0;

        var distanceToEdge = Math.Min(position, viewportSize - position);
        var proximity = Math.Clamp(1 - distanceToEdge / edgeWidth, 0, 1);
        var speed = AutoScrollMaxSpeed * proximity * proximity * (0.5 + 0.5 * proximity);
        return position < viewportSize / 2 ? -speed : speed;
    }

    private void CanvasScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource != CanvasScrollViewer || (e.HorizontalChange == 0 && e.VerticalChange == 0))
            return;

        var position = Mouse.GetPosition(ProjectCanvas);
        if (_isConnectingNodes)
            UpdateConnectionPreview(_connectionStartNode, position);
        else if (_floatingInsertedNode != null)
            MoveFloatingInsertedNode(position);
        else if (_draggedNode != null && Mouse.LeftButton == MouseButtonState.Pressed)
            MoveDraggedNode(position);
    }

    private void ProjectCanvas_DragLeave(object sender, DragEventArgs e)
        => StopEdgeAutoScroll();
}
