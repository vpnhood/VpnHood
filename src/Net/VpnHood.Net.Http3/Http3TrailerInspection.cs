namespace VpnHood.Net.Http3;

internal sealed class Http3TrailerInspection
{
    private readonly Http3TrailerSchedule _schedule;
    private readonly Http3ResponseDispatch _dispatch;
    private long _fields;
    private double _remaining = double.PositiveInfinity;
    private bool _selected;

    public Http3TrailerInspection(Http3TrailerSchedule schedule, Http3ResponseDispatch dispatch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(schedule.SelectionFields);
        if (double.IsNaN(schedule.ApplicationEmissionBytes) || schedule.ApplicationEmissionBytes < 0 ||
            double.IsNaN(schedule.LimitEmissionBytes) || schedule.LimitEmissionBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(schedule));
        _schedule = schedule;
        _dispatch = dispatch;
    }

    public void Start()
    {
        if (_schedule.SelectionFields == 0)
            Select();
        CheckForEmission();
    }

    public void CheckForEmission()
    {
        if (_selected && _remaining < 1)
            throw new Http3InspectionCompleteException();
    }

    public int LimitRead(int requested)
    {
        CheckForEmission();
        return (int)Math.Min(requested, _remaining);
    }

    public void Advance(int count)
    {
        if (_selected)
            _remaining -= count;
    }

    public void FieldCompleted()
    {
        if (++_fields == _schedule.SelectionFields)
            Select();
    }

    private void Select()
    {
        var exceedsRequestLimits = _dispatch.Select();
        _selected = true;
        _remaining = exceedsRequestLimits ? _schedule.LimitEmissionBytes : _schedule.ApplicationEmissionBytes;
    }
}
