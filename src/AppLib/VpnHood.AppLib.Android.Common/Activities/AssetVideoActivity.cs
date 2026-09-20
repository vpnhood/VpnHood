using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Views;
using VpnHood.Core.Client.Devices.Droid.ActivityEvents;

namespace VpnHood.AppLib.Droid.Common.Activities;

// Plays a video that ships as an asset, full screen, over whatever is showing - the shape every ad
// network uses for an interstitial, so a promotion of ours and a bought one look the same to the
// person watching, and the platform's own back button, rotation and lifecycle handling come free.
//
// The file is read where it lies, through the descriptor AssetManager gives for a STORED entry; it
// is never copied to storage. That costs nothing at run time and keeps the video once in the
// package. It does mean the entry must be stored rather than deflated - aapt leaves .mp4 alone by
// default, but a build that compressed it would fail here at run time and nowhere earlier.
//
// Deliberately knows nothing about ads: it is handed an asset path and reports how it ended. The
// countdown, the buttons and the reporting belong to whoever shows it.
[Activity(
    Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    ExcludeFromRecents = true)]
public class AssetVideoActivity : Activity, ISurfaceHolderCallback
{
    private const string AssetPathExtra = "assetPath";
    private const int RequestCode = 0x5664; // 'Vd'

    private MediaPlayer? _mediaPlayer;
    private SurfaceView? _surfaceView;
    private string? _assetPath;

    // Shows the video and returns when it is over: true when it played to the end, false when the
    // person left first. Faults if the asset cannot be opened or the video cannot be played, so a
    // caller that promised to show something knows it did not happen.
    public static async Task<bool> PlayAsync(IActivityEvent activityEvent, string assetPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);

        var completion = new TaskCompletionSource<bool>();

        void OnResult(object? sender, ActivityResultEventArgs args)
        {
            if (args.RequestCode != RequestCode)
                return;

            activityEvent.ActivityResultEvent -= OnResult;

            var error = args.Data?.GetStringExtra(nameof(Exception));
            if (error != null)
                completion.TrySetException(new InvalidOperationException(error));
            else
                completion.TrySetResult(args.ResultCode == Result.Ok);
        }

        activityEvent.ActivityResultEvent += OnResult;
        try {
            var intent = new Intent(activityEvent.Activity, typeof(AssetVideoActivity));
            intent.PutExtra(AssetPathExtra, assetPath);
            activityEvent.Activity.StartActivityForResult(intent, RequestCode);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch {
            activityEvent.ActivityResultEvent -= OnResult;
            throw;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _assetPath = Intent?.GetStringExtra(AssetPathExtra);
        if (string.IsNullOrWhiteSpace(_assetPath)) {
            Finish(new InvalidOperationException($"{nameof(AssetVideoActivity)} was started with no asset path."));
            return;
        }

        // The surface is not ready when the activity is created, so playback starts in SurfaceCreated.
        _surfaceView = new SurfaceView(this);
        _surfaceView.Holder?.AddCallback(this);
        SetContentView(_surfaceView);
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        try {
            var assets = Assets ?? throw new InvalidOperationException("The activity has no asset manager.");
            using var descriptor = assets.OpenFd(_assetPath ?? throw new InvalidOperationException("No asset path."));

            var mediaPlayer = new MediaPlayer();
            _mediaPlayer = mediaPlayer;
            mediaPlayer.SetDisplay(holder);
            mediaPlayer.SetDataSource(descriptor.FileDescriptor!, descriptor.StartOffset, descriptor.Length);
            mediaPlayer.Completion += (_, _) => Finish(Result.Ok);
            mediaPlayer.Error += (_, args) => Finish(
                new InvalidOperationException($"The video could not be played. What: {args.What}, Extra: {args.Extra}"));
            mediaPlayer.Prepared += (_, _) => mediaPlayer.Start();
            mediaPlayer.PrepareAsync();
        }
        catch (Exception ex) {
            Finish(ex);
        }
    }

    public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
    {
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _mediaPlayer?.SetDisplay(null);
    }

    // Leaving early is an answer, not a failure: the caller is told it was not watched to the end.
    protected override void OnPause()
    {
        base.OnPause();
        if (!IsFinishing)
            Finish(Result.Canceled);
    }

    protected override void OnDestroy()
    {
        var mediaPlayer = _mediaPlayer;
        _mediaPlayer = null;
        if (mediaPlayer != null) {
            mediaPlayer.Reset();
            mediaPlayer.Release();
            mediaPlayer.Dispose();
        }

        _surfaceView?.Holder?.RemoveCallback(this);
        base.OnDestroy();
    }

    private void Finish(Result result, Intent? data = null)
    {
        if (IsFinishing)
            return;

        SetResult(result, data);
        Finish();
    }

    private void Finish(Exception exception)
    {
        var data = new Intent();
        data.PutExtra(nameof(Exception), exception.Message);
        Finish(Result.Canceled, data);
    }
}
