using Ga4.Trackers.Ga4Measurements;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;

namespace VpnHood.Test.Tests;

// Each track here is canceled before it is sent: its URL is logged first, and nothing leaves the machine.
[TestClass]
public class Ga4TrackerTest
{
    [TestMethod]
    public async Task The_tag_tracker_encodes_its_names_and_values()
    {
        var logger = new ListLogger();
        var tracker = new Ga4TagTracker {
            MeasurementId = "G-TEST",
            SessionId = "7",
            ClientId = "client 1",
            SessionCount = 1,
            Logger = logger,
            UserProperties = new Dictionary<string, object> { { "plan", "a&b=c" } }
        };

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await tracker.Track(new Ga4TagEvent {
            EventName = "page view",
            DocumentTitle = "Tom & Jerry #1",
            EngagementTime = 1500,
            Properties = new Dictionary<string, object?> { { "ratio", 1.5 } }
        }, canceled.Token);

        var url = logger.Messages.Single(x => x.Contains("Sending Ga4Track"));
        StringAssert.Contains(url, "&_et=1500");
        StringAssert.Contains(url, "&en=page%20view&");
        StringAssert.Contains(url, "&dt=Tom%20%26%20Jerry%20%231");
        StringAssert.Contains(url, "&cid=client%201&");
        StringAssert.Contains(url, "&up.plan=a%26b%3Dc");
        StringAssert.Contains(url, "&ep.ratio=1.5");
    }

    [TestMethod]
    public async Task The_measurement_trackers_secret_is_not_logged()
    {
        const string secret = "top-secret-value";
        var logger = new ListLogger();
        var tracker = new Ga4MeasurementTracker {
            MeasurementId = "G-TEST",
            SessionId = "7",
            ClientId = "client1",
            ApiSecret = secret,
            Logger = logger
        };

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await tracker.Track(new Ga4MeasurementEvent { EventName = "test" }, canceled.Token);

        Assert.IsTrue(logger.Messages.Any(x => x.Contains("api_secret=***&measurement_id=G-TEST")));
        Assert.IsFalse(logger.Messages.Any(x => x.Contains(secret)));
    }

    // keeps each line as its logger would write it, its exception with it
    private sealed class ListLogger : ILogger
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages {
            get {
                lock (_messages)
                    return _messages.ToArray();
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
                _messages.Add(formatter(state, exception) + exception);
        }
    }
}
