using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Animation;

// The web UI's route transition (App.vue: translate-with-fade going deeper, short-translate coming
// back, both mode="out-in"), value for value: the leaving page fades out in 90ms, dropping 50px on
// the way back, and only then the arriving page fades in over 130ms, rising 50px going deeper or
// settling 30px coming back; both on the CSS 'ease' curve. Out-in keeps one page in the box at a
// time, so nothing ghosts through anything. The visuals are the host's two presenters, which it
// reuses: every state is set here before the first frame is drawn, and the arriving one is left
// clean; the leaving one stays dark until the host hides it.
public sealed class OutInTransition : IPageTransition
{
    private static readonly Easing Ease = new SplineEasing(0.25, 0.1, 0.25);
    private static readonly TimeSpan LeaveTime = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan EnterTime = TimeSpan.FromMilliseconds(130);

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var enter = new TranslateTransform();
        try {
            if (to != null) {
                to.Opacity = 0;
                to.RenderTransform = enter;
            }

            if (from != null) {
                var leave = new TranslateTransform();
                from.Opacity = 1;
                from.RenderTransform = leave;
                await FrameClock.Tween(LeaveTime, Ease, t => {
                    from.Opacity = 1 - t;
                    leave.Y = forward ? 0 : 50 * t;
                }, cancellationToken);
            }

            if (to != null && !cancellationToken.IsCancellationRequested) {
                var startY = forward ? 50 : -30;
                await FrameClock.Tween(EnterTime, Ease, t => {
                    to.Opacity = t;
                    enter.Y = startY * (1 - t);
                }, cancellationToken);
            }
        }
        finally {
            // a cancelled run is being replaced: the next one sets both presenters itself
            if (to != null && !cancellationToken.IsCancellationRequested) {
                to.ClearValue(Visual.OpacityProperty);
                to.ClearValue(Visual.RenderTransformProperty);
            }
        }
    }
}
