using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class LoggingTest : TestBase
{
    [TestMethod]
    public void The_text_logger_writes_the_level_and_the_scopes_each_by_its_own_switch()
    {
        var levelOnly = new ListLogger();
        new TextLogger(levelOnly, includeTime: false, includeScopes: false).LogWarning("hello");
        Assert.AreEqual("Warn | hello", levelOnly.Messages.Single());

        var neither = new ListLogger();
        new TextLogger(neither, includeTime: false, includeScopes: false, includeLevel: false).LogWarning("hello");
        Assert.AreEqual("hello", neither.Messages.Single());

        var both = new ListLogger();
        var bothLogger = new TextLogger(both, includeTime: false);
        using (bothLogger.BeginScope("scope1"))
            bothLogger.LogInformation("hello");
        Assert.AreEqual("Info | scope1 | hello", both.Messages.Single());

        var scopesOnly = new ListLogger();
        var scopesOnlyLogger = new TextLogger(scopesOnly, includeTime: false, includeLevel: false);
        using (scopesOnlyLogger.BeginScope("scope1"))
            scopesOnlyLogger.LogInformation("hello");
        Assert.AreEqual(" scope1 | hello", scopesOnly.Messages.Single());
    }

    [TestMethod]
    public void Log_event_names_come_from_the_parts_that_start_with_the_switch()
    {
        var names = LogService.GetLogEventNames("/log:Tcp,Udp other x/log:Bad /LOG:Dns, /log:").ToArray();
        CollectionAssert.AreEqual(new[] { "*", "Tcp", "Udp", "Dns" }, names);
    }

    // The exception's message goes into the line as it is. As a part of the line's template, its braces were read
    // as the template's own, and the line failed where it was written.
    [TestMethod]
    [DoNotParallelize] // sets the process's log level and its close event
    public void A_socket_close_whose_message_has_braces_is_logged_as_it_is()
    {
        var logCapture = TestLogCapture.Instance;
        var minLogLevel = VhLogger.MinLogLevel;
        var tcpCloseEventId = VhLogger.TcpCloseEventId;
        try {
            VhLogger.MinLogLevel = LogLevel.Debug;
            VhLogger.TcpCloseEventId = new EventId(1, TestLogCapture.CapturedEventName);
            var exception = new IOException("The peer said {0} and {name}.",
                new SocketException((int)SocketError.ConnectionReset));

            VhLogger.LogError(new EventId(2, "Test"), exception, "The stream is closed. StreamId: {StreamId}", 5);

            CollectionAssert.Contains(logCapture.GetEventLines().ToArray(),
                "The stream is closed. StreamId: 5 Message: The peer said {0} and {name}.");
        }
        finally {
            VhLogger.MinLogLevel = minLogLevel;
            VhLogger.TcpCloseEventId = tcpCloseEventId;
        }
    }

    [TestMethod]
    [DoNotParallelize] // sets the process's log level, and fails a line of the process's log
    public void The_log_service_closes_its_file_though_its_last_line_fails()
    {
        var logCapture = TestLogCapture.Instance;
        var minLogLevel = VhLogger.MinLogLevel;
        var isAnonymousMode = VhLogger.IsAnonymousMode;
        var filePath = Path.Combine(TestHelper.WorkingPath, $"{Guid.NewGuid()}.log");
        Directory.CreateDirectory(TestHelper.WorkingPath);
        var logService = new LogService(filePath);
        try {
            logService.Start(new LogServiceOptions { MinLogLevel = LogLevel.Debug });

            // a sink fails the service's last line: the failure is told, and the file is closed all the same
            logCapture.FailingDebugMessage = "LogService is stopping...";
            Assert.Throws<AggregateException>(logService.Stop);
            using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            }

            // a disposal with the file open again does not throw, and closes it
            logCapture.FailingDebugMessage = null;
            logService.Start(new LogServiceOptions { MinLogLevel = LogLevel.Debug }, deleteOldReport: false);
            logCapture.FailingDebugMessage = "LogService is stopping...";
            logService.Dispose();
            using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            }
        }
        finally {
            logCapture.FailingDebugMessage = null;
            logService.Dispose();
            VhLogger.MinLogLevel = minLogLevel;
            VhLogger.IsAnonymousMode = isAnonymousMode;
        }
    }

    // keeps each line as it is handed over
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
                _messages.Add(formatter(state, exception));
        }
    }
}
