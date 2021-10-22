using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
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
        public static bool IsConnectionRefusedException(Exception ex)
        {
            return
                ex is SocketException {SocketErrorCode: SocketError.ConnectionRefused} ||
                ex.InnerException is SocketException {SocketErrorCode: SocketError.ConnectionRefused};
        }

        public static bool IsSocketClosedException(Exception ex)
        {
            return ex is ObjectDisposedException or IOException or SocketException;
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
                foreach (var item in dirs)
                {
                    var tempPath = Path.Combine(destinationPath, item.Name);
                    DirectoryCopy(item.FullName, tempPath, recursive);
                }
        }

        public static async Task<T> RunTask<T>(Task<T> task, TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            await RunTask((Task)task, timeout, cancellationToken);
            return await task;
        }

        public static async Task RunTask(Task task, TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            if (timeout == TimeSpan.Zero) 
                timeout = TimeSpan.FromMilliseconds(-1);
            
            var timeoutTask = Task.Delay(timeout, cancellationToken);
            await Task.WhenAny(task, timeoutTask);

            cancellationToken.ThrowIfCancellationRequested();
            if (timeoutTask.IsCompleted)
                throw new TimeoutException();

            await task;
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

        public static string GetStringMd5(string value)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }
}