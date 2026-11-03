using Gameya.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Gameya.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private const string MigratorPassword = "migrator-test-pw";
    private const string AppPassword = "app-test-pw";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("gameya")
        .Build();

    public string AppConnectionString { get; private set; } = null!;
    public string MigratorConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var rolesSql = Path.Combine(FindRepoRoot(), "infra", "sql", "roles.sql");
        const string target = "/tmp/roles.sql";
        await _container.CopyAsync(await File.ReadAllBytesAsync(rolesSql), target);
        var result = await _container.ExecAsync(
        [
            "psql", "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d", "gameya",
            "-v", $"migrator_password={MigratorPassword}", "-v", $"app_password={AppPassword}",
            "-f", target
        ]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"roles.sql failed: {result.Stderr}");

        MigratorConnectionString = ConnectionStringFor("gameya_migrator", MigratorPassword);
        AppConnectionString = ConnectionStringFor("gameya_app", AppPassword);

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

    private string ConnectionStringFor(string user, string password) =>
        $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(5432)};Database=gameya;Username={user};Password={password}";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gameya.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Gameya.sln not found above test binaries.");
    }
}
