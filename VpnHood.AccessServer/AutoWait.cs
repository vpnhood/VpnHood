using System;
using System.Threading;

namespace VpnHood.AccessServer
{
    public class AutoWait : IDisposable
    {
        private readonly EventWaitHandle _eventWaitHandle;
        private bool _disposed;

        public AutoWait(string name)
        {
            _eventWaitHandle = new EventWaitHandle(true, EventResetMode.AutoReset, name);
            _eventWaitHandle.WaitOne();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _eventWaitHandle.Set();
            _eventWaitHandle.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}