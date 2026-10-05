namespace VpnHood.AppUi.Hosting.Desktop.Channel;

// A client's first line, sent before it reads anything: on Windows the service can read who is
// calling only once the caller has written. What it carries is for the service's log.
public class DaemonChannelHello
{
    public required string Version { get; init; }
}
