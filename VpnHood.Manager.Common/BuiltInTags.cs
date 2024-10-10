namespace VpnHood.Manager.Common;

public static  class BuiltInTags
{
    public const string Premium = "#premium";
    public const string Trial = "#trial";
    public static string[] PlanTags { get; } = [Premium, Trial];
}