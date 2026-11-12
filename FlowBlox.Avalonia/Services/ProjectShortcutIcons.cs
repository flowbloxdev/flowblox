using Avalonia.Media.Imaging;
using FlowBlox.Core;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.AvaloniaDesktop.Services;

internal static class ProjectShortcutIcons
{
    private static readonly SKColor ProjectColor = new(0x21, 0x96, 0xF3);
    private static readonly SKColor ExecutionColor = new(0x78, 0xB7, 0xC6);

    public static Bitmap Project { get; } = Create(FlowBloxIcons.file_document_outline, ProjectColor);
    public static Bitmap OpenProject { get; } = Create(FlowBloxIcons.file_import_outline, ProjectColor);
    public static Bitmap Execution { get; } = Create(FlowBloxIcons.cog_play_outline, ExecutionColor);
    public static Bitmap ViewExecution { get; } = Create(FlowBloxIcons.file_eye_outline, ExecutionColor);

    private static Bitmap Create(byte[] svg, SKColor color)
    {
        var image = FlowBloxIconUtil.CreateFromSVG(svg, 16, color);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new Bitmap(stream);
    }
}
