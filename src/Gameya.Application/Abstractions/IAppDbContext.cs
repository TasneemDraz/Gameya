using Gameya.Domain.Activity;
using Gameya.Domain.Auth;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gameya.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<OtpChallenge> OtpChallenges { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<ActivityLogEntry> ActivityLog { get; }

    // Writes go through the GameyaCircle root; the child sets are read-only projection sources.
    DbSet<GameyaCircle> Gameyas { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<JoinRequest> JoinRequests { get; }
    DbSet<PendingInvite> PendingInvites { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    IEnumerable<Entity> TrackedEntitiesWithEvents();
}
