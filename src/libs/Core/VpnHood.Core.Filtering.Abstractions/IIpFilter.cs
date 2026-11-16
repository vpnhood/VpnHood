using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Defines a stage of a pipe of IP filters, which decides what happens to a flow by its protocol and destination.
/// </summary>
/// <remarks>
/// A stage may wrap the next one and combine its verdict with its own. The client sends a flow no filter objects to
/// through the tunnel, an excluded one around it, and drops a blocked one; the server drops a blocked one.
/// </remarks>
public interface IIpFilter : IDisposable
{
    /// <summary>Decides what happens to a flow.</summary>
    /// <param name="protocol">The flow's protocol.</param>
    /// <param name="endPoint">The flow's destination.</param>
    /// <returns>The verdict; <see cref="FilterAction.Default"/> when the pipe has no objection.</returns>
    FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint);

    /// <summary>
    /// Occurs when the pipe's verdicts may have changed, as when a stage swaps its rules. A stage raises its next
    /// stage's event as its own, so the event reaches the outermost stage; a stage whose verdicts never change never
    /// raises it.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Tells the pipe that its configuration outside the program, such as a database's path, may have changed.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// Each stage passes it to its next one; a stage that owns such configuration reads it again and swaps its own
    /// parts, raising <see cref="Changed"/> if its verdicts may differ.
    /// </remarks>
    Task Reconfigure(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a value indicating whether the pipe holds no rule, so it can only answer
    /// <see cref="FilterAction.Default"/>. Read it again when <see cref="Changed"/> occurs.
    /// </summary>
    bool IsEmpty { get; }
}
