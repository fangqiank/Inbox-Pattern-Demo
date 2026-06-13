using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OutboxPatternDemo.Domain;

namespace OutboxPatternDemo.Infrastructure
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("users");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id");
            builder.Property(x => x.Username).HasColumnName("username").IsRequired().HasMaxLength(100);

            builder.OwnsMany(x => x.Following, fb =>
            {
                fb.ToTable("user_follows");
                fb.HasKey("FollowerId", "FollowedId");
                fb.WithOwner().HasForeignKey("FollowerId");
                fb.Property(x => x.FollowerId).HasColumnName("follower_id");
                fb.Property(x => x.FollowedId).HasColumnName("followed_id");
                fb.Property(x => x.FollowedOn).HasColumnName("followed_on").IsRequired();
            });

            builder.Ignore(x => x.DomainEvents);
        }
    }
}
