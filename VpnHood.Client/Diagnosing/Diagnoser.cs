using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Client.Exceptions;
using VpnHood.Logging;

namespace VpnHood.Client.Diagnosing
{
    public class Diagnoser
    {
        public IPAddress[] TestPingIpAddresses { get; set; } = new IPAddress[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1") };
        public IPEndPoint[] TestNsIpEndPoints { get; set; } = new IPEndPoint[] { new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53), new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53) };
        public Uri[] TestHttpUris { get; set; } = new Uri[] { new Uri("https://www.google.com"), new Uri("https://www.quad9.net/") };
        public int PingTtl { get; set; } = 128;
        public int HttpTimeout { get; set; } = 10 * 1000;
        public int NsTimeout { get; set; } = 10 * 1000;
        public bool IsWorking { get; private set; }

        public async Task Connect(VpnHoodConnect clientConnect)
        {
            try
            {
                await clientConnect.Connect();
            }
            catch
            {
                VhLogger.Instance.LogTrace($"Checking the Internet conenction...");
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

        public async Task Diagnose(VpnHoodConnect clientConnect)
        {
            try
            {
                VhLogger.Instance.LogTrace($"Checking the Internet conenction...");
                IsWorking = true;
                if (!await NetworkCheck(false))
                    throw new NoInternetException();

                // ping server
                VhLogger.Instance.LogTrace($"Checking the VpnServer ping...");
                await DiagnoseUtil.CheckPing(new IPAddress[] { clientConnect.Client.ServerTcpEndPoint.Address }, NsTimeout);

                // VpnConnect
                IsWorking = false;
                await clientConnect.Connect();

                VhLogger.Instance.LogTrace($"Checking the Vpn Connection...");
                IsWorking = true;
                if (!await NetworkCheck())
                    throw new NoStableVpnException();
            }
            finally
            {
                IsWorking = false;
            }
        }

        private async Task<bool> NetworkCheck(bool checkPing = true)
        {
            var taskPing = checkPing ? DiagnoseUtil.CheckPing(TestPingIpAddresses, NsTimeout, PingTtl) : Task.FromResult((Exception)null);
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
