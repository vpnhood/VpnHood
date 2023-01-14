using System;

namespace VpnHood.Common.JobController;

public class JobLock : IDisposable
{
    private readonly JobSection _jobSection;
    public bool ShouldEnter { get; }

    internal JobLock(JobSection jobSection, bool shouldEnter)
    {
        _jobSection = jobSection;
        ShouldEnter = shouldEnter;
    }

    public void Dispose()
    {
        if (ShouldEnter)
            _jobSection.Leave();
    }
}