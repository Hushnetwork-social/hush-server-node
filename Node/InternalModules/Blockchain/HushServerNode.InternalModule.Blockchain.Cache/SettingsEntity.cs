namespace HushServerNode.InternalModule.Blockchain.Cache;

public class SettingsEntity
{
    public string SettingsId { get; set; } = string.Empty;

    public string SettingsType { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty; 

    public long ValidSinceBlock { get; set; }

    public long ValidUntilBlock { get; set; }
}
