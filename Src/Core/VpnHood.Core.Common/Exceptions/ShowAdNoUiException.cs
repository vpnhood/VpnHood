namespace VpnHood.Core.Common.Exceptions;

public class ShowAdNoUiException : ShowAdException
{
    private const string Msg = "Could not show any ad because the app window was not open.";

    public ShowAdNoUiException() : base(Msg)
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        Data["LocalizationCode"] = "ERROR_SHOW_AD_APP_NOT_OPEN";
    }

    public ShowAdNoUiException(Exception innerException) : base(Msg, innerException)
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        Data["LocalizationCode"] = "ERROR_SHOW_AD_APP_NOT_OPEN";
    }
}