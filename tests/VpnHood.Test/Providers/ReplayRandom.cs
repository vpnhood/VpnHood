namespace VpnHood.Test.Providers;

internal sealed class ReplayRandom(IReadOnlyList<double> draws) : Random
{
    public int DrawsRead { get; private set; }

    public override double NextDouble() => draws[DrawsRead++];
}
