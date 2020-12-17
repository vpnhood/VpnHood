using System;
using System.IO;
using System.Threading.Tasks;

namespace VpnHood.Client.Device.WinDivert
{
    public class WinDivertDevice : IDevice
    {
#pragma warning disable 0067
        public event EventHandler OnStartAsService;
#pragma warning restore 0067

        private void SetWinDivertDllFolder()
        {
            var dllFolderName = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            var assemblyFolder = Path.GetDirectoryName(typeof(WinDivertDevice).Assembly.Location);
            var dllFolder = Path.Combine(assemblyFolder, dllFolderName);

            string path = Environment.GetEnvironmentVariable("PATH");
            if (path.IndexOf(dllFolder + ";") == -1)
                Environment.SetEnvironmentVariable("PATH", dllFolder + ";" + path);
        }

        public Task<IPacketCapture> CreatePacketCapture()
        {
            SetWinDivertDllFolder();

            var res = (IPacketCapture)new WinDivertPacketCapture();
            return Task.FromResult(res);
        }
    }
}
