using Microsoft.AspNetCore.Components;
using System;

namespace VpnHood.AccessServer.UI
{
    public static class AppState
    {
        public static Guid? ProjectId { get; set; }
        
        public static string Foo()
        {
            return "ddd";
        }
    }
}