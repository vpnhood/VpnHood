using VpnHood.AppLib.Api.UiAttachments;

namespace VpnHood.AppUi.Hosting.Cli.Internal;

// Names this window's attachment on every request it sends the daemon, once it has one, so what a
// request starts - a sign-in, a purchase - is carried out by this window (UiAttachment.HeaderName).
internal sealed class UiAttachmentHandler() : DelegatingHandler(new HttpClientHandler())
{
    private volatile string? _attachmentId;

    public string? AttachmentId {
        get => _attachmentId;
        set => _attachmentId = value;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_attachmentId is { } attachmentId)
            request.Headers.TryAddWithoutValidation(UiAttachment.HeaderName, attachmentId);

        return base.SendAsync(request, cancellationToken);
    }
}
