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
        public string TestServerAccessKey => "vh://eyJuYW1lIjoiUHVibGljIFNlcnZlciIsInYiOjEsInNpZCI6NCwidGlkIjoiMmMwMmFjNDEtMDQwZi00NTc2LWI4Y2MtZGNmZTViOTE3MGI3Iiwic2VjIjoid3hWeVZvbjkxME9iYURDNW9BenpCUT09IiwiZG5zIjoiYXp0cm8uc2lnbWFsaWIub3JnIiwiaXN2ZG5zIjpmYWxzZSwicGtoIjoiUjBiaEsyNyt4dEtBeHBzaGFKbGk4dz09IiwiZXAiOlsiNTEuODEuODQuMTQyOjQ0MyJdLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy9obWhjaDZiMDl4N2Z1eDMvcHVibGljLmFjY2Vzc2tleT9kbD0xIn0=";

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
