namespace VpnHood.AppLib.App.Android.Activities;

/// <summary>Represents the access keys a head's main activity takes: by a link's scheme, or by a file's type.</summary>
public class AndroidMainActivityOptions
{
    /// <summary>Gets the schemes of a link that is an access key itself, as <c>vh</c>. The default is none.</summary>
    public string[] AccessKeySchemes { get; init; } = [];

    /// <summary>Gets the MIME types of an access key's file the activity opens. The default is none.</summary>
    public string[] AccessKeyMimes { get; init; } = [];
}