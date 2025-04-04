﻿namespace VpnHood.Core.Client.DomainFiltering;

public class DomainFilterResult
{
    public DomainFilterAction Action { get; init; }
    public string? DomainName { get; init; }
    public byte[] ReadData { get; init; } = [];
}