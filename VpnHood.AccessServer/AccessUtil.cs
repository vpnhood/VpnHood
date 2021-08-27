using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Data.SqlClient;
using VpnHood.AccessServer.Exceptions;

namespace VpnHood.AccessServer
{
    public static class AccessUtil
    {
        // 2601 unique index
        // 2627 PRIMARY KEY duplicate
        public static bool IsAlreadyExistsException(Exception ex)
        {
            return ex is AlreadyExistsException ||
                   ex.InnerException is SqlException {Number: 2601 or 2627} ||
                   ex is SqlException {Number: 2601 or 2627};
        }

        public static bool IsNotExistsException(Exception ex)
        {
            return ex is KeyNotFoundException ||
                   ex is InvalidOperationException && ex.Message.Contains("Sequence contains no elements");
        }

        public static string ValidateIpEndPoint(string ipEndPoint)
        {
            return IPEndPoint.Parse(ipEndPoint).ToString();
        }
    }
}