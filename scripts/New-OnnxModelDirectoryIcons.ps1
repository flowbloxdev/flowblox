param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\FlowBlox.Avalonia\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Match the italic Project Space letter icons, using a neutral near-white tint.
# Render text outlines at higher resolution so italic overhangs stay inside each icon.
function New-ModelDirectoryIcon {
    param(
        [string]$Label,
        [string]$FileName,
        [int]$Size
    )

    $supersampling = 8
    $source = [System.Drawing.Bitmap]::new($Size * $supersampling, $Size * $supersampling)
    $output = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($source)
    $outputGraphics = [System.Drawing.Graphics]::FromImage($output)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $transform = New-Object System.Drawing.Drawing2D.Matrix
    # The longer label uses a condensed face to retain legible letter height at 16 pixels.
    $fontName = if ($Label -eq 'Gen') { 'Arial Narrow' } else { 'Arial' }
    $family = [System.Drawing.FontFamily]::new($fontName)
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(227, 229, 232))
    $format = [System.Drawing.StringFormat]::GenericTypographic.Clone()

    try {
        if ($family.Name -ne $fontName) {
            throw "Install the $fontName font before regenerating model-directory icons."
        }
        $fontStyle = [System.Drawing.FontStyle]::Bold -bor [System.Drawing.FontStyle]::Italic
        $path.AddString($Label, $family, [int]$fontStyle, 64, [System.Drawing.PointF]::Empty, $format)
        $bounds = $path.GetBounds()
        $padding = $Size / 16.0
        $scale = [Math]::Min(($Size - 2 * $padding) / $bounds.Width, ($Size - 4 * $padding) / $bounds.Height)

        $transform.Translate(-$bounds.X, -$bounds.Y)
        $transform.Scale($scale, $scale, [System.Drawing.Drawing2D.MatrixOrder]::Append)
        $transform.Translate(
            ($Size - $bounds.Width * $scale) / 2,
            ($Size - $bounds.Height * $scale) / 2,
            [System.Drawing.Drawing2D.MatrixOrder]::Append)
        $path.Transform($transform)

        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($supersampling, $supersampling)
        $graphics.FillPath($brush, $path)

        $outputGraphics.Clear([System.Drawing.Color]::Transparent)
        $outputGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $outputGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $outputGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $outputGraphics.DrawImage($source, 0, 0, $Size, $Size)
        $destination = Join-Path $OutputDirectory ($FileName + '-' + $Size + '.png')
        $output.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output $destination
    }
    finally {
        $format.Dispose()
        $brush.Dispose()
        $family.Dispose()
        $transform.Dispose()
        $path.Dispose()
        $outputGraphics.Dispose()
        $graphics.Dispose()
        $output.Dispose()
        $source.Dispose()
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($iconSize in @(16, 32)) {
    New-ModelDirectoryIcon -Label 'QA' -FileName 'onnx-qa' -Size $iconSize
    New-ModelDirectoryIcon -Label 'Gen' -FileName 'onnx-genai' -Size $iconSize
}
