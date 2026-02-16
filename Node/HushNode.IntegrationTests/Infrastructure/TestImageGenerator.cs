using System.Text;
using SkiaSharp;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Generates labeled PNG images and animated GIFs for E2E attachment tests.
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

    // =========================================================================
    // Animated GIF generation
    // =========================================================================

    private const int GifWidth = 50;
    private const int GifHeight = 50;

    /// <summary>
    /// Frame colors for the animated GIF: red → green → blue cycle.
    /// </summary>
    private static readonly (byte R, byte G, byte B)[] GifFrameColors =
    [
        (0xFF, 0x44, 0x44), // Red
        (0x44, 0xCC, 0x44), // Green
        (0x44, 0x44, 0xFF), // Blue
    ];

    /// <summary>
    /// Generates a 3-frame animated GIF (50×50, cycling red→green→blue).
    /// The GIF loops infinitely via the NETSCAPE2.0 extension.
    /// Uses the GIF89a format with proper LZW compression.
    /// </summary>
    /// <param name="gifIndex">1-based index for naming.</param>
    /// <param name="senderName">Display name of sender (e.g., "Alice").</param>
    /// <param name="targetName">Display name of target (e.g., "Bob").</param>
    /// <returns>Tuple of (fileName, gifBytes).</returns>
    public static (string FileName, byte[] GifBytes) GenerateAnimatedTestGif(
        int gifIndex, string senderName, string targetName)
    {
        var fileName = $"Animated-{gifIndex}-from-{senderName}-to-{targetName}.gif";
        var gifBytes = BuildAnimatedGif();
        return (fileName, gifBytes);
    }

    private static byte[] BuildAnimatedGif()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // === GIF89a Header ===
        bw.Write(Encoding.ASCII.GetBytes("GIF89a"));

        // === Logical Screen Descriptor ===
        bw.Write((ushort)GifWidth);
        bw.Write((ushort)GifHeight);
        // Packed: GCT flag=1, color res=1 (2-bit), sort=0, GCT size=1 (4 entries)
        bw.Write((byte)0x91);
        bw.Write((byte)0); // background color index
        bw.Write((byte)0); // pixel aspect ratio

        // === Global Color Table (4 entries × 3 bytes = 12 bytes) ===
        foreach (var (r, g, b) in GifFrameColors)
        {
            bw.Write(r);
            bw.Write(g);
            bw.Write(b);
        }

        // 4th entry (filler — GCT must have 2^(N+1) entries)
        bw.Write((byte)0xFF);
        bw.Write((byte)0xFF);
        bw.Write((byte)0xFF);

        // === NETSCAPE2.0 Application Extension (infinite loop) ===
        bw.Write((byte)0x21); // Extension Introducer
        bw.Write((byte)0xFF); // Application Extension Label
        bw.Write((byte)11);   // Block Size
        bw.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        bw.Write((byte)3);    // Sub-block Size
        bw.Write((byte)1);    // Sub-block ID
        bw.Write((ushort)0);  // Loop Count (0 = infinite)
        bw.Write((byte)0);    // Block Terminator

        // === 3 Frames ===
        for (var frame = 0; frame < 3; frame++)
        {
            // Graphic Control Extension
            bw.Write((byte)0x21); // Extension Introducer
            bw.Write((byte)0xF9); // Graphic Control Label
            bw.Write((byte)4);    // Block Size
            bw.Write((byte)0x00); // Packed: disposal=none, no user input, no transparent
            bw.Write((ushort)50); // Delay: 50 × 10ms = 500ms
            bw.Write((byte)0);    // Transparent Color Index
            bw.Write((byte)0);    // Block Terminator

            // Image Descriptor
            bw.Write((byte)0x2C); // Image Separator
            bw.Write((ushort)0);  // Left
            bw.Write((ushort)0);  // Top
            bw.Write((ushort)GifWidth);
            bw.Write((ushort)GifHeight);
            bw.Write((byte)0x00); // Packed: no local color table

            // Image Data: LZW min code size + compressed data
            const byte minCodeSize = 2;
            bw.Write(minCodeSize);

            var pixels = new byte[GifWidth * GifHeight];
            Array.Fill(pixels, (byte)frame);
            var lzwData = GifLzwEncode(pixels, minCodeSize);

            // Write as sub-blocks (max 255 bytes each)
            var offset = 0;
            while (offset < lzwData.Length)
            {
                var blockSize = Math.Min(255, lzwData.Length - offset);
                bw.Write((byte)blockSize);
                bw.Write(lzwData, offset, blockSize);
                offset += blockSize;
            }

            bw.Write((byte)0); // Block Terminator
        }

        // === Trailer ===
        bw.Write((byte)0x3B);

        return ms.ToArray();
    }

    /// <summary>
    /// LZW encoder for GIF image data.
    /// Implements variable-length code packing (LSB-first) per the GIF spec.
    /// </summary>
    private static byte[] GifLzwEncode(byte[] pixels, int minCodeSize)
    {
        var clearCode = 1 << minCodeSize;
        var eoiCode = clearCode + 1;
        var codeSize = minCodeSize + 1;
        var nextCode = eoiCode + 1;
        var maxCode = 1 << codeSize;

        // String table: (prefix code, suffix byte) → new code
        var table = new Dictionary<(int prefix, byte suffix), int>();

        // Bit packer: accumulates variable-length codes into bytes (LSB first)
        using var output = new MemoryStream();
        var bitBuffer = 0;
        var bitsInBuffer = 0;

        void WriteBits(int code, int bits)
        {
            bitBuffer |= code << bitsInBuffer;
            bitsInBuffer += bits;
            while (bitsInBuffer >= 8)
            {
                output.WriteByte((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitsInBuffer -= 8;
            }
        }

        // Start with clear code
        WriteBits(clearCode, codeSize);

        var prefix = (int)pixels[0];

        for (var i = 1; i < pixels.Length; i++)
        {
            var suffix = pixels[i];
            var key = (prefix, suffix);

            if (table.TryGetValue(key, out var existingCode))
            {
                prefix = existingCode;
            }
            else
            {
                WriteBits(prefix, codeSize);

                if (nextCode < 4096)
                {
                    table[key] = nextCode++;
                    if (nextCode >= maxCode && codeSize < 12)
                    {
                        codeSize++;
                        maxCode = 1 << codeSize;
                    }
                }
                else
                {
                    // Table full — emit clear code and reset
                    WriteBits(clearCode, codeSize);
                    table.Clear();
                    nextCode = eoiCode + 1;
                    codeSize = minCodeSize + 1;
                    maxCode = 1 << codeSize;
                }

                prefix = suffix;
            }
        }

        // Output final prefix and EOI
        WriteBits(prefix, codeSize);
        WriteBits(eoiCode, codeSize);

        // Flush remaining bits
        if (bitsInBuffer > 0)
        {
            output.WriteByte((byte)(bitBuffer & 0xFF));
        }

        return output.ToArray();
    }
}
