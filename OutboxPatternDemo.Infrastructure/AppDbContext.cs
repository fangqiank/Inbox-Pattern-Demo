using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Domain;

namespace OutboxPatternDemo.Infrastructure
{
    public class AppDbContext(DbContextOptions<AppDbContext> options): DbContext(options)
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

            modelBuilder.Entity<User>(builder =>
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
            });
        }
    }
}
