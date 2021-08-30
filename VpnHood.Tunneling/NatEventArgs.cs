using System;

namespace VpnHood.Tunneling
{
    public class NatEventArgs : EventArgs
    {
        public NatEventArgs(NatItem natItem)
        {
            NatItem = natItem;
        }

        public NatItem NatItem { get; }
    }
}