using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OutboxPatternDemo.Infrastructure
{
    public class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
    {
        public void Configure(EntityTypeBuilder<InboxMessage> builder)
        {
            builder.ToTable("inbox_messages");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id).HasColumnName("id");
            builder.Property(x => x.MessageId).HasColumnName("message_id");
            builder.Property(x => x.Name).HasColumnName("name")
                   .IsRequired()
                   .HasMaxLength(256);
            builder.Property(x => x.Content).HasColumnName("content")
                   .HasColumnType("jsonb");
            builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc")
                   .IsRequired();
            builder.Property(x => x.ProcessedOnUtc).HasColumnName("processed_on_utc");
            builder.Property(x => x.Error).HasColumnName("error");
            builder.Property(x => x.HandlerName).HasColumnName("handler_name")
                   .HasMaxLength(256);

            // 组合唯一索引确保幂等性 - 同一消息同一handler只处理一次
            builder.HasIndex(x => new { x.MessageId, x.HandlerName }).IsUnique();

            // 查询索引
            builder.HasIndex(x => x.ProcessedOnUtc);
        }
    }
}
