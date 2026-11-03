using System.Text.Json;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Storages;

namespace VpnHood.AppLib.App;

internal class AppPersistState(Storage storage)
{
    private const string FileName = "state.json";
    private readonly Lock _saveLock = new();
    // defer reading state.json until the persisted state is first accessed
    private readonly Lazy<PersistData> _lazyData =
        new(() => storage.TryReadJson<PersistData>(FileName) ?? new PersistData());
    private PersistData Data => _lazyData.Value;

    private class PersistData
    {
        public ApiError? LastError { get; set; }
        public ApiError? LastClearedError { get; set; }
        public DateTime UpdateIgnoreTime { get; set; } = DateTime.MinValue;
        public DateTime? ConnectRequestTime { get; set; }
        public bool HasDiagnoseRequested { get; set; }
        public int SuccessfulConnectionsCount { get; set; }
        public bool IsReconnectRequired { get; set; }
        public bool IsVpnServiceRevoked { get; set; }
        public DateTime? VpnServiceRevokeClearedTime { get; set; }
    }

    public int SuccessfulConnectionsCount {
        get => Data.SuccessfulConnectionsCount;
        set {
            if (Data.SuccessfulConnectionsCount == value)
                return;
            Data.SuccessfulConnectionsCount = value;
            Save();
        }
    }

    public ApiError? LastError {
        get => Data.LastError;
        set {
            if (Equals(Data.LastError, value))
                return;

            Data.LastError = value;
            Save();
        }
    }

    public ApiError? LastClearedError {
        get => Data.LastClearedError;
        set {
            if (Equals(Data.LastClearedError, value))
                return;

            Data.LastClearedError = value;
            Save();
        }
    }

    public bool HasDiagnoseRequested {
        get => Data.HasDiagnoseRequested;
        set {
            if (HasDiagnoseRequested == value)
                return;

            Data.HasDiagnoseRequested = value;
            Save();
        }
    }

    public DateTime UpdateIgnoreTime {
        get => Data.UpdateIgnoreTime;
        set {
            if (Data.UpdateIgnoreTime == value)
                return;

            Data.UpdateIgnoreTime = value;
            Save();
        }
    }

    // Persisted (not in-memory): the running VpnService session may outlive this app process, and the
    // "reconnect to apply your change" state must survive an app restart with it
    public bool IsReconnectRequired {
        get => Data.IsReconnectRequired;
        set {
            if (Data.IsReconnectRequired == value)
                return;

            Data.IsReconnectRequired = value;
            Save();
        }
    }

    // Another app took the tunnel (VpnServiceRevokedException). Kept across a restart and a reboot, since
    // always on would otherwise take the tunnel back at the next start; a person's connect clears it.
    public bool IsVpnServiceRevoked {
        get => Data.IsVpnServiceRevoked;
        set {
            if (Data.IsVpnServiceRevoked == value)
                return;

            Data.IsVpnServiceRevoked = value;
            Save();
        }
    }

    // When a person's connect last cleared it: a revoke the VPN service wrote before that is an old one, which a
    // connect that never started the service leaves in the service's last word.
    public DateTime? VpnServiceRevokeClearedTime {
        get => Data.VpnServiceRevokeClearedTime;
        set {
            if (Data.VpnServiceRevokeClearedTime == value)
                return;

            Data.VpnServiceRevokeClearedTime = value;
            Save();
        }
    }

    public DateTime? ConnectRequestTime {
        get => Data.ConnectRequestTime;
        set {
            if (Data.ConnectRequestTime == value)
                return;

            Data.ConnectRequestTime = value;
            Save();
        }
    }

    internal static AppPersistState Load(Storage storage)
    {
        var ret = new AppPersistState(storage);
        return ret;
    }

    private void Save()
    {
        lock (_saveLock) {
            var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
            storage.Write(FileName, json);
        }
    }
}