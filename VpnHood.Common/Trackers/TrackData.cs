using System.Net;

namespace VpnHood.Common.Trackers;

public class TrackData
{
    public TrackData(string category, string action, string? label = null, long? value = null)
    {
        Type = "event";
        Category = category;
        Action = action;
        Label = label;
        Value = value;
    }

    public TrackData(string pageUrl, string? pageTitle = null)
    {
        Type = "pageview";
        PageUrl = pageUrl;
        PageTitle = pageTitle;
    }

    public string Type { get; }
    public string? Category { get; }
    public string? Action { get; }
    public string? Label { get; set; }
    public long? Value { get; set; }
    public string? PageUrl { get; }
    public string? PageTitle { get; set; }
}