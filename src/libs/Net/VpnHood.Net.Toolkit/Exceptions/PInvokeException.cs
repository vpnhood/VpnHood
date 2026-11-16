using System.Runtime.InteropServices;

namespace VpnHood.Net.Toolkit.Exceptions;

/// <summary>
/// The exception that is thrown when a native call fails, with the error code it returned.
/// </summary>
public class PInvokeException : ExternalException
{
    /// <summary>Initializes a new instance of the <see cref="PInvokeException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="errorCode">The error code the native call returned.</param>
    public PInvokeException(string message, int errorCode)
        : base(message, errorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PInvokeException"/> class with the last native error of the
    /// thread, from <see cref="Marshal.GetLastWin32Error"/>.
    /// </summary>
    /// <param name="message">The message.</param>
    public PInvokeException(string message)
        : base(message, Marshal.GetLastWin32Error())
    {
    }
}