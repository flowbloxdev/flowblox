using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace FlowBlox.UICore.Rendering;

/// <summary>
/// Draws a continuation arrow in AvalonEdit's existing word-wrap indentation.
/// </summary>
internal sealed class SoftWrapContinuationRenderer(TextEditor editor) : IBackgroundRenderer
{
    private static readonly Geometry Arrow = (Geometry)Geometry.Parse("M1,-3 L1,0 L9,0 M6,-3 L9,0 L6,3").GetAsFrozen();

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || !editor.WordWrap) return;
        var brush = editor.Foreground.CloneCurrentValue();
        brush.Opacity *= .45;
        var pen = new Pen(brush, 0.75);
        foreach (var visualLine in textView.VisualLines)
        {
            // Only continuation rows within a document line, never explicit Enter breaks.
            for (int index = 1; index < visualLine.TextLines.Count; index++)
            {
                var line = visualLine.TextLines[index];
                int startColumn = visualLine.GetTextLineVisualStartColumn(line);
                double x = visualLine.GetTextLineVisualXPosition(line, startColumn) - textView.HorizontalOffset - 12;
                double y = visualLine.GetTextLineVisualYPosition(line, VisualYPosition.TextMiddle) - textView.VerticalOffset;
                drawingContext.PushTransform(new TranslateTransform(x, y));
                drawingContext.DrawGeometry(null, pen, Arrow);
                drawingContext.Pop();
            }
        }
    }
}
