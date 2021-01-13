using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Logging;

namespace VpnHood.Client.Diagnosing
{
    public class Diagnoser
    {
        //public IPAddress[] TestPingIpAddresses { get; set; } = new IPAddress[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1") };
        public IPAddress[] TestPingIpAddresses { get; set; } = new IPAddress[] { IPAddress.Parse("8.8.8.8") }; //todo: remove and unmark up
        public IPEndPoint[] TestNsIpEndPoints { get; set; } = new IPEndPoint[] { new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53), new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53) };
        public Uri[] TestHttpUris { get; set; } = new Uri[] { new Uri("https://www.google.com"), new Uri("https://www.quad9.net/") };
        public int PingTtl { get; set; } = 128;
        public int HttpTimeout { get; set; } = 10 * 1000;
        public int NsTimeout { get; set; } = 15 * 1000; //todo: must be 10 * 1000
        public bool IsWorking { get; private set; }

        public async Task Connect(VpnHoodClient vpnHoodClient)
        {
            try
            {
                await vpnHoodClient.Connect();
            }
            catch
            {
                VhLogger.Current.LogTrace($"Checking the Internet conenction...");
                IsWorking = true;
                if (!await NetworkCheck())
                    throw new NoInternetException();
                throw;
            }
            finally
            {
                IsWorking = false;
            }

        }

        public async Task Diagnose(VpnHoodClient vpnHoodClient)
        {
            try
            {
                VhLogger.Current.LogTrace($"Checking the Internet conenction...");
                IsWorking = true;
                //if (!await NetworkCheck()) //todo: unmark
                  //  throw new NoInternetException();

                // ping server
                VhLogger.Current.LogTrace($"Checking the VpnServer ping...");
                //todo: unmark
                //await DiagnoseUtil.CheckPing(new IPAddress[] { vpnHoodClient.ServerEndPoint.Address }, NsTimeout);

                // VpnConnect
                IsWorking = false;
                await vpnHoodClient.Connect();


                VhLogger.Current.LogTrace($"Checking the Vpn Connection...");
                IsWorking = true;
                if (!await NetworkCheck())
                    throw new NoStableVpnException();
            }
            finally
            {
                IsWorking = false;
            }
        }

        private async Task<bool> NetworkCheck()
        {
            var taskPing = DiagnoseUtil.CheckPing(TestPingIpAddresses, NsTimeout, PingTtl);
            var taskUdp = DiagnoseUtil.CheckUdp(TestNsIpEndPoints, NsTimeout);
            var taskHttps = DiagnoseUtil.CheckHttps(TestHttpUris, HttpTimeout);

            await Task.WhenAll(taskPing, taskUdp, taskHttps);
            var hasInternet =
                taskPing.Result == null &&
                taskUdp.Result == null &&
                taskHttps.Result == null;

            return hasInternet;
        }
    }
}
