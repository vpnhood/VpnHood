namespace VpnHood.Tunneling.Messages
{
    public enum ResponseCode
    {
        Ok,
        GeneralError,
        SessionClosed,
        SessionSuppressedBy,
        AccessExpired,
        AccessTrafficOverflow,
    }

}
