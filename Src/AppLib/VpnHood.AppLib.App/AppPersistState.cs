using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib;

internal class AppPersistState
{
    private readonly object _saveLock = new();

    [JsonIgnore] public string FilePath { get; private set; } = null!;

    // prop
    private ApiError? _lastError;

    public ApiError? LastError {
        get => _lastError;
        set {
            _lastError = value;
            Save();
        }
    }

    // prop
    private DateTime _updateIgnoreTime = DateTime.MinValue;

    public DateTime UpdateIgnoreTime {
        get => _updateIgnoreTime;
        set {
            _updateIgnoreTime = value;
            Save();
        }
    }

    // prop
    private string? _clientCountryCode;

    public string? ClientCountryCode {
        get => _clientCountryCode;
        set {
            if (_clientCountryCode == value)
                return;

            // set country code and its name
            _clientCountryCode = value?.ToUpper();
            Save();
        }
    }

    // prop
    private string? _clientCountryCodeByServer;

    public string? ClientCountryCodeByServer {
        get => _clientCountryCodeByServer;
        set {
            if (_clientCountryCodeByServer == value)
                return;

            // set country code and its name
            _clientCountryCodeByServer = value?.ToUpper();
            Save();
        }
    }

    internal static AppPersistState Load(string filePath)
    {
        var ret = VhUtil.JsonDeserializeFile<AppPersistState>(filePath, logger: VhLogger.Instance) ??
                  new AppPersistState();
        ret.FilePath = filePath;
        return ret;
    }

    private void Save()
    {
        lock (_saveLock) {
            if (string.IsNullOrEmpty(FilePath))
                return; // loading

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json, Encoding.UTF8);
        }
    }
}