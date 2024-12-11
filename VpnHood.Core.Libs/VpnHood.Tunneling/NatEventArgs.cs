namespace VpnHood.Tunneling;

public class NatEventArgs(NatItem natItem) : EventArgs
{
    public NatItem NatItem { get; } = natItem;
}