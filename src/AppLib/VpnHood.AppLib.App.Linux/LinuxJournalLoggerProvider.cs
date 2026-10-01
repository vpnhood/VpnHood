using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Linux;

// The journal as the daemon's console sink, where stdout is the journal (LinuxJournalLogger.IsConsole):
// the Linux host offers it in the terminal's place, so each line has one writer whatever the unit says.
public sealed class LinuxJournalLoggerProvider(bool includeScopes = true)
    : TextLoggerProvider(new LinuxJournalLogger(), includeTime: false, includeScopes: includeScopes);
