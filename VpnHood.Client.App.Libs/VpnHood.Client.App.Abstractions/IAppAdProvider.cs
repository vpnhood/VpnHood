﻿using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppAdProvider : IDisposable
{
    string NetworkName { get; }
    AppAdType AdType { get; }
    Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken);
    Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken);
    DateTime? AdLoadedTime { get; }
    TimeSpan AdLifeSpan { get; }
}