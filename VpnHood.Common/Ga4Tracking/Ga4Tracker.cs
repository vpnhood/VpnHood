using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace Ga4.Ga4Tracking;

public class Ga4Tracker
{
    private static readonly Lazy<HttpClient> HttpClientLazy = new(() => new HttpClient());
    private static HttpClient HttpClient => HttpClientLazy.Value;

    public required string MeasurementId { get; init; }
    public required string ApiSecret { get; init; }
    public required string SessionId { get; set; }
    public long SessionEngagementTime { get; set; } = 1000;
    public string UserAgent { get; init; } = Environment.OSVersion.ToString().Replace(" ", "");
    public required string ClientId { get; init; }
    public string? UserId { get; init; }
    public bool IsEnabled { get; set; } = true;
    public bool IsDebugEndPoint { get; set; }
    public bool IsAdminDebugView { get; set; }
    public bool IsLogEnabled => IsDebugEndPoint || IsAdminDebugView;
    public Dictionary<string, object> UserParameters { get; } = new();
    public bool? IsMobile { get; init; } // GTag only
    public required string AppName { get; init; }
    public required string AppVersion { get; init; }


    private static string GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Windows";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Linux";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macOS";

        return "Unknown";
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

    private static long _eventSequenceNumber = 1;
    public Task TrackByGTag(Ga4TagParam tagParam)
    {
        if (!IsEnabled) return Task.CompletedTask;
        var isMobile = IsMobile ?? CheckIsMobileByUserAgent(UserAgent);

        // ReSharper disable StringLiteralTypo
        var parameters = new List<(string, object)>
        {
            ("v", 2), // MeasurementId
            ("tid", MeasurementId), // MeasurementId
            ("gtm", Environment.TickCount), // GTM Hash
            ("_p", Environment.TickCount + 10), // finger print, a random number 
            ("cid", ClientId), // Client Id
            ("ul", CultureInfo.CurrentCulture.Name.ToLower()), // user language
            //("sr", "1024x768"), //screen resolution 
            ("uaa", RuntimeInformation.ProcessArchitecture.ToString().ToLower()), //User Agent Architecture
            ("uab", Environment.Is64BitOperatingSystem ? "x64" : "x86"), //User Agent Bits
            ("uafvl", $"{AppName} {AppVersion}"), // Not.A%252FBrand%3B8.0.0.0%7CChromium%3B114.0.5735.134%7CGoogle%2520Chrome%3B114.0.5735.134  // User Agent Full Version List
            ("uamb", isMobile ? 1 : 0), // User Agent Mobile
            ("uam", ""), // User Agent Model. The device model on which the browser is running. Will likely be empty for desktop browsers, Example "Nexus 6"
            ("uap", GetPlatform()), // User Agent Mobile
            ("uapv", Environment.OSVersion.Version.ToString(3)), //User Agent Platform Version, The version of the operating system on which the user agent is running, "14.0.0"
            ("uaw", 0), // User Agent WOW64
            ("_s", _eventSequenceNumber++), // event sequence number
            ("sid", SessionId), // Session Id
            ("sct", 1), // Session Count
            ("seg", 1), // Session Engagement. If the current user is engaged in any way, this value will be 1
            ("en", tagParam.EventName), // event name
            ("_ee", "1"), // External Event
        };

        if (UserId != null) parameters.Add(("uid", UserId));
        if (tagParam.DocumentLocation != null) parameters.Add(("dl", tagParam.DocumentLocation)); // Document Location
        if (tagParam.DocumentTitle != null) parameters.Add(("dt", tagParam.DocumentTitle)); // Document Title
        if (IsAdminDebugView) parameters.Add(("_dbg", 1)); // Analytics debug view

        var url = new Uri(
            "https://www.google-analytics.com/g/collect?" +
            string.Join('&', parameters.Select(x => $"{x.Item1}={x.Item2}"))
            );

        if (IsLogEnabled)
            Console.Out.WriteLine("Sending GTag: " + url);

        return HttpClient.GetStringAsync(url);
    }


    public Task Track(Ga4Event ga4Event)
    {
        var tracks = new[] { ga4Event };
        return Track(tracks);
    }

    public async Task Track(IEnumerable<Ga4Event> ga4Events)
    {
        if (!IsEnabled) return;
        var gaEventArray = ga4Events.Select(x => (Ga4Event)x.Clone()).ToArray();
        if (!gaEventArray.Any()) throw new ArgumentException("Events can not be empty! ", nameof(ga4Events));

        // updating events by default values
        foreach (var ga4Event in gaEventArray)
        {
            if (IsAdminDebugView && !ga4Event.Parameters.TryGetValue("debug_mode", out _))
                ga4Event.Parameters.Add("debug_mode", 1);

            if (!string.IsNullOrEmpty(SessionId) && !ga4Event.Parameters.TryGetValue("session_id", out _))
                ga4Event.Parameters.Add("session_id", SessionId);

            if (SessionEngagementTime != 0 && !ga4Event.Parameters.TryGetValue("engagement_time_msec", out _))
                ga4Event.Parameters.Add("engagement_time_msec", SessionEngagementTime.ToString());
        }


        var ga4Payload = new Ga4Payload
        {
            ClientId = ClientId,
            UserId = UserId,
            Events = gaEventArray,
            UserProperties = UserParameters.Any() ? UserParameters.ToDictionary(p => p.Key, p => new Ga4Payload.UserProperty { Value = p.Value }) : null,
        };

        var baseUri = IsDebugEndPoint ? new Uri("https://www.google-analytics.com/debug/mp/collect") : new Uri("https://www.google-analytics.com/mp/collect");
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"?api_secret={ApiSecret}&measurement_id={MeasurementId}"));
        requestMessage.Headers.Add("User-Agent", UserAgent);
        requestMessage.Content = new StringContent(JsonSerializer.Serialize(ga4Payload));
        requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            var res = await HttpClient.SendAsync(requestMessage);
            if (IsLogEnabled)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(ga4Payload, new JsonSerializerOptions { WriteIndented = true }));
                await Console.Out.WriteLineAsync(await res.Content.ReadAsStringAsync());
            }
        }
        catch
        {
            if (IsDebugEndPoint)
                throw;
        }

    }
}