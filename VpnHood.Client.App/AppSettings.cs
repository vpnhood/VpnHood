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
        public string TestServerAccessKey => "eyJuYW1lIjoiUHVibGljIFNlcnZlciIsInYiOjEsInNpZCI6MiwidGlkIjoiNjI5MmQwY2ItNTViYy00ZDRlLTk2ZDktMDRiNTU5OTRjZjg3Iiwic2VjIjoiMXcrMHBBRDVhNzQrQUlOZ2hIYzZlZz09IiwiZG5zIjoiYXp0cm8uc2lnbWFsaWIub3JnIiwicGtoIjoiUjBiaEsyNyt4dEtBeHBzaGFKbGk4dz09IiwiZXAiOlsiNTEuODEuODQuMTQyOjQ0MyJdLCJwYiI6dHJ1ZX0=";

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
