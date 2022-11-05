using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

// ReSharper disable UnusedMember.Global
namespace VpnHood.Common.Client;

public sealed class ApiException : Exception
{
    private class ServerException
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        public Dictionary<string, string?> Data { get; set; } = new();
        public string? TypeName { get; set; }
        public string? TypeFullName { get; set; }
        public string? Message { get; set; }

        public static bool TryParse(string? value, [NotNullWhen(true)] out ServerException? serverException)
        {
            serverException = null!;
            if (value == null)
                return false;

            try { serverException = JsonSerializer.Deserialize<ServerException>(value); }
            catch { /* ignored */}

            return serverException?.TypeName != null;
        }
    }

    public int StatusCode { get; }
    public string? Response { get; }
    public string? ExceptionTypeName { get; }
    public string? ExceptionTypeFullName { get; }
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
            ExceptionTypeName = serverException.TypeName;
            ExceptionTypeFullName = serverException.TypeFullName;
        }
    }

    public override string ToString()
    {
        return $"HTTP Response: \n\n{Response}\n\n{base.ToString()}";
    }
    public bool Is<T>() => ExceptionTypeName == typeof(T).Name;
}