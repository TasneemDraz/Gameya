using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gameya.Infrastructure.Persistence.Configurations;

public sealed class GameyaCircleConfiguration : IEntityTypeConfiguration<GameyaCircle>
{
    public void Configure(EntityTypeBuilder<GameyaCircle> builder)
    {
        builder.ToTable("gameyas");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Ignore(g => g.DomainEvents);
        builder.Ignore(g => g.Pot);
        builder.Ignore(g => g.RoundCount);
        builder.Ignore(g => g.TotalPerShare);
        builder.Ignore(g => g.EndDate);
        builder.Ignore(g => g.AssignedShares);
        builder.Ignore(g => g.ReservedShares);
        builder.Ignore(g => g.AvailableShares);
        builder.Ignore(g => g.IsFull);

        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(g => g.InviteCode).HasMaxLength(6);
        builder.Property(g => g.CancelReason).HasMaxLength(500);
        builder.Property(g => g.Currency).HasColumnType("char(3)");
        builder.Property(g => g.Name).HasMaxLength(60);
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.ContributionAmount).HasColumnType("decimal(18,2)");
        builder.Property(g => g.Frequency).HasConversion<string>().HasMaxLength(20);
        builder.Property(g => g.StartDate).HasColumnType("date");
        builder.Property(g => g.PayoutTiming).HasConversion<string>().HasMaxLength(20);
        builder.Property(g => g.OrderMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(g => g.RulesText).HasMaxLength(2000);

        // Guards the whole aggregate: the auditing interceptor updates this row whenever a child changes.
        builder.Property<byte[]>("row_version").IsRowVersion();

        builder.HasOne<User>().WithMany().HasForeignKey(g => g.OrganizerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(g => g.Memberships).WithOne().HasForeignKey(m => m.GameyaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(g => g.JoinRequests).WithOne().HasForeignKey(r => r.GameyaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(g => g.PendingInvites).WithOne().HasForeignKey(i => i.GameyaId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(g => g.InviteCode).IsUnique().HasFilter("invite_code IS NOT NULL");
        builder.HasIndex(g => new { g.OrganizerId, g.Status });
    }
}

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Ignore(m => m.DomainEvents);

        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.RemovalReason).HasMaxLength(500);

        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(m => m.Shares).WithOne().HasForeignKey("MembershipId").IsRequired().OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.GameyaId, m.UserId }).IsUnique().HasFilter("[status] = 'Active'");
        builder.HasIndex(m => m.UserId);
    }
}

public sealed class MemberShareConfiguration : IEntityTypeConfiguration<MemberShare>
{
    public void Configure(EntityTypeBuilder<MemberShare> builder)
    {
        builder.ToTable("member_shares");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Ignore(s => s.DomainEvents);

        builder.Property(s => s.Preferences)
            .HasConversion(PreferencesConversion.Converter, PreferencesConversion.Comparer)
            .HasColumnType(PreferencesConversion.ColumnType);
    }
}

public sealed class JoinRequestConfiguration : IEntityTypeConfiguration<JoinRequest>
{
    public void Configure(EntityTypeBuilder<JoinRequest> builder)
    {
        builder.ToTable("join_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Ignore(r => r.DomainEvents);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.RejectReason).HasMaxLength(500);

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Shares).WithOne().HasForeignKey("JoinRequestId").IsRequired().OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.GameyaId, r.UserId }).IsUnique().HasFilter("[status] = 'Pending'");
    }
}

public sealed class JoinRequestShareConfiguration : IEntityTypeConfiguration<JoinRequestShare>
{
    public void Configure(EntityTypeBuilder<JoinRequestShare> builder)
    {
        builder.ToTable("join_request_shares");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Ignore(s => s.DomainEvents);

        builder.Property(s => s.Preferences)
            .HasConversion(PreferencesConversion.Converter, PreferencesConversion.Comparer)
            .HasColumnType(PreferencesConversion.ColumnType);
    }
}

public sealed class PendingInviteConfiguration : IEntityTypeConfiguration<PendingInvite>
{
    public void Configure(EntityTypeBuilder<PendingInvite> builder)
    {
        builder.ToTable("pending_invites");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Ignore(i => i.DomainEvents);

        builder.Property(i => i.Phone).HasConversion(PhoneNumberConverter.Instance).HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.GameyaId, i.Phone }).IsUnique().HasFilter("[status] = 'Pending'");
        builder.HasIndex(i => i.Phone);
    }
}
