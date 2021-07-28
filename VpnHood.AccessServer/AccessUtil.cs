using Microsoft.Data.SqlClient;
using System;
using System.Net;

namespace VpnHood.AccessServer
{
    public static class AccessUtil
    {
        public static bool IsAlreadyExistsException(Exception ex) =>
            ex.InnerException is SqlException sqlException && sqlException.Number == 2601 ||
            ex is SqlException sqlException2 && sqlException2.Number == 2601;

        public static string ValidateIpEndPoint(string ipEndPoint) =>
            IPEndPoint.Parse(ipEndPoint).ToString();
    }
}
