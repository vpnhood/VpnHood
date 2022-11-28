using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common;

public abstract class AppBaseNet<T> : IDisposable where T : AppBaseNet<T>
{
    private static T? _instance;
    private Mutex? _instanceMutex;
    protected bool Disposed;

    protected AppBaseNet(string appName)
    {
        if (IsInit) throw new InvalidOperationException($"Only one instance of {typeof(T)} can be initialized");
        AppName = appName;

        // logger
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        _instance = (T)this;
    }

    public string AppName { get; }
    public string AppVersion => typeof(T).Assembly.GetName().Version?.ToString() ?? "*";

    public static T Instance =>
        _instance ?? throw new InvalidOperationException($"{typeof(T)} has not been initialized yet!");

    public static bool IsInit => _instance != null;

    public static string AppFolderPath => Path.GetDirectoryName(typeof(T).Assembly.Location) ??
                                          throw new Exception($"Could not acquire {nameof(AppFolderPath)}!");
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void Start(string[] args)
    {
        try
        {
            OnStart(args);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Stopped program because of exception!");
            throw;
        }
    }

    public bool IsAnotherInstanceRunning(string? name = null)
    {
        name ??= typeof(T).FullName;
        _instanceMutex ??= new Mutex(false, name);

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        return !_instanceMutex.WaitOne(TimeSpan.FromSeconds(0), false);
    }

    protected abstract void OnStart(string[] args);


    protected virtual void Dispose(bool disposing)
    {
        if (Disposed) return;
        if (disposing)
        {
            _instance = null;
        }

        Disposed = true;
    }
}