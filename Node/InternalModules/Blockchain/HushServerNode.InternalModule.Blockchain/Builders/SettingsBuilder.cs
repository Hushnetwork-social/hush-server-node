using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Blockchain.Builders;

public class SettingsBuilder
{
    private string _setingsId = string.Empty;
    private string _type = string.Empty;
    private string _key = string.Empty;
    private string _value = string.Empty;
    private long _validSinceBlock;

    public SettingsBuilder WithSettingsId(Guid settingsId)
    {
        this._setingsId = settingsId.ToString();
        return this;
    }

    public SettingsBuilder WithSetting(string type, string key, string value, long validSinceBlock)
    {
        this._type = type;
        this._key = key;
        this._value = value;
        this._validSinceBlock = validSinceBlock;

        return this;
    }

    public Settings Build()
    {
        return new Settings
        {
            SettingId = this._setingsId,
            SettingsTable = this._type,
            Key = this._key,
            Value = this._value,
            ValidSinceBlock = this._validSinceBlock
        };
    }
}
