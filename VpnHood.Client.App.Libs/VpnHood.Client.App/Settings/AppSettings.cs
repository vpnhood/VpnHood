﻿using System.Text;
using System.Text.Json;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Settings;

public class AppSettings
{
    private string _filePath = default!;
    private readonly object _saveLock = new();
    public event EventHandler? BeforeSave;

    public int Version { get; set; } = 2;
    public bool? IsQuickLaunchEnabled { get; set; }
    public bool? IsNotificationEnabled { get; set; }
    public DateTime ConfigTime { get; set; } = DateTime.Now;
    public UserSettings UserSettings { get; set; } = new();
    public string ClientId { get; set; } = Guid.NewGuid().ToString();

    public void Save()
    {
        BeforeSave?.Invoke(this, EventArgs.Empty);

        lock (_saveLock) {
            ConfigTime = DateTime.Now;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json, Encoding.UTF8);
        }
    }

    internal static AppSettings Load(string settingsFilePath)
    {
        var isChanged = false;
        var res = VhUtil.JsonDeserializeFile<AppSettings>(settingsFilePath);
        if (res == null) {
            res = new AppSettings();
            isChanged = true;
        }
        
        // Update version and settings file path
        res._filePath = settingsFilePath;

        // Save settings if changed
        if (isChanged) {
            try {
                res.Save();
            }
            catch (Exception ex) {
                Console.WriteLine($"Could not save new settings. Error: {ex.Message}");
            }
        }

        return res;
    }
}