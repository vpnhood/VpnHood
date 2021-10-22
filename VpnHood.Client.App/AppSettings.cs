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
            "vh://eyJuYW1lIjoiVnBuSG9vZCBQdWJsaWMgU2VydmVycyIsInYiOjEsInNpZCI6MTAwMSwidGlkIjoiNWFhY2VjNTUtNWNhYy00NTdhLWFjYWQtMzk3Njk2OTIzNmY4Iiwic2VjIjoiNXcraUhNZXcwQTAzZ3c0blNnRFAwZz09IiwiaXN2IjpmYWxzZSwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6NDQzLCJjaCI6IjNnWE9IZTVlY3VpQzlxK3NiTzdobExva1FiQT0iLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy82YWlrdHFmM2xhZW9vaGY/ZGw9MSIsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIl19";
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