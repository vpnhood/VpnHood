namespace VpnHood.Net.Http3;

internal sealed class Http3TrailerScheduleSampler(Random random) : IHttp3TrailerScheduleSampler
{
    // Calibrated against ASP.NET Core 11.0.0-rc.3.26522.148 in two sessions on one host:
    // 300 bursts per limit row, then 2,000 per row including DATA; odds vary with host and load.
    private const long FirstFields = 1;
    private const long SecondFields = 8;
    private const long ThirdFields = 51;
    private const long LastFields = 151;
    private const double FirstSurvival = 0.80;
    private const double SecondSurvival = 0.27;
    private const double ThirdSurvival = 0.05;
    private const double LastSurvival = 0.03;
    private const double ImmediateEmissionProbability = 0.20;
    private const double MeanApplicationEmissionBytes = 380;
    private const double MeanLimitEmissionBytes = 10000;

    public static Http3TrailerScheduleSampler Shared { get; } = new(Random.Shared);

    public Http3TrailerSchedule Sample()
    {
        var selectionFields = SelectFields(ReadUniform());
        // Only the frozen response uses a distance, so both outcomes share the draw.
        var emissionDraw = ReadUniform();
        var emissionDistance = emissionDraw <= ImmediateEmissionProbability
            ? 0
            : -Math.Log((1 - emissionDraw) / (1 - ImmediateEmissionProbability));
        return new Http3TrailerSchedule(selectionFields, MeanApplicationEmissionBytes * emissionDistance,
            MeanLimitEmissionBytes * emissionDistance);
    }

    private double ReadUniform()
    {
        var value = random.NextDouble();
        if (!(value >= 0 && value <= 1))
            throw new ArgumentOutOfRangeException(nameof(random), "A uniform draw must be between zero and one.");
        return value;
    }

    private static long SelectFields(double survival)
    {
        if (survival > FirstSurvival)
            return 0;
        if (survival == 0)
            return long.MaxValue;

        // Exact anchors avoid losing a completed field to floating-point inversion.
        if (survival == FirstSurvival) return FirstFields;
        if (survival == SecondSurvival) return SecondFields;
        if (survival == ThirdSurvival) return ThirdFields;
        if (survival == LastSurvival) return LastFields;

        if (survival > SecondSurvival)
            return Invert(survival, FirstFields, FirstSurvival, SecondFields, SecondSurvival);
        if (survival > ThirdSurvival)
            return Invert(survival, SecondFields, SecondSurvival, ThirdFields, ThirdSurvival);
        return Invert(survival, ThirdFields, ThirdSurvival, LastFields, LastSurvival);
    }

    private static long Invert(double survival, long startFields, double startSurvival,
        long endFields, double endSurvival)
    {
        var exponent = Math.Log((double)endFields / startFields) / Math.Log(endSurvival / startSurvival);
        var fields = startFields * Math.Pow(survival / startSurvival, exponent);
        return fields >= long.MaxValue ? long.MaxValue : (long)fields;
    }
}
