using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Net;
using VpnHood.AccessServer.Exceptions;

namespace VpnHood.AccessServer
{
    public static class AccessUtil
    {
        // 2601 unique index
        // 2627 PRIMARY KEY duplicate
        public static bool IsAlreadyExistsException(Exception ex) =>
            ex is AlreadyExistsException ||
            ex.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627 ||
            ex is SqlException sqlException2 && sqlException2.Number is 2601 or 2627;

        public static bool IsNotExistsException(Exception ex) =>
            ex is KeyNotFoundException ||
            ex is InvalidOperationException && ex.Message.Contains("Sequence contains no elements");

        public static string ValidateIpEndPoint(string ipEndPoint) =>
            IPEndPoint.Parse(ipEndPoint).ToString();

    }
}
