using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace VpnHood.AccessServer;

internal static class AppExceptionExtension
{
    public class CustomExceptionMiddleware
    {
        private readonly RequestDelegate _next;

        public CustomExceptionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        private static string GetTypeName(Exception ex)
        {
            if (AccessUtil.IsAlreadyExistsException(ex)) return "AlreadyExistsException";
            if (AccessUtil.IsNotExistsException(ex)) return "NotExistsException";
            return ex.GetType().ToString();
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next.Invoke(context);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.ContentType = MediaTypeNames.Application.Json;
                var error = new
                {
                    Data = new Dictionary<string, string?>(),
                    Type = GetTypeName(ex),
                    ex.Message
                };

                foreach (DictionaryEntry item in ex.Data)
                {
                    var key = item.Key.ToString();
                    if (key != null)
                        error.Data.Add(key, item.Value?.ToString());
                }
                await context.Response.WriteAsync(JsonSerializer.Serialize(error));
            }
        }
    }

    public static IApplicationBuilder UseAppExceptionHandler(this IApplicationBuilder app)
    {
        app.UseMiddleware<CustomExceptionMiddleware>();
        return app;
    }
}