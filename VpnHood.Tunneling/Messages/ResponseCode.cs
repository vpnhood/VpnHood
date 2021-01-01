namespace VpnHood.Tunneling.Messages
{
    public enum ResponseCode
    {
        Ok,
        GeneralError,
        InvalidSessionId,
        SessionClosed,
        SessionSuppressedBy,
        AccessExpired,
        AccessTrafficOverflow,
    }

}
