namespace VpnHood.Manager.Common.Utils;

public static class VhValidator
{
    public static void ValidateSwapMemory(int? value, string valueName)
    {
        switch (value)
        {
            case < 10:
                throw new ArgumentException($"You can not set {valueName} smaller than {10} megabytes.", valueName);
            case > 100_000:
                throw new ArgumentException($"You can not set {valueName} larger than {100_000} megabytes.", valueName);
        }
    }

}