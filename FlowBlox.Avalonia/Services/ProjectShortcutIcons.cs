using Avalonia.Media.Imaging;
using FlowBlox.Core;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.AvaloniaDesktop.Services;

internal static class ProjectShortcutIcons
{
    private static readonly SKColor ExecutionColor = new(0x78, 0xB7, 0xC6);

    public static Bitmap Project { get; } = Create(FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.project, 16));
    public static Bitmap OpenProject => Project;
    public static Bitmap Execution { get; } = Create(FlowBloxIcons.cog_play_outline, ExecutionColor);
    public static Bitmap ViewExecution { get; } = Create(FlowBloxIcons.file_eye_outline, ExecutionColor);

    private static Bitmap Create(byte[] svg, SKColor color)
    {
        var image = FlowBloxIconUtil.CreateFromSVG(svg, 16, color);
        return Create(image);
    }

    private static Bitmap Create(SKImage image)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new Bitmap(stream);
    }
}
