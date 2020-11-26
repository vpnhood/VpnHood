using PacketDotNet;
using PacketDotNet.Utils;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace VpnHood
{
    public static class Util
    {
        public const int SocketStackSize_Datagram = 65536;
        public const int SocketStackSize_Stream = 65536 * 2;
        public const int TlsHandshakeLength = 5000;

        public static void UpdateICMPChecksum(IcmpV4Packet icmpPacket)
        {
            icmpPacket.Checksum = 0;
            var buf = icmpPacket.Bytes;
            icmpPacket.Checksum = (ushort)ChecksumUtils.OnesComplementSum(buf, 0, buf.Length);
        }
        public static ulong RandomLong()
        {
            Random random = new Random();
            byte[] bytes = new byte[8];
            random.NextBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        public static byte[] Stream_ReadWaitForFill(Stream stream, int count)
        {
            var buffer = new byte[count];
            if (!Stream_ReadWaitForFill(stream, buffer, 0, buffer.Length))
                return null;
            return buffer;
        }

        public static bool Stream_ReadWaitForFill(Stream stream, byte[] buffer, int startIndex, int count)
        {
            var totalReaded = 0;
            while (totalReaded != count)
            {
                var read = stream.Read(buffer, startIndex + totalReaded, count - totalReaded);
                totalReaded += read;
                if (read == 0)
                    return false;
            }

            return true;
        }

        public static IPPacket Stream_ReadIpPacket(Stream stream, byte[] buffer)
        {
            if (!Stream_ReadWaitForFill(stream, buffer, 0, 4))
                return null;

            var packetLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, 2));
            if (packetLength < IPv4Packet.HeaderMinimumLength)
                throw new Exception($"A packet with invalid length has been received! Length: {packetLength}");

            if (!Stream_ReadWaitForFill(stream, buffer, 4, packetLength - 4))
                return null;

            var segment = new ByteArraySegment(buffer, 0, packetLength);
            var ipPacket = new IPv4Packet(segment);
            return ipPacket;
        }

        public static void Stream_WriteJson(Stream stream, object obj)
        {
            var jsonBuffer = JsonSerializer.SerializeToUtf8Bytes(obj);
            var newBuffer = new byte[4 + jsonBuffer.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(jsonBuffer.Length), 0, newBuffer, 0, 4);
            Buffer.BlockCopy(jsonBuffer, 0, newBuffer, 4, jsonBuffer.Length);
            stream.Write(newBuffer);
        }

        public static T Stream_ReadJson<T>(Stream stream, int maxLength = 0xFFFF)
        {
            // read length
            var buffer = Stream_ReadWaitForFill(stream, 4);
            if (buffer == null)
                throw new Exception($"Could not read {typeof(T).Name}");

            // check json size
            var jsonSize = BitConverter.ToInt32(buffer);
            if (jsonSize == 0)
                throw new Exception("json length is zero!");
            if (jsonSize > maxLength)
                throw new Exception($"json length is too big! It should be less than {maxLength} bytes but it was {jsonSize} bytes");

            // read json body...
            buffer = Stream_ReadWaitForFill(stream, jsonSize);
            if (buffer == null)
                throw new Exception("Could not read Message Length!"); 

            // serialize the request
            var json = Encoding.UTF8.GetString(buffer);
            return JsonSerializer.Deserialize<T>(json);
        }

        public static  bool TryParseIpEndPoint(string value, out IPEndPoint ipEndPoint)
        {
            ipEndPoint = null;
            var addr = value.Split(':');
            if (addr.Length != 2) return false;
            if (!IPAddress.TryParse(addr[0], out IPAddress ipAddress)) return false;
            if (!int.TryParse(addr[1], out int port)) return false;
            ipEndPoint = new IPEndPoint(ipAddress, port);
            return true;
        }

        public static IPEndPoint ParseIpEndPoint(string value)
        {
            if (!TryParseIpEndPoint(value, out IPEndPoint ipEndPoint))
                throw new ArgumentException($"Could not parse {value} to an IpEndPoint");
            return ipEndPoint;
        }

        public static bool IsSocketClosedException(Exception ex)
        {
            return ex is ObjectDisposedException || ex is IOException || ex is SocketException;
        }

        public static IPAddress GetLocalIpAddress()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            socket.Connect("8.8.8.8", 0);
            var endPoint = (IPEndPoint)socket.LocalEndPoint;
            return endPoint.Address;
        }
    }
}
