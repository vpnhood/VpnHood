using System;

namespace VpnHood.AccessServer.Exceptions
{
    public sealed class QuotaException : Exception
    {
        public QuotaException(string? message, string quotaName, string quotaValue) 
            : base(message)
        {
            Data.Add("QuotaName", quotaName);
            Data.Add("QuotaValue", quotaValue);
        }
    }
}