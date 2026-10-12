using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>A window that goes to the background and comes back to the front.</summary>
internal class TestWindowUiContext : IUiContext
{
    public bool IsInFront { get; set; } = true;

    public Task<bool> IsActive() => Task.FromResult(IsInFront);
    public Task<bool> IsDestroyed() => Task.FromResult(false);
}
