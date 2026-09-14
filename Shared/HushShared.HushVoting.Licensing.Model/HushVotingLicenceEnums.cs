namespace HushShared.HushVoting.Licensing.Model;

/// <summary>Closed licence family. Wire values are ordinal and culture-independent.</summary>
public enum HushVotingLicenceFamily
{
    Direct = 0,
    Veritas = 1,
    Enterprise = 2,
}

/// <summary>Closed licence availability state for a plan in a catalogue snapshot.</summary>
public enum HushVotingLicenceAvailability
{
    Default = 0,
    AutomaticUpgrade = 1,
    Unavailable = 2,
}

/// <summary>Closed binding status of an election using a governance option.</summary>
public enum HushVotingBindingStatus
{
    NonBinding = 0,
    Binding = 1,
}

/// <summary>Ordinal wire-name helpers for licence enums (culture-independent, case-sensitive).</summary>
public static class HushVotingLicenceEnumNames
{
    public static string FamilyToWire(HushVotingLicenceFamily family) => family switch
    {
        HushVotingLicenceFamily.Direct => "Direct",
        HushVotingLicenceFamily.Veritas => "Veritas",
        HushVotingLicenceFamily.Enterprise => "Enterprise",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown family."),
    };

    public static string AvailabilityToWire(HushVotingLicenceAvailability availability) => availability switch
    {
        HushVotingLicenceAvailability.Default => "Default",
        HushVotingLicenceAvailability.AutomaticUpgrade => "AutomaticUpgrade",
        HushVotingLicenceAvailability.Unavailable => "Unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, "Unknown availability."),
    };

    public static string BindingStatusToWire(HushVotingBindingStatus status) => status switch
    {
        HushVotingBindingStatus.NonBinding => "NonBinding",
        HushVotingBindingStatus.Binding => "Binding",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown binding status."),
    };

    /// <summary>Parse a wire family name ordinally; null when unsupported (never coerced).</summary>
    public static HushVotingLicenceFamily? TryParseFamily(string? value) => value switch
    {
        "Direct" => HushVotingLicenceFamily.Direct,
        "Veritas" => HushVotingLicenceFamily.Veritas,
        "Enterprise" => HushVotingLicenceFamily.Enterprise,
        _ => null,
    };

    public static HushVotingLicenceAvailability? TryParseAvailability(string? value) => value switch
    {
        "Default" => HushVotingLicenceAvailability.Default,
        "AutomaticUpgrade" => HushVotingLicenceAvailability.AutomaticUpgrade,
        "Unavailable" => HushVotingLicenceAvailability.Unavailable,
        _ => null,
    };

    public static HushVotingBindingStatus? TryParseBindingStatus(string? value) => value switch
    {
        "NonBinding" => HushVotingBindingStatus.NonBinding,
        "Binding" => HushVotingBindingStatus.Binding,
        _ => null,
    };
}
