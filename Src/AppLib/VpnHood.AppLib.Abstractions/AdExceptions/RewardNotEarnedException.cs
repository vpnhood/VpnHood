namespace VpnHood.AppLib.Abstractions.AdExceptions;

public class RewardNotEarnedException : AdException
{
    public RewardNotEarnedException(string message) : base(message)
    {
    }

    public RewardNotEarnedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}