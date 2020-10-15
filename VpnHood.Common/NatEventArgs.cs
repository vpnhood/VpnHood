using System;

namespace VpnHood
{
    public class NatEventArgs : EventArgs 
    {
        public NatItem NatItem { get; }

        public NatEventArgs(NatItem natItem)
        {
            NatItem = natItem;
        }
    }
}