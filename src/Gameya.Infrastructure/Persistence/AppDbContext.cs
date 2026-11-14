using Gameya.Application.Abstractions;
using Gameya.Domain.Activity;
using Gameya.Domain.Auth;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Gameya.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gameya.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ActivityLogEntry> ActivityLog => Set<ActivityLogEntry>();
    public DbSet<GameyaCircle> Gameyas => Set<GameyaCircle>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<JoinRequest> JoinRequests => Set<JoinRequest>();
    public DbSet<PendingInvite> PendingInvites => Set<PendingInvite>();

    /// <summary>
    /// Single place that defines provider, naming convention and interceptors. Without an auditing interceptor
    /// (design time, <c>--migrate</c>, tests) audit columns use the system clock and no actor.
    /// </summary>
    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder builder, string connectionString, AuditingInterceptor? auditing = null) =>
        builder
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                ActivityLogImmutabilityInterceptor.Instance,
                auditing ?? new AuditingInterceptor(new SystemClock(), null));

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
