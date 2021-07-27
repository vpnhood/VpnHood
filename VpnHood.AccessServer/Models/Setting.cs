using System;
using System.Collections.Generic;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class Setting
    {
        public int SettingId { get; set; }
        public bool IsProduction { get; set; }
    }
}
