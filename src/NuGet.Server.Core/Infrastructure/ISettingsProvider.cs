namespace NuGet.Server.Core.Infrastructure
{
    public interface ISettingsProvider
    {
        bool GetBoolSetting(string key, bool defaultValue);
    }
}
