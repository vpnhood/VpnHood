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
            "vh://eyJuYW1lIjoiUHVibGljIFNlcnZlcnMiLCJ2IjoxLCJzaWQiOjEwMDAsInRpZCI6IjJlZmMwYzc5LWMxNzYtNDQzYy05MjBhLTFiOGNlZTNlMjU4OCIsInNlYyI6ImZHVHN2SFdWRVFIZHdUYlY1Vkt4MlE9PSIsImlzdiI6ZmFsc2UsImhuYW1lIjoibWFlLmN1ZHV4aXB1Lm5ldCIsImhwb3J0Ijo0NDMsImhlcCI6IjEzNS4xNDguMTIxLjEyNTo0NDMiLCJjaCI6ImY0VGRUUVlHWGlDZnFCSjc3V0xBcDlkNkk3MD0iLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy8xMTdsemx4NmdjdmMzcmYvcHVibGljMi5hY2Nlc3NrZXk/ZGw9MSJ9";
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