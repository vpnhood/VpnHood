namespace VpnHood.Core.Common.Exceptions;

public class ShowAdNoUiException : ShowAdException
{
    private const string Msg = "Could not show any ad because the app window was not open.";

    public ShowAdNoUiException() : base(Msg)
    {
    }

    public ShowAdNoUiException(Exception innerException) : base(Msg, innerException)
    {
    }

    public ShowAdNoUiException(string msg) : base(msg)
    {
    }

    public ShowAdNoUiException(string msg, Exception innerException) : base(msg, innerException)
    {
    }
}