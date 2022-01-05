using System;

namespace VpnHood.Common.Collections
{
    public class TimeoutItem<T> : ITimeoutItem
    {
        public DateTime AccessedTime { get; set ; }

        public bool IsDisposed { get; }
        public T Value { get; set; }

        public TimeoutItem(T value)
        {
            Value = value;
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}