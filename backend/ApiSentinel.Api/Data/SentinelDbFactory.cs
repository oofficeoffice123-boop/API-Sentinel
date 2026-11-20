using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ApiSentinel.Api;

namespace ApiSentinel.Api.Data;

public sealed class SentinelDbFactory : IDesignTimeDbContextFactory<SentinelDb>
{
    public SentinelDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SentinelDb>()
            .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Server=(localdb)\\MSSQLLocalDB;Database=ApiSentinel;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new SentinelDb(options);
    }
}