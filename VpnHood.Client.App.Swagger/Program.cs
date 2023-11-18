using System.Net;
using NJsonSchema;
using NJsonSchema.Generation.TypeMappers;
using VpnHood.Common.Net;

namespace VpnHood.Client.App.Swagger;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddSwaggerDocument(configure =>
        {
            configure.Title = "VpnHood.Client.Api";
            configure.RequireParametersWithoutDefault = true;
            configure.SchemaSettings.TypeMappers = new List<ITypeMapper>
            {
                new PrimitiveTypeMapper(typeof(IPAddress), s => { s.Type = JsonObjectType.String; }),
                new PrimitiveTypeMapper(typeof(IPEndPoint), s => { s.Type = JsonObjectType.String; }),
                new PrimitiveTypeMapper(typeof(Version), s => { s.Type = JsonObjectType.String; }),
                new PrimitiveTypeMapper(typeof(IpRange), s => { s.Type = JsonObjectType.String; }),
            };
        });


        builder.Services.AddControllers();

        var app = builder.Build();

        app.UseOpenApi();
        app.UseSwaggerUi();

        app.MapControllers();
        app.Run();
    }
}