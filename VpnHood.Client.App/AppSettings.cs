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
        private string _settingsFilePath;

        public bool LogToFile { get; set; } = false;
        public bool LogVerbose { get; set; } = true;
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public string CultureName { get; set; } = "en";

        public void Save()
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(_settingsFilePath, json, Encoding.UTF8);
        }

        internal static AppSettings Load(string settingsFilePath)
        {
            try
            {
                var json = File.ReadAllText(settingsFilePath, Encoding.UTF8);
                var ret = JsonSerializer.Deserialize<AppSettings>(json);
                ret._settingsFilePath = settingsFilePath;
                return ret;

            }
            catch
            {
                var ret = new AppSettings
                {
                    _settingsFilePath = settingsFilePath
                };
                return ret;
            }
        }
    }
}
