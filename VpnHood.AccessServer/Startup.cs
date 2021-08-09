using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using VpnHood.AccessServer.Auth;

namespace VpnHood.AccessServer
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            //read settings
            App.Configure(Configuration);

            //enable cross-origin; MUST before anything
            services.AddCors(o => o.AddPolicy("CorsPolicy", builder =>
            {
                builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .SetPreflightMaxAge(TimeSpan.FromHours(24 * 30));
            }));

            // Add framework services.
            if (App.AuthProviderItems.Length > 0)
                services.AddAppAuthentication(App.AuthProviderItems);

            services.AddControllers();
            services.AddAppSwaggerGen();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var _ = env;
            //if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            // Cors must configure before any Authorization to allow token request
            app.UseCors("CorsPolicy");

            // add swagger
            app.UseAppSwagger();

            //before UseAuthentication
            if (App.AuthProviderItems.Length > 0)
                app.UseAppAuthentication(App.AuthProviderItems);

            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                //endpoints.MapControllers();
                endpoints.MapControllerRoute("default", "/api");
            });
        }
    }
}
