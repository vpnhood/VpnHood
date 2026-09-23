namespace VpnHood.AppLib.App.WebHosting;

// How the app gets its web hosts, since a host needs the API to exist first while the app must say at
// construction whether pairing is supported. A head hands in a factory; the app calls it once per
// host, the first time anything asks for that one, and owns what comes back.
//
// Two hosts, one implementation bound differently: the local one is what this device loads from,
// the remote one is what a phone pairs with. A head that shows a native UI never asks for the first.
//
// Everything it hands over is in WebHostCreateParams, so an implementation needs nothing of the engine -
// only the API contract it puts on HTTP.
public interface IAppWebHostFactory
{
    IAppWebHost CreateLocal(WebHostCreateParams createParams);
    IAppWebHost CreateRemote(WebHostCreateParams createParams);
}
