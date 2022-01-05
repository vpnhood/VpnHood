using System;

namespace VpnHood.Common.Collections
{
    public class DisposabeTimeoutItem<T> : TimeoutItem<T> where T : IDisposable
    {
        public DisposabeTimeoutItem(T value) : base(value)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Value?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}