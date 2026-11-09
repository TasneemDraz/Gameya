using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gameya.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef` and by the self-contained efbundle, which is the production migration path (R31).
/// It requires ConnectionStrings__Migrator and never falls back to a default, so a bundle cannot silently
/// migrate the wrong database. Local use: `ConnectionStrings__Migrator="Server=localhost,1433;Database=gameya;User Id=sa;Password=...;TrustServerCertificate=True" dotnet ef ...`
/// (any value works for `migrations add` and for building the bundle; neither connects).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Migrator");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings__Migrator is not set. Set this environment variable to a SQL Server connection string (a login that can alter the schema) before running dotnet ef or the migrations bundle.");
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(builder, connectionString);
        return new AppDbContext(builder.Options);
    }
}
