namespace VpnHood.Core.Server.ProbeResistance;

// what a connection does after the web front answered a request on it
internal enum WebFrontNext
{
    NextRequest,
    Close,
    Reset // as Kestrel resets a connection whose body comes too slowly
}
