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
        public string TestServerAccessKey => "vh://eyJuYW1lIjoiUHVibGljIFNlcnZlciIsInYiOjEsInNpZCI6MTEsInRpZCI6IjEwNDczNTljLWExMDctNGU0OS04NDI1LWMwMDRjNDFmZmI4ZiIsInNlYyI6IlRmK1BpUTRaS1oyYW1WcXFPNFpzdGc9PSIsImRucyI6Im1vLmdpd293eXZ5Lm5ldCIsImlzdmRucyI6ZmFsc2UsInBraCI6Ik1Da3lsdTg0N2J5U0Q4bEJZWFczZVE9PSIsImNoIjoiM2dYT0hlNWVjdWlDOXErc2JPN2hsTG9rUWJBPSIsImVwIjpbIjUxLjgxLjgxLjI1MDo0NDMiXSwicGIiOnRydWUsInVybCI6Imh0dHBzOi8vd3d3LmRyb3Bib3guY29tL3MvaG1oY2g2YjA5eDdmdXgzL3B1YmxpYy5hY2Nlc3NrZXk/ZGw9MSJ9";

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
