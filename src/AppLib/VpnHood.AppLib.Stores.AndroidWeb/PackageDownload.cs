using Android.App;

namespace VpnHood.AppLib.Stores.AndroidWeb;

// One row of DownloadManager's list that is this provider's package, known by the version its
// description holds.
internal class PackageDownload
{
    public required long Id { get; init; }
    public required Version Version { get; init; }

    // null until DownloadManager starts writing: a download that waits, or failed before it wrote
    public required string? FilePath { get; init; }

    public required DownloadStatus Status { get; init; }

    // why it failed or paused: an HTTP status below 1000, a DownloadError from 1000
    public required int Reason { get; init; }

    public required long DownloadedBytes { get; init; }

    // -1 until the server has told the size
    public required long TotalBytes { get; init; }
}
