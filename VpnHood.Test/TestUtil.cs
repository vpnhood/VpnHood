using VpnHood.Loggers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace VpnHood.Test
{
    static class TestUtil
    {
        public static IPEndPoint GetFreeEndPoint()
        {
            var address = IPAddress.Any;
            var l = new TcpListener(address, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return new IPEndPoint(address, port);
        }

        public static IPHostEntry GetHostEntry(string host, IPEndPoint dnsEndPoint, UdpClient udpClient = null)
        {
            if (string.IsNullOrEmpty(host)) return null;

            // prepare  udpClient
            using var udpClientTemp = new UdpClient();
            if (udpClient == null) udpClient = udpClientTemp;

            using var ms = new MemoryStream();
            var rnd = new Random();
            //About the dns message:http://www.ietf.org/rfc/rfc1035.txt

            //Write message header.
            ms.Write(new byte[] {
                    (byte)rnd.Next(0, 0xFF),(byte)rnd.Next(0, 0xFF),
                    0x01,
                    0x00,
                    0x00,0x01,
                    0x00,0x00,
                    0x00,0x00,
                    0x00,0x00
                }, 0, 12);

            //Write the host to query.
            foreach (string block in host.Split('.'))
            {
                byte[] data = Encoding.UTF8.GetBytes(block);
                ms.WriteByte((byte)data.Length);
                ms.Write(data, 0, data.Length);
            }
            ms.WriteByte(0);//The end of query, muest 0(null string)

            //Query type:A
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);

            //Query class:IN
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);

            //send to dns server
            var buffer = ms.ToArray();
            udpClient.Client.SendTimeout = 5000;
            udpClient.Send(buffer, buffer.Length, dnsEndPoint);

            buffer = new byte[0x100];
            var ep = new IPEndPoint(IPAddress.Any, 0);
            udpClient.Client.ReceiveTimeout = 5000;
            buffer = udpClient.Receive(ref ep);

            //The response message has the same header and question structure, so we move index to the answer part directly.
            var index = (int)ms.Length;
            //Parse response records.
            void SkipName()
            {
                while (index < buffer.Length)
                {
                    int length = buffer[index++];
                    if (length == 0)
                    {
                        return;
                    }
                    else if (length > 191)
                    {
                        return;
                    }
                    index += length;
                }
            }

            var addresses = new List<IPAddress>();
            while (index < buffer.Length)
            {
                SkipName();//Seems the name of record is useless in this scense, so we just needs to get the next index after name.
                var type = buffer[index += 2];
                index += 7;//Skip class and ttl

                var length = buffer[index++] << 8 | buffer[index++];//Get record data's length

                if (type == 0x01)//A record
                {
                    if (length == 4)//Parse record data to ip v4, this is what we need.
                    {
                        addresses.Add(new IPAddress(new byte[] { buffer[index], buffer[index + 1], buffer[index + 2], buffer[index + 3] }));
                    }
                }
                index += length;
            }
            return new IPHostEntry { AddressList = addresses.ToArray() };
        }

    }
}
