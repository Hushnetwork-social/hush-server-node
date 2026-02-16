using System.Text;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Generates minimal valid PDF and video test files for E2E attachment tests.
/// Each file has a labeled name encoding sender, target, and file type
/// so test screenshots and verification can identify exactly which file
/// arrived at which recipient.
///
/// PDFs are generated as minimal valid PDF 1.0 with correct cross-reference offsets.
/// Videos use a minimal binary blob (frame extraction will fail gracefully,
/// resulting in the video-fallback UI - which is sufficient for E2E verification).
/// </summary>
internal static class TestFileGenerator
{
    /// <summary>
    /// Generates a labeled PDF document and returns the file name and PDF bytes.
    /// The PDF is minimal (single blank page) but valid enough for the attachment pipeline.
    /// pdfjs-dist in the browser may or may not render the first page, but the
    /// processAttachments pipeline handles errors gracefully.
    /// </summary>
    /// <param name="senderName">Display name of sender (e.g., "Alice").</param>
    /// <param name="targetName">Display name of target (e.g., "Bob").</param>
    /// <returns>Tuple of (fileName, pdfBytes).</returns>
    public static (string FileName, byte[] PdfBytes) GenerateTestPdf(string senderName, string targetName)
    {
        var fileName = $"Test-PDF-from-{senderName}-to-{targetName}.pdf";
        var pdfBytes = BuildMinimalPdf();
        return (fileName, pdfBytes);
    }

    /// <summary>
    /// Generates a labeled video file and returns the file name and bytes.
    /// The video is a minimal binary blob with correct MP4 ftyp header.
    /// Video frame extraction will fail gracefully in the browser, resulting
    /// in the fallback UI (Video icon) which is sufficient for E2E testing.
    /// </summary>
    /// <param name="senderName">Display name of sender (e.g., "Alice").</param>
    /// <param name="targetName">Display name of target (e.g., "Bob").</param>
    /// <returns>Tuple of (fileName, mp4Bytes).</returns>
    public static (string FileName, byte[] Mp4Bytes) GenerateTestVideo(string senderName, string targetName)
    {
        var fileName = $"Test-Video-from-{senderName}-to-{targetName}.mp4";
        var mp4Bytes = BuildMinimalMp4();
        return (fileName, mp4Bytes);
    }

    /// <summary>
    /// Returns a tiny "executable" file for blocked file rejection testing.
    /// </summary>
    public static (string FileName, byte[] ExeBytes) GenerateBlockedExe()
    {
        var fileName = "program.exe";
        var exeBytes = Encoding.UTF8.GetBytes("MZ_FAKE_EXECUTABLE");
        return (fileName, exeBytes);
    }

    /// <summary>
    /// Builds a minimal valid PDF 1.0 document (single blank page, ~230 bytes).
    /// Cross-reference offsets are computed accurately.
    /// </summary>
    private static byte[] BuildMinimalPdf()
    {
        // Build the PDF body with known byte offsets
        var sb = new StringBuilder();
        sb.Append("%PDF-1.0\n");

        var obj1Offset = sb.Length;
        sb.Append("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n");

        var obj2Offset = sb.Length;
        sb.Append("2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");

        var obj3Offset = sb.Length;
        sb.Append("3 0 obj<</Type/Page/MediaBox[0 0 612 792]/Parent 2 0 R>>endobj\n");

        var xrefOffset = sb.Length;
        sb.Append("xref\n0 4\n");
        sb.Append("0000000000 65535 f \n");
        sb.Append($"{obj1Offset:D10} 00000 n \n");
        sb.Append($"{obj2Offset:D10} 00000 n \n");
        sb.Append($"{obj3Offset:D10} 00000 n \n");
        sb.Append("trailer<</Root 1 0 R/Size 4>>\n");
        sb.Append($"startxref\n{xrefOffset}\n%%EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Builds a minimal MP4 file with an ftyp box and an empty mdat box (~24 bytes).
    /// This is enough to pass MIME type detection and the attachment pipeline.
    /// The browser's video decoder will not be able to extract frames, which causes
    /// the VideoThumbnail component to render the fallback UI (Video icon).
    /// </summary>
    private static byte[] BuildMinimalMp4()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ftyp box (File Type Box)
        // Box size (big-endian uint32): 20 bytes
        bw.Write(ToBigEndian(20));
        bw.Write(Encoding.ASCII.GetBytes("ftyp"));
        bw.Write(Encoding.ASCII.GetBytes("isom")); // major brand
        bw.Write(ToBigEndian(0x200));               // minor version
        bw.Write(Encoding.ASCII.GetBytes("isom")); // compatible brand

        // mdat box (Media Data Box) - empty
        // Box size (big-endian uint32): 8 bytes (header only)
        bw.Write(ToBigEndian(8));
        bw.Write(Encoding.ASCII.GetBytes("mdat"));

        return ms.ToArray();
    }

    /// <summary>
    /// Converts a 32-bit integer to big-endian byte order (for MP4 box headers).
    /// </summary>
    private static byte[] ToBigEndian(int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }
}
