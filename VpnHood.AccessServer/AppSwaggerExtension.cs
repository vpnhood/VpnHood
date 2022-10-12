using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using NJsonSchema.Generation.TypeMappers;
using NSwag;
using NSwag.Generation.Processors.Security;

namespace VpnHood.AccessServer;
internal static class AppSwaggerExtension
{
    public static IServiceCollection AddAppSwagger(this IServiceCollection services, string title)
    {
        services.AddSwaggerDocument(configure =>
        {
            configure.Title = title;

            configure.TypeMappers = new List<ITypeMapper>
            {
                new PrimitiveTypeMapper(typeof(IPAddress), s => { s.Type = JsonObjectType.String; }),
                new PrimitiveTypeMapper(typeof(IPEndPoint), s => { s.Type = JsonObjectType.String; }),
                new PrimitiveTypeMapper(typeof(Version), s => { s.Type = JsonObjectType.String; }),
            };

            configure.OperationProcessors.Add(new OperationSecurityScopeProcessor("Bearer"));
            configure.DocumentProcessors.Add(
                new SecurityDefinitionAppender("Bearer", new OpenApiSecurityScheme()
                {
                    Name = "Authorization",
                    Type = OpenApiSecuritySchemeType.ApiKey,
                    In = OpenApiSecurityApiKeyLocation.Header,
                    Description = "Type into the text-box: Bearer {your JWT token}"
                })
            );
        });

        return services;

    }
}