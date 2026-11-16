namespace VpnHood.Net.Http3;

internal interface IHttp3GracefulCloseCallbacks
{
    void BeginGracefulClose();
    void OnControlInputPending();
}
