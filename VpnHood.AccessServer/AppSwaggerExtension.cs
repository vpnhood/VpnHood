using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using NJsonSchema.Generation.TypeMappers;
using NSwag;
using NSwag.Generation.Processors.Security;

//using Microsoft.OpenApi.Models;
//using Swashbuckle.AspNetCore.SwaggerGen;

namespace VpnHood.AccessServer;

internal static class AppSwaggerExtension
{
    public static IServiceCollection AddAppSwagger(this IServiceCollection services)
    {
        services.AddSwaggerDocument(configure =>
        {
            configure.TypeMappers = new List<ITypeMapper>
            {
                new PrimitiveTypeMapper(typeof(IPAddress), s=> {s.Type = JsonObjectType.String;  }),
                new PrimitiveTypeMapper(typeof(IPEndPoint), s=> {s.Type = JsonObjectType.String;  }),
                new PrimitiveTypeMapper(typeof(Version), s=> {s.Type = JsonObjectType.String;  })
            };

            configure.DocumentProcessors.Add(
                new SecurityDefinitionAppender("Bearer", new OpenApiSecurityScheme()
                {
                    Type = OpenApiSecuritySchemeType.ApiKey,
                    In = OpenApiSecurityApiKeyLocation.Header
                })
            );
        });

        return services;

    }

    //public static IServiceCollection AddAppSwaggerGen(this IServiceCollection services)
    //{
    //    services.AddSwaggerGen(c =>
    //    {
    //        c.SwaggerDoc(
    //            "v1",
    //            new OpenApiInfo
    //            {
    //                Title = AppOptions.Name,
    //                Version = "v1"
    //            });

    //        c.AddSecurityDefinition(
    //            "Bearer",
    //            new OpenApiSecurityScheme
    //            {
    //                Description = "JWT Authorization header using the Bearer scheme (Example: 'Bearer 12345abcdef')",
    //                In = ParameterLocation.Header,
    //                Name = "Authorization",
    //                Type = SecuritySchemeType.ApiKey,
    //                Scheme = "Bearer"
    //            });

    //        c.AddSecurityRequirement(new OpenApiSecurityRequirement
    //        {
    //            {
    //                new OpenApiSecurityScheme
    //                {
    //                    Reference = new OpenApiReference {Type = ReferenceType.SecurityScheme, Id = "Bearer"}
    //                },
    //                Array.Empty<string>()
    //            }
    //        });

    //        // XML Documentation
    //        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    //        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    //        c.IncludeXmlComments(xmlPath);
    //        c.SchemaFilter<MySchemaFilter>();
    //        c.MapType<IPAddress>(() => new OpenApiSchema { Type = "string" });
    //        c.MapType<IPEndPoint>(() => new OpenApiSchema { Type = "string" });
    //        c.MapType<Version>(() => new OpenApiSchema { Type = "string" });
    //        c.MapType<TimeSpan>(() => new OpenApiSchema { Type = "number" });
    //    });
    //    return services;
    //}

    //public class MySchemaFilter : ISchemaFilter
    //{
    //    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    //    {

    //    }
    //}
}