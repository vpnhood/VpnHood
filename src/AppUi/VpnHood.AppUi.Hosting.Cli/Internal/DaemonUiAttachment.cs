using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.UiAttachments;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppUi.Hosting.Cli.Internal;

// The window's side of its attachment to the daemon. It attaches before the UI runs, so every request
// the UI makes names this window; then it holds the long poll and carries out each action the daemon
// asks of a UI as the person logged in here - their browser, the window they look at - through the
// UI's own context in this process (AppUiContext), and answers. A daemon that restarted no longer
// knows the window, which attaches again; disposing detaches it, failing what it has not answered.
internal sealed class DaemonUiAttachment : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private readonly DaemonConnection _connection;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _loop;
    private string _attachmentId;

    private DaemonUiAttachment(DaemonConnection connection, string attachmentId)
    {
        _connection = connection;
        _attachmentId = attachmentId;
        _loop = Task.Run(RunLoop);
    }

    public static async Task<DaemonUiAttachment> Attach(DaemonConnection connection,
        CancellationToken cancellationToken)
    {
        var attachment = await connection.UiAttachments.Attach(cancellationToken).Vhc();
        connection.UiAttachmentId = attachment.AttachmentId;
        return new DaemonUiAttachment(connection, attachment.AttachmentId);
    }

    private async Task RunLoop()
    {
        var cancellationToken = _cancellation.Token;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var action = await _connection.UiAttachments.WaitForAction(_attachmentId, cancellationToken).Vhc();
                if (action != null)
                    await Answer(action, cancellationToken).Vhc();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return;
            }
            catch (Exception ex) when (NotExistsException.Is(ex)) {
                // the daemon restarted and no longer knows this window
                await TryAttachAgain(cancellationToken).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex, "The window's attachment poll failed. Retrying...");
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task TryAttachAgain(CancellationToken cancellationToken)
    {
        try {
            var attachment = await _connection.UiAttachments.Attach(cancellationToken).Vhc();
            _attachmentId = attachment.AttachmentId;
            _connection.UiAttachmentId = attachment.AttachmentId;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            VhLogger.Instance.LogDebug(ex, "Could not attach the window again. Retrying...");
            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task Answer(UiAction action, CancellationToken cancellationToken)
    {
        string? errorMessage = null;
        try {
            var uiContext = AppUiContext.Context ?? throw new UiContextNotAvailableException();
            switch (action.Type) {
                case UiActionType.OpenUrl:
                    var url = action.Url ?? throw new ArgumentException("The daemon asked to open no address.");
                    await uiContext.OpenUrl(url, cancellationToken).Vhc();
                    break;

                case UiActionType.BringToFront:
                    await uiContext.BringToFront(cancellationToken).Vhc();
                    break;

                default:
                    throw new NotSupportedException($"This window does not know the action {action.Type}.");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            errorMessage = ex.Message;
        }

        await _connection.UiAttachments.SetActionResult(_attachmentId, action.ActionId,
            new UiActionResult { ErrorMessage = errorMessage }, cancellationToken).Vhc();
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().Vhc();
        await _loop.Vhc();
        _connection.UiAttachmentId = null;
        await VhUtils.TryInvokeAsync("Detach the window from the daemon",
            () => _connection.UiAttachments.Detach(_attachmentId, CancellationToken.None)).Vhc();
        _cancellation.Dispose();
    }
}
