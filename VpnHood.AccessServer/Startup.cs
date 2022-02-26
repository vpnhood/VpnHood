using System;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using VpnHood.AccessServer.Authentication;

namespace VpnHood.AccessServer;

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
        AccessServerApp.Instance.Configure(configuration);
    }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        //enable cross-origin; MUST before anything
        services.AddCors(o => o.AddPolicy("CorsPolicy", builder =>
        {
            builder
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetPreflightMaxAge(TimeSpan.FromHours(24 * 30));
        }));

        // Add authentications
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddAppAuthentication(Configuration.GetSection("AuthProviders"))
            .AddMicrosoftIdentityWebApi(Configuration.GetSection("AzureB2C"));

        services.AddControllers(options =>
        {
            options.ModelMetadataDetailsProviders.Add(
                new SuppressChildValidationMetadataProvider(typeof(IPAddress)));
        });
        services.AddAppSwaggerGen();
        services.AddMemoryCache();

        // Create server manager
        services.AddSingleton(typeof(ServerManager));
        services.AddSingleton(typeof(UsageCycleManager));
        services.AddSingleton(typeof(SyncManager));

        // Create TimedHostedService
        services.AddHostedService<TimedHostedService>();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        var _ = env;
        //if (env.IsDevelopment())
        //app.UseDeveloperExceptionPage();

        // Cors must configure before any Authorization to allow token request
        app.UseCors("CorsPolicy");

        // add swagger
        app.UseSwagger();
        app.UseSwaggerUI();

        app.UseRouting();
        app.UseAuthorization();
        app.UseAppExceptionHandler();

        app.UseEndpoints(endpoints =>
        {
            //endpoints.MapControllers();
            endpoints.MapControllerRoute("default", "/api");
        });
    }
}