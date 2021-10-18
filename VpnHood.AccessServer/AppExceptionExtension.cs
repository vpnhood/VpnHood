using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace VpnHood.AccessServer
{
    internal static class AppExceptionExtension
    {
        public class CustomExceptionMiddleware
        {
            private readonly RequestDelegate _next;

            public CustomExceptionMiddleware(RequestDelegate next)
            {
                _next = next;
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
                        Type = ex.GetType().ToString(),
                        ex.Message
                    };
                    foreach (DictionaryEntry item in ex.Data)
                    {
                        var key = item.Key.ToString();
                        if (key!=null)
                            error.Data.Add(key, item.Value?.ToString());
                    }
                    await context.Response.WriteAsync(JsonSerializer.Serialize(error));
                }
            }
        }

        public static IApplicationBuilder UseAppExceptionHandler(this IApplicationBuilder app)
        {
            app.UseMiddleware<CustomExceptionMiddleware>();
            //app.UseExceptionHandler(errorApp =>
            //{
            //    errorApp.Run(async context =>
            //    {
            //        var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
            //        var ex = exceptionHandlerPathFeature.Error;
            //        if (ex is SqlException)
            //        {
            //            context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
            //            context.Response.ContentType = "text/html";
            //            await context.Response.WriteAsync(exceptionHandlerPathFeature.Error.Message);
            //        }
            //    });
            //});

            return app;
        }
    }
}