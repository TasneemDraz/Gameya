using Gameya.Application.Abstractions;
using Gameya.Domain.Activity;
using Gameya.Domain.Auth;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gameya.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ActivityLogEntry> ActivityLog => Set<ActivityLogEntry>();

    /// <summary>Single place that defines provider, naming convention and interceptors.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(ActivityLogImmutabilityInterceptor.Instance);

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        await Database.BeginTransactionAsync(cancellationToken);

    public IEnumerable<Entity> TrackedEntitiesWithEvents() =>
        ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
