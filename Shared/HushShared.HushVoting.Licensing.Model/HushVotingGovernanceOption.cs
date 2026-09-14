namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Customer-visible governance option metadata. Zero customer trustees maps internally to an admin
/// circuit but the customer projection stays 0/0. Fixed trustee schemes carry exact customer
/// trustee and required-approval counts and support both Binding and Non-Binding elections.
/// </summary>
public sealed class HushVotingGovernanceOption : IEquatable<HushVotingGovernanceOption>
{
    public HushVotingGovernanceOption(
        HushVotingGovernanceOptionId id,
        int customerTrusteeCount,
        int requiredApprovalCount,
        string safeLabel,
        IReadOnlySet<HushVotingBindingStatus> supportedBindingStatuses)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(safeLabel);
        ArgumentNullException.ThrowIfNull(supportedBindingStatuses);

        if (!id.IsKnown)
        {
            throw new ArgumentException("A governance option must use a known closed identifier.", nameof(id));
        }

        if (customerTrusteeCount < 0 || requiredApprovalCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(customerTrusteeCount),
                "Trustee and approval counts must be non-negative.");
        }

        if (supportedBindingStatuses.Count == 0)
        {
            throw new ArgumentException(
                "A governance option must support at least one binding status.",
                nameof(supportedBindingStatuses));
        }

        Id = id;
        CustomerTrusteeCount = customerTrusteeCount;
        RequiredApprovalCount = requiredApprovalCount;
        SafeLabel = safeLabel;
        SupportedBindingStatuses = supportedBindingStatuses;
    }

    public HushVotingGovernanceOptionId Id { get; }

    public int CustomerTrusteeCount { get; }

    public int RequiredApprovalCount { get; }

    public string SafeLabel { get; }

    /// <summary>Immutable set of supported Binding/Non-Binding modes.</summary>
    public IReadOnlySet<HushVotingBindingStatus> SupportedBindingStatuses { get; }

    public bool SupportsBindingStatus(HushVotingBindingStatus status) =>
        SupportedBindingStatuses.Contains(status);

    public bool Equals(HushVotingGovernanceOption? other) =>
        other is not null &&
        Id == other.Id &&
        CustomerTrusteeCount == other.CustomerTrusteeCount &&
        RequiredApprovalCount == other.RequiredApprovalCount &&
        string.Equals(SafeLabel, other.SafeLabel, StringComparison.Ordinal) &&
        SupportedBindingStatuses.SetEquals(other.SupportedBindingStatuses);

    public override bool Equals(object? obj) => Equals(obj as HushVotingGovernanceOption);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(CustomerTrusteeCount);
        hash.Add(RequiredApprovalCount);
        hash.Add(SafeLabel, StringComparer.Ordinal);
        foreach (var status in SupportedBindingStatuses.Order())
        {
            hash.Add(status);
        }

        return hash.ToHashCode();
    }
}
