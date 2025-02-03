﻿namespace VpnHood.Core.Client.Abstractions;

public class DomainFilter
{
    public string[] Blocks { get; set; } = [];
    public string[] Excludes { get; set; } = [];
    public string[] Includes { get; set; } = [];
}