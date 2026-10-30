using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.ProbeResistance;

namespace VpnHood.Test.KestrelParity.Servers;

// What our server's answers to strangers copy: ASP.NET Core's Kestrel, HTTP/1.1 only, JWT bearer with its error
// details hidden and a fallback authorization policy, so 401 on every path, as the webapi template sets it up,
// and HTTPS redirection on the plain port. The redirect names our server's TLS port, so both send one Location.
internal sealed class ReferenceServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public IPEndPoint TlsEndPoint { get; }
    public IPEndPoint HttpEndPoint { get; }

    // the ASP.NET Core build the replies come from, by its product version, as the assembly's is 11.0.0.0 for every
    // build of 11.0; the probes' results hold for this build
    public static string Version => typeof(KestrelServerOptions).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private ReferenceServer(WebApplication app, IPEndPoint tlsEndPoint, IPEndPoint httpEndPoint)
    {
        _app = app;
        TlsEndPoint = tlsEndPoint;
        HttpEndPoint = httpEndPoint;
    }

    public static async Task<ReferenceServer> Start(X509Certificate2 certificate, int redirectPort, byte[] farmSecret,
        CancellationToken cancellationToken)
    {
        var tlsEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var httpEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => {
            kestrel.Listen(tlsEndPoint, listen => {
                listen.Protocols = HttpProtocols.Http1;
                listen.UseHttps(certificate);
            });
            kestrel.Listen(httpEndPoint, listen => listen.Protocols = HttpProtocols.Http1);
        });

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => {
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters {
                    IssuerSigningKey = new SymmetricSecurityKey(RequestProof.DeriveSigningKey(farmSecret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = RequestProof.ClockTolerance,
                    ValidateIssuer = false,
                    ValidateAudience = false
                };
            });
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = redirectPort);

        var app = builder.Build();
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/api/status", () => "ok");
        try {
            await app.StartAsync(cancellationToken);
        }
        catch {
            await app.DisposeAsync();
            throw;
        }

        return new ReferenceServer(app, tlsEndPoint, httpEndPoint);
    }

    public async ValueTask DisposeAsync()
    {
        // the app goes even when it doesn't stop in time
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try {
            await _app.StopAsync(stopCts.Token);
        }
        finally {
            await _app.DisposeAsync();
        }
    }
}
