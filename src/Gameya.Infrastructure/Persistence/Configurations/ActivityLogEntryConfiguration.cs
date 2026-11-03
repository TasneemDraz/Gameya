using Gameya.Domain.Activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gameya.Infrastructure.Persistence.Configurations;

public sealed class ActivityLogEntryConfiguration : IEntityTypeConfiguration<ActivityLogEntry>
{
    public void Configure(EntityTypeBuilder<ActivityLogEntry> builder)
    {
        builder.ToTable("activity_log");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.DetailsJson).HasColumnName("details").HasColumnType("jsonb");

        builder.HasIndex(e => new { e.GameyaId, e.OccurredAt });
    }
}
