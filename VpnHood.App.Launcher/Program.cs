using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using VpnHood.Common;

namespace VpnHood.App.Launcher
{
    class Program
    {
        static int Main(string[] args)
        {
            var moduleFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var jsonFilePath = Path.Combine(moduleFolder, "publish.json");

            // read run.json
            var json = File.ReadAllText(jsonFilePath);
            var launcherInfo = JsonSerializer.Deserialize<PublishInfo>(json);
            var launchPath = Path.Combine(moduleFolder, launcherInfo.LaunchPath);

            var argsList = args.ToList();
            argsList.Insert(0, launchPath);

            // create processStartInfo
            ProcessStartInfo processStartInfo = new() { FileName = "dotnet" };
            processStartInfo.ArgumentList.Add(launchPath);
            if (args != null)
            {
                foreach (var arg in args)
                    processStartInfo.ArgumentList.Add(arg);
            }

            var process = Process.Start(processStartInfo);
            // wait for any error or early exit to share the console properly
            // exit this process for later update
            process.WaitForExit(10000); 
            return process.HasExited ? process.ExitCode : 0;
        }
    }
}
