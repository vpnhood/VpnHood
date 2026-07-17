using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Extensions;

public static class TaskExtensions
{
    /// <summary>
    /// The single, process-wide <c>continueOnCapturedContext</c> value used by every <see cref="Vhc(Task)"/>
    /// await. Defaults to <c>false</c>, so by default <c>.Vhc()</c> behaves exactly like
    /// <c>.ConfigureAwait(false)</c>: continuations resume on a thread-pool thread and are never marshaled back
    /// onto a captured <see cref="SynchronizationContext"/>.
    /// </summary>
    /// <remarks>
    /// This exists so all library/background awaits can be flipped from one place. VpnHood's hot paths run in
    /// contexts with no UI and often no <see cref="SynchronizationContext"/> at all (the iOS Network Extension,
    /// the headless VPN service), where continuing on the captured context is at best pointless overhead and at
    /// worst a deadlock. Setting this to <c>true</c> once — e.g. for a diagnostic run, or a host that installs a
    /// context we deliberately want continuations to flow back onto — changes every <c>.Vhc()</c> call site at
    /// once, instead of editing hundreds of individual awaits.
    /// </remarks>
    public static bool DefaultContinueOnCapturedContext { get; set; }

    /// <summary>
    /// VpnHood's <c>ConfigureAwait</c>. Use <c>.Vhc()</c> on every non-UI await in library and background code
    /// in place of <c>.ConfigureAwait(false)</c>. It routes through the shared
    /// <see cref="DefaultContinueOnCapturedContext"/> switch, so the whole codebase's context-capture behaviour
    /// is controlled from a single place rather than baked into each call site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With the default switch (<c>false</c>) this is identical to <c>.ConfigureAwait(false)</c> — the correct
    /// choice for code that has no affinity to a UI/synchronization context, which is essentially all the
    /// core, tunneling, QUIC, TCP-stack and VPN-service code.
    /// </para>
    /// <para>
    /// Do <b>not</b> use <c>.Vhc()</c> when an await must have a fixed, non-toggleable behaviour:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     Code running on a UI thread that <b>must</b> get off the captured context (so it can never be flipped
    ///     back on) should hardcode <c>.ConfigureAwait(false)</c>.
    ///   </item>
    ///   <item>
    ///     Code that <b>must</b> resume on the captured (e.g. UI) context should use a bare <c>await</c> or
    ///     <c>.ConfigureAwait(true)</c>.
    ///   </item>
    /// </list>
    /// <para>Overloads cover <see cref="Task"/>, <see cref="Task{T}"/>, <see cref="ValueTask"/>,
    /// <see cref="ValueTask{T}"/> and <see cref="IAsyncEnumerable{T}"/> (for <c>await foreach</c>).</para>
    /// </remarks>
    public static ConfiguredTaskAwaitable Vhc(this Task task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    /// <inheritdoc cref="Vhc(Task)"/>
    public static ConfiguredTaskAwaitable<T> Vhc<T>(this Task<T> task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    /// <inheritdoc cref="Vhc(Task)"/>
    public static ConfiguredValueTaskAwaitable Vhc(this ValueTask task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    /// <inheritdoc cref="Vhc(Task)"/>
    public static ConfiguredValueTaskAwaitable<T> Vhc<T>(this ValueTask<T> task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    /// <inheritdoc cref="Vhc(Task)"/>
    public static ConfiguredCancelableAsyncEnumerable<T> Vhc<T>(this IAsyncEnumerable<T> source)
    {
        return source.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    public static void VhBlock(this ValueTask task)
    {
        // Some IValueTaskSource-backed ValueTasks do not support GetResult before completion.
        // Use the fast path when already completed; otherwise convert to Task and block on it.
        if (task.IsCompleted)
            task.GetAwaiter().GetResult(); // observe completion / re-throw any exception
        else
            task.AsTask().GetAwaiter().GetResult();
    }

    extension(CancellationTokenSource cancellationTokenSource)
    {
        public void TryCancel()
        {
            try {
                if (!cancellationTokenSource.IsCancellationRequested)
                    cancellationTokenSource.Cancel();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex,
                    "Failed to cancel the CancellationTokenSource. This is not a critical error.");
            }
        }

        public async Task TryCancelAsync()
        {
            try {
                if (!cancellationTokenSource.IsCancellationRequested)
                    await cancellationTokenSource.CancelAsync().Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex,
                    "Failed to cancel the CancellationTokenSource. This is not a critical error.");
            }
        }
    }
}