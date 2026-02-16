using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Generates labeled PNG images and animated GIFs for E2E attachment tests.
/// Each image has a unique color, descriptive text, and watermark number
/// so that test screenshots and verification can identify exactly which
/// image arrived at which recipient.
///
/// PNGs are rendered and encoded with SkiaSharp.
/// Animated GIFs are rendered with SkiaSharp and encoded with SixLabors.ImageSharp.
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
        RenderLabeledFrame(surface.Canvas, label, imageIndex, bgColor);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Renders a labeled frame onto a canvas. Shared by PNG and animated GIF generators.
    /// Draws: colored background, white border, semi-transparent watermark, centered text.
    /// </summary>
    private static void RenderLabeledFrame(
        SKCanvas canvas, string label, int imageIndex, SKColor bgColor)
    {
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
        using var watermarkFont = new SKFont { Size = 120 };
        canvas.DrawText($"#{imageIndex}", Width - 140, 110, watermarkFont, watermarkPaint);

        // Main label text - centered
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };
        using var textFont = new SKFont { Size = 22 };
        var textWidth = textFont.MeasureText(label);
        var x = (Width - textWidth) / 2;
        var y = Height / 2 + 8;
        canvas.DrawText(label, x, y, textFont, textPaint);
    }

    // =========================================================================
    // Animated GIF generation (SkiaSharp rendering + ImageSharp GIF encoding)
    // =========================================================================

    /// <summary>
    /// Background colors for animated GIF frames (cycling red → teal → blue).
    /// </summary>
    private static readonly SKColor[] GifFrameBgColors =
    [
        new SKColor(0xCC, 0x44, 0x44), // Red
        new SKColor(0x22, 0x88, 0x66), // Teal
        new SKColor(0x33, 0x66, 0xCC), // Blue
    ];

    /// <summary>
    /// Generates a labeled animated GIF (400×200, 3 frames cycling background colors)
    /// with text "Animated #N from Sender to Target" and a watermark number.
    /// The GIF loops infinitely. Frames rendered by SkiaSharp, encoded by ImageSharp.
    /// </summary>
    public static (string FileName, byte[] GifBytes) GenerateAnimatedTestGif(
        int gifIndex, string senderName, string targetName)
    {
        var fileName = $"Animated-{gifIndex}-from-{senderName}-to-{targetName}.gif";
        var label = $"Animated #{gifIndex} from {senderName} to {targetName}";
        var gifBytes = BuildLabeledAnimatedGif(label, gifIndex);
        return (fileName, gifBytes);
    }

    /// <summary>
    /// Builds a labeled animated GIF by rendering each frame with SkiaSharp,
    /// converting to ImageSharp pixel data, and encoding with ImageSharp's GIF encoder.
    /// </summary>
    private static byte[] BuildLabeledAnimatedGif(string label, int imageIndex)
    {
        var skBitmaps = new List<SKBitmap>();
        try
        {
            // Step 1: Render all frames with SkiaSharp
            foreach (var bgColor in GifFrameBgColors)
            {
                using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
                RenderLabeledFrame(surface.Canvas, label, imageIndex, bgColor);
                using var image = surface.Snapshot();
                skBitmaps.Add(SKBitmap.FromImage(image));
            }

            // Step 2: Create animated GIF with ImageSharp
            // Build first frame as the root image
            var firstPixels = SkBitmapToRgba32(skBitmaps[0]);
            using var gif = Image.LoadPixelData<Rgba32>(firstPixels, Width, Height);

            // Set root frame delay (150 × 10ms = 1.5s per frame → ~4.5s full cycle)
            var rootMeta = gif.Frames.RootFrame.Metadata.GetGifMetadata();
            rootMeta.FrameDelay = 150;

            // Add remaining frames
            for (var f = 1; f < skBitmaps.Count; f++)
            {
                var framePixels = SkBitmapToRgba32(skBitmaps[f]);
                using var frameImage = Image.LoadPixelData<Rgba32>(framePixels, Width, Height);
                var addedFrame = gif.Frames.AddFrame(frameImage.Frames.RootFrame);
                var frameMeta = addedFrame.Metadata.GetGifMetadata();
                frameMeta.FrameDelay = 150;
            }

            // Set infinite loop
            var gifMeta = gif.Metadata.GetGifMetadata();
            gifMeta.RepeatCount = 0;

            // Encode
            using var ms = new MemoryStream();
            gif.SaveAsGif(ms, new GifEncoder
            {
                ColorTableMode = GifColorTableMode.Global,
            });
            return ms.ToArray();
        }
        finally
        {
            foreach (var bm in skBitmaps) bm.Dispose();
        }
    }

    /// <summary>
    /// Converts an SKBitmap to an Rgba32 pixel array for ImageSharp.
    /// </summary>
    private static Rgba32[] SkBitmapToRgba32(SKBitmap bitmap)
    {
        var pixels = new Rgba32[bitmap.Width * bitmap.Height];
        var idx = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                pixels[idx++] = new Rgba32(c.Red, c.Green, c.Blue, c.Alpha);
            }
        }

        return pixels;
    }
}
