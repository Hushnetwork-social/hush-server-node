using HushShared.Elections.Model;

namespace HushNode.Elections;

public static class ElectionEligibilityContracts
{
    public const string TemporaryVerificationCode = "1111";

    public static IReadOnlyList<string> ValidateRosterImportEntries(
        IReadOnlyList<ElectionRosterImportItem> rosterEntries)
    {
        var errors = new List<string>();
        if (rosterEntries.Count == 0)
        {
            errors.Add("At least one roster entry is required.");
            return errors;
        }

        var seenOrganizationVoterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rosterEntries.Count; index++)
        {
            var entry = rosterEntries[index];
            var itemNumber = index + 1;

            if (string.IsNullOrWhiteSpace(entry.OrganizationVoterId))
            {
                errors.Add($"Roster entry {itemNumber} is missing organization_voter_id.");
            }
            else if (!seenOrganizationVoterIds.Add(entry.OrganizationVoterId.Trim()))
            {
                errors.Add($"Roster entry {itemNumber} duplicates organization_voter_id '{entry.OrganizationVoterId.Trim()}'.");
            }

            if (!Enum.IsDefined(entry.ContactType))
            {
                errors.Add($"Roster entry {itemNumber} has an unsupported contact type.");
            }

            if (string.IsNullOrWhiteSpace(entry.ContactValue))
            {
                errors.Add($"Roster entry {itemNumber} is missing the imported contact value.");
            }
        }

        return errors;
    }
}
