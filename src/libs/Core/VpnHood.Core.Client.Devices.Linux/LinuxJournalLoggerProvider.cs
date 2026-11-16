using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Linux;

/// <summary>
/// Provides the journal as the daemon's console sink, where stdout is the journal
/// (<see cref="LinuxJournalLogger.IsConsole"/>): the Linux host offers it in the terminal's place, so each line has one
/// writer whatever the unit says.
/// </summary>
/// <param name="includeScopes">
/// <see langword="true"/> to write each entry's level and scopes. The default is <see langword="true"/>.
/// </param>
public sealed class LinuxJournalLoggerProvider(bool includeScopes = true)
    : TextLoggerProvider(new LinuxJournalLogger(), includeTime: false, includeScopes: includeScopes,
        includeLevel: includeScopes);
