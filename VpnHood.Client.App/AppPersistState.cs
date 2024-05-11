using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

internal class AppPersistState
{
    private readonly object _saveLock = new();

    [JsonIgnore]
    public string FilePath { get; private set; } = null!;

    // prop
    private string? _lastErrorMessage;
    public string? LastErrorMessage
    {
        get => _lastErrorMessage;
        set { _lastErrorMessage = value; Save(); }
    }

    // prop
    private DateTime _updateIgnoreTime = DateTime.MinValue;
    public DateTime UpdateIgnoreTime
    {
        get => _updateIgnoreTime;
        set { _updateIgnoreTime = value; Save(); }
    }

    internal static AppPersistState Load(string filePath)
    {
        var ret = VhUtil.JsonDeserializeFile<AppPersistState>(filePath, logger: VhLogger.Instance) ?? new AppPersistState();
        ret.FilePath = filePath;
        return ret;
    }

    private void Save()
    {
        lock (_saveLock)
        {
            if (string.IsNullOrEmpty(FilePath))
                return; // loading

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json, Encoding.UTF8);
        }
    }
}