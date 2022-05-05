using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

// ReSharper disable once CheckNamespace
namespace VpnHood.AccessServer.Api;

public sealed class ApiException : Exception
{
    private class ServerException
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        public Dictionary<string, string?> Data { get; set; } = new();
        public string Type { get; set; } = "General";
        public string? Message { get; set; }

        public static bool TryParse(string? value, [NotNullWhen(true)] out ServerException? serverException)
        {
            serverException = null!;
            if (value == null)
                return false;

            try { serverException = JsonSerializer.Deserialize<ServerException>(value); }
            catch { /* ignored */}

            return serverException != null;
        }
    }

    public int StatusCode { get; }
    public string? Response { get; }
    public string? ExceptionType { get; }
    public IReadOnlyDictionary<string, IEnumerable<string>> Headers { get; }

    private static string? BuildMessage(string message, int statusCode, string? response)
    {
        return ServerException.TryParse(response, out var serverException)
            ? serverException.Message
            : $"{message}\n\nStatus: {statusCode}\nResponse: \n{response?[..Math.Min(512, response.Length)]}";
    }

    public ApiException(string message, int statusCode, string? response, IReadOnlyDictionary<string, IEnumerable<string>> headers, Exception? innerException)
        : base(BuildMessage(message, statusCode, response), innerException)
    {
        StatusCode = statusCode;
        Response = response;
        Headers = headers;

        //try to deserialize response
        if (ServerException.TryParse(response, out var serverException))
        {
            foreach (var item in serverException.Data)
                Data.Add(item.Key, item.Value);

            Response = JsonSerializer.Serialize(serverException, new JsonSerializerOptions { WriteIndented = true });
            ExceptionType = serverException.Type;
        }
    }

    public override string ToString()
    {
        return $"HTTP Response: \n\n{Response}\n\n{base.ToString()}";
    }

    public bool IsNotExistsException => ExceptionType?.Contains("NotExistsException") == true;
    public bool IsQuotaException => ExceptionType?.Contains("QuotaException") == true;
}