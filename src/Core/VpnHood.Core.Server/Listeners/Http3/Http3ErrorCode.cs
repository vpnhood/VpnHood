namespace VpnHood.Net.Http3;

// Application errors used by the listener and its connection budget.
public static class Http3ErrorCode
{
    public const long NoError = 0x100;
    public const long InternalError = 0x102;
    public const long ExcessiveLoad = 0x107;
}
