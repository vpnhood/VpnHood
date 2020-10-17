using System;
using System.IO;

namespace VpnHood.Client.App
{
    public interface IAppProvider
    {
        IDevice Device { get; }
    }
}