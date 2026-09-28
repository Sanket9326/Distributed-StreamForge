using Microsoft.EntityFrameworkCore;
using StreamForge.Engagement.Api.Data.Entities;

namespace StreamForge.Engagement.Api.Data;

public sealed class EngagementDbContext(DbContextOptions<EngagementDbContext> options) : DbContext(options)
{
    public const string Schema = "engagement";
    public DbSet<KnownVideo> Videos => Set<KnownVideo>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<UserSubscription> Subscriptions => Set<UserSubscription>();
    public DbSet<WatchHistory> WatchHistory => Set<WatchHistory>();
    public DbSet<VideoView> VideoViews => Set<VideoView>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ConsumedKafkaMessage> ConsumedMessages => Set<ConsumedKafkaMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<WatchHistory>(entity =>
        {
            entity.ToTable("watch_history", table => table.HasCheckConstraint("ck_watch_history_progress",
                "position_ms >= 0 AND duration_ms > 0 AND position_ms <= duration_ms AND duration_ms <= 9007199254740991"));
            entity.HasKey(x => new { x.UserId, x.VideoId }).HasName("pk_watch_history");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.VideoId).HasColumnName("video_id");
            entity.Property(x => x.PositionMs).HasColumnName("position_ms");
            entity.Property(x => x.DurationMs).HasColumnName("duration_ms");
            entity.Property(x => x.IsCompleted).HasColumnName("is_completed");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(x => x.SourcePartition).HasColumnName("source_partition");
            entity.Property(x => x.SourceOffset).HasColumnName("source_offset");
            entity.HasIndex(x => new { x.UserId, x.UpdatedAtUtc, x.VideoId })
                .IsDescending(false, true, true).HasDatabaseName("ix_watch_history_user_updated");
        });
        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.ToTable("subscriptions", table => table.HasCheckConstraint(
                "ck_subscriptions_not_self", "subscriber_id <> creator_id"));
            entity.HasKey(x => new { x.SubscriberId, x.CreatorId }).HasName("pk_subscriptions");
            entity.Property(x => x.SubscriberId).HasColumnName("subscriber_id");
            entity.Property(x => x.CreatorId).HasColumnName("creator_id");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(x => x.SourcePartition).HasColumnName("source_partition");
            entity.Property(x => x.SourceOffset).HasColumnName("source_offset");
            entity.HasIndex(x => new { x.SubscriberId, x.CreatedAtUtc, x.CreatorId })
                .IsDescending(false, true, true).HasFilter("is_active").HasDatabaseName("ix_subscriptions_following");
            entity.HasIndex(x => new { x.CreatorId, x.CreatedAtUtc, x.SubscriberId })
                .IsDescending(false, true, true).HasFilter("is_active").HasDatabaseName("ix_subscriptions_subscribers");
        });

        modelBuilder.Entity<KnownVideo>(entity =>
        {
            entity.ToTable("videos");
            entity.HasKey(x => x.VideoId).HasName("pk_videos");
            entity.Property(x => x.VideoId).HasColumnName("video_id").ValueGeneratedNever();
            entity.Property(x => x.AvailableAtUtc).HasColumnName("available_at_utc");
        });

        modelBuilder.Entity<Reaction>(entity =>
        {
            entity.ToTable("reactions", table => table.HasCheckConstraint(
                "ck_reactions_value", "value IN ('like', 'dislike')"));
            entity.HasKey(x => new { x.VideoId, x.UserId }).HasName("pk_reactions");
            entity.Property(x => x.VideoId).HasColumnName("video_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Value).HasColumnName("value").HasMaxLength(10);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(x => x.SourcePartition).HasColumnName("source_partition");
            entity.Property(x => x.SourceOffset).HasColumnName("source_offset");
            entity.HasOne(x => x.Video).WithMany().HasForeignKey(x => x.VideoId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_reactions_videos");
        });

        modelBuilder.Entity<VideoView>(entity =>
        {
            entity.ToTable("video_views", table => table.HasCheckConstraint("ck_video_views_count", "count >= 0"));
            entity.HasKey(x => x.VideoId).HasName("pk_video_views");
            entity.Property(x => x.VideoId).HasColumnName("video_id").ValueGeneratedNever();
            entity.Property(x => x.Count).HasColumnName("count");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasOne(x => x.Video).WithOne().HasForeignKey<VideoView>(x => x.VideoId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_video_views_videos");
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.ToTable("comments");
            entity.HasKey(x => x.Id).HasName("pk_comments");
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.VideoId).HasColumnName("video_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Body).HasColumnName("body").HasMaxLength(2_000);
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasOne(x => x.Video).WithMany().HasForeignKey(x => x.VideoId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_comments_videos");
            entity.HasIndex(x => new { x.VideoId, x.CreatedAtUtc, x.Id })
                .IsDescending(false, true, true).HasDatabaseName("ix_comments_video_created");
        });

        modelBuilder.Entity<ConsumedKafkaMessage>(entity =>
        {
            entity.ToTable("consumed_messages");
            entity.HasKey(x => new { x.Topic, x.Partition, x.Offset }).HasName("pk_consumed_messages");
            entity.Property(x => x.Topic).HasColumnName("topic").HasMaxLength(249);
            entity.Property(x => x.Partition).HasColumnName("partition");
            entity.Property(x => x.Offset).HasColumnName("offset");
            entity.Property(x => x.EventId).HasColumnName("event_id");
            entity.Property(x => x.ConsumedAtUtc).HasColumnName("consumed_at_utc");
            entity.Property(x => x.RejectionCode).HasColumnName("rejection_code").HasMaxLength(100);
            entity.HasIndex(x => x.EventId).IsUnique().HasDatabaseName("ux_consumed_messages_event_id");
        });
    }
}
