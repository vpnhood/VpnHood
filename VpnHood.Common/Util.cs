using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Common
{
    public static class Util
    {
        public static async Task<IPAddress> GetLocalIpAddress()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            await socket.ConnectAsync("8.8.8.8", 0);
            var endPoint = (IPEndPoint) socket.LocalEndPoint;
            return endPoint.Address;
        }

        public static async Task<IPAddress?> GetPublicIpAddress()
        {
            try
            {
                using HttpClient httpClient = new();
                var json = await httpClient.GetStringAsync("https://api.ipify.org?format=json");
                var document = JsonDocument.Parse(json);
                var ip = document.RootElement.GetProperty("ip").GetString();
                return IPAddress.Parse(ip);
            }
            catch
            {
                return null;
            }
        }

        public static bool IsConnectionRefusedException(Exception ex)
        {
            return
                ex is SocketException socketException &&
                socketException.SocketErrorCode == SocketError.ConnectionRefused ||
                ex.InnerException is SocketException socketException2 &&
                socketException2.SocketErrorCode == SocketError.ConnectionRefused;
        }

        public static bool IsSocketClosedException(Exception ex)
        {
            return ex is ObjectDisposedException || ex is IOException || ex is SocketException;
        }

        public static IPEndPoint GetFreeEndPoint(IPAddress ipAddress, int defaultPort = 0)
        {
            try
            {
                // check recommended port
                var listener = new TcpListener(ipAddress, defaultPort);
                listener.Start();
                var port = ((IPEndPoint) listener.LocalEndpoint).Port;
                listener.Stop();
                return new IPEndPoint(ipAddress, port);
            }
            catch when (defaultPort != 0)
            {
                // try any port
                var listener = new TcpListener(ipAddress, 0);
                listener.Start();
                var port = ((IPEndPoint) listener.LocalEndpoint).Port;
                listener.Stop();
                return new IPEndPoint(ipAddress, port);
            }
        }

        public static void DirectoryCopy(string sourcePath, string destinationPath, bool recursive)
        {
            // Get the subdirectories for the specified directory.
            var dir = new DirectoryInfo(sourcePath);

            if (!dir.Exists)
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourcePath);

            var dirs = dir.GetDirectories();

            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destinationPath);

            // Get the files in the directory and copy them to the new location.
            var files = dir.GetFiles();
            foreach (var file in files)
            {
                var tempPath = Path.Combine(destinationPath, file.Name);
                file.CopyTo(tempPath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (recursive)
                foreach (var subdir in dirs)
                {
                    var tempPath = Path.Combine(destinationPath, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, recursive);
                }
        }

        public static Task TcpClient_ConnectAsync(TcpClient tcpClient, IPEndPoint ipEndPoint, int timeout,
            CancellationToken cancellationToken)
        {
            return TcpClient_ConnectAsync(tcpClient, ipEndPoint.Address, ipEndPoint.Port, timeout, cancellationToken);
        }

        public static async Task TcpClient_ConnectAsync(TcpClient tcpClient, IPAddress address, int port, int timeout,
            CancellationToken cancellationToken)
        {
            if (tcpClient == null) throw new ArgumentNullException(nameof(tcpClient));
            if (timeout == 0) timeout = -1;

            await using var _ = cancellationToken.Register(() => tcpClient.Close());
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var connectTask = tcpClient.ConnectAsync(address, port);
                var timeoutTask = Task.Delay(timeout);
                await Task.WhenAny(connectTask, timeoutTask);

                if (!connectTask.IsCompleted)
                    throw new TimeoutException();

                if (connectTask.IsFaulted)
                    throw connectTask.Exception.InnerException;
            }
            catch
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
        }

        public static bool IsNullOrEmpty<T>([NotNullWhen(false)] T[]? array)
        {
            return array == null || array.Length == 0;
        }


        public static void TcpClient_SetKeepAlive(TcpClient tcpClient, bool value)
        {
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, value);
        }

        public static IEnumerable<string> ParseArguments(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                yield break;

            var sb = new StringBuilder();
            var inQuote = false;
            foreach (var c in commandLine)
            {
                if (c == '"' && !inQuote)
                {
                    inQuote = true;
                    continue;
                }

                if (c != '"' && !(char.IsWhiteSpace(c) && !inQuote))
                {
                    sb.Append(c);
                    continue;
                }

                if (sb.Length > 0)
                {
                    var result = sb.ToString();
                    sb.Clear();
                    inQuote = false;
                    yield return result;
                }
            }

            if (sb.Length > 0)
                yield return sb.ToString();
        }

        public static byte[] GenerateSessionKey()
        {
            using var aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();
            return aes.Key;
        }

        public static T JsonDeserialize<T>(string json, JsonSerializerOptions? options = null)
        {
            return JsonSerializer.Deserialize<T>(json, options) ??
                   throw new InvalidDataException($"{typeof(T)} could not be deserialized!");
        }

        public static byte[] EncryptClientId(Guid clientId, byte[] key)
        {
            // Validate request by shared secret
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Key = key;
            aes.IV = new byte[key.Length];
            aes.Padding = PaddingMode.None;

            using var cryptor = aes.CreateEncryptor();
            return cryptor.TransformFinalBlock(clientId.ToByteArray(), 0, clientId.ToByteArray().Length);
        }
    }
}