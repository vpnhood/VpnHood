namespace VpnHood.Messages
{
    public enum ResponseCode
    {
        Ok,
        GeneralError,
        SessionClosed, //todo: test
        SessionSuppressedBy,
        AccessExpired, //todo: test
        AccessTrafficOverflow, //todo: test
    }

}
