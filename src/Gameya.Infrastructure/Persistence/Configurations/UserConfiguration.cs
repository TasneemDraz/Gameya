using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gameya.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Ignore(u => u.DomainEvents);

        // Phone is null after account deletion; EF never runs the converter for null.
        builder.Property(u => u.Phone).HasConversion(p => p!.E164, s => PhoneNumber.Parse(s));
        builder.Property(u => u.Language).HasConversion<string>();

        // Roles is exposed read-only; the list lives in the backing field.
        builder.Ignore(u => u.Roles);
        builder.PrimitiveCollection<List<string>>("_roles").HasColumnName("roles").HasColumnType("nvarchar(max)");

        builder.PrimitiveCollection(u => u.EmailSendTimes).HasColumnType("nvarchar(max)");

        // Optimistic concurrency via a rowversion column: parallel email changes cannot all pass the send limit,
        // and a photo upload cannot land on a row that DeleteAccount anonymized in between.
        builder.Property<byte[]>("row_version").IsRowVersion();

        builder.HasIndex(u => u.Phone).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("email IS NOT NULL");
        builder.HasIndex(u => u.PendingEmailTokenHash);
    }
}
