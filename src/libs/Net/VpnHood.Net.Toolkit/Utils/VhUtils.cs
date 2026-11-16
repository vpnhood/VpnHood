using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides assorted helpers: sizes and their text, sockets and free ports, tasks with a timeout, keys and hashes, files,
/// and countries.
/// </summary>
public static class VhUtils
{
    /// <summary>The bytes in a megabyte, 2^20.</summary>
    public const long Megabytes = 1L << 20;

    /// <summary>The bytes in a gigabyte, 2^30.</summary>
    public const long Gigabytes = 1L << 30;

    /// <summary>The bytes in a terabyte, 2^40.</summary>
    public const long Terabytes = 1L << 40;

    /// <summary>The bytes in a petabyte, 2^50.</summary>
    public const long Petabytes = 1L << 50;

    /// <summary>
    /// Gets or sets the timeout <see cref="VhUtilsExtensions.WhenNoDebugger"/> gives while a debugger is attached, or
    /// <see langword="null"/> to keep the timeouts as they are. The default is a day.
    /// </summary>
    public static TimeSpan? DebuggerTimeout { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Determines whether an exception, or its inner exception, is a refused connection.</summary>
    /// <param name="ex">The exception.</param>
    /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
    public static bool IsConnectionRefusedException(Exception ex)
    {
        return
            ex is SocketException { SocketErrorCode: SocketError.ConnectionRefused } ||
            ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused };
    }

    /// <summary>
    /// Determines whether an exception is of the kinds a closed socket throws: <see cref="ObjectDisposedException"/>,
    /// <see cref="IOException"/> or <see cref="SocketException"/>, whatever its cause.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
    public static bool IsSocketClosedException(Exception ex)
    {
        return ex is ObjectDisposedException or IOException or SocketException;
    }

    /// <summary>
    /// Finds a TCP port free on an address now: the given one when it is, else one the system picks. Another process may
    /// take it before the caller binds it.
    /// </summary>
    /// <param name="ipAddress">The address.</param>
    /// <param name="defaultPort">The port to try first, or 0 for any.</param>
    /// <returns>The endpoint.</returns>
    /// <exception cref="SocketException">No port can be bound on the address.</exception>
    public static IPEndPoint GetFreeTcpEndPoint(IPAddress ipAddress, int defaultPort = 0)
    {
        try {
            // check recommended port
            var listener = new TcpListener(ipAddress, defaultPort);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return new IPEndPoint(ipAddress, port);
        }
        catch when (defaultPort != 0) {
            return GetFreeTcpEndPoint(ipAddress);
        }
    }

    /// <summary>
    /// Finds a UDP port free on an address now: the given one when it is, else one the system picks. Another process may
    /// take it before the caller binds it.
    /// </summary>
    /// <param name="ipAddress">The address.</param>
    /// <param name="defaultPort">The port to try first, or 0 for any.</param>
    /// <returns>The endpoint.</returns>
    /// <exception cref="SocketException">No port can be bound on the address.</exception>
    /// <exception cref="InvalidOperationException">The socket has no local endpoint.</exception>
    public static IPEndPoint GetFreeUdpEndPoint(IPAddress ipAddress, int defaultPort = 0)
    {
        try {
            // check recommended port
            using var udpClient = new UdpClient(new IPEndPoint(ipAddress, defaultPort));
            var localEndPoint = (IPEndPoint?)udpClient.Client.LocalEndPoint ??
                                throw new InvalidOperationException("Failed to get local endpoint for UDP client.");
            return new IPEndPoint(ipAddress, localEndPoint.Port);
        }
        catch when (defaultPort != 0) {
            return GetFreeUdpEndPoint(ipAddress);
        }
    }

    /// <summary>Creates a completed task source that runs continuations asynchronously.</summary>
    /// <returns>The task source.</returns>
    public static TaskCompletionSource CreateCompletedTcs()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }

    private static string FixBase64String(string base64)
    {
        base64 = base64.Trim();
        var padding = base64.Length % 4;
        if (padding > 0)
            base64 = base64.PadRight(base64.Length + (4 - padding), '=');
        return base64;
    }

    /// <summary>Decodes Base64, adding the padding that is missing.</summary>
    /// <param name="base64">The Base64 text.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="FormatException">The text is not Base64.</exception>
    public static byte[] ConvertFromBase64AndFixPadding(string base64)
    {
        try {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException) {
            return Convert.FromBase64String(FixBase64String(base64));
        }
    }

    /// <summary>Copies a folder's files to another folder, which is made when missing; an existing file is not overwritten.</summary>
    /// <param name="sourcePath">The folder to copy.</param>
    /// <param name="destinationPath">The folder to copy into.</param>
    /// <param name="recursive"><see langword="true"/> to copy the subfolders too.</param>
    /// <exception cref="DirectoryNotFoundException">The source folder does not exist.</exception>
    /// <exception cref="IOException">A file exists already, or cannot be copied.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not read or write a file or folder.</exception>
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
        foreach (var file in files) {
            var tempPath = Path.Combine(destinationPath, file.Name);
            file.CopyTo(tempPath, false);
        }

        // If copying subdirectories, copy them and their contents to new location.
        if (recursive)
            foreach (var item in dirs) {
                var tempPath = Path.Combine(destinationPath, item.Name);
                DirectoryCopy(item.FullName, tempPath, recursive);
            }
    }

    /// <summary>Copies a collection into an array under a lock.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="lockObject">The lock.</param>
    /// <param name="collection">The collection.</param>
    /// <returns>The array.</returns>
    public static T[] SafeToArray<T>(object lockObject, IEnumerable<T> collection)
    {
        lock (lockObject)
            return [.. collection];
    }

    /// <summary>Waits for a task until a token is canceled; the task itself is not canceled.</summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="task">The task.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the task's.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="Exception">The task's own exception.</exception>
    public static Task<T> RunTask<T>(Task<T> task, CancellationToken cancellationToken)
    {
        return RunTask(task, TimeSpan.Zero, cancellationToken);
    }

    /// <summary>Waits for a task at most a timeout; the task itself is not canceled.</summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="task">The task.</param>
    /// <param name="timeout">How long to wait; zero for no limit.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the task's.</returns>
    /// <exception cref="TimeoutException">The timeout passed first.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is negative, other than infinite, or too long.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="Exception">The task's own exception.</exception>
    public static async Task<T> RunTask<T>(Task<T> task, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await RunTask((Task)task, timeout, cancellationToken).Vhc();
        return await task.Vhc();
    }

    /// <summary>Waits for a task until a token is canceled; the task itself is not canceled.</summary>
    /// <param name="task">The task.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the task.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="Exception">The task's own exception.</exception>
    public static Task RunTask(Task task, CancellationToken cancellationToken)
    {
        return RunTask(task, TimeSpan.Zero, cancellationToken);
    }

    /// <summary>Waits for a task at most a timeout; the task itself is not canceled.</summary>
    /// <param name="task">The task.</param>
    /// <param name="timeout">How long to wait; zero for no limit.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the task.</returns>
    /// <exception cref="TimeoutException">The timeout passed first.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is negative, other than infinite, or too long.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="Exception">The task's own exception.</exception>
    public static async Task RunTask(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout == TimeSpan.Zero)
            timeout = Timeout.InfiniteTimeSpan;

        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completedTask = await Task.WhenAny(task, timeoutTask).Vhc();

        // Still check if it was actually a timeout
        if (completedTask == timeoutTask) {
            if (timeoutTask.IsCompletedSuccessfully)
                throw new TimeoutException();
            cancellationToken.ThrowIfCancellationRequested(); // clearer path for cancellation
        }

        await task.Vhc();
    }

    /// <summary>Determines whether a sequence is null or has no item.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="array">The sequence, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if it is null or empty; otherwise, <see langword="false"/>.</returns>
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] IEnumerable<T>? array)
    {
        return array == null || !array.Any();
    }

    /// <summary>Determines whether an array is null or empty.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="array">The array, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if it is null or empty; otherwise, <see langword="false"/>.</returns>
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] T[]? array)
    {
        return array == null || array.Length == 0;
    }

    /// <summary>Determines whether two sequences have the same items in order, a null one counting as empty.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="first">The first sequence, or <see langword="null"/>.</param>
    /// <param name="second">The second sequence, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if they are the same; otherwise, <see langword="false"/>.</returns>
    public static bool SequenceEquals<T>(IEnumerable<T>? first, IEnumerable<T>? second)
    {
        first ??= [];
        second ??= [];
        return first.SequenceEqual(second);
    }

    /// <summary>Splits a command line into arguments at white space, keeping a double-quoted part together.</summary>
    /// <remarks>
    /// A quoted part and what stands right beside it are one argument, and a pair of quotes alone is an empty one.
    /// </remarks>
    /// <param name="commandLine">The command line.</param>
    /// <returns>The arguments, without their quotes.</returns>
    public static IEnumerable<string> ParseArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            yield break;

        // A quote opens or closes a quoted part and is not kept; only white space outside one ends an argument. So
        // quoted and plain parts side by side are one argument, and a pair of quotes alone is an empty one.
        var sb = new StringBuilder();
        var inQuote = false;
        var hasArgument = false;
        foreach (var c in commandLine) {
            if (c == '"') {
                inQuote = !inQuote;
                hasArgument = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuote) {
                if (hasArgument) {
                    yield return sb.ToString();
                    sb.Clear();
                    hasArgument = false;
                }

                continue;
            }

            sb.Append(c);
            hasArgument = true;
        }

        if (hasArgument)
            yield return sb.ToString();
    }

    /// <summary>Generates a random 128-bit AES key.</summary>
    /// <returns>The key.</returns>
    public static byte[] GenerateKey()
    {
        // ReSharper disable once IntroduceOptionalParameters.Global
        return GenerateKey(128);
    }

    /// <summary>Generates a random AES key of a size.</summary>
    /// <param name="keySizeInBit">The key's size in bits: 128, 192 or 256.</param>
    /// <returns>The key.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The size is not an AES key's.</exception>
    public static byte[] GenerateKey(int keySizeInBit)
    {
        using var aes = Aes.Create();
        aes.KeySize = keySizeInBit;
        aes.GenerateKey();
        return aes.Key;
    }

    /// <summary>
    /// Encrypts a client ID with a shared key, as the server checks it: AES-CBC with a zero IV, over the ID's GUID
    /// bytes, or its UTF-8 bytes when it is no GUID, filled up with zeros to a whole number of blocks.
    /// </summary>
    /// <remarks>
    /// The same ID and key always give the same bytes: the server makes them again to check that the client holds
    /// the key. They are a proof of that, not a secret kept.
    /// </remarks>
    /// <param name="clientId">The client ID.</param>
    /// <param name="key">The AES key, of 128, 192 or 256 bits.</param>
    /// <returns>The encrypted bytes.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The key is of no AES size.</exception>
    public static byte[] EncryptClientId(string clientId, byte[] key)
    {
        // Validate request by shared secret
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Key = key;

        // a block of zeros, whatever the key's size: made as long as the key, it fit a 128-bit key alone
        aes.IV = new byte[aes.BlockSize / 8];

        // none is added to a GUID's 16 bytes, so their bytes are as they always were; an ID of another length is
        // filled up, where it could not be encrypted at all
        aes.Padding = PaddingMode.Zeros;

        // compatibility with old clients
        var buffer = Guid.TryParse(clientId, out var clientGuid)
            ? clientGuid.ToByteArray()
            : Encoding.UTF8.GetBytes(clientId);

        using var cryptor = aes.CreateEncryptor();
        return cryptor.TransformFinalBlock(buffer, 0, buffer.Length);
    }

    /// <summary>Computes the MD5 hash of bytes, for change detection, not security.</summary>
    /// <param name="value">The bytes.</param>
    /// <returns>The hash, in upper-case hex.</returns>
    public static string GetBufferMd5(byte[] value)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(value);
        return BitConverter.ToString(hash).Replace("-", "");
    }

    /// <summary>Computes the MD5 hash of a text's UTF-8 bytes, for change detection, not security.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The hash, in upper-case hex.</returns>
    public static string GetStringMd5(string value)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(hash).Replace("-", "");
    }

    // Stable content id (change detection, not security), as lowercase hex, optionally truncated —
    // e.g. 16 chars (64 bits) already keep accidental collisions out of reach for file-name ids.
    /// <summary>
    /// Computes a stable ID of a text's content, for change detection, not security: its SHA-256 in lower-case hex,
    /// optionally cut.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="length">The number of hex characters to keep, or <see langword="null"/> for all 64.</param>
    /// <returns>The hex text.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or over 64.</exception>
    public static string GetHexStringSha256(string value, int? length = null)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return length is null ? hash : hash[..length.Value];
    }

    private static double Round(double value, bool round)
    {
        return round ? Math.Round(value) : value;
    }

    // Pseudocode:
    // - Input: size (long), use1024 (bool), round (bool)
    // - Define unit sizes (KB, MB, GB, TB) based on use1024
    // - Compute sign (negative or not) and absolute value using double to avoid long.MinValue overflow
    // - Compare absolute value with thresholds to pick unit
    // - Format value with existing Round() helper and original numeric formats
    // - Prefix sign and append unit
    // - Preserve original behavior for zero (return "0")
    /// <summary>Formats a size in bytes for reading, such as <c>1.5 GB</c>, in the current culture.</summary>
    /// <param name="size">The size in bytes; a negative one keeps its sign.</param>
    /// <param name="use1024"><see langword="true"/> for units of 1024; <see langword="false"/> for units of 1000.</param>
    /// <param name="round"><see langword="true"/> to round to a whole number of the unit.</param>
    /// <returns>The text, or <c>0</c> for zero.</returns>
    public static string FormatBytes(long size, bool use1024 = true, bool round = false)
    {
        var kb = use1024 ? 1024d : 1000d;
        var mb = kb * kb;
        var gb = mb * kb;
        var tb = gb * kb;

        var negative = size < 0;
        var abs = Math.Abs((double)size);
        var sign = negative ? "-" : "";

        if (abs >= tb) // Terabyte
            return sign + Round(abs / tb, round).ToString("0.## ") + "TB";

        if (abs >= gb) // Gigabyte
            return sign + Round(abs / gb, round).ToString("0.# ") + "GB";

        if (abs >= mb) // Megabyte
            return sign + Round(abs / mb, round).ToString("0 ") + "MB";

        if (abs >= kb) // Kilobyte
            return sign + Round(abs / kb, round).ToString("0 ") + "KB";

        if (abs > 0) // Byte
            return sign + abs.ToString("0 ") + "B";

        // Zero bytes
        return "0";
    }

    // megabytes to bytes, 1 MB being 1024 * 1024 bytes
    /// <summary>Converts megabytes to bytes, a megabyte being 1024 × 1024 bytes.</summary>
    /// <param name="megabytes">The megabytes.</param>
    /// <returns>The bytes.</returns>
    public static long FromMB(long megabytes)
    {
        return megabytes * 1024 * 1024;
    }

    /// <summary>Formats a size in megabytes for reading, such as <c>2 GB</c>, in the current culture.</summary>
    /// <param name="sizeMb">The size in megabytes; a negative one keeps its sign.</param>
    /// <param name="use1024"><see langword="true"/> for units of 1024; <see langword="false"/>, the default, for units of 1000.</param>
    /// <param name="round"><see langword="true"/> to round to a whole number of the unit.</param>
    /// <returns>The text, or <c>0</c> for zero.</returns>
    public static string FormatMegaBytes(long sizeMb, bool use1024 = false, bool round = false)
    {
        var divisor = use1024 ? 1024L : 1000L;
        var gb = divisor;             // 1 GB = 1024 MB
        var tb = gb * divisor;        // 1 TB = 1024 * 1024 MB
        var pb = tb * divisor;        // 1 PB = 1024 * 1024 * 1024 MB

        var negative = sizeMb < 0;
        var abs = Math.Abs((double)sizeMb);
        var sign = negative ? "-" : "";

        if (abs >= pb) // Petabyte
            return sign + GetVal(abs / pb).ToString("0.## ") + "PB";

        if (abs >= tb) // Terabyte
            return sign + GetVal(abs / tb).ToString("0.# ") + "TB";

        if (abs >= gb) // Gigabyte (No decimal)
            return sign + GetVal(abs / gb).ToString("0 ") + "GB";

        if (abs > 0) // Megabyte (No decimal)
            return sign + abs.ToString("0 ") + "MB";

        // Zero megabytes
        return "0";

        // Helper to handle the "round" bool logic inline
        double GetVal(double val) => round ? Math.Round(val) : val;
    }

    /// <summary>Formats a rate in bytes per second as bits per second, such as <c>8 Mbps</c>, in units of 1024.</summary>
    /// <param name="bytes">The bytes per second.</param>
    /// <returns>The text.</returns>
    public static string FormatBits(long bytes)
    {
        bytes *= 8; //convertTo bit
        // ReSharper disable PossibleLossOfFraction

        // Get absolute value
        if (bytes >= 0x40000000) // Gigabyte, with the fraction its format asks for: divided as a real number
            return (bytes / (double)0x40000000).ToString("0.# ") + "Gbps";

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

    /// <summary>Determines whether a time span means no limit: <see cref="TimeSpan.MaxValue"/> or <see cref="Timeout.InfiniteTimeSpan"/>.</summary>
    /// <param name="timeSpan">The time span.</param>
    /// <returns><see langword="true"/> if it does; otherwise, <see langword="false"/>.</returns>
    public static bool IsInfinite(TimeSpan timeSpan)
    {
        return timeSpan == TimeSpan.MaxValue || timeSpan == Timeout.InfiniteTimeSpan;
    }

    private static bool _hasKeepAliveError;

    /// <summary>
    /// Configures a TCP client: Nagle's delay, buffer sizes above zero, address reuse and keep-alive, each when given. A
    /// failure to set keep-alive is logged as a warning, and keep-alive is skipped from then on, process-wide.
    /// </summary>
    /// <param name="tcpClient">The TCP client.</param>
    /// <param name="sendBufferSize">The send buffer's size, or <see langword="null"/> to keep it.</param>
    /// <param name="receiveBufferSize">The receive buffer's size, or <see langword="null"/> to keep it.</param>
    /// <param name="reuseAddress">Whether the address may be reused, or <see langword="null"/> to keep it.</param>
    /// <param name="keepAlive">Whether to send keep-alives, or <see langword="null"/> to keep it.</param>
    /// <param name="noDelay"><see langword="true"/>, the default, to send without Nagle's delay.</param>
    /// <exception cref="SocketException">An option other than keep-alive cannot be set.</exception>
    public static void ConfigTcpClient(TcpClient tcpClient,
        int? sendBufferSize = null,
        int? receiveBufferSize = null,
        bool? reuseAddress = null,
        bool? keepAlive = null,
        bool noDelay = true)
    {
        tcpClient.NoDelay = noDelay;
        if (sendBufferSize > 0) tcpClient.SendBufferSize = sendBufferSize.Value;
        if (receiveBufferSize > 0) tcpClient.ReceiveBufferSize = receiveBufferSize.Value;
        if (reuseAddress != null)
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                reuseAddress.Value);

        // keep alive (guarded: older/limited OSes may not support it; warn once then no-op)
        if (keepAlive != null && !_hasKeepAliveError) {
            try {
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, keepAlive.Value);
            }
            catch (Exception ex) {
                _hasKeepAliveError = true;
                VhLogger.Instance.LogWarning(ex, "KeepAlive is not supported! Consider upgrading your OS.");
            }
        }
    }

    /// <summary>Determines whether a TCP client is connected and its socket reports no error.</summary>
    /// <param name="tcpClient">The TCP client.</param>
    /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>, also when the check fails.</returns>
    public static bool IsTcpClientHealthy(TcpClient tcpClient)
    {
        try {
            // Check if the TcpClient is connected
            if (!tcpClient.Connected)
                return false;

            // Check if the underlying socket is connected
            var socket = tcpClient.Client;
            var healthy = socket is { Connected: true } && !socket.Poll(1, SelectMode.SelectError);

            return healthy;
        }
        catch (Exception) {
            // An error occurred while checking the TcpClient
            return false;
        }
    }

    /// <summary>Returns an object that must have been set.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="obj">The object, or <see langword="null"/>.</param>
    /// <returns>The object.</returns>
    /// <exception cref="InvalidOperationException">The object is <see langword="null"/>.</exception>
    public static T GetRequiredInstance<T>(T? obj)
    {
        return obj ?? throw new InvalidOperationException($"{typeof(T)} has not been initialized yet.");
    }

    /// <summary>Cuts a time to whole seconds, keeping its kind.</summary>
    /// <param name="dateTime">The time.</param>
    /// <returns>The time without its fraction of a second.</returns>
    public static DateTime RemoveMilliseconds(DateTime dateTime)
    {
        return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute,
            dateTime.Second, dateTime.Kind);
    }

    /// <summary>Reads an assembly's metadata value by key.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The value when the key is missing or empty.</param>
    /// <returns>The value.</returns>
    public static string GetAssemblyMetadata(Assembly assembly, string key, string defaultValue)
    {
        var metadataAttribute = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == key);

        return string.IsNullOrEmpty(metadataAttribute?.Value) ? defaultValue : metadataAttribute.Value;
    }

    /// <summary>Deletes a file when it exists, and gives back a failure instead of throwing it.</summary>
    /// <param name="filePath">The file's path.</param>
    /// <returns>The failure, or <see langword="null"/>.</returns>
    public static Exception? TryDeleteFile(string filePath)
    {
        try {
            if (File.Exists(filePath))
                File.Delete(filePath);

            return null;
        }
        catch (Exception ex) {
            return ex;
        }
    }

    /// <summary>Shuffles an array in place (Fisher-Yates), with a non-cryptographic random.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="array">The array.</param>
    public static void Shuffle<T>(T[] array)
    {
        var rng = new Random();
        var n = array.Length;
        for (var i = n - 1; i > 0; i--) {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    /// <summary>Gets a country's English name from its code.</summary>
    /// <param name="countryCode">The country's two-letter code, or <see langword="null"/>.</param>
    /// <returns>The name, or <see langword="null"/> for an empty or unknown code.</returns>
    public static string? TryGetCountryName(string? countryCode)
    {
        if (string.IsNullOrEmpty(countryCode))
            return null;

        try {
            return new RegionInfo(countryCode).EnglishName;
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>Gets a currency's symbol from its ISO code, from the first culture whose region uses it.</summary>
    /// <param name="currencyCode">The currency's ISO code, such as <c>USD</c>.</param>
    /// <returns>The symbol, or the code itself when no culture uses it.</returns>
    public static string GetCurrencySymbol(string currencyCode)
    {
        var culture = CultureInfo
            .GetCultures(CultureTypes.AllCultures)
            .FirstOrDefault(c => {
                try {
                    var region = new RegionInfo(c.LCID);
                    return region.ISOCurrencySymbol == currencyCode;
                }
                catch {
                    return false;
                }
            });

        if (culture == null)
            return currencyCode; // fallback

        var regionInfo = new RegionInfo(culture.LCID);
        return regionInfo.CurrencySymbol;
    }

    /// <summary>
    /// Disposes every item; a throw stops the rest. Callers keep their own locks around the enumeration.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="items">The items, or <see langword="null"/>.</param>
    public static void DisposeAll<T>(IEnumerable<T>? items) where T : IDisposable
    {
        if (items == null)
            return;

        foreach (var item in items)
            item.Dispose();
    }

    /// <summary>Parses a URL.</summary>
    /// <param name="stringUrl">The URL's text, or <see langword="null"/>.</param>
    /// <param name="uriKind">The kind of URL expected. The default is absolute.</param>
    /// <returns>The URL, or <see langword="null"/> when the text is not one.</returns>
    /// <exception cref="ArgumentException"><paramref name="uriKind"/> is not a valid kind.</exception>
    public static Uri? TryParseUrl(string? stringUrl, UriKind uriKind = UriKind.Absolute)
    {
        return Uri.TryCreate(stringUrl, uriKind, out var result) ? result : null;
    }
}