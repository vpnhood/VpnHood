using System.Runtime.InteropServices;

namespace VpnHood.Core.Toolkit.Exceptions;

public class PInvokeException : ExternalException
{
    public PInvokeException(string message, int errorCode)
        : base(message, errorCode)
    {
    }

    public PInvokeException(string message)
        : base(message, Marshal.GetLastWin32Error())
    {
    }
}