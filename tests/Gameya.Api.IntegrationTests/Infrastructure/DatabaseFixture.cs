using Gameya.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Gameya.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>, ICollectionFixture<MailpitFixture>
{
    public const string Name = "database";
}

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string AppConnectionString { get; private set; } = null!;
    public string MigratorConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // One sa login serves both roles, as on the host (one login may serve App and Migrator).
        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "gameya",
            TrustServerCertificate = true
        }.ConnectionString;
        MigratorConnectionString = connectionString;
        AppConnectionString = connectionString;

        await using var migrator = CreateContext(MigratorConnectionString);
        await migrator.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AppDbContext CreateAppContext() => CreateContext(AppConnectionString);

    private static AppDbContext CreateContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(builder, connectionString);
        return new AppDbContext(builder.Options);
    }
}
