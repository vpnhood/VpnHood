using System.IO;

namespace VpnHood.AccessServer
{
    public class VersionChecker
    {
        class LastVersion
        {
            public string Version { get; set; }
            public string LaunchPath { get; set; }
        }

        public bool CheckNewVersion()
        {
            var jsonPath = Path.Combine(Path.GetDirectoryName(Directory.GetCurrentDirectory()), "lastver.json");
            if (File.Exists(jsonPath))
            {
                Directory.SetCurrentDirectory(Path.GetDirectoryName(jsonPath));

                // read json
                var lastVer = "ss";
                if (lastVer != null)
                {
                    //stop

                    // run  last version
                }
            }
            return false;
        }
    }
}
