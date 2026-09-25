using System.Diagnostics;
using System.Text;

namespace VpnHood.AppUi.Hosting.Cli.Windows.Internal;

// A service has no console: what its run writes to stderr - the answer a person would have read -
// goes to the Application log as an error, under the service's name.
internal sealed class EventLogErrorWriter(EventLog eventLog) : TextWriter
{
    public override Encoding Encoding => Encoding.Unicode;

    public override void WriteLine(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            eventLog.WriteEntry(value, EventLogEntryType.Error);
    }
}
