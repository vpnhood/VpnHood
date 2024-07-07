using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace Ga4.Ga4Tracking;

public abstract class Ga4TrackerBase : ITracker
{

}

public class Ga4MeasurementTracker : Ga4TrackerBase
{

}

public class Ga4TagTracker : Ga4TrackerBase
{

}



public class Ga4Tracker : IGa4TagTracker, IGa4MeasurementTracker
{
    private static readonly Lazy<HttpClient> HttpClientLazy = new(() => new HttpClient());
    private static HttpClient HttpClient => HttpClientLazy.Value;

    public required string MeasurementId { get; init; }
    public required string ApiSecret { get; init; }
    public required string SessionId { get; set; }
    public required int SessionCount { get; set; } = 1;
    public string UserAgent { get; set; } = Environment.OSVersion.ToString().Replace(" ", "");
    public required string ClientId { get; init; }
    public string? UserId { get; init; }
    public bool IsEnabled { get; set; } = true;
    public bool IsDebugEndPoint { get; set; }
    public bool IsAdminDebugView { get; set; }
    public bool IsLogEnabled => IsDebugEndPoint || IsAdminDebugView;
    public bool? IsMobile { get; init; } // GTag only
    public ILogger? Logger { get; set; }
    public EventId LoggerEventId { get; set; } = new(0, "Ga4Tracker");



    // private static string GetPlatform()
    // {
    //    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    //        return "Windows";
    //
    //    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    //        return "Linux";
    //
    //    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    //        return "macOS";
    //
    //    return "Unknown";
    // }

    public void UseSimpleLogger(bool singleLine = false)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(configure =>
            {
                // ReSharper disable once StringLiteralTypo
                configure.TimestampFormat = "[HH:mm:ss.ffff] ";
                configure.IncludeScopes = false;
                configure.SingleLine = singleLine;
            });
        });

        Logger = loggerFactory.CreateLogger("");
    }

    private void PrepareHttpHeaders(HttpHeaders httpHeaders)
    {
        httpHeaders.Add("User-Agent", UserAgent);
        //requestMessage.Headers.Add("Sec-Ch-Ua", "\"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"114\", \"Microsoft Edge\";v=\"114\"");
        //httpHeaders.Add("Sec-Ch-Ua-Mobile", "1");
        //httpHeaders.Add("Sec-Ch-Ua-Platform", "\"Android\"");
    }

    private static bool CheckIsMobileByUserAgent(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return false;

        // check isMobile from agent
        var mobileRegex = new Regex(
            @"(android|bb\d+|meego).+mobile|avantgo|bada\/|blackberry|blazer|compal|elaine|fennec|hiptop|iemobile|ip(hone|od)|iris|kindle|lge |maemo|midp|mmp|mobile.+firefox|netfront|opera m(ob|in)i|palm( os)?|phone|p(ixi|re)\/|plucker|pocket|psp|series(4|6)0|symbian|treo|up\.(browser|link)|vodafone|wap|windows ce|xda|xiino",
            RegexOptions.IgnoreCase);

        var isMobile = !string.IsNullOrEmpty(userAgent) && mobileRegex.IsMatch(userAgent);
        return isMobile;
    }

    public Task Track(Ga4TagEvent ga4Event, Dictionary<string, object>? userProperties = null)
    {
        if (!IsEnabled) return Task.CompletedTask;
        var isMobile = IsMobile ?? CheckIsMobileByUserAgent(UserAgent);
        userProperties ??= new Dictionary<string, object>();

        // ReSharper disable StringLiteralTypo
        var parameters = new List<(string, object)>
        {
            ("v", 2), // MeasurementId
            ("tid", MeasurementId), // MeasurementId
            ("gtm", Environment.TickCount), // GTM Hash
            ("_p", Environment.TickCount + 10), // fingerprint, a random number 
            ("cid", ClientId), // Client Id

            // client device info, user agent info
            ("ul", CultureInfo.CurrentCulture.Name.ToLower() ), // user language
            // ("sr", "1024x768"),  *Finalized // screen resolution        
            ("uaa", RuntimeInformation.ProcessArchitecture.ToString().ToLower()), //User Agent Architecture
            ("uab", Environment.Is64BitOperatingSystem ? "64" : "32"), //User Agent Bits
            ("uamb",isMobile ? 1 : 0), // User Agent Mobile
            // ("uam", "MyModel"), *Finalized // User Agent Model. The device model on which the browser is running. Will likely be empty for desktop browsers, Example "Nexus 6"
            // ("uap", "Android"), *Finalized //GetPlatform() ), // User Agent Platform, 	Android, macOS
            ("uapv", Environment.OSVersion.Version.ToString(3)), //User Agent Platform Version, The version of the operating system on which the user agent is running, "14.0.0"
            // ("uaw", 0), *Finalized // User Agent WOW64

            // "Not.A%2FBrand;8.0.0.0|Chromium;114.0.5735.199|Google%20Chrome;114.0.5735.199" 
            // "Not.A%2FBrand;8.0.0.0|Chromium;114.0.5735.201|Microsoft%20Edge;114.0.1823.67"
            // ReSharper disable once CommentTypo
            // ("uafvl", "Not.A%2FBrand;8.0.0.0|Chromium;114.0.5735.201|Microsoft%20Edge;114.0.1823.67"),  *Finalized  // User Agent Full Version List
                                  
            // session
            ("sid", SessionId), // GA4 Session id. This comes from the GA4 Cookie. It may be different for each Stream ID Configured on the site
            ("sct", SessionCount), //Count of sessions. This value increases by one each time a new session is detected ( when the session expires )
            ("seg", 1), // Required.  Session Engagement. If the current user is engaged in any way, this value will be 1
            ("_s", 1), // Hit Counter. Current hits counter for the current page load

            // event
            ("en", ga4Event.EventName), // event name
            ("_ee", "1") // External Event

            // Reference: https://www.thyngster.com/ga4-measurement-protocol-cheatsheet/
            //("up.foo_string", "1.1"), //User Property String
            //("upn.foo-number", 1), //User Property Number
            //("ep.foo-string", "hello"), //Event Property string 
            //("epn.foo-number", 1), //Event Property Number
            
        };

        if (UserId != null) parameters.Add(("uid", UserId));
        if (ga4Event.DocumentLocation != null) parameters.Add(("dl", ga4Event.DocumentLocation)); // Document Location, Actual page's Pathname. It does not include the hostname, query String or Fragment. document.location.pathname. eg: "https://localhost"
        if (ga4Event.DocumentTitle != null) parameters.Add(("dt", ga4Event.DocumentTitle)); // Document Title, Actual page's Title, document.title. eg: "My page title"
        if (ga4Event.DocumentReferrer != null) parameters.Add(("dr", ga4Event.DocumentReferrer)); // Document Referrer, Actual page's Referrer, document.referrer. eg: "https://www.google.com"
        if (ga4Event.IsFirstVisit) parameters.Add(("_fv", 1)); // first visit,  if the "_ga_THYNGSTER" cookie is not set, the first event will have this value present. This will internally create a new "first_visit" event on GA4. If this event is also a conversion the value will be "2" if not, will be "1"

        // It's the total engagement time in milliseconds since the last event.
        // The engagement time is measured only when the current page is visible and active ( ie: the browser window/tab must be active and visible ),
        // for this GA4 uses the window.events: focus, blur, page_show, page_hide and the document:visibility change, these will determine when the timer starts and pauses
        if (ga4Event.EngagementTime != null) parameters.Add(("_et ", ga4Event.EngagementTime));
        if (IsAdminDebugView) parameters.Add(("_dbg", 1)); // Analytics debug view

        // add user Properties
        foreach (var userProperty in userProperties)
        {
            var propName = userProperty.Value is int or long ? "upn" : "up";
            propName += "." + userProperty.Key;
            parameters.Add((propName, userProperty.Value));
        }

        // add Event Properties
        foreach (var eventProperties in ga4Event.Properties)
        {
            var propName = eventProperties.Value is int or long ? "epn" : "ep";
            propName += "." + eventProperties.Key;
            parameters.Add((propName, eventProperties.Value));
        }

        var url = new Uri(
            "https://www.google-analytics.com/g/collect?" +
            string.Join('&', parameters.Select(x => $"{x.Item1}={x.Item2}"))
            );

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        PrepareHttpHeaders(requestMessage.Headers);
        return SendHttpRequest(requestMessage, "GTag");
    }

    public async Task Track(IEnumerable<Ga4TagEvent> ga4Events, Dictionary<string, object>? userProperties = null)
    {
        foreach (var ga4TagEvent in ga4Events)
            await Track(ga4TagEvent, userProperties).ConfigureAwait(false);
    }

    public Task Track(Ga4MeasurementEvent ga4Event)
    {
        var tracks = new[] { ga4Event };
        return Track(tracks);
    }

    public Task Track(IEnumerable<Ga4MeasurementEvent> ga4Events, Dictionary<string, object>? userProperties = null)
    {
        if (!IsEnabled) return Task.CompletedTask;
        var gaEventArray = ga4Events.Select(x => (Ga4MeasurementEvent)x.Clone()).ToArray();
        if (!gaEventArray.Any()) throw new ArgumentException("Events can not be empty! ", nameof(ga4Events));

        // updating events by default values
        foreach (var ga4Event in gaEventArray)
        {
            if (IsAdminDebugView && !ga4Event.Parameters.TryGetValue("debug_mode", out _))
                ga4Event.Parameters.Add("debug_mode", 1);

            if (!string.IsNullOrEmpty(SessionId) && !ga4Event.Parameters.TryGetValue("session_id", out _))
                ga4Event.Parameters.Add("session_id", SessionId);
        }


        var ga4Payload = new Ga4MeasurementPayload
        {
            ClientId = ClientId,
            UserId = UserId,
            Events = gaEventArray,
            UserProperties = userProperties != null && userProperties.Any() ? userProperties.ToDictionary(p => p.Key, p => new Ga4MeasurementPayload.UserProperty { Value = p.Value }) : null
        };

        var baseUri = IsDebugEndPoint ? new Uri("https://www.google-analytics.com/debug/mp/collect") : new Uri("https://www.google-analytics.com/mp/collect");
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"?api_secret={ApiSecret}&measurement_id={MeasurementId}"));
        PrepareHttpHeaders(requestMessage.Headers);
        return SendHttpRequest(requestMessage, "Measurement", ga4Payload);
    }

    public virtual Task Track(IEnumerable<TrackEvent> trackEvents, Dictionary<string, object>? userProperties = null)
    {
        return UseGa4TagAsDefault 
            ? TrackByTag(trackEvents, userProperties) 
            : TrackByMeasurement(trackEvents, userProperties);
    }

    private Task TrackByTag(IEnumerable<TrackEvent> trackEvents, Dictionary<string, object>? userProperties = null)
    {
        var ga4TagEvents = trackEvents.Select(x =>
            new Ga4TagEvent
            {
                EventName = x.EventName,
                Properties = x.Parameters
            });

        return Track(ga4TagEvents, userProperties);
    }

    private Task TrackByMeasurement(IEnumerable<TrackEvent> trackEvents, Dictionary<string, object>? userProperties = null)
    {
        var ga4MeasurementEvents = trackEvents.Select(x =>
            new Ga4MeasurementEvent
            {
                EventName = x.EventName,
                Parameters = x.Parameters
            });

        return Track(ga4MeasurementEvents, userProperties);
    }


    public Task TrackErrorByTag(string action, string msg)
    {
        return Track(new Ga4TagEvent
        {
            EventName = "exception",
            Properties = new Dictionary<string, object>
            {
                {"page_location", "ex/" + action},
                {"page_title", msg}
            }
        });
    }

    private async Task SendHttpRequest(HttpRequestMessage requestMessage, string name, object? jsonData = null)
    {
        try
        {
            if (IsLogEnabled)
            {
                Logger?.LogInformation(LoggerEventId,
                    "Sending Ga4Track: {name}, Url: {Url},  Headers: {Headers}",
                    name, requestMessage.RequestUri, JsonSerializer.Serialize(requestMessage.Headers, new JsonSerializerOptions { WriteIndented = true }));
            }

            if (jsonData != null)
            {
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(jsonData));
                requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                if (IsLogEnabled)
                {
                    var data = JsonSerializer.Serialize(jsonData, new JsonSerializerOptions { WriteIndented = true });
                    Logger?.LogInformation(LoggerEventId, "Ga4Track Data: {Data}", data);
                }
            }

            var res = await HttpClient.SendAsync(requestMessage).ConfigureAwait(false);
            if (IsLogEnabled)
            {
                var result = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                Logger?.LogInformation(LoggerEventId, "Ga4Track Result: {Rrsult}", result);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(LoggerEventId, ex, "Ga4Track could not send its track");
            if (IsDebugEndPoint)
                throw;
        }
    }
}