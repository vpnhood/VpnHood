namespace VpnHood.Messages
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
