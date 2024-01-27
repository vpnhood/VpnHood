using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Test.Helper;

internal class VhContextManual(string connectionString) : VhContext
{
    private readonly string? _connectionString = connectionString;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(_connectionString);
    }

}