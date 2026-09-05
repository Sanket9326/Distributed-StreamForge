using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StreamForge.Identity.Api.Data;

/// <summary>Owns the isolated Identity PostgreSQL schema.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>Gets persisted accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        var user = modelBuilder.Entity<User>();
        user.ToTable("users");
        user.HasKey(x => x.Id);
        user.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        user.Property(x => x.Username).HasColumnName("username").HasMaxLength(50).IsRequired();
        user.Property(x => x.NormalizedUsername).HasColumnName("normalized_username").HasMaxLength(100).IsRequired();
        user.Property(x => x.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        user.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(508).IsRequired();
        user.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
        user.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        user.Property(x => x.Dob).HasColumnName("dob");
        user.Property(x => x.Address).HasColumnName("address").HasMaxLength(1000);
        user.HasIndex(x => x.NormalizedUsername).IsUnique();
        user.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}

/// <summary>Creates migration contexts without requiring a running application.</summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <inheritdoc />
    public IdentityDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__IdentityDatabase") ??
            "Host=localhost;Database=streamforge", x => x.MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
        .Options);
}
