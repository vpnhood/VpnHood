namespace VpnHood.AppLib.Abstractions;

public class SystemBarsInfo
{
    public static SystemBarsInfo Default = new() { TopHeight = 0, BottomHeight = 0 };
    public required int TopHeight { get; init; }
    public required int BottomHeight { get; init; }
}