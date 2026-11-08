using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Extensions;

/// <summary>
/// Provides VpnHood's await helpers: <see cref="Vhc(Task)"/>, which configures every await the same way, a helper that
/// blocks on a value task, and helpers that cancel a token source without throwing.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// The single, process-wide <c>continueOnCapturedContext</c> value used by every <see cref="Vhc(Task)"/>
    /// await. Defaults to <c>false</c>, so by default <c>.Vhc()</c> behaves exactly like
    /// <c>.ConfigureAwait(false)</c>: an await does not capture the current <see cref="SynchronizationContext"/>, so its
    /// continuation is not marshaled back onto it.
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
    ///     Code that <b>must</b> not capture the context (so it can never be flipped back on) should hardcode
    ///     <c>.ConfigureAwait(false)</c>.
    ///   </item>
    ///   <item>
    ///     Code that <b>must</b> resume on the captured (e.g. UI) context should use a bare <c>await</c> or
    ///     <c>.ConfigureAwait(true)</c>.
    ///   </item>
    /// </list>
    /// <para>Overloads cover <see cref="Task"/>, <see cref="Task{T}"/>, <see cref="ValueTask"/>,
    /// <see cref="ValueTask{T}"/> and <see cref="IAsyncEnumerable{T}"/> (for <c>await foreach</c>).</para>
    /// </remarks>
    /// <param name="task">The task to await.</param>
    /// <returns>The configured awaitable.</returns>
    public static ConfiguredTaskAwaitable Vhc(this Task task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    /// <inheritdoc cref="Vhc(Task)"/>
    /// <typeparam name="T">The type of the task's result.</typeparam>
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
    /// <typeparam name="T">The type of the task's result.</typeparam>
    public static ConfiguredValueTaskAwaitable<T> Vhc<T>(this ValueTask<T> task)
    {
        return task.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    /// <inheritdoc cref="Vhc(Task)"/>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="source">The sequence to enumerate.</param>
    public static ConfiguredCancelableAsyncEnumerable<T> Vhc<T>(this IAsyncEnumerable<T> source)
    {
        return source.ConfigureAwait(DefaultContinueOnCapturedContext);
    }

    /// <summary>Blocks the calling thread until a value task completes, and rethrows its exception.</summary>
    /// <remarks>
    /// A task not complete yet is turned into a <see cref="Task"/> first, as some value task sources do not allow
    /// reading the result before completion.
    /// </remarks>
    /// <param name="task">The task, which must not be awaited elsewhere.</param>
    /// <exception cref="OperationCanceledException">The task was canceled.</exception>
    /// <exception cref="Exception">The task's own exception.</exception>
    public static void VhBlock(this ValueTask task)
    {
        // Some IValueTaskSource-backed ValueTasks do not support GetResult before completion.
        // Use the fast path when already completed; otherwise convert to Task and block on it.
        if (task.IsCompleted)
            task.GetAwaiter().GetResult(); // observe completion / re-throw any exception
        else
            task.AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Provides cancellation that logs a failure instead of throwing it.
    /// </summary>
    /// <param name="cancellationTokenSource">The token source.</param>
    extension(CancellationTokenSource cancellationTokenSource)
    {
        /// <summary>
        /// Cancels the token source unless it is canceled already, and logs a failure at Error instead of throwing it,
        /// such as a callback's exception or the source's disposal.
        /// </summary>
        public void TryCancel()
        {
            try {
                if (!cancellationTokenSource.IsCancellationRequested)
                    cancellationTokenSource.Cancel();
            }
            catch (Exception ex) {
                // a callback that throws is a bug
                VhInvoker.LogFailure(ex, "cancel a token source", LogLevel.Error);
            }
        }

        /// <summary>
        /// Cancels the token source asynchronously unless it is canceled already, and logs a failure at Error instead of
        /// throwing it.
        /// </summary>
        /// <returns>
        /// A task that completes when the callbacks have run, failed or not, or at once when the source was canceled
        /// already, even while its callbacks still run.
        /// </returns>
        public async Task TryCancelAsync()
        {
            try {
                if (!cancellationTokenSource.IsCancellationRequested)
                    await cancellationTokenSource.CancelAsync().Vhc();
            }
            catch (Exception ex) {
                VhInvoker.LogFailure(ex, "cancel a token source", LogLevel.Error);
            }
        }
    }
}