using System;

namespace VpnHood.Common.JobController;

public class JobLock : IDisposable
{
    private readonly JobSection _jobSection;
    public bool IsEntered { get; }

    internal JobLock(JobSection jobSection, bool isEntered)
    {
        _jobSection = jobSection;
        IsEntered = isEntered;
    }

    public void Dispose()
    {
        if (IsEntered)
            _jobSection.Leave();
    }
}