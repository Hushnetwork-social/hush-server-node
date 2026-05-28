using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionLikeOperationalRunFixtureTests
{
    private static readonly string[] RequiredNegativeCategories =
    [
        "local_only",
        "private_chain_only",
        "missing_deployment_proof",
        "missing_webclient_proof",
        "missing_runtime_binding",
        "stale_security_evidence",
        "missing_monitoring",
        "missing_support",
        "missing_backup_restore",
        "missing_incident_declaration",
        "missing_operator_handoff",
        "missing_postmortem",
        "placeholder_evidence",
        "direct_register_mutation",
        "public_safe_forbidden_material"
    ];

    [Fact]
    public void ReleaseBaselineSource_ContainsEverySchemaRequiredSection()
    {
        var schema = LoadSchema();
        var baseline = LoadBaseline();

        var requiredSections = GetStringArray(schema, "required");

        foreach (var section in requiredSections)
        {
            baseline.ContainsKey(section).Should().BeTrue($"the release-baseline fixture must include {section}");
        }

        baseline["status"]!.GetValue<string>().Should().Be("accepted");
        baseline["runProfile"]!.AsObject()["localOnly"]!.GetValue<bool>().Should().BeFalse();
        baseline["runProfile"]!.AsObject()["privateChainOnly"]!.GetValue<bool>().Should().BeFalse();
        baseline["runProfile"]!.AsObject()["uncontrolledProduction"]!.GetValue<bool>().Should().BeFalse();
        baseline["readinessProposal"]!.AsObject()["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();
        baseline["readinessProposal"]!.AsObject()["promotionOwner"]!.GetValue<string>().Should().Be("FEAT-156");
    }

    [Fact]
    public void FixtureCatalog_LoadsEveryCaseWithStableExpectations()
    {
        var catalog = LoadCatalog();
        var workspaceRoot = FindWorkspaceRoot();

        catalog["schemaVersion"]!.GetValue<string>().Should().Be("production-like-operational-run-fixture-catalog.v1");
        catalog["featureId"]!.GetValue<string>().Should().Be("FEAT-154");

        var baselineSource = catalog["baselineSource"]!.GetValue<string>();
        File.Exists(Path.Combine(workspaceRoot, "hush-memory-bank", baselineSource)).Should().BeTrue();

        var cases = catalog["cases"]!.AsArray().Select(item => item!.AsObject()).ToArray();
        cases.Should().HaveCountGreaterThan(10);
        cases.Select(item => item["caseId"]!.GetValue<string>()).Should().OnlyHaveUniqueItems();

        foreach (var fixtureCase in cases)
        {
            fixtureCase["caseId"]!.GetValue<string>().Should().StartWith("FEAT154-");
            fixtureCase["category"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            fixtureCase["expectedStatus"]!.GetValue<string>().Should().BeOneOf(
                "accepted",
                "accepted_with_limitations",
                "blocked",
                "development_placeholder");
            fixtureCase["expectedDiagnostics"]!.AsArray().Should().NotBeNull();
            fixtureCase["claimImpact"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void FixtureCatalog_CoversPositiveLimitedBlockedStaleAndPlaceholderStates()
    {
        var cases = LoadCases();

        cases.Select(item => item["expectedStatus"]!.GetValue<string>())
            .Should()
            .Contain(["accepted", "accepted_with_limitations", "blocked", "development_placeholder"]);

        cases.Select(item => item["category"]!.GetValue<string>())
            .Should()
            .Contain(RequiredNegativeCategories);

        foreach (var negativeCase in cases.Where(IsNegativeCase))
        {
            negativeCase["expectedDiagnostics"]!.AsArray()
                .Should()
                .NotBeEmpty($"{negativeCase["caseId"]!.GetValue<string>()} must carry a stable diagnostic expectation");
        }

        DiagnosticFor("local_only").Should().Contain("FEAT154-RUN-PROFILE-LOCAL-ONLY");
        DiagnosticFor("private_chain_only").Should().Contain("FEAT154-RUN-PROFILE-PRIVATE-CHAIN-ONLY");
        DiagnosticFor("missing_deployment_proof").Should().Contain("FEAT154-DEPLOYMENT-PROOF-MISSING");
        DiagnosticFor("missing_webclient_proof").Should().Contain("FEAT154-WEBCLIENT-PROOF-MISSING");
        DiagnosticFor("missing_runtime_binding").Should().Contain("FEAT154-RUNTIME-BINDING-MISSING");
        DiagnosticFor("stale_security_evidence").Should().Contain("FEAT154-SECURITY-FRESHNESS-STALE");
        DiagnosticFor("missing_monitoring").Should().Contain("FEAT154-MONITORING-MISSING");
        DiagnosticFor("missing_support").Should().Contain("FEAT154-SUPPORT-MISSING");
        DiagnosticFor("missing_backup_restore").Should().Contain("FEAT154-BACKUP-RESTORE-MISSING");
        DiagnosticFor("missing_incident_declaration").Should().Contain("FEAT154-INCIDENT-DECLARATION-MISSING");
        DiagnosticFor("missing_operator_handoff").Should().Contain("FEAT154-OPERATOR-HANDOFF-MISSING");
        DiagnosticFor("missing_postmortem").Should().Contain("FEAT154-POSTMORTEM-MISSING");
        DiagnosticFor("direct_register_mutation").Should().Contain("FEAT154-REGISTER-MUTATION-FORBIDDEN");

        string[] DiagnosticFor(string category) =>
            cases.Single(item => item["category"]!.GetValue<string>() == category)["expectedDiagnostics"]!
                .AsArray()
                .Select(item => item!.GetValue<string>())
                .ToArray();
    }

    [Fact]
    public void AcceptedBaseline_DoesNotCountBlockingOrPlaceholderEvidenceStatuses()
    {
        var forbiddenStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            "blocked",
            "missing",
            "placeholder",
            "private_only",
            "mismatched",
            "stale",
            "superseded"
        };

        var statusValues = CollectPropertyValues(LoadBaseline(), "status");
        var freshnessValues = CollectPropertyValues(LoadBaseline(), "freshness");

        statusValues.Should().NotContain(value => forbiddenStatuses.Contains(value));
        freshnessValues.Should().NotContain(value => forbiddenStatuses.Contains(value));
    }

    [Fact]
    public void PublicSafeBaselineText_DoesNotContainForbiddenMaterialNeedles()
    {
        var catalog = LoadCatalog();
        var baseline = LoadBaseline();

        var forbiddenNeedles = catalog["forbiddenPublicMaterialNeedles"]!
            .AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();
        var publicSafeText = string.Join(
            "\n",
            CollectPublicSafeStrings(baseline).Select(value => value.ToLowerInvariant()));

        foreach (var needle in forbiddenNeedles)
        {
            publicSafeText.Should().NotContain(needle.ToLowerInvariant());
        }
    }

    private static bool IsNegativeCase(JsonObject fixtureCase)
    {
        var category = fixtureCase["category"]!.GetValue<string>();
        return category is not "accepted" and not "accepted_with_limitations";
    }

    private static JsonObject LoadSchema() =>
        LoadMemoryBankJson(
            "Overview",
            "HushVotingReadiness",
            "Production-Like-Operational-Run",
            "schemas",
            "production-like-operational-run-source.schema.json");

    private static JsonObject LoadBaseline() =>
        LoadMemoryBankJson(
            "Overview",
            "HushVotingReadiness",
            "Production-Like-Operational-Run",
            "examples",
            "release-baseline",
            "production-like-operational-run-source.json");

    private static JsonObject LoadCatalog() =>
        LoadMemoryBankJson(
            "Overview",
            "HushVotingReadiness",
            "Production-Like-Operational-Run",
            "examples",
            "fixture-catalog.json");

    private static JsonObject[] LoadCases() =>
        LoadCatalog()["cases"]!.AsArray().Select(item => item!.AsObject()).ToArray();

    private static JsonObject LoadMemoryBankJson(params string[] relativePath)
    {
        var fullPath = Path.Combine(new[] { FindWorkspaceRoot(), "hush-memory-bank" }.Concat(relativePath).ToArray());
        return JsonNode.Parse(File.ReadAllText(fullPath))!.AsObject();
    }

    private static string FindWorkspaceRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), sourceFilePath })
        {
            var root = FindWorkspaceRootFrom(startPath);
            if (root is not null)
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException("Could not locate HushNetworkOrg workspace root.");
    }

    private static string? FindWorkspaceRootFrom(string startPath)
    {
        var directory = new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "hush-memory-bank")) &&
                Directory.Exists(Path.Combine(directory.FullName, "hush-server-node")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string[] GetStringArray(JsonObject schema, string name) =>
        schema[name]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();

    private static string[] CollectPropertyValues(JsonNode? node, string propertyName)
    {
        var values = new List<string>();
        CollectPropertyValues(node, propertyName, values);
        return values.ToArray();
    }

    private static void CollectPropertyValues(JsonNode? node, string propertyName, List<string> values)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject)
            {
                if (property.Key == propertyName && property.Value is not null)
                {
                    values.Add(property.Value.GetValue<string>());
                }

                CollectPropertyValues(property.Value, propertyName, values);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                CollectPropertyValues(item, propertyName, values);
            }
        }
    }

    private static string[] CollectPublicSafeStrings(JsonNode? node)
    {
        var values = new List<string>();
        CollectPublicSafeStrings(node, values, isInsidePublicSection: false);
        return values.ToArray();
    }

    private static void CollectPublicSafeStrings(JsonNode? node, List<string> values, bool isInsidePublicSection)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject)
            {
                var isPublicSection = isInsidePublicSection ||
                    property.Key.Contains("public", StringComparison.OrdinalIgnoreCase);
                CollectPublicSafeStrings(property.Value, values, isPublicSection);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                CollectPublicSafeStrings(item, values, isInsidePublicSection);
            }
        }
        else if (isInsidePublicSection && node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var value))
            {
                values.Add(value);
            }
        }
    }
}
