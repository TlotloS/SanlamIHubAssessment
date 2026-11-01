using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SanlamIHubAssessment.Data;

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("activity", table =>
        {
            table.HasCheckConstraint("ck_activity_version_positive", "version > 0");
            table.HasCheckConstraint("ck_activity_currency_format", "currency ~ '^[A-Z]{3}$'");
        });

        // Keyed on the upstream identity, so Postgres rejects a second row for the same activity.
        builder.HasKey(a => new { a.Source, a.ExternalId });

        builder.Property(a => a.Source).HasColumnName("source").HasMaxLength(100);
        builder.Property(a => a.ExternalId).HasColumnName("external_id").HasMaxLength(100);
        builder.Property(a => a.Version).HasColumnName("version");
        builder.Property(a => a.IntermediaryId).HasColumnName("intermediary_id").HasMaxLength(100);
        builder.Property(a => a.ActivityType).HasColumnName("activity_type").HasMaxLength(100);
        builder.Property(a => a.OccurredAt).HasColumnName("occurred_at");
        builder.Property(a => a.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
        builder.Property(a => a.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(a => a.ReceivedAt).HasColumnName("received_at").HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        // Covers the statement query, which filters by intermediary and a time range.
        builder.HasIndex(a => new { a.IntermediaryId, a.OccurredAt }).HasDatabaseName("ix_activity_intermediary_occurred");
    }
}
