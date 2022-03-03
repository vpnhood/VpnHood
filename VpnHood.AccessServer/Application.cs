using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer;

public class Application
{
    private readonly IConfiguration _configuration;
    public const string Name = "Authorization Server";
    public const string AuthIssuer = "auth.vpnhood.com";
    public const string AuthAudience = "access.vpnhood.com";
    public const string AuthRobotScheme = "Robot";
    public ILogger<Application> Logger { get; }
    public bool AutoMaintenance { get; set; }
    public byte[] AuthenticationKey => GetAuthenticationKey(_configuration);
    public static byte[] GetAuthenticationKey(IConfiguration configuration) => Convert.FromBase64String(configuration.GetValue<string>("AuthenticationKey"));
    public Uri AgentUri => new(_configuration.GetValue("AgentUrl", ""));
    
    public Application(ILogger<Application> logger, IConfiguration configuration)
    {
        _configuration = configuration;
        Logger = logger;
    }
}