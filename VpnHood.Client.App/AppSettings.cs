using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Common;

namespace VpnHood.Client.App
{
    public class AppSettings
    {
        [JsonIgnore] public string SettingsFilePath { get; private set; } = null!;

        public UserSettings UserSettings { get; set; } = new();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Token TestServerToken => Token.FromAccessKey(TestServerAccessKey);
        public string? TestServerTokenAutoAdded { get; set; }

        // ReSharper disable StringLiteralTypo
        public string TestServerAccessKey =>
            "vh://eyJuYW1lIjoiUHVibGljIiwidiI6MSwic2lkIjoxMDAwLCJ0aWQiOiIyZWZjMGM3OS1jMTc2LTQ0M2MtOTIwYS0xYjhjZWUzZTI1ODgiLCJzZWMiOiJmR1RzdkhXVkVRSGR3VGJWNVZLeDJRPT0iLCJpc3YiOmZhbHNlLCJobmFtZSI6Im1hZS5jdWR1eGlwdS5uZXQiLCJocG9ydCI6NDQzLCJoZXAiOiIxMzUuMTQ4LjEyMS4xMjU6NDQzIiwiY2giOiJmNFRkVFFZR1hpQ2ZxQko3N1dMQXA5ZDZJNzA9IiwicGIiOnRydWUsInVybCI6Imh0dHBzOi8vd3d3LmRyb3Bib3guY29tL3MvMTE3bHpseDZnY3ZjM3JmL3B1YmxpYzIuYWNjZXNza2V5P2RsPTEifQ==";
        // ReSharper restore StringLiteralTypo

        public event EventHandler? OnSaved;

        public void Save()
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
            OnSaved?.Invoke(this, EventArgs.Empty);
        }


        internal static AppSettings Load(string settingsFilePath)
        {
            try
            {
                var json = File.ReadAllText(settingsFilePath, Encoding.UTF8);
                var ret = JsonSerializer.Deserialize<AppSettings>(json) ??
                          throw new FormatException(
                              $"Could not deserialize {nameof(AppSettings)} from {settingsFilePath}");
                ret.SettingsFilePath = settingsFilePath;
                return ret;
            }
            catch
            {
                var ret = new AppSettings
                {
                    SettingsFilePath = settingsFilePath
                };
                ret.Save();
                return ret;
            }
        }
    }
}