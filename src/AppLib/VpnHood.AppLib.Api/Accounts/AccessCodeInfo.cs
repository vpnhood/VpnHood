namespace VpnHood.AppLib.Api.Accounts;

/// <summary>Represents the premium code that serves an account.</summary>
/// <remarks>
/// The account backend ranks what the account holds, what is paid for now first, then the best of the rest, and names
/// one code at each read: the app never picks, and nothing needs repair when a code ends.
/// </remarks>
public class AccessCodeInfo
{
    /// <summary>Gets or sets the premium code.</summary>
    public required string AccessCode { get; set; }

    /// <summary>
    /// Gets or sets when the code expires, where the account backend can tell, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// It informs what the UI shows and when the app reads the account again; it does not end the code, as whether a code
    /// works is the access server's answer at connect.
    /// </remarks>
    public DateTime? ExpirationTime { get; set; }
}
