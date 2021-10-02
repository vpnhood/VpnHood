using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using MudBlazor.Services;
using Blazored.LocalStorage;

namespace VpnHood.AccessServer.UI
{
    public class Program
    {
        public static string AuthScope { get; private set; } = null!;
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.Services.AddLocalization();

            builder.RootComponents.Add<App>("#app");
            AuthScope = builder.Configuration["AuthScope"] ?? throw new InvalidOperationException($"{nameof(AuthScope)} is not set in appsettings.json");

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

            builder.Services.AddMudServices();
            builder.Services.AddBlazoredLocalStorage();

            await builder.Build().RunAsync();
        }
    }
}
