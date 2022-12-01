using System;

namespace VpnHood.Common.Exceptions;

public class AnotherInstanceIsRunning : Exception
{
    public AnotherInstanceIsRunning() : base("Another instance is running.")
    {
    }
}
