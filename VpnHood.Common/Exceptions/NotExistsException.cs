using System;
using System.Collections.Generic;

namespace VpnHood.Common.Exceptions;

public class NotExistsException : Exception
{
    public static bool Is(Exception ex)
    {
        if (ex is NotExistsException or KeyNotFoundException)
            return true;

        if (ex is InvalidOperationException && (
                ex.Message.Contains("Sequence contains no matching element") ||
                ex.Message.Contains("Sequence contains no elements")))
            return true;

        return ex.InnerException != null && Is(ex.InnerException);
    }   
}