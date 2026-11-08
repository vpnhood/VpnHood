using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides the process's log file: opened at each start request, in place of what it had or after it, and closed at
/// stop; closed, it takes nothing.
/// </summary>
/// <param name="filePath">The file's path.</param>
public sealed class FileLogger(string filePath) : ILogger
{
    private const int DefaultBufferSize = 1024;
    private readonly Lock _lock = new();
    private StreamWriter? _writer;

    /// <summary>Gets the file's path.</summary>
    public string FilePath { get; } = filePath;

    /// <summary>Gets a value indicating whether the file is open.</summary>
    public bool IsOpen => _writer != null;

    /// <summary>
    /// Gets or sets a value indicating whether every line is flushed as it comes; otherwise, lines are buffered, and
    /// flushed at an error either way.
    /// </summary>
    public bool AutoFlush { get; set; }

    /// <summary>Opens the file, closing it first when it is open; others may read it meanwhile.</summary>
    /// <param name="deleteOld"><see langword="true"/> to start the file afresh; <see langword="false"/> to append.</param>
    /// <exception cref="IOException">
    /// The file cannot be opened or deleted, or the open file could not be flushed as it closed.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    /// <exception cref="ArgumentException">The file's path is empty or not valid.</exception>
    public void Open(bool deleteOld)
    {
        lock (_lock) {
            _writer?.Dispose();
            _writer = null;
            if (deleteOld && File.Exists(FilePath))
                File.Delete(FilePath);

            _writer = new StreamWriter(
                new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read),
                Encoding.UTF8, DefaultBufferSize);
        }
    }

    /// <summary>
    /// Closes the file. A failure to flush what is buffered, as on a full disk, is written to the console instead of
    /// thrown.
    /// </summary>
    public void Close()
    {
        lock (_lock) {
            // the close flushes what is buffered, which a full disk fails; the file is closed all the same
            try {
                _writer?.Dispose();
            }
            catch (Exception ex) {
                Console.WriteLine($"Error: Could not close the log. {ex.Message}");
            }

            _writer = null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>It keeps no scopes: it returns <see langword="null"/>.</remarks>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/> while the file is open.</remarks>
    public bool IsEnabled(LogLevel logLevel)
    {
        return IsOpen;
    }

    /// <inheritdoc/>
    /// <remarks>It writes the formatted message as a line; a failure goes to the console.</remarks>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lock) {
            if (_writer == null)
                return;

            try {
                _writer.WriteLine(formatter(state, exception));
                if (AutoFlush || logLevel >= LogLevel.Error)
                    _writer.Flush();
            }
            catch (Exception ex) {
                Console.WriteLine($"Error: Could not write the log. {ex.Message}");
            }
        }
    }
}
