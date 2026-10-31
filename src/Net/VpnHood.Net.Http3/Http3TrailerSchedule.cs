namespace VpnHood.Net.Http3;

internal readonly record struct Http3TrailerSchedule(long SelectionFields,
    double ApplicationEmissionBytes, double LimitEmissionBytes);
