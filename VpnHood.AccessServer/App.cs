using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using VpnHood.AccessServer.Settings;

namespace VpnHood.AccessServer
{
    public static class App
    {
        public static string ConnectionString { get; set; }
        public static AuthProviderItem[] AuthProviderItems { get; set; }
        public static SqlConnection OpenConnection() => new SqlConnection(ConnectionString);
        public static string AgentUserId { get; set; }
        public static void Configure(IConfiguration configuration)
        {
            //load settings
            AuthProviderItems = configuration.GetSection("AuthProviders").Get<AuthProviderItem[]>() ?? System.Array.Empty<AuthProviderItem>();
            AgentUserId = configuration.GetValue<string>("AgentUserId");
            ConnectionString = configuration.GetValue<string>("ConnectionString");
        }
    }
}
