using System;
using System.IO;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class AppOptions
{
    public string AppDataFolderPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood");
    public TimeSpan SessionTimeout { get; set; } = new ClientOptions().SessionTimeout;
    public SocketFactory? SocketFactory { get; set; }
    public TimeSpan UpdateCheckerInterval { get; set; } = TimeSpan.FromHours(6);
    public bool LoadCountryIpGroups { get; set; } = true;
    // ReSharper disable once StringLiteralTypo
    public string? AppGa4MeasurementId { get; set; } = "G-4LE99XKZYE";
    public AppResources Resources { get; set; } = new();
}