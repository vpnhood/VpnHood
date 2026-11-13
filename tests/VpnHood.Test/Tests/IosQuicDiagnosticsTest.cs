using Microsoft.Extensions.Logging;
using VpnHood.Net.Quic.Ios;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Test.Tests;

// The iOS QUIC client's counters, compiled into this project from the iOS library's source (see the project file):
// they count only while the log level is below Information, and the level may change while streams are open.
[TestClass]
[DoNotParallelize] // sets the process's log level; the counters are the process's too
public class IosQuicDiagnosticsTest : TestBase
{
    [TestMethod]
    public void What_began_uncounted_takes_nothing_off_when_it_ends_counted()
    {
        var logLevel = VhLogger.MinLogLevel;
        try {
            // a stream and a connection open, a send and a teardown begin, while the counters are off
            VhLogger.MinLogLevel = LogLevel.Information;
            IosQuicDiagnostics.TakeMaxStreamCancelMs();
            var liveStreams = IosQuicDiagnostics.LiveStreamCount;
            var liveConnections = IosQuicDiagnostics.LiveConnectionCount;
            var outstandingSendBytes = IosQuicDiagnostics.OutstandingSendBytes;
            var streamId = IosQuicDiagnostics.OnStreamOpened();
            var connectionId = IosQuicDiagnostics.OnConnectionOpened();
            var countedSendBytes = IosQuicDiagnostics.AddOutstandingSend(1000);
            var teardownStart = IosQuicDiagnostics.BeginTiming();

            // they end with the counters on: nothing was counted, so nothing comes off, and no teardown is timed
            // from the device's start
            VhLogger.MinLogLevel = LogLevel.Debug;
            IosQuicDiagnostics.OnStreamClosed(streamId);
            IosQuicDiagnostics.OnConnectionClosed(connectionId);
            IosQuicDiagnostics.SubtractOutstandingSend(countedSendBytes);
            IosQuicDiagnostics.EndStreamTeardown(teardownStart);
            Assert.AreEqual(liveStreams, IosQuicDiagnostics.LiveStreamCount);
            Assert.AreEqual(liveConnections, IosQuicDiagnostics.LiveConnectionCount);
            Assert.AreEqual(outstandingSendBytes, IosQuicDiagnostics.OutstandingSendBytes);
            Assert.AreEqual(0L, IosQuicDiagnostics.TakeMaxStreamCancelMs());
        }
        finally {
            VhLogger.MinLogLevel = logLevel;
        }
    }

    [TestMethod]
    public void What_began_counted_comes_off_when_it_ends_uncounted()
    {
        var logLevel = VhLogger.MinLogLevel;
        try {
            // a stream and a connection open, and a send begins, while the counters are on
            VhLogger.MinLogLevel = LogLevel.Debug;
            var liveStreams = IosQuicDiagnostics.LiveStreamCount;
            var liveConnections = IosQuicDiagnostics.LiveConnectionCount;
            var outstandingSendBytes = IosQuicDiagnostics.OutstandingSendBytes;
            var streamId = IosQuicDiagnostics.OnStreamOpened();
            var connectionId = IosQuicDiagnostics.OnConnectionOpened();
            var countedSendBytes = IosQuicDiagnostics.AddOutstandingSend(1000);
            Assert.AreEqual(liveStreams + 1, IosQuicDiagnostics.LiveStreamCount);
            Assert.AreEqual(liveConnections + 1, IosQuicDiagnostics.LiveConnectionCount);
            Assert.AreEqual(outstandingSendBytes + 1000, IosQuicDiagnostics.OutstandingSendBytes);

            // they end with the counters off: counted at their start, they still come off
            VhLogger.MinLogLevel = LogLevel.Information;
            IosQuicDiagnostics.OnStreamClosed(streamId);
            IosQuicDiagnostics.OnConnectionClosed(connectionId);
            IosQuicDiagnostics.SubtractOutstandingSend(countedSendBytes);
            Assert.AreEqual(liveStreams, IosQuicDiagnostics.LiveStreamCount);
            Assert.AreEqual(liveConnections, IosQuicDiagnostics.LiveConnectionCount);
            Assert.AreEqual(outstandingSendBytes, IosQuicDiagnostics.OutstandingSendBytes);
        }
        finally {
            VhLogger.MinLogLevel = logLevel;
        }
    }
}
