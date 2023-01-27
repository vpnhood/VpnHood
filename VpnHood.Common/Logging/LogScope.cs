using System;
using System.Collections.Generic;

namespace VpnHood.Common.Logging;

public class LogScope
{
    public List<Tuple<string, object?>> Data { get; } = new();
}