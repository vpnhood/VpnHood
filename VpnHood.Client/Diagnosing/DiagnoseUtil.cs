using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Logging;

namespace VpnHood.Client.Diagnosing
{
    public class DiagnoseUtil
    {
        public static Task<Exception?> CheckHttps(Uri[] uris, int timeout)
        {
            var tasks = uris.Select(x => CheckHttps(x, timeout));
            return WhenAnySuccess(tasks.ToArray());
        }

        public static Task<Exception?> CheckUdp(IPEndPoint[] nsIpEndPoints, int timeout)
        {
            var tasks = nsIpEndPoints.Select(x => CheckUdp(x, timeout));
            return WhenAnySuccess(tasks.ToArray());
        }

        public static Task<Exception?> CheckPing(IPAddress[] ipAddresses, int timeout, int pingTtl = 128)
        {
            var tasks = ipAddresses.Select(x => CheckPing(x, timeout, pingTtl));
            return WhenAnySuccess(tasks.ToArray());
        }

        private static async Task<Exception?> WhenAnySuccess(Task<Exception?>[] tasks)
        {
            Exception? exception = null;
            while (tasks.Length > 0)
            {
                var task = await Task.WhenAny(tasks);
                exception = task.Result;
                if (task.Result == null)
                    return null; //at least one task is success
                tasks = tasks.Where(x => x != task).ToArray();
            }

            return exception;
        }

        public static async Task<Exception?> CheckHttps(Uri uri, int timeout)
        {
            try
            {
                VhLogger.Instance.LogInformation($"HttpTest: Started, Uri: {uri}, Timeout: {timeout}...");

                using var httpClient = new HttpClient {Timeout = TimeSpan.FromMilliseconds(timeout)};
                var result = await httpClient.GetStringAsync(uri);
                if (result.Length < 100)
                    throw new Exception("The http response data length is not expected!");

                VhLogger.Instance.LogInformation($"HttpTest: Succeeded, Started, Uri: {uri}.");
                return null;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogInformation($"HttpTest: Failed, Uri: {uri}. Message: {ex.Message}");
                return ex;
            }
        }

        public static async Task<Exception?> CheckUdp(IPEndPoint nsIpEndPoint, int timeout)
        {
            using var udpClient = new UdpClient();
            var dnsName = "www.google.com";
            try
            {
                VhLogger.Instance.LogInformation(
                    $"UdpTest: Started, DnsName: {dnsName}, NsServer: {nsIpEndPoint}, Timeout: {timeout}...");

                var res = await GetHostEntry(dnsName, nsIpEndPoint, udpClient, timeout);
                if (res.AddressList.Length == 0)
                    throw new Exception("Could not find any host!");

                VhLogger.Instance.LogInformation($"UdpTest: Succeeded. DnsName: {dnsName}, NsServer: {nsIpEndPoint}.");
                return null;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(
                    $"UdpTest: Failed! DnsName: {dnsName}, NsServer: {nsIpEndPoint}, Message: {ex.Message}.");
                return ex;
            }
        }

        public static async Task<Exception?> CheckPing(IPAddress ipAddress, int timeout, int pingTtl = 128)
        {
            try
            {
                using var ping = new Ping();
                var pingOptions = new PingOptions {Ttl = pingTtl};
                VhLogger.Instance.LogInformation(
                    $"PingTest: Started, RemoteAddress: {ipAddress}, Timeout: {timeout}...");

                var buf = new byte[40];
                for (var i = 0; i < buf.Length; i++)
                    buf[i] = 5;

                var pingReply = await ping.SendPingAsync(ipAddress, timeout, buf, pingOptions);
                if (pingReply.Status != IPStatus.Success)
                    throw new Exception($"Status: {pingReply.Status}");

                VhLogger.Instance.LogInformation($"PingTest: Succeeded. RemoteAddress: {ipAddress}.");
                return null;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"PingTest: Failed! RemoteAddress: {ipAddress}, Message: {ex.Message}.");
                return ex;
            }
        }

        public static async Task<IPHostEntry> GetHostEntry(string host, IPEndPoint dnsEndPoint,
            UdpClient? udpClient = null, int timeout = 5000)
        {
            // prepare  udpClient
            using var udpClientTemp = new UdpClient();
            udpClient ??= udpClientTemp;

            await using var ms = new MemoryStream();
            var rnd = new Random();
            //About the dns message:http://www.ietf.org/rfc/rfc1035.txt

            //Write message header.
            ms.Write(new byte[]
            {
                (byte) rnd.Next(0, 0xFF), (byte) rnd.Next(0, 0xFF),
                0x01,
                0x00,
                0x00, 0x01,
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00
            }, 0, 12);

            //Write the host to query.
            foreach (var block in host.Split('.'))
            {
                var data = Encoding.UTF8.GetBytes(block);
                ms.WriteByte((byte) data.Length);
                ms.Write(data, 0, data.Length);
            }

            ms.WriteByte(0); //The end of query, must be 0 (null string)

            //Query type:A
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);

            //Query class:IN
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);

            //send to dns server
            var buffer = ms.ToArray();
            udpClient.Client.SendTimeout = timeout;
            udpClient.Client.ReceiveTimeout = timeout;
            await udpClient.SendAsync(buffer, buffer.Length, dnsEndPoint);
            var receiveTask = await Util.RunTask(udpClient.ReceiveAsync(), timeout); 
            buffer = receiveTask.Buffer;

            //The response message has the same header and question structure, so we move index to the answer part directly.
            var index = (int) ms.Length;

            //Parse response records.
            void SkipName()
            {
                while (index < buffer.Length)
                {
                    int length = buffer[index++];
                    if (length == 0)
                        return;
                    if (length > 191) return;
                    index += length;
                }
            }

            var addresses = new List<IPAddress>();
            while (index < buffer.Length)
            {
                SkipName(); //Seems the name of record is useless in this case, so we just need to get the next index after name.
                var type = buffer[index += 2];
                index += 7; //Skip class and ttl

                var length = (buffer[index++] << 8) | buffer[index++]; //Get record data length

                if (type == 0x01) //A record
                    if (length == 4) //Parse record data to ip v4, this is what we need.
                        addresses.Add(new IPAddress(new[]
                            {buffer[index], buffer[index + 1], buffer[index + 2], buffer[index + 3]}));
                index += length;
            }

            return new IPHostEntry {AddressList = addresses.ToArray()};
        }
    }
}