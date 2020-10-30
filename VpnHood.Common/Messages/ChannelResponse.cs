namespace VpnHood.Messages
{
    public class ChannelResponse
    {
        public enum Code
        {
            Ok,
            SessionClosed, //todo: test
            Error,
            Suppressed
        }

        public Code ResponseCode { get; set; }
        public string ErrorMessage { get; set; }
        public SuppressType SuppressedBy { get; set; }
    }


}
