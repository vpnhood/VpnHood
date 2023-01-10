using System;

namespace VpnHood.Common.Utils;

public interface ILockAsyncResult : IDisposable
{
    public bool Succeeded { get; }
}