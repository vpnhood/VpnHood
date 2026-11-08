using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;


// ReSharper disable once CheckNamespace
namespace Ga4.Trackers;

/// <summary>
/// Provides the base of the Google Analytics 4 trackers: their IDs, their switch and logging, and the HTTP send that
/// they share.
/// </summary>
public abstract class TrackerBase : ITracker
{
    private static readonly Lazy<HttpClient> HttpClientLazy = new(() => new HttpClient());
    private static HttpClient HttpClient => HttpClientLazy.Value;
    /// <summary>Gets the Google Analytics 4 measurement ID, such as <c>G-XXXXXXXXXX</c>.</summary>
    public required string MeasurementId { get; init; }

    /// <summary>Gets or sets the session's ID.</summary>
    public required string SessionId { get; set; }

    /// <summary>Gets the ID of the client, the device or install.</summary>
    public required string ClientId { get; init; }

    /// <summary>Gets the ID of the signed-in user, or <see langword="null"/> for none.</summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Gets or sets the User-Agent header the requests send. The default is the OS version, without spaces.
    /// </summary>
    public string UserAgent { get; set; } = Environment.OSVersion.ToString().Replace(" ", "");

    /// <inheritdoc/>
    /// <remarks>The default is <see langword="true"/>.</remarks>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the events show in Google Analytics' DebugView.
    /// </summary>
    public bool IsAdminDebugView { get; set; }

    /// <summary>Gets or sets the logger of the requests and their failures, or <see langword="null"/> for none.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Gets or sets the event ID the log lines carry. The default is <c>Ga4Tracker</c>.</summary>
    public EventId LoggerEventId { get; set; } = new(0, "Ga4Tracker");

    /// <summary>
    /// Gets or sets a value indicating whether a failed request throws, after it is logged; otherwise, it is only
    /// logged.
    /// </summary>
    public bool ThrowExceptionOnError { get; set; }

    /// <summary>
    /// Gets or sets how long a request may take: its token is canceled after it. The default is 10 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets the user properties sent with the events. The default is none.</summary>
    public Dictionary<string, object> UserProperties { get; set; } = new();

    /// <inheritdoc/>
    public abstract Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken);

    /// <inheritdoc/>
    /// <exception cref="Exception">The request failed, with <see cref="TrackerBase.ThrowExceptionOnError"/>.</exception>
    public Task Track(TrackEvent trackEvent, CancellationToken cancellationToken) =>
        Track([trackEvent], cancellationToken);

    /// <summary>
    /// Sends an exception as an <c>exception</c> event, the action in its page location (<c>ex/action</c>) and the message
    /// in its page title.
    /// </summary>
    /// <param name="action">What was being done.</param>
    /// <param name="ex">The exception.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="Exception">The request failed, with <see cref="TrackerBase.ThrowExceptionOnError"/>.</exception>
    public Task TrackError(string action, Exception ex, CancellationToken cancellationToken)
    {
        var trackEvent = new TrackEvent {
            EventName = "exception",
            Parameters = new Dictionary<string, object?> {
                { "page_location", "ex/" + action },
                { "page_title", ex.Message }
            }
        };

        return Track([trackEvent], cancellationToken);
    }

    /// <summary>Adds the tracker's headers to a request: its User-Agent.</summary>
    /// <param name="httpHeaders">The request's headers.</param>
    protected void PrepareHttpHeaders(HttpHeaders httpHeaders)
    {
        httpHeaders.Add("User-Agent", UserAgent);
        //requestMessage.Headers.Add("Sec-Ch-Ua", "\"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"114\", \"Microsoft Edge\";v=\"114\"");
        //httpHeaders.Add("Sec-Ch-Ua-Mobile", "1");
        //httpHeaders.Add("Sec-Ch-Ua-Platform", "\"Android\"");
    }

    /// <summary>
    /// Sends a request, with a body of JSON when there is one, and logs it with its answer; the request's token is
    /// canceled after <see cref="RequestTimeout"/>. Off, it sends nothing.
    /// </summary>
    /// <remarks>
    /// A failure is logged, and thrown only with <see cref="ThrowExceptionOnError"/>. The answer's status is not checked.
    /// </remarks>
    /// <param name="requestMessage">The request.</param>
    /// <param name="name">The request's name, for the log.</param>
    /// <param name="jsonData">The body, sent as JSON, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="Exception">The request failed, with <see cref="ThrowExceptionOnError"/>.</exception>
    protected async Task SendHttpRequest(HttpRequestMessage requestMessage, string name, object? jsonData,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return;

        try {
            using var timeoutCts = new CancellationTokenSource(RequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            // log
            Logger?.LogInformation(LoggerEventId,
                "Sending Ga4Track: {name}, Url: {Url},  Headers: {Headers}",
                name, requestMessage.RequestUri,
                JsonSerializer.Serialize(requestMessage.Headers, new JsonSerializerOptions { WriteIndented = true }));

            if (jsonData != null) {
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(jsonData));
                requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                // log
                var data = JsonSerializer.Serialize(jsonData, new JsonSerializerOptions { WriteIndented = true });
                Logger?.LogInformation(LoggerEventId, "Ga4Track Data: {Data}", data);
            }

            var res = await HttpClient.SendAsync(requestMessage, linkedCts.Token).Vhc();

            // log
            var result = await res.Content.ReadAsStringAsync(linkedCts.Token).Vhc();
            Logger?.LogInformation(LoggerEventId, "Ga4Track Result: {Result}", result);
        }
        catch (Exception ex) {
            Logger?.LogError(LoggerEventId, ex, "Ga4Track could not send its track");
            if (ThrowExceptionOnError)
                throw;
        }
    }

    /// <summary>Sets <see cref="Logger"/> to a simple console logger, for a tool or a test.</summary>
    /// <param name="singleLine"><see langword="true"/> to write each entry on one line.</param>
    public void UseSimpleLogger(bool singleLine = false)
    {
        using var loggerFactory = LoggerFactory.Create(builder => {
            builder.AddSimpleConsole(configure => {
                // ReSharper disable once StringLiteralTypo
                configure.TimestampFormat = "[HH:mm:ss.ffff] ";
                configure.IncludeScopes = false;
                configure.SingleLine = singleLine;
            });
        });

        Logger = loggerFactory.CreateLogger("");
    }
}