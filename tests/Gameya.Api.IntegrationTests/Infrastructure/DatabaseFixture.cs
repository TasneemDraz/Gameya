using Gameya.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Gameya.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>, ICollectionFixture<MailpitFixture>, ICollectionFixture<MinioFixture>
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

    /// <summary>A context whose audit columns come from the given interceptor (fixed clock, chosen actor).</summary>
    public AppDbContext CreateAppContext(AuditingInterceptor auditing) => CreateContext(AppConnectionString, auditing);

    /// <summary>A context on another database in the same container, for migration tests.</summary>
    public AppDbContext CreateContextFor(string database) =>
        CreateContext(new SqlConnectionStringBuilder(MigratorConnectionString) { InitialCatalog = database }.ConnectionString);

    private static AppDbContext CreateContext(string connectionString, AuditingInterceptor? auditing = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(builder, connectionString, auditing);
        return new AppDbContext(builder.Options);
    }
}
