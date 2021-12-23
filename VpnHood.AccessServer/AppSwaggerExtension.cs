using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace VpnHood.AccessServer
{
    internal static class AppSwaggerExtension
    {
        public static IApplicationBuilder UseAppSwagger(this IApplicationBuilder app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{AccessServerApp.Instance.ProductName} v1");
                c.RoutePrefix = string.Empty;
            });

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
                        Title = AccessServerApp.Instance.ProductName,
                        Version = "v1"
                    });

                c.AddSecurityDefinition(
                    "Bearer",
                    new OpenApiSecurityScheme
                    {
                        Description =
                            "JWT Authorization header using the Bearer scheme (Example: 'Bearer 12345abcdef')",
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
                            Reference = new OpenApiReference {Type = ReferenceType.SecurityScheme, Id = "Bearer"}
                        },
                        Array.Empty<string>()
                    }
                });

                // XML Documentation
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
                c.SchemaFilter<MySchemaFilter>();
                c.MapType<IPAddress>(() => new OpenApiSchema { Type = "string" });
                c.MapType<IPEndPoint>(() => new OpenApiSchema { Type = "string" });
                c.MapType<Version>(() => new OpenApiSchema { Type = "string" });
                c.MapType<TimeSpan>(() => new OpenApiSchema { Type = "string" });
            });
            return services;
        }

        public class MySchemaFilter : ISchemaFilter
        {
            public void Apply(OpenApiSchema schema, SchemaFilterContext context)
            {
                if (schema.Properties == null)
                    return;

                var skipProperties = context.Type
                    .GetProperties()
                    .Where(t => t.GetMethod?.IsVirtual == true);

                foreach (var skipProperty in skipProperties)
                {
                    var propertyToSkip = schema.Properties.Keys.SingleOrDefault(x =>
                        string.Equals(x, skipProperty.Name, StringComparison.OrdinalIgnoreCase));
                    //if (propertyToSkip != null)
                    //  schema.Properties.Remove(propertyToSkip);
                }
            }
        }
    }
}