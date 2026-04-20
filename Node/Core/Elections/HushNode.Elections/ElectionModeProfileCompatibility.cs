using HushShared.Elections.Model;

namespace HushNode.Elections;

public static class ElectionModeProfileCompatibility
{
    public static bool IsProfileAllowed(
        ElectionBindingStatus bindingStatus,
        ElectionCeremonyProfileRecord profile) =>
        bindingStatus switch
        {
            ElectionBindingStatus.NonBinding => true,
            _ => !profile.DevOnly,
        };

    public static string BuildIncompatibilityReason(
        ElectionBindingStatus bindingStatus,
        ElectionCeremonyProfileRecord profile) =>
        bindingStatus switch
        {
            _ =>
                $"Binding elections cannot use dev/open ceremony profiles. Profile {profile.ProfileId} is marked dev-only.",
        };
}
