using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>The platform's restore-key double: hands out a fixed assertion, or none.</summary>
internal class TestRestoreCredentialProvider(string? assertionResponseJson) : IRestoreCredentialProvider
{
    public Task<string> Create(IUiContext? uiContext, string requestJson, CancellationToken cancellationToken)
    {
        return Task.FromResult("{}");
    }

    public Task<string?> TryGet(IUiContext? uiContext, string requestJson, CancellationToken cancellationToken)
    {
        return Task.FromResult(assertionResponseJson);
    }

    public Task Clear(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
