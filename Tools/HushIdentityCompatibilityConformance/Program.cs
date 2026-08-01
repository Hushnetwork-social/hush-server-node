using HushIdentityCompatibilityConformance.Corpus;

namespace HushIdentityCompatibilityConformance;

/// <summary>
/// Hush identity compatibility conformance runner (.NET adapter).
///
/// Usage:
///   dotnet run --project Tools/HushIdentityCompatibilityConformance -- \
///     --corpus <path> --manifest-digest <sha256> [--report <path>]
///
/// Exit codes:
///   0  All applicable vectors pass
///   1  Conformance mismatch
///   2  Invalid corpus, schema, or integrity input
///   3  Runner/internal failure
///
/// The runner is non-production tooling: it never enters HushServerNode
/// runtime dependency injection and never derives user private keys at server
/// runtime. Reports carry digests only; no credential values are ever emitted.
/// </summary>
public static class Program
{
    private const int ExitPass = 0;
    private const int ExitMismatch = 1;
    private const int ExitInvalidCorpus = 2;
    private const int ExitInternalFailure = 3;

    public static int Main(string[] args)
    {
        try
        {
            var (corpus, manifestDigest, reportPath) = ParseArgs(args);
            if (corpus is null)
            {
                PrintUsage();
                return ExitInternalFailure;
            }

            Console.WriteLine($"Corpus: {corpus}");
            Console.WriteLine($"Expected manifest digest: {manifestDigest}");

            var validation = CorpusValidator.Validate(corpus, manifestDigest!);
            if (!validation.Valid)
            {
                foreach (var error in validation.Errors) Console.Error.WriteLine("INPUT FAILURE: " + error);
                WriteFailureReport(reportPath, validation.Errors);
                Console.Error.WriteLine($"Invalid corpus input — exit {ExitInvalidCorpus}");
                return ExitInvalidCorpus;
            }

            var result = ConformanceRunner.Run(corpus);
            var report = result.Report;
            ReportWriter.Write(report, reportPath);
            WriteTimings(result.Timings, reportPath);
            Console.WriteLine($"Result: {report.Result} | total={report.Summary.Total} passed={report.Summary.Passed} failed={report.Summary.Failed}");
            foreach (var t in result.Timings)
            {
                Console.WriteLine($"TIMING {t.Operation} {t.ProducerId} {t.Milliseconds:F1}ms");
            }
            Console.WriteLine($"Report: {reportPath}");
            if (report.Records.Count > 0)
            {
                Console.Error.WriteLine($"Conformance mismatch — exit {ExitMismatch}");
                return ExitMismatch;
            }
            return ExitPass;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("RUNNER FAILURE: " + ex.Message);
            Console.Error.WriteLine($"Internal failure — exit {ExitInternalFailure}");
            return ExitInternalFailure;
        }
    }

    private static (string? Corpus, string? ManifestDigest, string ReportPath) ParseArgs(string[] args)
    {
        string? corpus = null;
        string? manifestDigest = null;
        var reportPath = Path.Combine(Environment.CurrentDirectory, "conformance", "reports", "dotnet-identity-report.json");
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--corpus" when i + 1 < args.Length:
                    corpus = args[++i];
                    break;
                case "--manifest-digest" when i + 1 < args.Length:
                    manifestDigest = args[++i];
                    break;
                case "--report" when i + 1 < args.Length:
                    reportPath = args[++i];
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(ExitPass);
                    break;
            }
        }
        if (corpus is null || manifestDigest is null)
        {
            return (null, null, reportPath);
        }
        return (corpus, manifestDigest, reportPath);
    }

    private static void WriteFailureReport(string reportPath, string[] errors)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var report = new
            {
                schemaVersion = ConformanceRunner.SchemaVersion,
                contractVersion = ConformanceRunner.ContractVersion,
                runtime = "dotnet",
                result = "ERROR",
                summary = new { total = 0, passed = 0, failed = 0 },
                records = Array.Empty<object>(),
                inputFailures = errors,
            };
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.Error.WriteLine($"Input failure report: {reportPath}");
        }
        catch
        {
            // report write failure must not mask the exit code
        }
    }

    /// <summary>Write per-group timings beside the report (no credential values).</summary>
    private static void WriteTimings(IReadOnlyList<ConformanceRunner.Timing> timings, string reportPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            var timingsPath = Path.Combine(dir ?? Environment.CurrentDirectory, "dotnet-timings.json");
            var payload = new
            {
                contractVersion = ConformanceRunner.ContractVersion,
                schemaVersion = ConformanceRunner.SchemaVersion,
                runtime = "dotnet",
                timings = timings.Select(t => new { operation = t.Operation, producerId = t.ProducerId, milliseconds = Math.Round(t.Milliseconds, 1) }).ToArray(),
            };
            File.WriteAllText(timingsPath, System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n", new System.Text.UTF8Encoding(false));
            Console.WriteLine($"Timings: {timingsPath}");
        }
        catch
        {
            // timing side-output must never mask the conformance result
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            HushIdentityCompatibilityConformance (.NET adapter)

            Usage:
              dotnet run --project Tools/HushIdentityCompatibilityConformance -- \
                --corpus <path> --manifest-digest <sha256> [--report <path>]

            Options:
              --corpus            Path to the canonical corpus root (conformance/identity/v1)
              --manifest-digest   Expected SHA-256 of manifest.json (pinned by CI/release config)
              --report            Output report path (default: ./conformance/reports/dotnet-identity-report.json)
              --help              Show this help

            Exit codes:
              0  All applicable vectors pass
              1  Conformance mismatch
              2  Invalid corpus, schema, or integrity input
              3  Runner/internal failure
            """);
    }
}
