using SkiaSharp;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Generates labeled PNG images for E2E attachment tests.
/// Each image has a unique color, descriptive text, and watermark number
/// so that test screenshots and verification can identify exactly which
/// image arrived at which recipient.
/// </summary>
internal static class TestImageGenerator
{
    private const int Width = 400;
    private const int Height = 200;

    /// <summary>
    /// Background colors cycled by image index (1-based).
    /// Chosen for good contrast with white text.
    /// </summary>
    private static readonly SKColor[] BackgroundColors =
    [
        new SKColor(0x33, 0x66, 0xCC), // Blue
        new SKColor(0x99, 0x44, 0x99), // Plum
        new SKColor(0xCC, 0x88, 0x00), // Amber
        new SKColor(0x22, 0x88, 0x66), // Teal
        new SKColor(0xCC, 0x44, 0x44), // Red
        new SKColor(0x55, 0x77, 0x22), // Olive
    ];

    /// <summary>
    /// Generates a labeled test image and returns the file name and PNG bytes.
    /// </summary>
    /// <param name="imageIndex">1-based image index (determines color and label).</param>
    /// <param name="senderName">Display name of sender (e.g., "Alice").</param>
    /// <param name="targetName">Display name of target (e.g., "Bob" or "Open Forum").</param>
    /// <returns>Tuple of (fileName, pngBytes).</returns>
    public static (string FileName, byte[] PngBytes) GenerateTestAttachment(
        int imageIndex, string senderName, string targetName)
    {
        var fileName = $"Image-{imageIndex}-from-{senderName}-to-{targetName}.png";
        var label = $"Image #{imageIndex} from {senderName} to {targetName}";
        var bgColor = BackgroundColors[(imageIndex - 1) % BackgroundColors.Length];

        var pngBytes = RenderLabeledPng(label, imageIndex, bgColor);
        return (fileName, pngBytes);
    }

    private static byte[] RenderLabeledPng(string label, int imageIndex, SKColor bgColor)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;

        // Fill background
        canvas.Clear(bgColor);

        // White border (2px)
        using var borderPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            IsAntialias = true,
        };
        canvas.DrawRect(2, 2, Width - 4, Height - 4, borderPaint);

        // Large semi-transparent watermark number in top-right
        using var watermarkPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 60),
            IsAntialias = true,
        };
        using var watermarkFont = new SKFont
        {
            Size = 120,
        };
        var watermarkText = $"#{imageIndex}";
        canvas.DrawText(watermarkText, Width - 140, 110, watermarkFont, watermarkPaint);

        // Main label text - centered
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };
        using var textFont = new SKFont
        {
            Size = 22,
        };

        // Measure text to center it
        var textWidth = textFont.MeasureText(label);
        var x = (Width - textWidth) / 2;
        var y = Height / 2 + 8; // slight offset below center (watermark is above)

        canvas.DrawText(label, x, y, textFont, textPaint);

        // Encode as PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
