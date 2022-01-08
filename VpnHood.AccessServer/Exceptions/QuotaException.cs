using System;

namespace VpnHood.AccessServer.Exceptions;

public sealed class QuotaException : Exception
{
    public QuotaException(string quotaName, long quotaValue, string? message = null) 
        : base(message ?? $"You can not have more than {quotaValue} {quotaName}")
    {
        Data.Add("QuotaName", quotaName);
        Data.Add("QuotaValue", quotaValue);
    }
}