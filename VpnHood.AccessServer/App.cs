using System.Data.SqlClient;

namespace VpnHood.AccessServer
{
    public static class App
    {
        public static SqlConnection OpenConnection() => new SqlConnection("Server=.; initial catalog=Vh; Integrated Security=true;");
    }
}
