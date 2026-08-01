using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushIdentityCompatibilityConformance;

/// <summary>Writes the cross-runtime secret-safe JSON report (report.schema.json).</summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Write(ConformanceRunner.ConformanceReport report, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(report, Options);
        // Corpus formatting rules require UTF-8 without BOM.
        File.WriteAllText(path, json + "\n", new System.Text.UTF8Encoding(false));
    }
}
