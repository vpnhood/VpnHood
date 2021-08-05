using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using VpnHood.AccessServer.Controllers;

namespace VpnHood.AccessServer
{
    static class AppSwaggerExtension
    {
        public class MySchemaFilter : ISchemaFilter
        {
            public void Apply(OpenApiSchema schema, SchemaFilterContext schemaFilterContext)
            {
                if (schema?.Properties == null )
                    return;

                var skipProperties = schemaFilterContext.Type.GetProperties().Where(t => t.GetMethod.IsVirtual);
                foreach (var skipProperty in skipProperties)
                {
                    var propertyToSkip = schema.Properties.Keys.SingleOrDefault(x => string.Equals(x, skipProperty.Name, StringComparison.OrdinalIgnoreCase));
                    if (propertyToSkip != null)
                        schema.Properties.Remove(propertyToSkip);
                }
            }
        }

        public static IApplicationBuilder UseAppSwagger(this IApplicationBuilder app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{App.ProductName} v1"));
            return app;
        }

        public static IServiceCollection AddAppSwaggerGen(this IServiceCollection services)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc(
                    "v1",
                    new OpenApiInfo
                    {
                        Title = App.ProductName,
                        Version = Assembly.GetExecutingAssembly().GetName().Version.ToString()
                    });

                c.AddSecurityDefinition(
                    "Bearer",
                    new OpenApiSecurityScheme
                    {
                        Description = "JWT Authorization header using the Bearer scheme (Example: 'Bearer 12345abcdef')",
                        In = ParameterLocation.Header,
                        Name = "Authorization",
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = "Bearer"
                    });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference{Type = ReferenceType.SecurityScheme, Id = "Bearer"}
                        },
                        Array.Empty<string>()
                    }
                });

                c.SchemaFilter<MySchemaFilter>();
                c.MapType<IPAddress>(() => new OpenApiSchema() { Type = "string" });
                c.MapType<IPEndPoint>(() => new OpenApiSchema() { Type = "string" });
                c.MapType<Version>(() => new OpenApiSchema() { Type = "string" });
            });
            return services;
        }
    }
}
