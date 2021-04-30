using System;
using System.Text.Json.Serialization;

namespace VpnHood.Client.App
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AppFiltersMode
    {
        All,
        Exclude,
        Include
    }

    public class AppUserSettings
    {
        public bool LogToFile { get; set; } = false;
        public bool LogVerbose { get; set; } = true;
        public string CultureName { get; set; } = "en";
        public Guid? DefaultClientProfileId { get; set; }
        public int MaxReconnectCount { get; set; } = 3;
        public int IsDebugMode { get; set; } = 0;
        public string[] IncludeNetworks { get; set; } = Array.Empty<string>();
        public string[] ExcludeNetworks { get; set; } = Array.Empty<string>();
        public string[] AppFilters { get; set; } = Array.Empty<string>();
        public AppFiltersMode AppFiltersMode { get; set; } = AppFiltersMode.All;

}
}
