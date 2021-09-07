using System;

namespace VpnHood.Server.Exceptions
{
    public class MaintenanceException : Exception
    {
        public MaintenanceException()
        {
        }

        public MaintenanceException(string message) : base(message)
        {
        }
    }
}