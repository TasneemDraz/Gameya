using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gameya.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` only; it never connects when merely generating migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Migrator")
            ?? "Host=localhost;Database=gameya;Username=gameya_migrator;Password=gameya_migrator_dev";
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(builder, connectionString);
        return new AppDbContext(builder.Options);
    }
}
