using System.Text;
using System.Text.Json;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib;

internal class AppPersistState(string filePath)
{
    private readonly object _saveLock = new();
    private readonly Data _data = JsonUtils.TryDeserializeFile<Data>(filePath) ?? new Data();

    private class Data
    {
        public ApiError? LastError { get; set; }
        public ApiError? LastClearedError { get; set; }
        public DateTime UpdateIgnoreTime { get; set; } = DateTime.MinValue;
        public bool HasDisconnectedByUser { get; set; }
        public DateTime? ConnectRequestTime { get; set; }
        public bool HasDiagnoseRequested { get; set; }
    }

    public ApiError? LastError {
        get => _data.LastError;
        set {
            if (Equals(_data.LastError, value))
                return;

            _data.LastError = value;
            Save();
        }
    }

    public ApiError? LastClearedError {
        get => _data.LastClearedError;
        set {
            if (Equals(_data.LastClearedError, value))
                return;

            _data.LastClearedError = value;
            Save();
        }
    }
    public bool HasDiagnoseRequested {
        get => _data.HasDiagnoseRequested;
        set {
            if (HasDiagnoseRequested==value)
                return;

            _data.HasDiagnoseRequested = value;
            Save();
        }
    }

    public DateTime UpdateIgnoreTime {
        get => _data.UpdateIgnoreTime;
        set {
            if (_data.UpdateIgnoreTime == value)
                return;

            _data.UpdateIgnoreTime = value;
            Save();
        }
    }

    public bool HasDisconnectedByUser
    {
        get => _data.HasDisconnectedByUser;
        set
        {
            if (_data.HasDisconnectedByUser == value)
                return;

            _data.HasDisconnectedByUser = value;
            Save();
        }
    }

    public DateTime? ConnectRequestTime
    {
        get => _data.ConnectRequestTime;
        set
        {
            if (_data.ConnectRequestTime == value)
                return;

            _data.ConnectRequestTime = value;
            Save();
        }
    }

    internal static AppPersistState Load(string filePath)
    {
        var ret = new AppPersistState(filePath);
        return ret;
    }

    private void Save()
    {
        lock (_saveLock) {
            if (string.IsNullOrEmpty(filePath))
                return; // loading

            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
    }
}