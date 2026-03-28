namespace HushNode.Elections;

public record ElectionCeremonyOptions(
    bool EnableDevCeremonyProfiles = true,
    string ApprovedRegistryRelativePath = "ceremony-profiles/omega-v1.0.0/approved-ceremony-profiles.json",
    string RequiredRolloutVersion = "omega-v1.0.0");
