namespace VpnHood.Core.Filtering.Abstractions;

public interface IDomainFilter : IDisposable
{
    FilterAction Process(string? domainName);

    // Raised when the filter's verdicts may have changed (e.g. a stage swapped its gates).
    // Wrapping stages forward their inner filter's event, so the change rolls UP the pipe and outer
    // stages react without knowing where it happened — the cached stages drop their memoized verdicts
    // by themselves. A filter whose verdicts never change simply never raises it.
    event EventHandler? Changed;

    // The command twin of Changed: tells the pipe its external configuration may have changed. It rolls
    // DOWN the pipe — every wrapping stage forwards it to its inner filter — and a stage that owns
    // external configuration (e.g. the sqlite gate chain and its db paths) re-reads it and swaps its own
    // parts, raising Changed if verdicts may differ. A stage with nothing to re-read simply forwards;
    // a leaf does nothing. The caller never knows which stage reacted.
    void Reconfigure();

    // True while this pipe can never return a non-Default verdict — no rules anywhere down the chain
    // (wrapping stages aggregate: own rules AND next). Lets the endpoint skip work that only exists to
    // feed the pipe: the client extracts SNI — sensitive and costly — only while a rule could consume
    // it, re-checking whenever Changed fires.
    bool IsEmpty { get; }
}
