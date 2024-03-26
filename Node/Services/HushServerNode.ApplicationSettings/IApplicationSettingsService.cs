using HushServerNode.ApplicationSettings.Model;

namespace HushServerNode.ApplicationSettings;

public interface IApplicationSettingsService
{
    ServerInfo ServerInfo { get; }

    StackerInfo StackerInfo { get; }

    void LoadSettings();
}
