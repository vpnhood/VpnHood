using System;

namespace VpnHood.AccessServer.Exceptions;

public class UnregisteredUser : Exception
{
    public UnregisteredUser(string message) : base(message)
    {
    }
}