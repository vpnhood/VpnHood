using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VpnHood.App.Launcher
{
    public class UpdaterOptions
    {
        public int CheckIntervalMinutes { get; set; } = 1 * (24 * 60);
        public ILogger Logger { get; set; } = NullLogger.Instance;
    }
}
