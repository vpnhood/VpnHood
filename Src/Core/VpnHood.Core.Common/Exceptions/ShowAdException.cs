namespace VpnHood.Core.Common.Exceptions;

public class ShowAdException : AdException
{
    public string? AdNetworkName { get; set; }
    public ShowAdException(string message) : base(message)
    {
    }

    public ShowAdException(string message, Exception innerException) : base(message, innerException)
    {
    }
}