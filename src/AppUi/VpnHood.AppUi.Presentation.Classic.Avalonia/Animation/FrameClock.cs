using System.Diagnostics;
using Avalonia.Animation.Easings;
using Avalonia.Threading;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Animation;

// The frame clock the ripples and the page transition run on: one dispatcher timer at render
// priority, 125 ticks a second, alive only while something is animating. Avalonia 12 keeps its
// own animation clock internal, and a top level's RequestAnimationFrame is not the tool either: a
// callback that requests the next frame from inside itself runs again in the same frame.
internal static class FrameClock
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(8);
    private static readonly List<Action> Ticks = [];
    private static DispatcherTimer? _timer;

    // Calls tick once per frame until the subscription is disposed.
    public static IDisposable OnFrame(Action tick)
    {
        Dispatcher.UIThread.VerifyAccess();
        Ticks.Add(tick);
        _timer ??= new DispatcherTimer(Interval, DispatcherPriority.Render, OnTimer);
        return new Subscription(tick);
    }

    // Calls step with the eased progress once per frame over the duration, 1 on the last frame; the
    // first step runs at once, so the start state is drawn before the first frame. A cancellation
    // ends the tween where it is, without a further step.
    public static Task Tween(TimeSpan duration, Easing easing, Action<double> step, CancellationToken cancellationToken)
    {
        step(easing.Ease(0));
        var tween = new TweenRun(duration, easing, step, cancellationToken);
        tween.Start();
        return tween.Task;
    }

    private static void OnTimer(object? sender, EventArgs e)
    {
        // a tick may end its own subscription
        foreach (var tick in Ticks.ToArray())
            tick();

        if (Ticks.Count == 0) {
            _timer?.Stop();
            _timer = null;
        }
    }

    private sealed class Subscription(Action tick) : IDisposable
    {
        public void Dispose()
        {
            Ticks.Remove(tick);
        }
    }

    private sealed class TweenRun(TimeSpan duration, Easing easing, Action<double> step, CancellationToken cancellationToken)
    {
        private readonly TaskCompletionSource _completion = new();
        private readonly long _startedAt = Stopwatch.GetTimestamp();
        private IDisposable? _frames;
        private CancellationTokenRegistration _cancellation;

        public Task Task => _completion.Task;

        public void Start()
        {
            _frames = OnFrame(Tick);
            _cancellation = cancellationToken.Register(Finish);
            if (_completion.Task.IsCompleted)
                _frames.Dispose();
        }

        private void Tick()
        {
            if (_completion.Task.IsCompleted)
                return;

            var progress = Math.Min(1, Stopwatch.GetElapsedTime(_startedAt) / duration);
            step(easing.Ease(progress));
            if (progress >= 1)
                Finish();
        }

        private void Finish()
        {
            if (!_completion.TrySetResult())
                return;

            _cancellation.Dispose();
            _frames?.Dispose();
        }
    }
}
