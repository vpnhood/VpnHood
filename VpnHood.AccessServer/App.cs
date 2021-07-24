using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Data.SqlClient;
using System.Reflection;
using VpnHood.AccessServer.Settings;

namespace VpnHood.AccessServer
{
    public static class App
    {
        public static string ProductName => ((AssemblyProductAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyProductAttribute), false)).Product;
        public static string ConnectionString { get; set; }
        public static AuthProviderItem[] AuthProviderItems { get; set; }
        public static SqlConnection OpenConnection() => new SqlConnection(ConnectionString); //todo remove
        public static string AdminUserId { get; set; }
        public static string VpnServerUserId { get; set; }
        public static void Configure(IConfiguration configuration)
        {
            //load settings
            AuthProviderItems = configuration.GetSection("AuthProviders").Get<AuthProviderItem[]>() ?? System.Array.Empty<AuthProviderItem>();
            AdminUserId = configuration.GetValue<string>("AgentUserId");
            VpnServerUserId = configuration.GetValue<string>("VpnServerUserId");
            ConnectionString = configuration.GetValue<string>("ConnectionString");
        }
    }
}
