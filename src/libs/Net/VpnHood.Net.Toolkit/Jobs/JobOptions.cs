namespace VpnHood.Net.Toolkit.Jobs;

/// <summary>
/// Provides the options of a <see cref="Job"/>.
/// </summary>
public record JobOptions
{
    /// <summary>
    /// Gets or sets, process-wide, the interval of options that set none. The default is 30 seconds.
    /// </summary>
    public static TimeSpan DefaultInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the time from the end of a run to the next one. The default is <see cref="DefaultInterval"/>.
    /// </summary>
    public TimeSpan Interval { get; init; } = DefaultInterval;

    /// <summary>Gets the job's name, for the log, or <see langword="null"/> for <c>NoName</c>.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the time from the job's start to its first run, or <see langword="null"/> for <see cref="Interval"/>.
    /// </summary>
    public TimeSpan? DueTime { get; init; }

    /// <summary>
    /// Gets how many failures in a row the job runs again after: the next failure stops it. A success resets the count.
    /// <see langword="null"/>, the default, never stops it.
    /// </summary>
    public int? MaxRetry { get; init; }

    /// <summary>
    /// Gets a value indicating whether the job starts when it is made. The default is <see langword="true"/>.
    /// </summary>
    public bool AutoStart { get; init; } = true;
}