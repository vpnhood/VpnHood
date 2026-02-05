using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server;

//todo: add test
internal class DownloadService(string? downloadsPath)
{
    private const string RandomFileName = "random";
    private const int DefaultRandomSizeMb = 50;
    public string? DownloadPath => downloadsPath;

    /// <summary>
    /// Tries to serve a download file from the specified URL path.
    /// </summary>
    /// <param name="connection">The connection to write the response to.</param>
    /// <param name="httpRequestLine">The HTTP request line (e.g., "GET /downloads/file.txt HTTP/1.1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the request was handled, false if not applicable.</returns>
    public async Task<bool> TryServeDownloadAsync(IConnection connection, string httpRequestLine,
        CancellationToken cancellationToken)
    {
        // Check if downloads path is configured
        if (string.IsNullOrEmpty(downloadsPath))
            return false;

        // Parse the request line to get the path
        // Expected format: "GET /downloads/filename HTTP/1.1"
        var parts = httpRequestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        var method = parts[0];
        var urlPath = parts[1];

        // Only handle GET requests
        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check if the path starts with /downloads/
        if (!urlPath.StartsWith("/downloads/", StringComparison.OrdinalIgnoreCase))
            return false;

        // Resolve the file path
        var filePath = ResolveFilePath(downloadsPath, urlPath, out var relativePath, out var queryString);
        if (filePath == null) {
            VhLogger.Instance.LogDebug(GeneralEventId.Request,
                "Download file not found or invalid path. UrlPath: {UrlPath}", urlPath);
            return false;
        }

        // Handle random with size parameter
        var isRandomFile = Path.GetFileNameWithoutExtension(relativePath)
            .Equals(RandomFileName, StringComparison.OrdinalIgnoreCase);
        if (isRandomFile) {
            var sizeMb = ParseSizeParameter(queryString, DefaultRandomSizeMb);
            await ServeRandomDataAsync(connection, sizeMb, cancellationToken).Vhc();
            return true;
        }

        // Serve the actual file
        await ServeFileAsync(connection, filePath, cancellationToken).Vhc();
        return true;
    }

    private static int ParseSizeParameter(string queryString, int defaultValue)
    {
        // Parse query string for size parameter (e.g., "size=10" for 10MB)
        if (string.IsNullOrEmpty(queryString))
            return defaultValue;

        var parameters = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var param in parameters) {
            var keyValue = param.Split('=', 2);
            if (keyValue.Length == 2 &&
                keyValue[0].Equals("size", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(keyValue[1], out var size) &&
                size > 0) {
                // Limit to reasonable size (e.g., 1024MB = 1GB)
                return Math.Min(size, 1024);
            }
        }

        return defaultValue;
    }

    private static async Task ServeRandomDataAsync(IConnection connection, int sizeMb,
        CancellationToken cancellationToken)
    {
        var totalBytes = (long)sizeMb * 1024 * 1024;

        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Download started for random data. SizeMB: {SizeMb}, RemoteEp: {RemoteEp}",
            sizeMb, VhLogger.Format(connection.RemoteEndPoint));

        // Write HTTP response header
        var response = BuildOkResponse(totalBytes, "application/octet-stream", RandomFileName);
        await connection.Stream.WriteAsync(response, cancellationToken).Vhc();

        // Write random data in chunks
        const int chunkSize = 64 * 1024; // 64KB chunks
        var buffer = new byte[chunkSize];
        var bytesRemaining = totalBytes;

        while (bytesRemaining > 0) {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesToWrite = (int)Math.Min(chunkSize, bytesRemaining);
            RandomNumberGenerator.Fill(buffer.AsSpan(0, bytesToWrite));
            await connection.Stream.WriteAsync(buffer.AsMemory(0, bytesToWrite), cancellationToken).Vhc();
            bytesRemaining -= bytesToWrite;
        }

        connection.PreventReuse();
        await connection.DisposeAsync();

        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Download completed for random data. SizeMB: {SizeMb}, RemoteEp: {RemoteEp}",
            sizeMb, VhLogger.Format(connection.RemoteEndPoint));
    }

    private static async Task ServeFileAsync(IConnection connection, string filePath,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);

            // Log download start
        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Download started. FileName: {FileName}, Size: {Size}, RemoteEp: {RemoteEp}",
            fileInfo.FullName, fileInfo.Length, VhLogger.Format(connection.RemoteEndPoint));

        // Determine content type
        var contentType = MimeTypeUtils.GetContentType(fileInfo.FullName);

        // Write HTTP response header
        var response = BuildOkResponse(fileInfo.Length, contentType, fileInfo.Name);
        await connection.Stream.WriteAsync(response, cancellationToken).Vhc();

        // Stream the file content
        await using var fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        await fileStream.CopyToAsync(connection.Stream, cancellationToken).Vhc();

        // Log download completion
        connection.PreventReuse();
        await connection.DisposeAsync();

        // Log download completion
        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Download completed. FileName: {FileName}, RemoteEp: {RemoteEp}",
            fileInfo.FullName, VhLogger.Format(connection.RemoteEndPoint));

    }

    private static ReadOnlyMemory<byte> BuildOkResponse(long contentLength, string contentType, string fileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent([])
        };
        response.Headers.Date = DateTimeOffset.Now;
        response.Headers.ConnectionClose = true;
        response.Content.Headers.ContentLength = contentLength;
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        response.Content.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") {
                FileName = fileName
            };

        return HttpResponseBuilder.Build(response);
    }

    private static string? ResolveFilePath(string downloadsPath, string urlPath, out string relativePath, out string queryString)
    {
        relativePath = string.Empty;
        queryString = string.Empty;

        if (!urlPath.StartsWith("/downloads/", StringComparison.OrdinalIgnoreCase))
            return null;

        var pathAfterDownloads = urlPath["/downloads/".Length..];
        var queryIndex = pathAfterDownloads.IndexOf('?');
        relativePath = queryIndex >= 0 ? pathAfterDownloads[..queryIndex] : pathAfterDownloads;
        queryString = queryIndex >= 0 ? pathAfterDownloads[(queryIndex + 1)..] : string.Empty;

        // Sanitize to allow nested folders but prevent traversal
        relativePath = relativePath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var rootFullPath = Path.GetFullPath(downloadsPath);
        var filePath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath));

        // Ensure the resolved path is still under the root downloads directory
        if (!filePath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            return null;

        // Check if the file exists. Random file is a virtual file and must exist to prevent fingerprinting
        if (!File.Exists(filePath))
            return null;

        return filePath;
    }
}
