using System;

namespace VpnHood.Common.Exceptions
{
    public class MaintenanceException : Exception
    {
        public MaintenanceException()
            : base("The server is in maintenance mode! Please try again later.")
        {
        }

        public MaintenanceException(string message) 
            : base(message)
        {
        }
    }
}