using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace VpnHood.Common.Utils;

public static class VhUtil
{
    public static bool IsConnectionRefusedException(Exception ex)
    {
        return
            ex is SocketException { SocketErrorCode: SocketError.ConnectionRefused } ||
            ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused };
    }

    public static bool IsSocketClosedException(Exception ex)
    {
        return ex is ObjectDisposedException or IOException or SocketException;
    }

    public static IPEndPoint GetFreeTcpEndPoint(IPAddress ipAddress, int defaultPort = 0)
    {
        try
        {
            // check recommended port
            var listener = new TcpListener(ipAddress, defaultPort);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return new IPEndPoint(ipAddress, port);
        }
        catch when (defaultPort != 0)
        {
            return GetFreeTcpEndPoint(ipAddress);
        }
    }

    public static IPEndPoint GetFreeUdpEndPoint(IPAddress ipAddress, int defaultPort = 0)
    {
        try
        {
            // check recommended port
            using var udpClient = new UdpClient(new IPEndPoint(ipAddress, defaultPort));
            var port = ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
            return new IPEndPoint(ipAddress, port);
        }
        catch when (defaultPort != 0)
        {
            return GetFreeUdpEndPoint(ipAddress);
        }
    }

    private static string FixBase64String(string base64)
    {
        base64 = base64.Trim();
        var padding = base64.Length % 4;
        if (padding > 0)
            base64 = base64.PadRight(base64.Length + (4 - padding), '=');
        return base64;
    }

    public static byte[] ConvertFromBase64AndFixPadding(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return Convert.FromBase64String(FixBase64String(base64));
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

    public static T[] SafeToArray<T>(object lockObject, IEnumerable<T> collection)
    {
        lock (lockObject)
            return collection.ToArray();
    }

    public static async Task<T> RunTask<T>(Task<T> task, TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        await RunTask((Task)task, timeout, cancellationToken).VhConfigureAwait();
        return await task.VhConfigureAwait();
    }

    public static async Task RunTask(Task task, TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        if (timeout == TimeSpan.Zero)
            timeout = Timeout.InfiniteTimeSpan;

        var timeoutTask = Task.Delay(timeout, cancellationToken);
        await Task.WhenAny(task, timeoutTask).VhConfigureAwait();

        cancellationToken.ThrowIfCancellationRequested();
        if (timeoutTask.IsCompleted)
            throw new TimeoutException();

        await task.VhConfigureAwait();
    }

    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] IEnumerable<T>? array)
    {
        return array == null || !array.Any();
    }


    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] T[]? array)
    {
        return array == null || array.Length == 0;
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

    public static byte[] GenerateKey()
    {
        return GenerateKey(128);
    }

    public static byte[] GenerateKey(int keySizeInBit)
    {
        using var aes = Aes.Create();
        aes.KeySize = keySizeInBit;
        aes.GenerateKey();
        return aes.Key;
    }

    public static T JsonDeserialize<T>(string json, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json, options) ??
               throw new InvalidDataException($"{typeof(T)} could not be deserialized!");
    }

    public static T? JsonDeserializeFile<T>(string filePath, JsonSerializerOptions? options = null, ILogger? logger = null)
    {
        try
        {
            if (!File.Exists(filePath))
                return default;

            var json = File.ReadAllText(filePath);
            var obj = JsonDeserialize<T>(json, options);
            return obj;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Could not read json file. FilePath: {FilePath}", filePath);
            return default;
        }
    }

    public static bool JsonEquals(object? obj1, object? obj2)
    {
        if (obj1 == null && obj2 == null) return true;
        if (obj1 == null || obj2 == null) return false;
        return JsonSerializer.Serialize(obj1) == JsonSerializer.Serialize(obj2);
    }

    public static T JsonClone<T>(T obj, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(obj, options);
        return JsonDeserialize<T>(json, options);
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

    public static string RedactHostName(string hostName)
    {
        return hostName.Length <= 8
            ? "***" + hostName[^4..]
            : hostName[..2] + "***" + hostName[^4..];
    }


    public static string RedactEndPoint(IPEndPoint ipEndPoint)
    {
        return RedactIpAddress(ipEndPoint.Address) + ":" + ipEndPoint.Port;
    }

    public static string RedactIpAddress(IPAddress ipAddress)
    {
        var addressBytes = ipAddress.GetAddressBytes();

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork &&
            !ipAddress.Equals(IPAddress.Any) &&
            !ipAddress.Equals(IPAddress.Loopback))
            return $"{addressBytes[0]}.*.*.{addressBytes[3]}";

        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6 &&
            !ipAddress.Equals(IPAddress.IPv6Any) &&
            !ipAddress.Equals(IPAddress.IPv6Loopback))
            return $"{addressBytes[0]:x2}{addressBytes[1]:x2}:***:{addressBytes[14]:x2}{addressBytes[15]:x2}";

        return ipAddress.ToString();
    }

    public static string FormatBytes(long size, bool use1024 = true)
    {
        var kb = use1024 ? (long)1024 : 1000;
        var mb = kb * kb;
        var gb = mb * kb;
        var tb = gb * kb;

        if (size >= tb) // Terabyte
            return (size / tb).ToString("0.## ") + "TB";

        if (size >= gb) // Gigabyte
            return (size / gb).ToString("0.# ") + "GB";

        if (size >= mb) // Megabyte
            return (size / mb).ToString("0 ") + "MB";

        if (size >= kb) // Kilobyte
            return (size / kb).ToString("0 ") + "KB";

        if (size > 0) // Kilobyte
            return size.ToString("0 ") + "B";

        // Byte
        return size.ToString("0");
    }

    public static string FormatBits(long bytes)
    {
        bytes *= 8; //convertTo bit
        // ReSharper disable PossibleLossOfFraction

        // Get absolute value
        if (bytes >= 0x40000000) // Gigabyte
            return ((double)(bytes / 0x40000000)).ToString("0.# ") + "Gbps";

        if (bytes >= 0x100000) // Megabyte
            return ((double)(bytes / 0x100000)).ToString("0 ") + "Mbps";

        if (bytes >= 1024) // Kilobyte
            return ((double)(bytes / 1024)).ToString("0 ") + "Kbps";

        if (bytes > 0) // Kilobyte
            return ((double)bytes).ToString("0 ") + "bps";
        // ReSharper restore PossibleLossOfFraction

        // Byte
        return bytes.ToString("0");
    }

    public static bool IsInfinite(TimeSpan timeSpan)
    {
        return timeSpan == TimeSpan.MaxValue || timeSpan == Timeout.InfiniteTimeSpan;
    }

    public static ValueTask DisposeAsync(IAsyncDisposable? channel)
    {
        return channel?.DisposeAsync() ?? default;
    }

    public static void ConfigTcpClient(TcpClient tcpClient, int? sendBufferSize, int? receiveBufferSize, bool? reuseAddress = null)
    {
        tcpClient.NoDelay = true;
        if (sendBufferSize != null) tcpClient.SendBufferSize = sendBufferSize.Value;
        if (receiveBufferSize != null) tcpClient.ReceiveBufferSize = receiveBufferSize.Value;
        if (reuseAddress != null) tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, reuseAddress.Value);
    }

    public static bool IsTcpClientHealthy(TcpClient tcpClient)
    {
        try
        {
            // Check if the TcpClient is connected
            if (!tcpClient.Connected)
                return false;

            // Check if the underlying socket is connected
            var socket = tcpClient.Client;
            var healthy = tcpClient.Connected && socket.Connected && !tcpClient.Client.Poll(1, SelectMode.SelectError);

            return healthy;
        }
        catch (Exception)
        {
            // An error occurred while checking the TcpClient
            return false;
        }
    }

    public static string RedactJsonValue(string json, string[] keys)
    {
        foreach (var key in keys)
        {
            // array
            var jsonLength = json.Length;
            var pattern = @"""key""\s*:\s*\[[^\]]*\]".Replace("key", key);
            json = Regex.Replace(json, pattern, $"\"{key}\": [\"***\"]");
            if (jsonLength != json.Length)
                continue;

            // single
            pattern = "(?<=\"key\":)[^,|}|\r]+(?=,|}|\r)".Replace("key", key);
            json = Regex.Replace(json, pattern, " \"***\"");
        }

        return json;
    }

    public static T GetRequiredInstance<T>(T? obj)
    {
        return obj ?? throw new InvalidOperationException($"{typeof(T)} has not been initialized yet.");
    }

    public static DateTime RemoveMilliseconds(DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Kind);
    }

    public static string GetAssemblyMetadata(Assembly assembly, string key, string defaultValue)
    {
        var metadataAttribute = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == key);

        return string.IsNullOrEmpty(metadataAttribute?.Value) ? defaultValue : metadataAttribute.Value;
    }

    public static async Task ParallelForEachAsync<T>(IEnumerable<T> source, Func<T, Task> body, int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        foreach (var t in source)
        {
            cancellationToken.ThrowIfCancellationRequested();

            tasks.Add(body(t));
            if (tasks.Count == maxDegreeOfParallelism)
            {
                await Task.WhenAny(tasks).VhConfigureAwait();
                foreach (var completedTask in tasks.Where(x => x.IsCompleted).ToArray())
                    tasks.Remove(completedTask);
            }
        }
        await Task.WhenAll(tasks).VhConfigureAwait();
    }

    public static bool TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Shuffle<T>(T[] array)
    {
        var rng = new Random();
        var n = array.Length;
        for (var i = n - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}