using System.Text;

namespace VpnHood.Test.KestrelParity.Probing;

// requests sent the same way to each server; the log is what a prober sees of the answer
internal sealed record Probe(string Name, ProbeKind Kind, Func<ProbeTarget, StringBuilder, CancellationToken, Task> Run);
