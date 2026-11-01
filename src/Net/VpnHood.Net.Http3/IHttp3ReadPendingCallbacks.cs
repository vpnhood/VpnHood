namespace VpnHood.Net.Http3;

internal interface IHttp3ReadPendingCallbacks
{
    void OnReadPending(Http3StreamReader reader);
}
