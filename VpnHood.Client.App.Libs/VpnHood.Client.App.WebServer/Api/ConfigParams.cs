﻿namespace VpnHood.Client.App.WebServer.Api;

public class ConfigParams
{
    public string[] AvailableCultures { get; init; } = [];
    public AppResource.AppStrings? Strings { get; init; }
}