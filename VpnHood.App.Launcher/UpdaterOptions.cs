using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VpnHood.App.Launcher
{
    public class UpdaterOptions
    {
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(12);
        public ILogger Logger { get; set; } = NullLogger.Instance;
    }
}