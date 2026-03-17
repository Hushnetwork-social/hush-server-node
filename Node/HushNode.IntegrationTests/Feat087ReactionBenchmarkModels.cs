using System.Text.Json;
using HushServerNode.Testing;

namespace HushNode.IntegrationTests;

internal sealed record Feat087ReactionBenchmarkWorkload(
    string WorkloadKind,
    int Seed,
    int VoterCount,
    string PublicPostContent,
    double VisibilitySlaMs,
    IReadOnlyList<Feat087ReactionBenchmarkWorkloadItem> Voters);

internal sealed record Feat087ReactionBenchmarkWorkloadItem(
    int VoterNumber,
    string IdentitySeed,
    string DisplayName,
    int EmojiIndex);

internal sealed record Feat087ReactionBenchmarkMeasurement(
    int VoterNumber,
    string DisplayName,
    int EmojiIndex,
    int PreviousCount,
    int ExpectedCount,
    double SubmitMs,
    double PostBlockVisibilityMs,
    double EndToEndMs);

internal sealed record Feat087ReactionBenchmarkSummary(
    int VoterCount,
    double SubmitP50Ms,
    double SubmitP95Ms,
    double SubmitMaxMs,
    double PostBlockP50Ms,
    double PostBlockP95Ms,
    double PostBlockMaxMs,
    double EndToEndP50Ms,
    double EndToEndP95Ms,
    double EndToEndMaxMs);

internal sealed record Feat087ReactionBenchmarkReport(
    string ReportKind,
    string SubmitterKind,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ReactionProofPathReadiness ProofPathReadiness,
    Feat087ReactionBenchmarkWorkload Workload,
    Feat087ReactionBenchmarkSummary Summary,
    IReadOnlyList<Feat087ReactionBenchmarkMeasurement> Measurements);

internal static class Feat087ReactionBenchmarkReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Write(Feat087ReactionBenchmarkReport report)
    {
        var runDirectory = GetRunDirectory(report.CompletedAtUtc);
        Directory.CreateDirectory(runDirectory);

        var fileStem = $"feat087-reaction-benchmark-{report.SubmitterKind}-{report.CompletedAtUtc:yyyyMMdd-HHmmss}";
        var jsonPath = Path.Combine(runDirectory, $"{fileStem}.json");
        var summaryPath = Path.Combine(runDirectory, $"{fileStem}.summary.txt");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(summaryPath, BuildSummaryText(report));

        return jsonPath;
    }

    private static string GetRunDirectory(DateTimeOffset completedAtUtc)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "TestResults",
            "Benchmarks",
            $"TestRun_{completedAtUtc:yyyy-MM-dd_HH-mm-ss}"));
    }

    private static string BuildSummaryText(Feat087ReactionBenchmarkReport report)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"ReportKind: {report.ReportKind}",
            $"SubmitterKind: {report.SubmitterKind}",
            $"StartedAtUtc: {report.StartedAtUtc:O}",
            $"CompletedAtUtc: {report.CompletedAtUtc:O}",
            $"NonDevBenchmarkReady: {report.ProofPathReadiness.NonDevBenchmarkReady}",
            $"ClientProverArtifactsAvailable: {report.ProofPathReadiness.ClientProverArtifactsAvailable}",
            $"ClientHeadlessProverDependencyAvailable: {report.ProofPathReadiness.ClientHeadlessProverDependencyAvailable}",
            $"ClientWasmPath: {report.ProofPathReadiness.ClientWasmPath}",
            $"ClientZkeyPath: {report.ProofPathReadiness.ClientZkeyPath}",
            $"ClientPackageJsonPath: {report.ProofPathReadiness.ClientPackageJsonPath}",
            $"ServerVerificationKeyAvailable: {report.ProofPathReadiness.ServerVerificationKeyAvailable}",
            $"ServerVerificationKeyParsingImplemented: {report.ProofPathReadiness.ServerVerificationKeyParsingImplemented}",
            $"ServerFullGroth16VerificationImplemented: {report.ProofPathReadiness.ServerFullGroth16VerificationImplemented}",
            $"ServerVerificationKeyPath: {report.ProofPathReadiness.ServerVerificationKeyPath}",
            $"TestHostDevModeEnabled: {report.ProofPathReadiness.TestHostDevModeEnabled}",
            $"ReadinessNotes: {report.ProofPathReadiness.Notes}",
            $"WorkloadKind: {report.Workload.WorkloadKind}",
            $"Seed: {report.Workload.Seed}",
            $"VoterCount: {report.Workload.VoterCount}",
            $"VisibilitySlaMs: {report.Workload.VisibilitySlaMs:F1}",
            $"Submit P50/P95/Max (ms): {report.Summary.SubmitP50Ms:F1} / {report.Summary.SubmitP95Ms:F1} / {report.Summary.SubmitMaxMs:F1}",
            $"PostBlock P50/P95/Max (ms): {report.Summary.PostBlockP50Ms:F1} / {report.Summary.PostBlockP95Ms:F1} / {report.Summary.PostBlockMaxMs:F1}",
            $"EndToEnd P50/P95/Max (ms): {report.Summary.EndToEndP50Ms:F1} / {report.Summary.EndToEndP95Ms:F1} / {report.Summary.EndToEndMaxMs:F1}"
        });
    }
}
