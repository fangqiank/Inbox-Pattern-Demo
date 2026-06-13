using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OutboxPatternDemo.Infrastructure
{
    public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("outbox_messages");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id).HasColumnName("id");
            builder.Property(x => x.MessageId).HasColumnName("message_id");
            builder.Property(x => x.Name).HasColumnName("name")
                .IsRequired()
                .HasMaxLength(256);
            builder.Property(x => x.Content).HasColumnName("content")
                .IsRequired()
                .HasColumnType("jsonb");
            builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc")
                .IsRequired();
            builder.Property(x => x.ProcessedOnUtc).HasColumnName("processed_on_utc");
            builder.Property(x => x.Error).HasColumnName("error");
            builder.Property(x => x.RetryCount).HasColumnName("retry_count")
                .HasDefaultValue(0);

            builder.HasIndex(x => x.MessageId);
            builder.HasIndex(x => x.ProcessedOnUtc);
            builder.HasIndex(x => x.CreatedOnUtc);
        }
    }
}
