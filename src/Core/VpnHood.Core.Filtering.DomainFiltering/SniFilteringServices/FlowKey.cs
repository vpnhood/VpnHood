using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

// A flow by both its ends. By its source alone, the packets one port sends to another destination, as a socket that
// talks to several servers does, took the verdict of the first destination's name
internal readonly record struct FlowKey(IpEndPointValue Source, IpEndPointValue Destination);
