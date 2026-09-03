namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Closed licence term. <see cref="Perpetual"/> or a whole number of calendar years
/// (<see cref="CalendarYears"/>); it is never expressed as a fixed number of days.
/// </summary>
public readonly record struct HushVotingLicenceTerm(HushVotingLicenceTermKind Kind, int Years)
{
    public static HushVotingLicenceTerm Perpetual { get; } = new(HushVotingLicenceTermKind.Perpetual, 0);

    public static HushVotingLicenceTerm OneCalendarYear { get; } = CalendarYears(1);

    /// <summary>Creates a calendar-years term. Only whole calendar years are valid; never 365 days.</summary>
    public static HushVotingLicenceTerm CalendarYears(int years)
    {
        if (years < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(years), "A calendar-years term must be at least 1 year.");
        }

        return new HushVotingLicenceTerm(HushVotingLicenceTermKind.CalendarYears, years);
    }

    public bool IsPerpetual => Kind == HushVotingLicenceTermKind.Perpetual;

    public bool IsOneCalendarYear => Kind == HushVotingLicenceTermKind.CalendarYears && Years == 1;

    /// <summary>Safe display description for the term (never "365 days").</summary>
    public string SafeDescription => Kind switch
    {
        HushVotingLicenceTermKind.Perpetual => "Perpetual",
        HushVotingLicenceTermKind.CalendarYears when Years == 1 => "One calendar year",
        HushVotingLicenceTermKind.CalendarYears => $"{Years} calendar years",
        _ => throw new InvalidOperationException("Unsupported licence term kind."),
    };
}

public enum HushVotingLicenceTermKind
{
    Perpetual = 0,
    CalendarYears = 1,
}
