using Gameya.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gameya.Infrastructure.Persistence.Configurations;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges");
        builder.Ignore(c => c.DomainEvents);

        // The aggregate has no identity of its own; a shadow key keeps the domain type untouched.
        builder.Property<Guid>("id").ValueGeneratedOnAdd();
        builder.HasKey("id");

        builder.Property(c => c.Phone).HasConversion(PhoneNumberConverter.Instance);
        builder.HasIndex(c => c.Phone).IsUnique();

        // Optimistic concurrency via PostgreSQL's xmin system column.
        builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }
}
