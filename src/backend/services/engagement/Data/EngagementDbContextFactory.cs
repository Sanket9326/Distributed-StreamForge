using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StreamForge.Engagement.Api.Data;

public sealed class EngagementDbContextFactory : IDesignTimeDbContextFactory<EngagementDbContext>
{
    public EngagementDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<EngagementDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__EngagementDatabase") ??
            "Host=localhost;Database=streamforge;Username=streamforge;Password=design-time",
            x => x.MigrationsHistoryTable("__ef_migrations_history", EngagementDbContext.Schema))
        .Options);
}
