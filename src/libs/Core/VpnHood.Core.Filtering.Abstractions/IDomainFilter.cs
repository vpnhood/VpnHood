namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Defines a stage of a pipe of domain filters, which decides what happens to a flow by the domain name it is for.
/// </summary>
/// <remarks>
/// A stage may wrap the next one and combine its verdict with its own. The client asks the domain pipe first, with
/// the name a flow's TLS handshake gives, and its IP pipe only when the answer is <see cref="FilterAction.Default"/>.
/// </remarks>
public interface IDomainFilter : IDisposable
{
    /// <summary>Decides what happens to a flow for a domain name.</summary>
    /// <param name="domainName">The name, or <see langword="null"/> when the flow gives none.</param>
    /// <returns>The verdict; <see cref="FilterAction.Default"/> when the pipe has no objection.</returns>
    FilterAction Process(string? domainName);

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
    /// <remarks>
    /// The client reads a flow's domain name from its TLS handshake only while the pipe is not empty, or while its
    /// options force the names to be logged.
    /// </remarks>
    bool IsEmpty { get; }
}
