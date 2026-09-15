using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StreamForge.Engagement.Api.Data;

namespace StreamForge.Engagement.Api.Migrations;

[DbContext(typeof(EngagementDbContext))]
[Migration("20260914120000_AddSubscriptions")]
public sealed partial class AddSubscriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "subscriptions", schema: "engagement", columns: table => new
        {
            subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
            creator_id = table.Column<Guid>(type: "uuid", nullable: false),
            is_active = table.Column<bool>(type: "boolean", nullable: false),
            created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            source_partition = table.Column<int>(type: "integer", nullable: false),
            source_offset = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_subscriptions", x => new { x.subscriber_id, x.creator_id });
            table.CheckConstraint("ck_subscriptions_not_self", "subscriber_id <> creator_id");
        });
        migrationBuilder.CreateIndex(name: "ix_subscriptions_following", schema: "engagement", table: "subscriptions",
            columns: ["subscriber_id", "created_at_utc", "creator_id"], descending: [false, true, true], filter: "is_active");
        migrationBuilder.CreateIndex(name: "ix_subscriptions_subscribers", schema: "engagement", table: "subscriptions",
            columns: ["creator_id", "created_at_utc", "subscriber_id"], descending: [false, true, true], filter: "is_active");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "subscriptions", schema: "engagement");

}
