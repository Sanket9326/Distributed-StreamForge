using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StreamForge.Feed.Api.Data;

#nullable disable

namespace StreamForge.Feed.Api.Migrations;

[DbContext(typeof(FeedDbContext))]
[Migration("20260831120000_InitialFeed")]
public partial class InitialFeed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "feed");

        migrationBuilder.CreateTable(
            name: "consumed_messages",
            schema: "feed",
            columns: table => new
            {
                topic = table.Column<string>(type: "character varying(249)", maxLength: 249, nullable: false),
                partition = table.Column<int>(type: "integer", nullable: false),
                offset = table.Column<long>(type: "bigint", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: true),
                consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                rejection_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_consumed_messages", x => new { x.topic, x.partition, x.offset });
            });

        migrationBuilder.CreateTable(
            name: "videos",
            schema: "feed",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                hashtags = table.Column<string[]>(type: "text[]", nullable: false),
                owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                uploaded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                has_metadata = table.Column<bool>(type: "boolean", nullable: false),
                available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                has_completion = table.Column<bool>(type: "boolean", nullable: false),
                sort_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_videos", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "renditions",
            schema: "feed",
            columns: table => new
            {
                video_id = table.Column<Guid>(type: "uuid", nullable: false),
                tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                width = table.Column<int>(type: "integer", nullable: false),
                height = table.Column<int>(type: "integer", nullable: false),
                video_codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                audio_codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                bucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                etag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_renditions", x => new { x.video_id, x.tier });
                table.ForeignKey(
                    name: "fk_renditions_videos",
                    column: x => x.video_id,
                    principalSchema: "feed",
                    principalTable: "videos",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_consumed_messages_event_id",
            schema: "feed",
            table: "consumed_messages",
            column: "event_id");
        migrationBuilder.CreateIndex(
            name: "ix_videos_ready_feed",
            schema: "feed",
            table: "videos",
            columns: new[] { "has_metadata", "has_completion", "sort_key" });
        migrationBuilder.CreateIndex(
            name: "ux_videos_sort_key",
            schema: "feed",
            table: "videos",
            column: "sort_key",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ux_renditions_object_key",
            schema: "feed",
            table: "renditions",
            column: "object_key",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "consumed_messages", schema: "feed");
        migrationBuilder.DropTable(name: "renditions", schema: "feed");
        migrationBuilder.DropTable(name: "videos", schema: "feed");
    }
}
