using Gameya.Domain.Activity;
using Gameya.Domain.Auth;
using Gameya.Domain.Common;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    IEnumerable<Entity> TrackedEntitiesWithEvents();
}
