using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VpnHood.Common.ApiClients;
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
    private ApiError? _lastError;
    public ApiError? LastError
    {
        get => _lastError;
        set { _lastError = value; Save(); }
    }

    // prop
    private DateTime _updateIgnoreTime = DateTime.MinValue;
    public DateTime UpdateIgnoreTime
    {
        get => _updateIgnoreTime;
        set { _updateIgnoreTime = value; Save(); }
    }

    // prop
    private string? _clientCountryCode;
    public string? ClientCountryCode
    {
        get => _clientCountryCode;
        set
        {
            if (_clientCountryCode == value)
                return;
            
            // set country code and its name
            _clientCountryCode = value?.ToUpper();
            try
            {
                ClientCountryName = value!=null ? new RegionInfo(value).EnglishName : null;
            }
            catch(Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not get country name for code: {Code}", value);
                ClientCountryName = value;
            }
            Save();
        }
    }

    // prop
    [JsonIgnore]
    public string? ClientCountryName { get; private set; }


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