using System;
using System.IO;
using System.Text;
using System.Text.Json;
using VpnHood.Common;

namespace VpnHood.Client.App
{
    public class AppSettings
    {

        public string SettingsFilePath { get; private set; }
        public AppUserSettings UserSettings { get; set; } = new AppUserSettings();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid? TestServerTokenIdAutoAdded { get; set; }
        public Guid? TestServerTokenId => Token.FromAccessKey(TestServerAccessKey).TokenId;
        public string TestServerAccessKey => "vh://eyJuYW1lIjoiVnBuSG9vZCBQdWJsaWMgU2VydmVyIiwidiI6MSwic2lkIjo0LCJ0aWQiOiIyYzAyYWM0MS0wNDBmLTQ1NzYtYjhjYy1kY2ZlNWI5MTcwYjciLCJzZWMiOiJ3eFZ5Vm9uOTEwT2JhREM1b0F6ekJRPT0iLCJkbnMiOiJhenRyby5zaWdtYWxpYi5vcmciLCJpc3ZkbnMiOmZhbHNlLCJwa2giOiJSMGJoSzI3K3h0S0F4cHNoYUpsaTh3PT0iLCJlcCI6WyI1MS44MS44NC4xNDI6NDQzIl0sInBiIjp0cnVlLCJ1cmwiOiJodHRwczovL3d3dy5kcm9wYm94LmNvbS9zL2htaGNoNmIwOXg3ZnV4My9wdWJsaWMuYWNjZXNza2V5P2RsPTEifQ==";

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
                ret.Save();
                return ret;
            }
        }
    }
}
