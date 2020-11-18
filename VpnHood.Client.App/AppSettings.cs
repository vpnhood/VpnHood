using System;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VpnHood.Client.App
{
    public class AppSettings
    {
        public string SettingsFilePath { get; private set; }
        public AppUserSettings UserSettings { get; set; } = new AppUserSettings();
        public Guid ClientId { get; set; } = Guid.NewGuid();

        public void Save()
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
        }

        internal static AppSettings Load(string settingsFilePath)
        {
            try
            {
                var json = File.ReadAllText(settingsFilePath, Encoding.UTF8);
                var ret = JsonSerializer.Deserialize<AppSettings>(json);
                ret.SettingsFilePath = settingsFilePath;
                return ret;
            }
            catch
            {
                var ret = new AppSettings
                {
                    SettingsFilePath = settingsFilePath
                };
                return ret;
            }
        }
    }
}
