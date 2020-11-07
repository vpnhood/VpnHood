using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using VpnHood.AccessServer.Settings;

namespace VpnHood.AccessServer
{
    public static class App
    {
        public static AuthProviderItem[] AuthProviderItems { get; set; }
        public static SqlConnection OpenConnection() => new SqlConnection("Server=.; initial catalog=Vh; Integrated Security=true;");

        public static void Configure(IConfiguration configuration)
        {
            //load settings
            AuthProviderItems = configuration.GetSection("AuthProviders").Get<AuthProviderItem[]>() ?? new AuthProviderItem[0];
        }
    }
}
