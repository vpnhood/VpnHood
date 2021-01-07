using System;
using System.Net;
using System.Threading.Tasks;

namespace VpnHood.Client.Diagnosing
{
    public class Diagnoser
    {
        public DiagnoseTest[] Tests { get; private set; }
        public bool IsWorking { get; set; }
        public IPAddress[] TestPingIpAddresses { get; set; } = new IPAddress[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1") };
        public IPEndPoint[] TestNsIpEndPoints { get; set; } = new IPEndPoint[] { new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53), new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53) };
        public Uri[] TestHttpUrls { get; set; } = new Uri[] { new Uri("https://www.google.com"), new Uri("https://www.quad9.net/") };

        public void Reset()
        {
            Tests = new DiagnoseTest[]
            {
                new DiagnoseTest { TestType = DiagnoseTestType.InternetPing },
                new DiagnoseTest { TestType = DiagnoseTestType.InternetUdp },
                new DiagnoseTest { TestType = DiagnoseTestType.InternetHttp },
                new DiagnoseTest { TestType = DiagnoseTestType.VpnServerPing },
                new DiagnoseTest { TestType = DiagnoseTestType.VpnConnect },
                new DiagnoseTest { TestType = DiagnoseTestType.VpnInternetPing },
                new DiagnoseTest { TestType = DiagnoseTestType.VpnInternetUdp },
                new DiagnoseTest { TestType = DiagnoseTestType.VpnInternetHttp }
            };
        }

        public async Task Start(VpnHoodClient vpnHoodClient)
        {
            Reset();

            try
            {
                IsWorking = true;
                await StartInternal(vpnHoodClient);
            }
            finally
            {
                IsWorking = false;
            }
        }

        public async Task StartInternal(VpnHoodClient vpnHoodClient)
        {
            int timeout = 15000;
            int testType;

            // InternetPing
            testType = (int)DiagnoseTestType.InternetPing;
            Tests[testType].State = DiagnoseState.Started;
            Tests[testType].ErrorMessage = await DiagnoseUtil.CheckPing(TestPingIpAddresses, timeout);
            Tests[testType].State = string.IsNullOrEmpty(Tests[testType].ErrorMessage) ? DiagnoseState.Succeeded : DiagnoseState.Failed;

            // InternetUdp
            testType = (int)DiagnoseTestType.InternetUdp;
            Tests[testType].State = DiagnoseState.Started;
            Tests[testType].ErrorMessage = await DiagnoseUtil.CheckUdp(TestNsIpEndPoints, timeout);
            Tests[testType].State = string.IsNullOrEmpty(Tests[testType].ErrorMessage) ? DiagnoseState.Succeeded : DiagnoseState.Failed;

            // InternetHttp
            testType = (int)DiagnoseTestType.InternetHttp;
            Tests[testType].State = DiagnoseState.Started;
            Tests[testType].ErrorMessage = await DiagnoseUtil.CheckHttps(TestHttpUrls, timeout);
            Tests[testType].State = string.IsNullOrEmpty(Tests[testType].ErrorMessage) ? DiagnoseState.Succeeded : DiagnoseState.Failed;

            // VpnServerPing
            testType = (int)DiagnoseTestType.VpnServerPing;
            Tests[testType].State = DiagnoseState.Started;
            Tests[testType].ErrorMessage = await DiagnoseUtil.CheckPing(new IPAddress[] { vpnHoodClient.ServerEndPoint.Address }, timeout);
            Tests[testType].State = string.IsNullOrEmpty(Tests[testType].ErrorMessage) ? DiagnoseState.Succeeded : DiagnoseState.Failed;

            // VpnConnect
            testType = (int)DiagnoseTestType.VpnConnect;
            Tests[testType].State = DiagnoseState.Started;
            try
            {
                await vpnHoodClient.Connect();
                Tests[testType].State = vpnHoodClient.State == ClientState.Connected ? DiagnoseState.Succeeded : DiagnoseState.Failed;
            }
            catch (Exception ex)
            {
                Tests[testType].ErrorMessage = ex.Message;
            }

            // return if could not connect to vpn server
            if (Tests[testType].State != DiagnoseState.Succeeded)
                return;

            // VpnInternetPing
            testType = (int)DiagnoseTestType.VpnInternetPing;
            Tests[testType].State = DiagnoseState.Started;
            Tests[testType].ErrorMessage = await DiagnoseUtil.CheckPing(TestPingIpAddresses, timeout);
            Tests[testType].State = string.IsNullOrEmpty(Tests[testType].ErrorMessage) ? DiagnoseState.Succeeded : DiagnoseState.Failed;

            // VpnInternetUdp
            testType = (int)DiagnoseTestType.VpnInternetUdp;
            Tests[testType].State = DiagnoseState.Started;
            Tests[testType].ErrorMessage = await DiagnoseUtil.CheckUdp(TestNsIpEndPoints, timeout);
            Tests[testType].State = string.IsNullOrEmpty(Tests[testType].ErrorMessage) ? DiagnoseState.Succeeded : DiagnoseState.Failed;

            // VpnInternetHttp
            testType = (int)DiagnoseTestType.VpnInternetHttp;
            Tests[testType].State = DiagnoseState.Started;
            Tests[testType].ErrorMessage = await DiagnoseUtil.CheckHttps(TestHttpUrls, timeout);
            Tests[testType].State = string.IsNullOrEmpty(Tests[testType].ErrorMessage) ? DiagnoseState.Succeeded : DiagnoseState.Failed;
        }

    }
}
