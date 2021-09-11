using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace VpnHood.AccessServer.UI
{
    public class CustomAuthorizationMessageHandler : AuthorizationMessageHandler
    {
        public CustomAuthorizationMessageHandler(IAccessTokenProvider provider, NavigationManager navigationManager)
            : base(provider, navigationManager)
        {
            ConfigureHandler(
                scopes: new[] { Program.AuthScope },
                authorizedUrls: new[] { "https://localhost:5001/" }
            );
        }
    }

    public class Program
    {
        public static string AuthScope { get; private set; }
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            AuthScope = builder.Configuration["AuthScope"];

            builder.Services.AddScoped<CustomAuthorizationMessageHandler>();
            builder.Services.AddHttpClient("AccessServer.Api", client => client.BaseAddress = new Uri("https://localhost:5001/"))
                .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

            // Supply HttpClient instances that include access tokens when making requests to the server project
            builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("AccessServer.Api"));

            builder.Services.AddMsalAuthentication(options =>
            {
                builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
                options.ProviderOptions.DefaultAccessTokenScopes.Add(AuthScope);
            });

            await builder.Build().RunAsync();
        }
    }
}
