using System.Text;

namespace VpnHood.Test.KestrelParity.Probing;

// Sends each probe to both servers and keeps what each answered. Each probe has its own connections and a bound
// of its own, and each case's start and end go to a progress file at once, so a stalled run shows where.
internal static class ProbeRunner
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(180);
    private static readonly Lock ProgressLock = new();

    // all at once, each probe's two servers side by side: most probes wait on a server's timeout. A bulk probe
    // runs alone, one server after the other, as anything beside it would slow the drain it measures
    public static async Task<IReadOnlyList<ProbeResult>> Run(IReadOnlyList<Probe> probes, ProbeTarget reference,
        ProbeTarget ours, string progressPath, CancellationToken cancellationToken)
    {
        var results = new List<ProbeResult>();
        var concurrent = probes.Where(x => x.Kind != ProbeKind.Bulk).Select(async probe => {
            var logs = await Task.WhenAll(
                Run(probe, reference, progressPath, cancellationToken),
                Run(probe, ours, progressPath, cancellationToken));
            return new ProbeResult(probe.Name, probe.Kind, logs[0], logs[1]);
        });
        results.AddRange(await Task.WhenAll(concurrent));

        foreach (var probe in probes.Where(x => x.Kind == ProbeKind.Bulk)) {
            var referenceLog = await Run(probe, reference, progressPath, cancellationToken);
            var ourLog = await Run(probe, ours, progressPath, cancellationToken);
            results.Add(new ProbeResult(probe.Name, probe.Kind, referenceLog, ourLog));
        }

        return results;
    }

    private static async Task<string> Run(Probe probe, ProbeTarget target, string progressPath,
        CancellationToken cancellationToken)
    {
        NoteProgress(progressPath, $"Started {target.Name} {probe.Name}.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);
        var log = new StringBuilder();
        try {
            await probe.Run(target, log, timeoutCts.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            // a probe that breaks breaks for both servers alike, so its error is part of what is compared
            log.AppendLine($"! {ProbeConnection.Describe(ex)}");
        }

        NoteProgress(progressPath, $"Finished {target.Name} {probe.Name}.");
        return log.ToString();
    }

    private static void NoteProgress(string progressPath, string message)
    {
        lock (ProgressLock)
            File.AppendAllText(progressPath, message + Environment.NewLine);
    }
}
