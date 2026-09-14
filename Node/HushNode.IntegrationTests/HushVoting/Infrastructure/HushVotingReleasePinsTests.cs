// FEAT-007/008/009 Phase 7: immutable public inputs; no private corpus access.
using System.Text;
using System.Text.Json.Nodes;
using HushVoting.IntegrationTests.Infrastructure;
using Xunit;

namespace HushVoting.IntegrationTests.ToolingTests;

[Trait("Category", "HushVoting")]
[Trait("Category", "HV-RELEASE-TOOLING")]
public sealed class HushVotingReleasePinsTests
{
    [Theory]
    [InlineData("intact")]
    [InlineData("modified")]
    [InlineData("length")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("traversal")]
    [InlineData("symlink-parent")]
    [InlineData("version")]
    public void CorpusIntegrity_RequiresEveryExactOwnedMember(string defect)
    {
        var root = Path.Combine(Path.GetTempPath(), "hv-release-unit-" + Guid.NewGuid().ToString("N"));
        var corpus = Path.Combine(root, "corpus");
        Directory.CreateDirectory(Path.Combine(corpus, "vectors"));
        var bytes = Encoding.UTF8.GetBytes("public-vector-A");
        var member = Path.Combine(corpus, "vectors", "public.txt");
        File.WriteAllBytes(member, bytes);
        var item = new JsonObject { ["path"] = "vectors/public.txt", ["bytes"] = bytes.Length, ["sha256"] = HushVotingReleasePins.Digest(bytes) };
        var entries = new JsonArray(item);
        var manifest = new JsonObject { ["contractVersion"] = "1.0.0", ["files"] = entries };
        try
        {
            switch (defect)
            {
                case "modified": File.WriteAllText(member, "public-vector-B"); break;
                case "length": item["bytes"] = bytes.Length + 1; break;
                case "missing": File.Delete(member); break;
                case "duplicate": entries.Add(item.DeepClone()); break;
                case "traversal": item["path"] = "../public.txt"; File.WriteAllBytes(Path.Combine(root, "public.txt"), bytes); break;
                case "symlink-parent":
                    Directory.CreateDirectory(Path.Combine(root, "external"));
                    File.WriteAllBytes(Path.Combine(root, "external", "public.txt"), bytes);
                    Directory.CreateSymbolicLink(Path.Combine(corpus, "linked"), Path.Combine(root, "external"));
                    item["path"] = "linked/public.txt";
                    break;
                case "version": manifest["contractVersion"] = "latest"; break;
            }
            File.WriteAllText(Path.Combine(corpus, "manifest.json"), manifest.ToJsonString());
            if (defect == "intact") HushVotingReleasePins.VerifyCorpus(corpus);
            else Assert.Throws<InvalidOperationException>(() => HushVotingReleasePins.VerifyCorpus(corpus));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
